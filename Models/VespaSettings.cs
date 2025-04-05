namespace PdfProcessingService.Models
{
    public class VespaSettings
    {
        public string Url { get; set; }
        public string IndexName { get; set; }
        public int BulkBatchSize { get; set; }
        public int ChunkSizeInBytes { get; set; }
        public int ConnectionTimeout { get; set; } // in seconds
    }
}
