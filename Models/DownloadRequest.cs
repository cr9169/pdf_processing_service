namespace PdfProcessingService.Models
{
    public class DownloadRequest
    {
        public string FolderId { get; set; }
        public string FileName { get; set; }
        public string LocalPath { get; set; }
    }
}
