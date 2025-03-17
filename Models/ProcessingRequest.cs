namespace PdfProcessingService.Models
{
    /// <summary>
    /// Request model for PDF processing operations.
    /// </summary>
    public class ProcessingRequest
    {
        /// <summary>File path to the PDF document to be processed.</summary>
        public string Path { get; set; } = string.Empty;
    }
}