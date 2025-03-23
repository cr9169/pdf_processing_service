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
        private readonly FileDownloaderService _fileDownloaderService;
        private readonly ILogger<TxtController> _logger;

        public TxtController(
            TxtService txtService,
            FileDownloaderService fileDownloaderService,
            ILogger<TxtController> logger)
        {
            _txtService = txtService;
            _fileDownloaderService = fileDownloaderService;
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

        /// <summary>
        /// Processes a TXT file and sends its processed content to a custom Elasticsearch plugin endpoint.
        /// The plugin expects a JSON payload with a "content" field containing the processed text.
        /// </summary>
        /// <param name="request">Request containing the path to the TXT file.</param>
        /// <returns>Processing result with status and performance metrics.</returns>
        [HttpPost("process/v3")]
        public async Task<IActionResult> ProcessTxtV3([FromBody] ProcessingRequest request)
        {
            _logger.LogInformation("Request to process TXT file via custom plugin (v3): {FilePath}", request.Path);

            var result = await _txtService.ProcessFileAsyncVersion3(request.Path);

            if (!result.Success)
            {
                _logger.LogWarning("Failed to process TXT file via custom plugin: {FilePath}. Error: {Error}",
                    request.Path, result.ErrorMessage);
                return BadRequest(result);
            }

            return Ok(result);
        }

        [HttpPost("download")]
        public async Task<IActionResult> DownloadFile([FromBody] DownloadRequest request)
        {

            _logger.LogInformation($"Attempting to download file '{request.FileName}' from folder '{request.FolderId}' to local path '{request.LocalPath}'");

            var result = await _fileDownloaderService.DownloadFileAsync(request.FolderId, request.FileName, request.LocalPath);

            if (!result.Success)
            {
                _logger.LogWarning("File download failed for {FileName}. Error: {Error}", request.FileName, result.ErrorMessage);
                return BadRequest(result);
            }

            return Ok(result);
        }
    }
}