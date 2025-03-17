using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PdfProcessingService.Models;
using PdfProcessingService.Services;
using System.Threading.Tasks;

namespace PdfProcessingService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TxtController : ControllerBase
    {
        private readonly TxtService _txtService;
        private readonly ILogger<TxtController> _logger;

        public TxtController(
            TxtService txtService,
            ILogger<TxtController> logger)
        {
            _txtService = txtService;
            _logger = logger;
        }

        [HttpPost("process")]
        public async Task<IActionResult> ProcessTxt([FromBody] ProcessingRequest request)
        {
            _logger.LogInformation("Request to process TXT file: {FilePath}", request.Path);

            var result = await _txtService.ProcessFileAsyncVersion2(request.Path);

            if (!result.Success)
            {
                _logger.LogWarning("Failed to process TXT file: {FilePath}. Error: {Error}",
                    request.Path, result.ErrorMessage);
                return BadRequest(result);
            }

            return Ok(result);
        }
    }
}