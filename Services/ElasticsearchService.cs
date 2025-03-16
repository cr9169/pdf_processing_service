using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nest;
using PdfProcessingService.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PdfProcessingService.Services
{
    /// <summary>
    /// Provides functionality for interacting with Elasticsearch to store and retrieve PDF document chunks.
    /// </summary>
    /// <remarks>
    /// This service handles all Elasticsearch operations including:
    /// - Index creation and management
    /// - Document indexing (single and bulk operations)
    /// - Detailed request/response logging for debugging
    /// </remarks>
    public class ElasticsearchService : IElasticsearchService
    {
        private readonly ElasticClient _client;
        private readonly ElasticsearchSettings _settings;
        private readonly ILogger<ElasticsearchService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ElasticsearchService"/> class.
        /// </summary>
        /// <param name="settings">Configuration settings for Elasticsearch connections and operations.</param>
        /// <param name="logger">Logger for capturing Elasticsearch operations.</param>
        public ElasticsearchService(
            IOptions<ElasticsearchSettings> settings,
            ILogger<ElasticsearchService> logger)
        {
            _settings = settings.Value;
            _logger = logger;

            // Configure the Elasticsearch client with connection settings, timeout, and logging
            var connectionSettings = new ConnectionSettings(new Uri(_settings.Url))
                .DefaultIndex(_settings.IndexName)
                .EnableDebugMode()                                           // Enable debug mode for detailed information
                .RequestTimeout(TimeSpan.FromSeconds(_settings.ConnectionTimeout))
                .DisableDirectStreaming()                                    // Allows for logging of requests/responses
                .OnRequestCompleted(details =>
                {
                    // Log request details (truncate large payloads)
                    if (details.RequestBodyInBytes != null)
                    {
                        _logger.LogDebug("Elasticsearch Request: {Method} {Uri} \n{RequestBody}",
                            details.HttpMethod, details.Uri,
                            details.RequestBodyInBytes.Length > 1000 ?
                                "Request body too large to log" :
                                System.Text.Encoding.UTF8.GetString(details.RequestBodyInBytes));
                    }

                    // Log response details (truncate large payloads)
                    if (details.ResponseBodyInBytes != null)
                    {
                        _logger.LogDebug("Elasticsearch Response: {StatusCode} \n{ResponseBody}",
                            details.HttpStatusCode,
                            details.ResponseBodyInBytes.Length > 1000 ?
                                "Response body too large to log" :
                                System.Text.Encoding.UTF8.GetString(details.ResponseBodyInBytes));
                    }
                });

            _client = new ElasticClient(connectionSettings);
        }

        /// <summary>
        /// Ensures that the required Elasticsearch index exists with proper mappings.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="Exception">Thrown when index creation fails.</exception>
        /// <remarks>
        /// This method checks if the configured index exists and creates it if necessary.
        /// The index is configured with mappings optimized for PDF document chunks, including:
        /// - Keyword fields for identifiers and filenames
        /// - Text fields with appropriate analyzers for content
        /// - Numeric fields for page numbers and sequence information
        /// - Date fields for processing timestamps
        /// 
        /// The index is also configured with appropriate shards, replicas, and settings.
        /// </remarks>
        public async Task EnsureIndexExistsAsync()
        {
            try
            {
                _logger.LogInformation("Checking if index {IndexName} exists", _settings.IndexName);

                var indexExists = await _client.Indices.ExistsAsync(_settings.IndexName);

                if (!indexExists.Exists)
                {
                    _logger.LogInformation("Creating index {IndexName}", _settings.IndexName);

                    // Define the index mapping for PdfDocumentChunk with appropriate field types
                    var createIndexResponse = await _client.Indices.CreateAsync(_settings.IndexName, c => c
                        .Map<PdfDocumentChunk>(m => m
                            .AutoMap()
                            .Properties(ps => ps
                                // Keyword fields for exact matching and filtering
                                .Keyword(k => k
                                    .Name(n => n.FileIdentifier)
                                    .IgnoreAbove(256)
                                )
                                .Keyword(k => k
                                    .Name(n => n.FileName)
                                    .IgnoreAbove(256)
                                )
                                // Text field with standard analyzer for full-text search
                                .Text(t => t
                                    .Name(n => n.Content)
                                    .Analyzer("standard")
                                    .Fields(f => f
                                        .Keyword(k => k
                                            .Name("keyword")
                                            .IgnoreAbove(256)
                                        )
                                    )
                                )
                                // Numeric fields for efficient range queries
                                .Number(n => n
                                    .Name(p => p.SequenceNumber)
                                    .Type(NumberType.Integer)
                                )
                                .Number(n => n
                                    .Name(p => p.StartPage)
                                    .Type(NumberType.Integer)
                                )
                                .Number(n => n
                                    .Name(p => p.EndPage)
                                    .Type(NumberType.Integer)
                                )
                                .Number(n => n
                                    .Name(p => p.TotalPages)
                                    .Type(NumberType.Integer)
                                )
                                // Date field for timestamp information
                                .Date(d => d
                                    .Name(p => p.ProcessedAt)
                                )
                            )
                        )
                        // Configure index-level settings
                        .Settings(s => s
                            .NumberOfShards(2)                             // Split index across 2 shards
                            .NumberOfReplicas(1)                           // Create 1 replica for redundancy
                            .Setting("index.mapping.total_fields.limit", 2000)  // Allow up to 2000 fields
                            .Setting("index.refresh_interval", "5s")       // Refresh every 5 seconds
                        )
                    );

                    if (!createIndexResponse.IsValid)
                    {
                        _logger.LogError("Failed to create index {IndexName}: {Error}",
                            _settings.IndexName, createIndexResponse.DebugInformation);
                        throw new Exception($"Failed to create index: {createIndexResponse.DebugInformation}");
                    }

                    _logger.LogInformation("Successfully created index {IndexName}", _settings.IndexName);
                }
                else
                {
                    _logger.LogInformation("Index {IndexName} already exists", _settings.IndexName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring index {IndexName} exists", _settings.IndexName);
                throw;
            }
        }

        /// <summary>
        /// Indexes a single PDF document chunk in Elasticsearch.
        /// </summary>
        /// <param name="chunk">The PDF document chunk to index.</param>
        /// <returns>A boolean value indicating whether the indexing operation was successful.</returns>
        /// <remarks>
        /// This method indexes a single chunk and forces an index refresh to make
        /// the document immediately available for search.
        /// </remarks>
        public async Task<bool> IndexChunkAsync(PdfDocumentChunk chunk)
        {
            try
            {
                _logger.LogDebug("Indexing chunk {SequenceNumber} of document {FileIdentifier}",
                    chunk.SequenceNumber, chunk.FileIdentifier);

                // Index the document, using the chunk's ID as the Elasticsearch document ID
                var response = await _client.IndexAsync(chunk, idx => idx
                    .Index(_settings.IndexName)
                    .Id(chunk.Id)
                    .Refresh(Elasticsearch.Net.Refresh.True)  // Immediate refresh for search visibility
                );

                if (!response.IsValid)
                {
                    _logger.LogError("Failed to index chunk {SequenceNumber} of document {FileIdentifier}: {Error}",
                        chunk.SequenceNumber, chunk.FileIdentifier, response.DebugInformation);
                    return false;
                }

                _logger.LogDebug("Successfully indexed chunk {SequenceNumber} of document {FileIdentifier}",
                    chunk.SequenceNumber, chunk.FileIdentifier);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing chunk {SequenceNumber} of document {FileIdentifier}",
                    chunk.SequenceNumber, chunk.FileIdentifier);
                return false;
            }
        }

        /// <summary>
        /// Indexes multiple PDF document chunks in Elasticsearch using bulk operations.
        /// </summary>
        /// <param name="chunks">The collection of PDF document chunks to index.</param>
        /// <returns>A boolean value indicating whether the bulk indexing operation was successful.</returns>
        /// <remarks>
        /// This method performs a bulk indexing operation which is significantly more efficient
        /// than indexing documents individually, especially for large batches.
        /// It forces an index refresh to make all indexed documents immediately available for search.
        /// </remarks>
        public async Task<bool> BulkIndexChunksAsync(IEnumerable<PdfDocumentChunk> chunks)
        {
            if (!chunks.Any())
            {
                _logger.LogWarning("No chunks to index in bulk operation");
                return true;
            }

            try
            {
                var fileId = chunks.First().FileIdentifier;
                var chunkCount = chunks.Count();

                _logger.LogInformation("Bulk indexing {ChunkCount} chunks for document {FileIdentifier}",
                    chunkCount, fileId);

                // Create a bulk operation descriptor
                var bulkDescriptor = new BulkDescriptor();

                // Add each chunk to the bulk operation
                foreach (var chunk in chunks)
                {
                    bulkDescriptor.Index<PdfDocumentChunk>(i => i
                        .Index(_settings.IndexName)
                        .Id(chunk.Id)
                        .Document(chunk)
                    );
                }

                // Execute the bulk operation with immediate refresh
                var bulkResponse = await _client.BulkAsync(bulkDescriptor.Refresh(Elasticsearch.Net.Refresh.True));

                if (!bulkResponse.IsValid)
                {
                    _logger.LogError("Failed to bulk index chunks for document {FileIdentifier}: {Error}",
                        fileId, bulkResponse.DebugInformation);

                    // Log each individual item failure for detailed debugging
                    if (bulkResponse.ItemsWithErrors.Any())
                    {
                        foreach (var itemWithError in bulkResponse.ItemsWithErrors)
                        {
                            _logger.LogError("Item error: {Error}", itemWithError.Error);
                        }
                    }

                    return false;
                }

                _logger.LogInformation("Successfully bulk indexed {ChunkCount} chunks for document {FileIdentifier}",
                    chunkCount, fileId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error bulk indexing chunks");
                return false;
            }
        }
    }
}