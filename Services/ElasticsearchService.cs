using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfProcessingService.Models;

namespace PdfProcessingService.Services
{
    /// <summary>
    /// Provides functionality for interacting with Elasticsearch to store and retrieve PDF document chunks.
    /// </summary>
    public class ElasticsearchService : IElasticsearchService
    {
        private readonly ElasticsearchClient _client;
        private readonly ElasticsearchSettings _settings;
        private readonly ILogger<ElasticsearchService> _logger;

        public ElasticsearchService(
            IOptions<ElasticsearchSettings> settings,
            ILogger<ElasticsearchService> logger)
        {
            _settings = settings.Value;
            _logger = logger;

            // Configure the Elasticsearch client using the new Elastic.Clients.Elasticsearch package.
            var clientSettings = new ElasticsearchClientSettings(new Uri(_settings.Url))
                .DefaultIndex(_settings.IndexName)
                .EnableDebugMode(); // Debug mode will include request/response details in the client logs

            _client = new ElasticsearchClient(clientSettings);
        }

        /// <summary>
        /// Ensures that the required Elasticsearch index exists with proper mappings.
        /// </summary>
        public async Task EnsureIndexExistsAsync()
        {
            try
            {
                _logger.LogInformation("Checking if index {IndexName} exists", _settings.IndexName);

                // Check if the index exists.
                var indexExistsResponse = await _client.Indices.ExistsAsync(_settings.IndexName);
                if (!indexExistsResponse.Exists)
                {
                    _logger.LogInformation("Creating index {IndexName}", _settings.IndexName);

                    // Create index with mappings and settings.
                    var createIndexResponse = await _client.Indices.CreateAsync(_settings.IndexName, c => c
                        .Settings(s => s
                            .NumberOfShards(2)
                            .NumberOfReplicas(1)
                            .RefreshInterval("5s")
                        )
                        .Mappings(m => m
                            .Properties<DocumentChunk>(ps => ps
                                // fileName as a keyword field
                                .Keyword("fileName", k => k
                                    .IgnoreAbove(256)
                                )
                                // content as a text field with a sub-field = keyword
                                .Text("content", t => t
                                    .Analyzer("standard")
                                    .Fields(ff => ff
                                        .Keyword("keyword", k => k
                                            .IgnoreAbove(256)
                                        )
                                    )
                                )
                                // processedAt as a date field
                                .Date("processedAt")
                            )
                        )
                    );

                    if (!createIndexResponse.Acknowledged)
                    {
                        _logger.LogError("Failed to create index {IndexName}: {Error}",
                            _settings.IndexName, createIndexResponse.IsValidResponse);
                        throw new Exception($"Failed to create index: {createIndexResponse.IsValidResponse}");
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
        public async Task<bool> IndexChunkAsync(DocumentChunk chunk)
        {
            try
            {
                _logger.LogDebug("Indexing chunk {SequenceNumber} of document {FileIdentifier}",
                    chunk.SequenceNumber, chunk.FileIdentifier);

                _logger.LogInformation("Before indexing file....");

                var response = await _client.IndexAsync(chunk, i => i
                    .Index(_settings.IndexName)
                    .Id(chunk.Id)
                    .Refresh(Refresh.True)
                );

                if (!response.IsValidResponse)
                {
                    _logger.LogError("Failed to index chunk {SequenceNumber} of document {FileIdentifier}: {Error}",
                        chunk.SequenceNumber, chunk.FileIdentifier, response.IsSuccess);
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
        public async Task<bool> BulkIndexChunksAsync(IEnumerable<DocumentChunk> chunks)
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

                // Build a list of BulkIndexOperation<T> (which implement IBulkOperation).
                var bulkOperations = chunks.Select(chunk =>
                    new BulkIndexOperation<DocumentChunk>(chunk)
                    {
                        Index = _settings.IndexName,
                        Id = chunk.Id
                    }
                ).Cast<IBulkOperation>().ToList();

                // Create and execute the bulk request.
                var bulkRequest = new BulkRequest
                {
                    Refresh = Refresh.True, // immediately refresh for search
                    Operations = bulkOperations
                };

                // Execute the bulk request.
                var bulkResponse = await _client.BulkAsync(bulkRequest);

                if (!bulkResponse.IsValidResponse)
                {
                    _logger.LogError("Failed to bulk index chunks for document {FileIdentifier}: {Error}",
                        fileId, bulkResponse.IsValidResponse);

                    if (bulkResponse.Items != null && bulkResponse.Items.Any(i => i.Error is not null))
                    {
                        foreach (var item in bulkResponse.Items.Where(i => i.Error is not null))
                        {
                            _logger.LogError("Item error: {Error}", item.Error);
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
