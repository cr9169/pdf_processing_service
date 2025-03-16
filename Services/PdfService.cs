using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfProcessingService.Helpers;
using PdfProcessingService.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PdfProcessingService.Services
{
    /// <summary>
    /// Handles the processing of PDF files, including text extraction and indexing in Elasticsearch.
    /// </summary>
    /// <remarks>
    /// This service orchestrates the full PDF processing workflow:
    /// 1. Validates the PDF file
    /// 2. Extracts text content and splits it into chunks
    /// 3. Indexes chunks into Elasticsearch
    /// 4. Tracks performance benchmarks throughout the process
    /// 5. Handles errors and timeout conditions
    /// </remarks>
    public class PdfService : IPdfService
    {
        private readonly IElasticsearchService _elasticsearchService;
        private readonly ILogger<PdfService> _logger;
        private readonly PdfProcessingSettings _settings;
        private readonly ElasticsearchSettings _elasticsearchSettings;

        /// <summary>
        /// Initializes a new instance of the <see cref="PdfService"/> class.
        /// </summary>
        /// <param name="elasticsearchService">Service for interacting with Elasticsearch.</param>
        /// <param name="settings">PDF processing configuration settings.</param>
        /// <param name="elasticsearchSettings">Elasticsearch configuration settings.</param>
        /// <param name="logger">Logger for the PDF service.</param>
        public PdfService(
            IElasticsearchService elasticsearchService,
            IOptions<PdfProcessingSettings> settings,
            IOptions<ElasticsearchSettings> elasticsearchSettings,
            ILogger<PdfService> logger)
        {
            _elasticsearchService = elasticsearchService;
            _logger = logger;
            _settings = settings.Value;
            _elasticsearchSettings = elasticsearchSettings.Value;
        }

        /// <summary>
        /// Processes a PDF file, extracting its content and indexing it in Elasticsearch.
        /// </summary>
        /// <param name="filePath">The full path to the PDF file to process.</param>
        /// <returns>A <see cref="PdfProcessingResponse"/> containing processing results and metrics.</returns>
        public async Task<PdfProcessingResponse> ProcessPdfFileAsync(string filePath)
        {
            var stopwatch = Stopwatch.StartNew();
            var response = new PdfProcessingResponse
            {
                Id = Guid.NewGuid().ToString(),
                FilePath = filePath,
                Success = false,
                Benchmarks = new Dictionary<string, double>()
            };

            // Set up cancellation token with timeout based on configured maximum processing time
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(_settings.MaxProcessingTimeInMinutes));
            var cancellationToken = cts.Token;

            try
            {
                _logger.LogInformation("Starting PDF processing for file: {FilePath}", filePath);

                // Step 1: Validate the PDF file
                // This ensures the file exists, has the correct extension, and meets size requirements
                using (var validationTracker = new PerformanceTracker(
                    "Validation",
                    (op, time) => _logger.LogInformation("Operation {Operation} completed in {Time:F2} seconds", op, time),
                    response.Benchmarks))
                {
                    var validationResult = await FileValidationHelper.ValidatePdfFileAsync(filePath, _settings, _logger);
                    if (!validationResult.IsValid)
                    {
                        response.ErrorMessage = validationResult.ErrorMessage;
                        return response;
                    }
                }

                // Generate a unique identifier for the file based on its name, size, and last modified date
                var fileInfo = new FileInfo(filePath);
                var fileId = $"{Path.GetFileNameWithoutExtension(filePath).Replace(" ", "_")}_{fileInfo.Length}_{fileInfo.LastWriteTimeUtc.Ticks}";

                response.FileSizeInBytes = fileInfo.Length;

                // Step 2: Extract text from PDF and split into chunks
                List<PdfDocumentChunk> chunks;
                int pageCount;
                bool extractionSuccess;
                string extractionError;

                using (var extractionTracker = new PerformanceTracker(
                    "TextExtraction",
                    (op, time) => _logger.LogInformation("Operation {Operation} completed in {Time:F2} seconds", op, time),
                    response.Benchmarks))
                {
                    var extractor = new PdfTextExtractor(_logger, _settings);
                    var extractionResult = await extractor.ExtractTextAsync(filePath, fileId, response.Benchmarks);

                    chunks = extractionResult.Chunks;
                    pageCount = extractionResult.PageCount;
                    extractionSuccess = extractionResult.Success;
                    extractionError = extractionResult.ErrorMessage;
                }

                if (!extractionSuccess)
                {
                    response.ErrorMessage = $"Error extracting text: {extractionError}";
                    return response;
                }

                response.PageCount = pageCount;
                response.ChunkCount = chunks.Count;

                // Step 3: Index chunks into Elasticsearch
                // Uses batched indexing for better performance with large documents
                using (var indexingTracker = new PerformanceTracker(
                    "Indexing",
                    (op, time) => _logger.LogInformation("Operation {Operation} completed in {Time:F2} seconds", op, time),
                    response.Benchmarks))
                {
                    // Calculate batch size and count for efficient indexing
                    var batchSize = Math.Min(_elasticsearchSettings.BulkBatchSize, 10); // Limit batch size for large docs
                    var batchCount = (int)Math.Ceiling(chunks.Count / (double)batchSize);

                    for (int i = 0; i < batchCount; i++)
                    {
                        // Check for cancellation to abort long-running operations
                        cancellationToken.ThrowIfCancellationRequested();

                        var batch = chunks.Skip(i * batchSize).Take(batchSize).ToList();

                        _logger.LogInformation("Indexing batch {CurrentBatch}/{TotalBatches} with {ChunkCount} chunks",
                            i + 1, batchCount, batch.Count);

                        var indexResult = await _elasticsearchService.BulkIndexChunksAsync(batch);

                        if (!indexResult)
                        {
                            response.ErrorMessage = $"Error indexing batch {i + 1}/{batchCount}";
                            return response;
                        }
                    }
                }

                // Update response with success status
                response.Success = true;

                stopwatch.Stop();
                response.ProcessingTimeInSeconds = stopwatch.Elapsed.TotalSeconds;

                _logger.LogInformation(
                    "PDF processing completed successfully. File: {FilePath}, Pages: {PageCount}, Chunks: {ChunkCount}, Time: {ProcessingTime:F2} seconds",
                    filePath, pageCount, chunks.Count, response.ProcessingTimeInSeconds);

                return response;
            }
            catch (OperationCanceledException)
            {
                // Handle timeout scenario
                stopwatch.Stop();
                response.ProcessingTimeInSeconds = stopwatch.Elapsed.TotalSeconds;
                response.ErrorMessage = $"Processing timed out after {_settings.MaxProcessingTimeInMinutes} minutes";

                _logger.LogError(
                    "PDF processing timed out. File: {FilePath}, Time: {ProcessingTime:F2} seconds",
                    filePath, response.ProcessingTimeInSeconds);

                return response;
            }
            catch (Exception ex)
            {
                // Handle unexpected errors
                stopwatch.Stop();
                response.ProcessingTimeInSeconds = stopwatch.Elapsed.TotalSeconds;
                response.ErrorMessage = $"Error processing PDF: {ex.Message}";

                _logger.LogError(ex,
                    "Error processing PDF. File: {FilePath}, Time: {ProcessingTime:F2} seconds",
                    filePath, response.ProcessingTimeInSeconds);

                return response;
            }
        }
    }
}