namespace PdfProcessingService.Models
{
    public class DownloadResult
    {
        public bool Success { get; set; }
        public string FilePath { get; set; }
        public string ErrorMessage { get; set; }
    }
}
