using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfProcessingService.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PdfProcessingService.Services
{
    /// <summary>
    /// Service for processing TXT files and indexing their content in Elasticsearch.
    /// </summary>
    public class TxtService : IExtractService
    {
        private readonly IElasticsearchService _elasticsearchService;
        private readonly ILogger<TxtService> _logger;
        private readonly ProcessingSettings _settings;

        // Default chunk size of 1MB if not configured
        private const int DefaultChunkSizeInBytes = 1 * 1024 * 1024;

        /// <summary>
        /// Initializes a new instance of the TxtService class.
        /// </summary>
        public TxtService(
            IElasticsearchService elasticsearchService,
            IOptions<ProcessingSettings> settings,
            ILogger<TxtService> logger)
        {
            _elasticsearchService = elasticsearchService;
            _logger = logger;
            _settings = settings.Value;
        }

        /// <summary>
        /// Processes a TXT file and indexes its content in Elasticsearch.
        /// </summary>
        public async Task<ProcessingResponse> ProcessFileAsync(string filePath)
        {
            var stopwatch = Stopwatch.StartNew();
            var response = new ProcessingResponse
            {
                Id = Guid.NewGuid().ToString(),
                FilePath = filePath,
                Success = false,
                Benchmarks = new Dictionary<string, double>()
            };

            try
            {
                _logger.LogInformation("Starting TXT processing for file: {FilePath}", filePath);

                // Validate the file exists
                if (!File.Exists(filePath))
                {
                    response.ErrorMessage = $"File does not exist: {filePath}";
                    return response;
                }

                // Validate file extension
                string extension = Path.GetExtension(filePath).ToLowerInvariant();
                if (extension != ".txt")
                {
                    response.ErrorMessage = $"Invalid file extension: {extension}. Expected .txt";
                    return response;
                }

                // Get file info
                var fileInfo = new FileInfo(filePath);
                response.FileSizeInBytes = fileInfo.Length;

                // Generate a unique identifier for the file
                var fileId = $"{Path.GetFileNameWithoutExtension(filePath).Replace(" ", "_")}_{fileInfo.Length}_{fileInfo.LastWriteTimeUtc.Ticks}";

                // Get chunk size from settings or use default
                int chunkSizeInBytes = _settings.ChunkSizeInBytes > 0
                    ? _settings.ChunkSizeInBytes
                    : DefaultChunkSizeInBytes;

                // Process the file in chunks
                List<DocumentChunk> chunks = await ChunkTextFileAsync(filePath, fileId, chunkSizeInBytes);
                response.ChunkCount = chunks.Count;

                // Index chunks in batches
                var maxBatchSize = 10; // Limit batch size for better performance
                for (int i = 0; i < chunks.Count; i += maxBatchSize)
                {
                    var batch = chunks.Skip(i).Take(maxBatchSize).ToList();
                    _logger.LogInformation("Indexing batch {CurrentBatch}/{TotalBatches} with {ChunkCount} chunks",
                        (i / maxBatchSize) + 1, (chunks.Count / maxBatchSize) + 1, batch.Count);

                    var indexResult = await _elasticsearchService.BulkIndexChunksAsync(batch);
                    if (!indexResult)
                    {
                        response.ErrorMessage = $"Failed to index batch {(i / maxBatchSize) + 1}/{(chunks.Count / maxBatchSize) + 1}";
                        return response;
                    }
                }

                // Update response with success
                response.Success = true;
                response.PageCount = 1; // TXT files don't have pages

                stopwatch.Stop();
                response.ProcessingTimeInSeconds = stopwatch.Elapsed.TotalSeconds;

                _logger.LogInformation("TXT processing completed successfully: {FilePath}, Chunks: {ChunkCount}",
                    filePath, chunks.Count);
                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                response.ProcessingTimeInSeconds = stopwatch.Elapsed.TotalSeconds;
                response.ErrorMessage = $"Error processing TXT: {ex.Message}";

                _logger.LogError(ex, "Error processing TXT file: {FilePath}", filePath);
                return response;
            }
        }

        /// <summary>
        /// Chunks a text file into smaller pieces for Elasticsearch indexing.
        /// </summary>
        /// <param name="filePath">Path to the text file</param>
        /// <param name="fileId">Unique identifier for the file</param>
        /// <param name="chunkSizeInBytes">Maximum size of each chunk in bytes</param>
        /// <returns>List of document chunks</returns>
        private async Task<List<DocumentChunk>> ChunkTextFileAsync(string filePath, string fileId, int chunkSizeInBytes)
        {
            var chunks = new List<DocumentChunk>();
            var fileInfo = new FileInfo(filePath);

            // For small files, just use a single chunk
            if (fileInfo.Length <= chunkSizeInBytes)
            {
                string content = await File.ReadAllTextAsync(filePath);
                chunks.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid().ToString(),
                    OriginalFilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    FileIdentifier = fileId,
                    SequenceNumber = 1,
                    StartPage = 1,
                    EndPage = 1,
                    TotalPages = 1,
                    Content = content,
                    ProcessedAt = DateTime.UtcNow,
                    FileSizeInBytes = fileInfo.Length,
                    TotalChunks = 1
                });
                return chunks;
            }

            // For large files, read in chunks
            _logger.LogInformation("File size {Size} MB exceeds chunk size {ChunkSize} MB. Splitting into chunks.",
                fileInfo.Length / (1024 * 1024), chunkSizeInBytes / (1024 * 1024));

            // Use a StreamReader for efficient reading of large files
            using (var reader = new StreamReader(filePath, Encoding.UTF8))
            {
                var buffer = new char[chunkSizeInBytes / 2]; // Use half the chunk size for the buffer
                var contentBuilder = new StringBuilder();
                int sequenceNumber = 1;

                int bytesRead;
                while ((bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    contentBuilder.Append(buffer, 0, bytesRead);

                    // Check if we have enough data for a chunk
                    string currentContent = contentBuilder.ToString();
                    int estimatedByteSize = Encoding.UTF8.GetByteCount(currentContent);

                    if (estimatedByteSize >= chunkSizeInBytes)
                    {
                        // Find a good break point (ideally at paragraph or sentence)
                        int breakPoint = FindBreakPoint(currentContent);

                        // Create a chunk with content up to the break point
                        string chunkContent = currentContent.Substring(0, breakPoint);

                        chunks.Add(new DocumentChunk
                        {
                            Id = Guid.NewGuid().ToString(),
                            OriginalFilePath = filePath,
                            FileName = Path.GetFileName(filePath),
                            FileIdentifier = fileId,
                            SequenceNumber = sequenceNumber++,
                            StartPage = 1, // TXT doesn't have pages
                            EndPage = 1,
                            TotalPages = 1,
                            Content = chunkContent,
                            ProcessedAt = DateTime.UtcNow,
                            FileSizeInBytes = fileInfo.Length,
                            TotalChunks = -1 // We'll update this later
                        });

                        // Keep the remainder for the next chunk
                        contentBuilder.Clear();
                        contentBuilder.Append(currentContent.Substring(breakPoint));
                    }
                }

                // Don't forget any remaining content
                if (contentBuilder.Length > 0)
                {
                    chunks.Add(new DocumentChunk
                    {
                        Id = Guid.NewGuid().ToString(),
                        OriginalFilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        FileIdentifier = fileId,
                        SequenceNumber = sequenceNumber,
                        StartPage = 1,
                        EndPage = 1,
                        TotalPages = 1,
                        Content = contentBuilder.ToString(),
                        ProcessedAt = DateTime.UtcNow,
                        FileSizeInBytes = fileInfo.Length,
                        TotalChunks = -1
                    });
                }
            }

            // Update the total chunks count
            int totalChunks = chunks.Count;
            foreach (var chunk in chunks)
            {
                chunk.TotalChunks = totalChunks;
            }

            return chunks;
        }

        /// <summary>
        /// Finds a good break point in the text (paragraph, sentence, or word boundary).
        /// </summary>
        /// <param name="text">The text to analyze</param>
        /// <returns>Index where the text should be split</returns>
        private int FindBreakPoint(string text)
        {
            // If text is short, just return the end
            if (text.Length < 1000)
                return text.Length;

            // Try to find paragraph break near the middle
            int midPoint = text.Length / 2;
            int searchRange = Math.Min(text.Length / 4, 5000); // Look within 25% of the middle or 5000 chars, whichever is less

            // Search for double newline (paragraph break)
            for (int i = midPoint; i < midPoint + searchRange && i < text.Length - 1; i++)
            {
                if (text[i] == '\n' && text[i + 1] == '\n')
                    return i + 2; // After the paragraph break
            }

            for (int i = midPoint; i > midPoint - searchRange && i > 1; i--)
            {
                if (text[i] == '\n' && text[i - 1] == '\n')
                    return i + 1; // After the paragraph break
            }

            // If no paragraph break, look for single newline
            for (int i = midPoint; i < midPoint + searchRange && i < text.Length; i++)
            {
                if (text[i] == '\n')
                    return i + 1; // After the newline
            }

            for (int i = midPoint; i > midPoint - searchRange && i > 0; i--)
            {
                if (text[i] == '\n')
                    return i + 1; // After the newline
            }

            // If no newline, look for sentence end (period, exclamation, question mark)
            for (int i = midPoint; i < midPoint + searchRange && i < text.Length; i++)
            {
                if ((text[i] == '.' || text[i] == '!' || text[i] == '?') &&
                    (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1])))
                    return i + 1; // After the sentence end
            }

            for (int i = midPoint; i > midPoint - searchRange && i > 0; i--)
            {
                if ((text[i] == '.' || text[i] == '!' || text[i] == '?') &&
                    (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1])))
                    return i + 1; // After the sentence end
            }

            // If no sentence break, look for space
            for (int i = midPoint; i < midPoint + searchRange && i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]))
                    return i + 1; // After the space
            }

            for (int i = midPoint; i > midPoint - searchRange && i > 0; i--)
            {
                if (char.IsWhiteSpace(text[i]))
                    return i + 1; // After the space
            }

            // If all else fails, just split at midpoint
            return midPoint;
        }
    }
}