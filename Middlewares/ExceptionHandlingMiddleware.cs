using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace PdfProcessingService.Middleware
{
    /// <summary>
    /// Middleware that catches and handles unhandled exceptions in the application.
    /// </summary>
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        /// <summary>
        /// Initializes a new instance of the ExceptionHandlingMiddleware.
        /// </summary>
        /// <param name="next">The next middleware in the pipeline.</param>
        /// <param name="logger">Logger for recording exception details.</param>
        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        /// <summary>
        /// Processes an HTTP request by invoking the next middleware and handling any exceptions.
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                // Continue the middleware pipeline
                await _next(context);
            }
            catch (Exception ex)
            {
                // Log the exception and convert it to a structured JSON response
                _logger.LogError(ex, "Unhandled exception occurred");
                await HandleExceptionAsync(context, ex);
            }
        }

        /// <summary>
        /// Transforms an exception into a structured JSON response.
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        /// <param name="exception">The exception that was caught.</param>
        private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            // Set response content type and status code
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            // Create standardized error response object
            var response = new
            {
                StatusCode = context.Response.StatusCode,
                Message = "An internal server error occurred. Please try again later.",
                ErrorDetail = exception.Message,
                TraceId = context.TraceIdentifier
            };

            // Serialize response with appropriate options
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(response, options);
            await context.Response.WriteAsync(json);
        }
    }
}