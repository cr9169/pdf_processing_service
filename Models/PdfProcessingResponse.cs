namespace PdfProcessingService.Models
{
    public class PdfProcessingResponse
    {
        public string Id { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int PageCount { get; set; }
        public int ChunkCount { get; set; }
        public long FileSizeInBytes { get; set; }
        public double ProcessingTimeInSeconds { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, double> Benchmarks { get; set; } = new Dictionary<string, double>();
    }
}