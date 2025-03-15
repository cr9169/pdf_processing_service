namespace PdfProcessingService.Models
{
    public class PdfDocumentChunk
    {
        public string Id { get; set; } = string.Empty;
        public string FileIdentifier { get; set; } = string.Empty;
        public string OriginalFilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public int SequenceNumber { get; set; }
        public int TotalChunks { get; set; }
        public int StartPage { get; set; }
        public int EndPage { get; set; }
        public int TotalPages { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
        public long FileSizeInBytes { get; set; }
    }
}