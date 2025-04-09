namespace PdfProcessingService.Models
{
    public class ProcessingResponseWithChunks
    {
        public string Id { get; set; }
        public string FilePath { get; set; }
        public long FileSizeInBytes { get; set; }
        public int ChunkCount { get; set; }
        public int PageCount { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public double ProcessingTimeInSeconds { get; set; }
        public Dictionary<string, double> Benchmarks { get; set; }

        public List<DocumentChunk> Chunks { get; set; } // החזרת הצ'אנקים עצמם
    }
}
