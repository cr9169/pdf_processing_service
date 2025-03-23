namespace PdfProcessingService.Models
{
    /// <summary>
    /// Configuration settings for PDF processing operations.
    /// </summary>
    public class ProcessingSettings
    {
        /// <summary>Maximum allowed size of PDF files (1GB default).</summary>
        public long MaxFileSizeInBytes { get; set; } = 1 * 1024 * 1024 * 1024; // 1GB default

        /// <summary>Buffer size used when reading PDF files (1MB default).</summary>
        public int ReadBufferSizeInBytes { get; set; } = 1024 * 1024; // 1MB read buffer

        /// <summary>Maximum processing time before timeout (5 minutes default).</summary>
        public int MaxProcessingTimeInMinutes { get; set; } = 5;

        /// <summary>Whether to use parallel processing (true by default).</summary>
        public bool ProcessInParallel { get; set; } = true;

        /// <summary>Maximum number of concurrent operations (4 default).</summary>
        public int MaxParallelism { get; set; } = 4;

        /// <summary>Size of each content chunk for splitting (5MB default).</summary>
        public int ChunkSizeInBytes { get; set; } = 9 * 1024 * 1024; // 5MB default, same as in ElasticsearchSettings
        public string Url { get; set; }
        public string ElasticUsername { get; set; }
        public string ElasticPassword { get; set; }
    }
}