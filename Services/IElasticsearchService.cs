using PdfProcessingService.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PdfProcessingService.Services
{
    /// <summary>
    /// Defines the contract for interacting with Elasticsearch in the PDF processing service.
    /// </summary>
    /// <remarks>
    /// This interface provides methods for managing PDF document chunks in Elasticsearch,
    /// including creating indices, indexing individual chunks, and bulk indexing operations.
    /// </remarks>
    public interface IElasticsearchService
    {
        /// <summary>
        /// Ensures that the required Elasticsearch index exists with proper mappings.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <remarks>
        /// This method is typically called at application startup to verify the
        /// Elasticsearch index is properly configured before processing begins.
        /// If the index doesn't exist, it will be created with appropriate mappings.
        /// </remarks>
        Task EnsureIndexExistsAsync();

        /// <summary>
        /// Indexes a single PDF document chunk in Elasticsearch.
        /// </summary>
        /// <param name="chunk">The PDF document chunk to index.</param>
        /// <returns>
        /// A boolean value indicating whether the indexing operation was successful.
        /// </returns>
        /// <remarks>
        /// Use this method for single-document operations. For better performance
        /// with multiple chunks, consider using <see cref="BulkIndexChunksAsync"/>.
        /// </remarks>
        Task<bool> IndexChunkAsync(DocumentChunk chunk);

        /// <summary>
        /// Indexes multiple PDF document chunks in Elasticsearch using bulk operations.
        /// </summary>
        /// <param name="chunks">The collection of PDF document chunks to index.</param>
        /// <returns>
        /// A boolean value indicating whether the bulk indexing operation was successful.
        /// </returns>
        /// <remarks>
        /// This method is optimized for indexing multiple chunks at once, 
        /// providing better performance than indexing individual chunks separately.
        /// </remarks>
        Task<bool> BulkIndexChunksAsync(IEnumerable<DocumentChunk> chunks);
    }
}