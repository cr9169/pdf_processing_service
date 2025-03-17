namespace PdfProcessingService.Models
{
    /// <summary>
    /// Response model containing results of PDF processing operations.
    /// </summary>
    public class ProcessingResponse
    {
        /// <summary>Unique identifier for the processing operation.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Path to the processed PDF file.</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>Number of pages in the processed PDF.</summary>
        public int PageCount { get; set; }

        /// <summary>Number of chunks the PDF content was split into.</summary>
        public int ChunkCount { get; set; }

        /// <summary>Size of the processed file in bytes.</summary>
        public long FileSizeInBytes { get; set; }

        /// <summary>Total time taken to process the PDF in seconds.</summary>
        public double ProcessingTimeInSeconds { get; set; }

        /// <summary>Whether the processing operation completed successfully.</summary>
        public bool Success { get; set; }

        /// <summary>Error message if processing failed, null if successful.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Performance metrics for different processing stages.</summary>
        public Dictionary<string, double> Benchmarks { get; set; } = new Dictionary<string, double>();
    }
}