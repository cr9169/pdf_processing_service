using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PdfProcessingService.Models;
using PdfProcessingService.Services;
using System.Threading.Tasks;
using System;                         // Exception, etc.
using System.IO;                      // StreamReader
using System.Text;                    // Encoding.UTF8
using System.Text.Json;               // JsonSerializer
using Microsoft.AspNetCore.Http;      // Request.EnableBuffering()

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

        [HttpPost("pluginManager/processFile")]
        public async Task<IActionResult> StartProcessFileFromNas()
        {
            _logger.LogWarning(".NET service got request from plugin.");
            // 1. אפשר קריאת ה־body מספר פעמים
            Request.EnableBuffering();

            // 2. קרא את הגולמי
            string rawJson;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
            {
                rawJson = await reader.ReadToEndAsync();
                Request.Body.Position = 0;
            }
            _logger.LogDebug("[DEBUG] Raw HTTP body: {RawJson}", rawJson);

            // 3. נסה לפענח ל־ProcessingRequest
            ProcessingRequest requestDto;
            try
            {
                requestDto = JsonSerializer.Deserialize<ProcessingRequest>(rawJson);
                if (requestDto == null)
                    throw new JsonException("Deserialized object was null");
                _logger.LogDebug("[DEBUG] Parsed ProcessingRequest.Path = {Path}", requestDto.Path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Invalid request JSON: {Error}", ex.Message);
                return BadRequest(new { error = "Invalid request JSON: " + ex.Message });
            }

            // 4. בדוק ModelState (במקרה שיש DataAnnotations)
            if (!ModelState.IsValid)
            {
                foreach (var kv in ModelState)
                {
                    foreach (var err in kv.Value.Errors)
                    {
                        _logger.LogWarning("ModelState error for '{Field}': {ErrorMessage}", kv.Key, err.ErrorMessage);
                    }
                }
            }

            // 5. לוג לפני קריאה לשירות
            _logger.LogInformation(".NET service got a request from plugin to start processing from NAS with the file path: {Path}", requestDto.Path);

            // 6. קרא לשירות
            var data = await _txtService.handlePluginNasProcessingRequest(requestDto.Path);

            if (!data.Success)
            {
                _logger.LogWarning("Failed to process TXT file via custom plugin: {FilePath}. Error: {Error}",
                    requestDto.Path, data.ErrorMessage);
                return BadRequest(data);
            }

            // 7. החזר OK עם הנתונים
            return Ok(data);
        }

        [HttpPost("process/vespa")]
        public async Task<IActionResult> ProcessTxtVespa([FromBody] ProcessingRequest request)
        {
            _logger.LogInformation("Request to process TXT file via Vespa plugin: {FilePath}", request.Path);

            var result = await _txtService.ProcessFileAsyncVespa(request.Path);

            if (!result.Success)
            {
                _logger.LogWarning("Failed to process TXT file via Vespa plugin: {FilePath}. Error: {Error}",
                    request.Path, result.ErrorMessage);
                return BadRequest(result);
            }

            return Ok(result);
        }
    }
}