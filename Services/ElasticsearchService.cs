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
    public class ElasticsearchService : IElasticsearchService
    {
        private readonly ElasticClient _client;
        private readonly ElasticsearchSettings _settings;
        private readonly ILogger<ElasticsearchService> _logger;

        public ElasticsearchService(
            IOptions<ElasticsearchSettings> settings,
            ILogger<ElasticsearchService> logger)
        {
            _settings = settings.Value;
            _logger = logger;

            var connectionSettings = new ConnectionSettings(new Uri(_settings.Url))
                .DefaultIndex(_settings.IndexName)
                .EnableDebugMode()
                .RequestTimeout(TimeSpan.FromSeconds(_settings.ConnectionTimeout))
                .DisableDirectStreaming() // For detailed logging
                .OnRequestCompleted(details =>
                {
                    if (details.RequestBodyInBytes != null)
                    {
                        _logger.LogDebug("Elasticsearch Request: {Method} {Uri} \n{RequestBody}",
                            details.HttpMethod, details.Uri,
                            details.RequestBodyInBytes.Length > 1000 ?
                                "Request body too large to log" :
                                System.Text.Encoding.UTF8.GetString(details.RequestBodyInBytes));
                    }

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

        public async Task EnsureIndexExistsAsync()
        {
            try
            {
                _logger.LogInformation("Checking if index {IndexName} exists", _settings.IndexName);

                var indexExists = await _client.Indices.ExistsAsync(_settings.IndexName);

                if (!indexExists.Exists)
                {
                    _logger.LogInformation("Creating index {IndexName}", _settings.IndexName);

                    var createIndexResponse = await _client.Indices.CreateAsync(_settings.IndexName, c => c
                        .Map<PdfDocumentChunk>(m => m
                            .AutoMap()
                            .Properties(ps => ps
                                .Keyword(k => k
                                    .Name(n => n.FileIdentifier)
                                    .IgnoreAbove(256)
                                )
                                .Keyword(k => k
                                    .Name(n => n.FileName)
                                    .IgnoreAbove(256)
                                )
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
                                .Date(d => d
                                    .Name(p => p.ProcessedAt)
                                )

                            )
                        )
                        .Settings(s => s
                            .NumberOfShards(2)
                            .NumberOfReplicas(1)
                            .Setting("index.mapping.total_fields.limit", 2000)
                            .Setting("index.refresh_interval", "5s")
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

        public async Task<bool> IndexChunkAsync(PdfDocumentChunk chunk)
        {
            try
            {
                _logger.LogDebug("Indexing chunk {SequenceNumber} of document {FileIdentifier}",
                    chunk.SequenceNumber, chunk.FileIdentifier);

                var response = await _client.IndexAsync(chunk, idx => idx
                    .Index(_settings.IndexName)
                    .Id(chunk.Id)
                    .Refresh(Elasticsearch.Net.Refresh.True)
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

                var bulkDescriptor = new BulkDescriptor();

                foreach (var chunk in chunks)
                {
                    bulkDescriptor.Index<PdfDocumentChunk>(i => i
                        .Index(_settings.IndexName)
                        .Id(chunk.Id)
                        .Document(chunk)
                    );
                }

                var bulkResponse = await _client.BulkAsync(bulkDescriptor.Refresh(Elasticsearch.Net.Refresh.True));

                if (!bulkResponse.IsValid)
                {
                    _logger.LogError("Failed to bulk index chunks for document {FileIdentifier}: {Error}",
                        fileId, bulkResponse.DebugInformation);

                    // Log specific item failures if available
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