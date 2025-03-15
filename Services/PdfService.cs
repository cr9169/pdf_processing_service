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
    public class PdfService : IPdfService
    {
        private readonly IElasticsearchService _elasticsearchService;
        private readonly ILogger<PdfService> _logger;
        private readonly PdfProcessingSettings _settings;

        private readonly ElasticsearchSettings _elasticsearchSettings;

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

            // Set up cancellation token with timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(_settings.MaxProcessingTimeInMinutes));
            var cancellationToken = cts.Token;

            try
            {
                _logger.LogInformation("Starting PDF processing for file: {FilePath}", filePath);

                // Validate file
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

                // Calculate a unique identifier for this file
                var fileInfo = new FileInfo(filePath);
                var fileId = $"{Path.GetFileNameWithoutExtension(filePath).Replace(" ", "_")}_{fileInfo.Length}_{fileInfo.LastWriteTimeUtc.Ticks}";

                response.FileSizeInBytes = fileInfo.Length;

                // Extract text from PDF
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

                // Index chunks into Elasticsearch
                using (var indexingTracker = new PerformanceTracker(
                    "Indexing",
                    (op, time) => _logger.LogInformation("Operation {Operation} completed in {Time:F2} seconds", op, time),
                    response.Benchmarks))
                {
                    // We'll do batched indexing for better performance
                    var batchSize = Math.Min(_elasticsearchSettings.BulkBatchSize, 10); // Limit batch size for large docs
                    var batchCount = (int)Math.Ceiling(chunks.Count / (double)batchSize);

                    for (int i = 0; i < batchCount; i++)
                    {
                        // Check for cancellation
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

                // Update response with success
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