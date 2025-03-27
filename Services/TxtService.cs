using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfProcessingService.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

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
        private const int DefaultChunkSizeInBytes = 9 * 1024 * 1024;

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


        ///////////////////////////////////////////////////////////////////////////////////////////////////


        /// <summary>
        /// During the processing process, the system first checks whether the file exists
        /// and matches the required type. If the file is large, it is divided into chunks,
        /// with each chunk represented as a DocumentChunk – a unit that groups all the relevant
        /// information for a fixed-size section of the file. Memory is used using Memory Mapped,
        /// which allows direct access to the file areas in memory without creating a separate
        /// FileStream for each chunk, thus improving performance. Each DocumentChunk is processed
        /// by a separate thread in parallel processing, which allows for simultaneous reading and
        /// processing of different parts of the file. After processing each chunk, the system performs
        /// a fine adjustment of the text boundaries to ensure that the cut is not made in the middle
        /// of a sentence or word. Finally, the sorted DocumentChunks are grouped into small groups
        /// (batches), with each batch being transferred for further processing – for example, to index
        /// in a search system – also carried out in parallel while limiting the number of threads
        /// active at the same time. This process, which combines partitioning for parallel processing,
        /// using DocumentChunk, and working with batches, makes optimal use of memory and dramatically
        /// improves the processing speed of large files.
        /// </summary>
        public async Task<ProcessingResponse> ProcessFileAsyncVersion2(string filePath)
        {
            var overallStopwatch = Stopwatch.StartNew();
            var response = new ProcessingResponse
            {
                Id = Guid.NewGuid().ToString(),
                FilePath = filePath,
                Success = false,
                Benchmarks = []
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
                int chunkSizeInBytes = _settings.ChunkSizeInBytes > 0 ? _settings.ChunkSizeInBytes : DefaultChunkSizeInBytes;

                // -------------------------------
                // STEP 1: Read and chunk the file
                // -------------------------------
                var readStopwatch = Stopwatch.StartNew();
                List<DocumentChunk> chunks = await ChunkTextFileParallelAsyncVersion2(filePath, fileId, chunkSizeInBytes);
                readStopwatch.Stop();
                _logger.LogInformation("Finished reading file. Time taken: {ReadTime} seconds", readStopwatch.Elapsed.TotalSeconds);

                response.ChunkCount = chunks.Count;

                // -------------------------------
                // STEP 2: Bulk index the chunks into Elasticsearch
                // -------------------------------
                var indexStopwatch = Stopwatch.StartNew();

                var maxBatchSize = 10; // Batch size for indexing
                var batchCount = (int)Math.Ceiling(chunks.Count / (double)maxBatchSize);
                var indexTasks = new List<Task<bool>>();

                for (int i = 0; i < chunks.Count; i += maxBatchSize)
                {
                    var batch = chunks.Skip(i).Take(maxBatchSize).ToList();
                    _logger.LogInformation("Indexing batch {CurrentBatch}/{TotalBatches} with {ChunkCount} chunks",
                        (i / maxBatchSize) + 1, batchCount, batch.Count);

                    indexTasks.Add(_elasticsearchService.BulkIndexChunksAsync(batch));

                    // Limit parallel indexing to _settings.MaxParallelism tasks concurrently.
                    if (indexTasks.Count >= _settings.MaxParallelism)
                    {
                        var completedTask = await Task.WhenAny(indexTasks);
                        indexTasks.Remove(completedTask);
                        if (!await completedTask)
                        {
                            response.ErrorMessage = "Failed to index batch";
                            return response;
                        }
                    }
                }

                // Wait for any remaining indexing tasks to complete.
                var results = await Task.WhenAll(indexTasks);
                if (results.Any(r => !r))
                {
                    response.ErrorMessage = "Failed to index one or more batches";
                    return response;
                }
                indexStopwatch.Stop();
                _logger.LogInformation("Finished indexing to Elasticsearch. Time taken: {IndexTime} seconds", indexStopwatch.Elapsed.TotalSeconds);

                // -------------------------------
                // Finalize response
                // -------------------------------
                response.Success = true;
                response.PageCount = 1; // TXT files don't have pages

                overallStopwatch.Stop();
                response.ProcessingTimeInSeconds = overallStopwatch.Elapsed.TotalSeconds;
                _logger.LogInformation("TXT processing completed successfully: {FilePath}, Chunks: {ChunkCount}", filePath, chunks.Count);
                return response;
            }
            catch (Exception ex)
            {
                overallStopwatch.Stop();
                response.ProcessingTimeInSeconds = overallStopwatch.Elapsed.TotalSeconds;
                response.ErrorMessage = $"Error processing TXT: {ex.Message}";
                _logger.LogError(ex, "Error processing TXT file: {FilePath}", filePath);
                return response;
            }
        }


        /// <summary>
        /// Chunks a text file in parallel for faster processing.
        /// </summary>
        private async Task<List<DocumentChunk>> ChunkTextFileParallelAsyncVersion2(string filePath, string fileId, int chunkSizeInBytes)
        {
            var fileInfo = new FileInfo(filePath);

            // For small files, use the original method
            if (fileInfo.Length <= chunkSizeInBytes)
            {
                return await ChunkTextFileAsync(filePath, fileId, chunkSizeInBytes);
            }

            _logger.LogInformation("Using parallel processing for large file: {FilePath}", filePath);

            int chunkCount = (int)Math.Ceiling((double)fileInfo.Length / chunkSizeInBytes);
            var chunkTasks = new List<Task<DocumentChunk>>();

            // Create a memory-mapped file once for the entire file.
            using (var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0L, MemoryMappedFileAccess.Read))
            {
                for (int i = 0; i < chunkCount; i++)
                {
                    int index = i; // Capture the loop variable
                                   // Calculate the start position and view size for this chunk.
                    long startPosition = (long)index * chunkSizeInBytes;
                    long viewSize = Math.Min(chunkSizeInBytes, fileInfo.Length - startPosition);
                    int sequenceNumber = index + 1;

                    // Use Task.Run to process each chunk concurrently.
                    chunkTasks.Add(Task.Run(() =>
                    {

                        _logger.LogInformation("Processing chunk {ChunkIndex} on thread {ThreadId}", index, Thread.CurrentThread.ManagedThreadId);

                        // Create a view stream for this chunk.
                        using (var viewStream = mmf.CreateViewStream(startPosition, viewSize, MemoryMappedFileAccess.Read))
                        {
                            using (var reader = new StreamReader(viewStream, Encoding.UTF8))
                            {
                                // Read the entire chunk synchronously (MemoryMappedViewStream reading is fast).
                                string content = reader.ReadToEnd();

                                // Adjust boundaries to find clean breaks if needed.
                                if (index > 0)
                                {
                                    int breakPoint = FindFirstBreakPointVersion2(content);
                                    content = content.Substring(breakPoint);
                                }
                                if (index < chunkCount - 1)
                                {
                                    int breakPoint = FindLastBreakPointVersion2(content);
                                    content = content.Substring(0, breakPoint);
                                }

                                return new DocumentChunk
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    OriginalFilePath = filePath,
                                    FileName = Path.GetFileName(filePath),
                                    FileIdentifier = fileId,
                                    SequenceNumber = sequenceNumber,
                                    StartPage = 1,
                                    EndPage = 1,
                                    TotalPages = 1,
                                    Content = content,
                                    ProcessedAt = DateTime.UtcNow,
                                    FileSizeInBytes = fileInfo.Length,
                                    TotalChunks = chunkCount
                                };
                            }
                        }
                    }));
                }

                var results = await Task.WhenAll(chunkTasks);
                return results.OrderBy(c => c.SequenceNumber).ToList();
            }
        }



        // Helper methods to find clean break points
        private int FindFirstBreakPointVersion2(string text)
        {
            // Simple implementation - find first line break or space
            for (int i = 0; i < Math.Min(text.Length, 500); i++)
            {
                if (text[i] == '\n' || text[i] == ' ')
                    return i + 1;
            }

            return 0; // No good break point found
        }

        private int FindLastBreakPointVersion2(string text)
        {
            // Simple implementation - find last line break or sentence end
            for (int i = text.Length - 1; i > Math.Max(0, text.Length - 500); i--)
            {
                if (text[i] == '\n' ||
                    ((text[i] == '.' || text[i] == '!' || text[i] == '?') && (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1]))))
                    return i + 1;
            }

            return text.Length; // No good break point found
        }


        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


        public async Task<ProcessingResponse> ProcessFileAsyncVersion3(string filePath)
        {
            var overallStopwatch = Stopwatch.StartNew();
            var response = new ProcessingResponse
            {
                Id = Guid.NewGuid().ToString(),
                FilePath = filePath,
                Success = false,
                Benchmarks = new Dictionary<string, double>()
            };

            try
            {
                _logger.LogInformation("Starting TXT processing (v3) for file: {FilePath}", filePath);

                // בדיקת קיום הקובץ
                if (!File.Exists(filePath))
                {
                    response.ErrorMessage = $"File does not exist: {filePath}";
                    return response;
                }

                // בדיקת סיומת
                string extension = Path.GetExtension(filePath).ToLowerInvariant();
                if (extension != ".txt")
                {
                    response.ErrorMessage = $"Invalid file extension: {extension}. Expected .txt";
                    return response;
                }

                // מידע על הקובץ
                var fileInfo = new FileInfo(filePath);
                response.FileSizeInBytes = fileInfo.Length;

                // זיהוי ייחודי
                var fileId = $"{Path.GetFileNameWithoutExtension(filePath).Replace(" ", "_")}_{fileInfo.Length}_{fileInfo.LastWriteTimeUtc.Ticks}";

                // גודל צ'אנק מתוך ההגדרות או ברירת מחדל
                int chunkSizeInBytes = _settings.ChunkSizeInBytes > 0 ? _settings.ChunkSizeInBytes : DefaultChunkSizeInBytes;

                // -------------------------------
                // STEP 1: פיצול הקובץ לצ'אנקים
                // -------------------------------
                var readStopwatch = Stopwatch.StartNew();
                List<DocumentChunk> chunks = await ChunkTextFileParallelAsyncVersion2(filePath, fileId, chunkSizeInBytes);
                readStopwatch.Stop();
                response.Benchmarks["FileReadTime"] = readStopwatch.Elapsed.TotalSeconds;
                _logger.LogInformation("Finished reading file. Time taken: {ReadTime} seconds", readStopwatch.Elapsed.TotalSeconds);

                response.ChunkCount = chunks.Count;

                // -------------------------------
                // STEP 2: שליחת הצ'אנקים ל-plugin
                // -------------------------------
                var indexStopwatch = Stopwatch.StartNew();

                // נגדיר גודל batch קטן כדי לא להעמיס על המערכת
                var maxBatchSize = 10;
                var batchCount = (int)Math.Ceiling(chunks.Count / (double)maxBatchSize);

                // נשתמש ברשימת משימות כדי לבצע במקביל (עד גבול מסוים)
                var indexTasks = new List<Task<bool>>();

                for (int i = 0; i < chunks.Count; i += maxBatchSize)
                {
                    var batch = chunks.Skip(i).Take(maxBatchSize).ToList();
                    _logger.LogInformation("Indexing batch {CurrentBatch}/{TotalBatches} with {ChunkCount} chunks (v3)",
                        (i / maxBatchSize) + 1, batchCount, batch.Count);

                    // כל batch שולח מספר צ'אנקים במקביל
                    var tasksForThisBatch = batch.Select(chunk => SendChunkToCustomPluginAsync(chunk)).ToList();
                    indexTasks.AddRange(tasksForThisBatch);

                    // הגבלת כמות מקסימלית של משימות שרצות במקביל
                    // אם לא הגדרת MaxParallelism ב-ProcessingSettings, אפשר לשים ערך ידני, למשל 4
                    var maxParallel = _settings.MaxParallelism > 0 ? _settings.MaxParallelism : 4;

                    // כל עוד יש יותר מדי משימות תלויות ועומס על המערכת, נחכה שמשהו יסיים
                    while (indexTasks.Count >= maxParallel)
                    {
                        var completedTask = await Task.WhenAny(indexTasks);
                        indexTasks.Remove(completedTask);

                        if (!await completedTask)
                        {
                            response.ErrorMessage = "Failed to index one of the chunks in the plugin.";
                            return response;
                        }
                    }
                }

                // חכים לכל המשימות שנשארו
                var results = await Task.WhenAll(indexTasks);
                if (results.Any(r => !r))
                {
                    response.ErrorMessage = "Failed to index one or more chunks via the plugin.";
                    return response;
                }

                // Verify all chunks were successfully indexed in Elasticsearch
                try
                {
                    string url = _settings.Url;
                    if (!url.StartsWith("http://"))
                    {
                        url = "http://" + url;
                    }

                    using var httpClient = new HttpClient();

                    // Force a refresh to make indexed documents visible for search
                    _logger.LogInformation("Requesting Elasticsearch refresh to ensure all documents are searchable");
                    var refreshUrl = $"{url}/target_index/_refresh";
                    await httpClient.PostAsync(refreshUrl, null);

                    // Request count of documents in the index
                    var countUrl = $"{url}/target_index/_count";
                    var countResponse = await httpClient.GetAsync(countUrl);

                    if (countResponse.IsSuccessStatusCode)
                    {
                        string countContent = await countResponse.Content.ReadAsStringAsync();
                        using JsonDocument jsonDoc = JsonDocument.Parse(countContent);

                        if (jsonDoc.RootElement.TryGetProperty("count", out JsonElement countElement))
                        {
                            int documentCount = countElement.GetInt32();

                            if (documentCount == chunks.Count)
                            {
                                _logger.LogInformation("ELASTICSEARCH INDEXING COMPLETE: All {ChunkCount} chunks were successfully indexed and verified", chunks.Count);
                                response.Benchmarks["IndexedDocumentCount"] = documentCount;
                            }
                            else
                            {
                                _logger.LogWarning("ELASTICSEARCH INDEXING INCOMPLETE: Expected {ChunkCount} chunks but found {DocumentCount} documents",
                                    chunks.Count, documentCount);
                                response.Benchmarks["ExpectedCount"] = chunks.Count;
                                response.Benchmarks["ActualCount"] = documentCount;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Unable to verify final document count in Elasticsearch: {ErrorMessage}", ex.Message);
                }

                indexStopwatch.Stop();
                response.Benchmarks["IndexingTime"] = indexStopwatch.Elapsed.TotalSeconds;
                _logger.LogInformation("Finished sending data to plugin /_target_index. Time taken: {IndexTime} seconds", indexStopwatch.Elapsed.TotalSeconds);

                // -------------------------------
                // סיום
                // -------------------------------
                response.Success = true;
                response.PageCount = 1; // TXT ללא עמודים

                overallStopwatch.Stop();
                response.ProcessingTimeInSeconds = overallStopwatch.Elapsed.TotalSeconds;
                _logger.LogInformation("TXT processing (v3) completed successfully: {FilePath}, Chunks: {ChunkCount}", filePath, chunks.Count);

                return response;
            }
            catch (Exception ex)
            {
                overallStopwatch.Stop();
                response.ProcessingTimeInSeconds = overallStopwatch.Elapsed.TotalSeconds;
                response.ErrorMessage = $"Error processing TXT (v3): {ex.Message}";
                _logger.LogError(ex, "Error processing TXT file (v3): {FilePath}", filePath);
                return response;
            }
        }

        /// <summary>
        /// Sends a single chunk as JSON to the /_custom_index endpoint.
        /// </summary>
        private async Task<bool> SendChunkToCustomPluginAsync(DocumentChunk chunk)
        {
            try
            {
                // Prepare JSON payload with relevant chunk data
                var bodyObject = new
                {
                    content = chunk.Content,
                    fileIdentifier = chunk.FileIdentifier,
                    fileName = chunk.FileName,
                    sequenceNumber = chunk.SequenceNumber,
                    totalChunks = chunk.TotalChunks
                };
                var json = JsonSerializer.Serialize(bodyObject);

                // Create simple HTTP client
                using var httpClient = new HttpClient();

                // Format URL correctly
                string url = _settings.Url;
                if (!url.StartsWith("http://"))
                {
                    url = "http://" + url;
                }
                var pluginUrl = $"{url}/_custom_index";

                // Log before sending request
                _logger.LogDebug("Sending chunk {SequenceNumber}/{TotalChunks} to Elasticsearch plugin",
                    chunk.SequenceNumber, chunk.TotalChunks);

                // Send the request
                using var requestContent = new StringContent(json, Encoding.UTF8, "application/json");
                var stopwatch = Stopwatch.StartNew();
                var response = await httpClient.PostAsync(pluginUrl, requestContent);
                stopwatch.Stop();

                if (!response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to POST chunk {SequenceNumber}/{TotalChunks} to {PluginUrl}. Status: {StatusCode}, Response: {Response}",
                        chunk.SequenceNumber, chunk.TotalChunks, pluginUrl, response.StatusCode, responseBody);
                    return false;
                }

                // Parse response to extract indexing details
                string responseContent = await response.Content.ReadAsStringAsync();
                using JsonDocument jsonDoc = JsonDocument.Parse(responseContent);

                // Check if indexing was successful directly from our custom field
                if (jsonDoc.RootElement.TryGetProperty("indexing_success", out JsonElement successElement) &&
                    successElement.GetBoolean())
                {
                    // Extract document number if provided by plugin
                    string docNumberInfo = "";
                    if (jsonDoc.RootElement.TryGetProperty("document_number", out JsonElement docNumElement) &&
                        jsonDoc.RootElement.TryGetProperty("total_processed", out JsonElement totalElement))
                    {
                        docNumberInfo = $"(Document #{docNumElement.GetInt32()} of {totalElement.GetInt32()} indexed)";
                    }

                    // Extract processing time if provided by plugin
                    string processingTimeInfo = "";
                    if (jsonDoc.RootElement.TryGetProperty("processing_time_ms", out JsonElement timeElement))
                    {
                        processingTimeInfo = $"in {timeElement.GetInt64()}ms";
                    }
                    else
                    {
                        processingTimeInfo = $"in {stopwatch.ElapsedMilliseconds}ms";
                    }

                    // Log success with detailed information
                    _logger.LogInformation("Chunk {SequenceNumber}/{TotalChunks} indexed successfully {ProcessingTime} {DocInfo}",
                        chunk.SequenceNumber, chunk.TotalChunks, processingTimeInfo, docNumberInfo);

                    return true;
                }
                else
                {
                    _logger.LogWarning("Chunk {SequenceNumber}/{TotalChunks} may not be indexed properly. Response: {Response}",
                        chunk.SequenceNumber, chunk.TotalChunks, responseContent);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending chunk {SequenceNumber}/{TotalChunks} to plugin",
                    chunk.SequenceNumber, chunk.TotalChunks);
                return false;
            }
        }
    }
}