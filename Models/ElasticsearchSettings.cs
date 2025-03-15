namespace PdfProcessingService.Models
{
    public class ElasticsearchSettings
    {
        public string Url { get; set; } = "http://localhost:9200";
        public string IndexName { get; set; } = "pdf_documents";
        public int BulkBatchSize { get; set; } = 1000;
        public int ChunkSizeInBytes { get; set; } = 5 * 1024 * 1024; // 5MB default
        public int ConnectionTimeout { get; set; } = 300; // 5 minutes in seconds
    }
}