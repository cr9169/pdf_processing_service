using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.Extensions.Logging;
using PdfProcessingService.Helpers;
using PdfProcessingService.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PdfProcessingService.Helpers
{
    public class PdfTextExtractor
    {
        private readonly ILogger _logger;
        private readonly PdfProcessingSettings _settings;

        public PdfTextExtractor(
            ILogger logger,
            PdfProcessingSettings settings)
        {
            _logger = logger;
            _settings = settings;
        }

        public async Task<(List<PdfDocumentChunk> Chunks, int PageCount, bool Success, string ErrorMessage)> ExtractTextAsync(
            string filePath,
            string fileIdentifier,
            Dictionary<string, double> benchmarks)
        {
            _logger.LogInformation("Beginning text extraction from PDF file: {FilePath}", filePath);
            
            // Return the result of an async operation to make this method truly async
            return await Task.Run(() => 
            {
                var chunks = new List<PdfDocumentChunk>();
                var chunkBuilder = new StringBuilder();
                int totalPages = 0;
                int currentChunk = 1;
                int chunkStartPage = 1;

                try
                {
                    // First, use PdfPig to get page count for progress tracking
                    using (var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(filePath))
                    {
                        totalPages = pdfPigDoc.NumberOfPages;
                    }

                    _logger.LogInformation("PDF document has {PageCount} pages", totalPages);

                    var fileInfo = new FileInfo(filePath);
                    var fileName = Path.GetFileName(filePath);

                    // Now use iText for better text extraction
                    using (var pdfReader = new PdfReader(filePath))
                    using (var pdfDocument = new iText.Kernel.Pdf.PdfDocument(pdfReader))
                    {
                        for (int i = 1; i <= totalPages; i++)
                        {
                            // Prevent memory leaks by creating a new listener for each page
                            var textExtractionStrategy = new LocationTextExtractionStrategy();

                            using (var perfTracker = new PerformanceTracker(
                                $"Page_{i}_Extraction",
                                (op, time) => _logger.LogDebug("Operation {Operation} completed in {Time:F2} seconds", op, time),
                                benchmarks))
                            {
                                string pageText = string.Empty;

                                try
                                {
                                    PdfPage page = pdfDocument.GetPage(i);
                                    pageText = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(page, textExtractionStrategy);

                                    if (string.IsNullOrWhiteSpace(pageText))
                                    {
                                        _logger.LogInformation("Page {PageNumber} contains no extractable text", i);
                                        pageText = $"[No extractable text on page {i}]";
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error extracting text from page {PageNumber}", i);
                                    pageText = $"[Error extracting text from page {i}: {ex.Message}]";
                                }

                                // Add page text to the current chunk
                                chunkBuilder.AppendLine($"--- Page {i} ---");
                                chunkBuilder.AppendLine(pageText);
                                chunkBuilder.AppendLine();

                                _logger.LogDebug("Page {PageNumber}/{TotalPages}: Extracted {CharCount} characters", i, totalPages, pageText.Length);
                            }

                            // Check if we need to create a new chunk due to size
                            if (chunkBuilder.Length * sizeof(char) >= _settings.ChunkSizeInBytes || i == totalPages)
                            {
                                var chunk = new PdfDocumentChunk
                                {
                                    Id = $"{fileIdentifier}_{currentChunk}",
                                    FileIdentifier = fileIdentifier,
                                    OriginalFilePath = filePath,
                                    FileName = fileName,
                                    SequenceNumber = currentChunk,
                                    StartPage = chunkStartPage,
                                    EndPage = i,
                                    TotalPages = totalPages,
                                    Content = chunkBuilder.ToString(),
                                    ProcessedAt = DateTime.UtcNow,
                                    FileSizeInBytes = fileInfo.Length
                                };

                                chunks.Add(chunk);

                                _logger.LogInformation("Created chunk {ChunkNumber} ({StartPage}-{EndPage}) with {CharCount} characters",
                                    currentChunk, chunkStartPage, i, chunkBuilder.Length);

                                // Reset for next chunk
                                chunkBuilder.Clear();
                                currentChunk++;
                                chunkStartPage = i + 1;
                            }
                        }
                    }

                    // Update total chunks count in each chunk
                    foreach (var chunk in chunks)
                    {
                        chunk.TotalChunks = chunks.Count;
                    }

                    _logger.LogInformation("Completed text extraction: {ChunkCount} chunks from {PageCount} pages",
                        chunks.Count, totalPages);

                    return (chunks, totalPages, true, string.Empty);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error extracting text from PDF file: {FilePath}", filePath);
                    return (chunks, totalPages, false, ex.Message);
                }
            });
        }
    }
}