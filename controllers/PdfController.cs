using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PdfProcessingService.Models;
using PdfProcessingService.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PdfProcessingService.Controllers
{
    /// <summary>
    /// Controller for PDF processing operations.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PdfController : ControllerBase
    {
        private readonly IExtractService _pdfService;
        private readonly ILogger<PdfController> _logger;

        /// <summary>
        /// Initializes a new instance of the PdfController.
        /// </summary>
        /// <param name="pdfService">Service for processing PDF files.</param>
        /// <param name="logger">Logger for controller operations.</param>
        public PdfController(
            IExtractService pdfService,
            ILogger<PdfController> logger)
        {
            _pdfService = pdfService;
            _logger = logger;
        }

        /// <summary>
        /// Processes a PDF file and indexes its content into Elasticsearch.
        /// </summary>
        /// <param name="request">Request containing the path to the PDF file.</param>
        /// <returns>Processing result with status and performance metrics.</returns>
        /// <response code="200">Returns the processing results when successful.</response>
        /// <response code="400">If the request is invalid or processing failed with validation errors.</response>
        /// <response code="500">If an unexpected error occurs during processing.</response>
        [HttpPost("process")]
        [ProducesResponseType(typeof(ProcessingResponse), 200)]
        [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
        [ProducesResponseType(typeof(ProblemDetails), 500)]
        public async Task<IActionResult> ProcessPdfFile([FromBody] ProcessingRequest request)
        {
            // Validate request
            if (string.IsNullOrWhiteSpace(request.Path))
            {
                return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
                {
                    ["Path"] = new[] { "Path is required" }
                }));
            }

            try
            {
                _logger.LogInformation("Received request to process PDF file: {FilePath}", request.Path);

                // Process the PDF file
                var result = await _pdfService.ProcessFileAsync(request.Path);

                // Handle processing failures
                if (!result.Success)
                {
                    return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
                    {
                        ["Processing"] = new[] { result.ErrorMessage }
                    }));
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request");
                return StatusCode(500, new ProblemDetails
                {
                    Title = "An error occurred while processing the PDF file",
                    Detail = ex.Message,
                    Status = 500
                });
            }
        }

        /// <summary>
        /// Health check endpoint to verify service availability.
        /// </summary>
        /// <returns>Status information indicating the service is running.</returns>
        /// <response code="200">Service is healthy and operational.</response>
        [HttpGet("health")]
        [ProducesResponseType(200)]
        public IActionResult HealthCheck()
        {
            return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
        }
    }
}