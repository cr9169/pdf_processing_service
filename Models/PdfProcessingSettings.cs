namespace PdfProcessingService.Models
{
    public class PdfProcessingSettings
    {
        public long MaxFileSizeInBytes { get; set; } = 1 * 1024 * 1024 * 1024; // 1GB default
        public int ReadBufferSizeInBytes { get; set; } = 1024 * 1024; // 1MB read buffer
        public int MaxProcessingTimeInMinutes { get; set; } = 5;
        public bool ProcessInParallel { get; set; } = true;
        public int MaxParallelism { get; set; } = 4;
        public int ChunkSizeInBytes { get; set; } = 5 * 1024 * 1024; // 5MB default, same as in ElasticsearchSettings
    }
}