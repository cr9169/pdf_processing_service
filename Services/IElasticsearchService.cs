using PdfProcessingService.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PdfProcessingService.Services
{
    public interface IElasticsearchService
    {
        Task EnsureIndexExistsAsync();
        Task<bool> IndexChunkAsync(PdfDocumentChunk chunk);
        Task<bool> BulkIndexChunksAsync(IEnumerable<PdfDocumentChunk> chunks);
    }
}