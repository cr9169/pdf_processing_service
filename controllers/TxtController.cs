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