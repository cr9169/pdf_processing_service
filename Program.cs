using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PdfProcessingService.Middleware;
using PdfProcessingService.Models;
using PdfProcessingService.Services;
using Serilog;
using Serilog.Events;
using System;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/pdf-processing-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Elasticsearch settings
builder.Services.Configure<ElasticsearchSettings>(
    builder.Configuration.GetSection("ElasticsearchSettings"));

// Configure PDF processing settings
builder.Services.Configure<PdfProcessingSettings>(
    builder.Configuration.GetSection("PdfProcessingSettings"));

// Register services
builder.Services.AddSingleton<IElasticsearchService, ElasticsearchService>();
builder.Services.AddScoped<IPdfService, PdfService>();

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.WriteIndented = true;
});

// Add CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configure HTTP client for Elasticsearch
builder.Services.AddHttpClient("elasticsearch", client =>
{
    var elasticsearchSettings = builder.Configuration.GetSection("ElasticsearchSettings").Get<ElasticsearchSettings>();
    client.BaseAddress = new Uri(elasticsearchSettings.Url);
    client.Timeout = TimeSpan.FromMinutes(5); // Set timeout to 5 minutes
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

try
{
    Log.Information("Starting PDF Processing Service");

    // Ensure Elasticsearch index exists on startup
    var elasticsearchService = app.Services.GetRequiredService<IElasticsearchService>();
    elasticsearchService.EnsureIndexExistsAsync().GetAwaiter().GetResult();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "PDF Processing Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}