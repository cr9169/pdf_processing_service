namespace PdfProcessingService.Models
{
    /// <summary>
    /// Configuration settings for Elasticsearch connections and operations.
    /// </summary>
    public class ElasticsearchSettings
    {
        /// <summary>Elasticsearch server URL (default: http://localhost:9200).</summary>
        public string Url { get; set; } = "http://localhost:9200";

        /// <summary>Name of the Elasticsearch index for storing PDF documents (default: pdf_documents).</summary>
        public string IndexName { get; set; } = $"target_index_{DateTime.Now.ToString("HH:mm")}";

        /// <summary>Maximum number of documents in a bulk indexing operation (default: 1000).</summary>
        public int BulkBatchSize { get; set; } = 1000;

        /// <summary>Maximum size of each document chunk in bytes (default: 5MB).</summary>
        public int ChunkSizeInBytes { get; set; } = 100 * 1024 * 1024; // 100MB default

        /// <summary>Timeout for Elasticsearch connection in seconds (default: 300 seconds/5 minutes).</summary>
        public int ConnectionTimeout { get; set; } = 300; // 5 minutes in seconds
    }
}