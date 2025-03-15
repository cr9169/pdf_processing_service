using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PdfProcessingService.Models;
using PdfProcessingService.Services;
using System;
using System.Threading.Tasks;

namespace PdfProcessingService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PdfController : ControllerBase
    {
        private readonly IPdfService _pdfService;
        private readonly ILogger<PdfController> _logger;

        public PdfController(
            IPdfService pdfService,
            ILogger<PdfController> logger)
        {
            _pdfService = pdfService;
            _logger = logger;
        }

        /// <summary>
        /// Processes a PDF file and indexes its content into Elasticsearch
        /// </summary>
        /// <param name="request">Request containing the path to the PDF file</param>
        /// <returns>Processing result with status and performance metrics</returns>
        [HttpPost("process")]
        [ProducesResponseType(typeof(PdfProcessingResponse), 200)]
        [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
        [ProducesResponseType(typeof(ProblemDetails), 500)]
        public async Task<IActionResult> ProcessPdfFile([FromBody] PdfProcessingRequest request)
        {
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

                var result = await _pdfService.ProcessPdfFileAsync(request.Path);

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
        /// Health check endpoint
        /// </summary>
        [HttpGet("health")]
        [ProducesResponseType(200)]
        public IActionResult HealthCheck()
        {
            return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
        }
    }
}