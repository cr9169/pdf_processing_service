using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PdfProcessingService.Middleware;
using PdfProcessingService.Models;
using PdfProcessingService.Services;
using Serilog;
using Serilog.Events;
using System;

namespace PdfProcessingService;

/// <summary>
/// Entry point for the PDF Processing Service application.
/// </summary>
public class Program
{
    /// <summary>
    /// Application entry point.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureLogging(builder);
        ConfigureServices(builder);

        var app = builder.Build();

        ConfigureMiddleware(app);
        ConfigureEndpoints(app);

        try
        {
            Log.Information("Starting PDF Processing Service");

            //Ensure Elasticsearch index exists on startup
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
    }

    /// <summary>
    /// Configures the application's logging.
    /// </summary>
    /// <param name="builder">The WebApplicationBuilder instance.</param>
    private static void ConfigureLogging(WebApplicationBuilder builder)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        builder.Host.UseSerilog();
    }

    /// <summary>
    /// Configures the application's services.
    /// </summary>
    /// <param name="builder">The WebApplicationBuilder instance.</param>
    private static void ConfigureServices(WebApplicationBuilder builder)
    {
        // Register API and documentation services
        builder.Services.AddControllers()
            .AddJsonOptions(options => options.JsonSerializerOptions.WriteIndented = true);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        // Configure settings from appsettings.json
        builder.Services.Configure<ElasticsearchSettings>(
            builder.Configuration.GetSection("ElasticsearchSettings"));
        builder.Services.Configure<ProcessingSettings>(
            builder.Configuration.GetSection("PdfProcessingSettings"));
        builder.Services.AddScoped<FileDownloaderService>();

        // Register application services
        builder.Services.AddSingleton<IElasticsearchService, ElasticsearchService>();
        builder.Services.AddScoped<IExtractService, PdfService>();
        builder.Services.AddScoped<TxtService>();

        // Configure CORS
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
            var elasticsearchSettings = builder.Configuration
                .GetSection("ElasticsearchSettings")
                .Get<ElasticsearchSettings>();

            client.BaseAddress = new Uri(elasticsearchSettings.Url);
            client.Timeout = TimeSpan.FromMinutes(5); // Set timeout to 5 minutes
        });
    }

    /// <summary>
    /// Configures the application's middleware pipeline.
    /// </summary>
    /// <param name="app">The WebApplication instance.</param>
    private static void ConfigureMiddleware(WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseHttpsRedirection();
        app.UseCors("AllowAll");
        app.UseAuthorization();
    }

    /// <summary>
    /// Configures the application's endpoints.
    /// </summary>
    /// <param name="app">The WebApplication instance.</param>
    private static void ConfigureEndpoints(WebApplication app)
    {
        app.MapControllers();
    }
}