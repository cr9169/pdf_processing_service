using Microsoft.Extensions.Logging;
using PdfProcessingService.Models;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PdfProcessingService.Helpers
{
    /// <summary>
    /// Provides validation functions for PDF files.
    /// </summary>
    public static class FileValidationHelper
    {
        /// <summary>
        /// Validates that a file exists, is a PDF, and meets size requirements.
        /// </summary>
        /// <param name="filePath">Path to the file to validate.</param>
        /// <param name="settings">Settings containing validation constraints.</param>
        /// <param name="logger">Logger for recording validation results.</param>
        /// <returns>A tuple indicating if the file is valid and any error message.</returns>
        public static async Task<(bool IsValid, string ErrorMessage)> ValidatePdfFileAsync(
            string filePath,
            ProcessingSettings settings,
            ILogger logger)
        {
            try
            {
                // Check if file exists
                if (!File.Exists(filePath))
                {
                    logger.LogWarning("File does not exist: {FilePath}", filePath);
                    return (false, $"File does not exist: {filePath}");
                }

                // Check file extension
                if (!Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("File is not a PDF: {FilePath}", filePath);
                    return (false, $"File is not a PDF: {filePath}");
                }

                // Check file size
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > settings.MaxFileSizeInBytes)
                {
                    logger.LogWarning("File exceeds maximum size of {MaxSizeMB}MB: {FilePath}, {SizeMB}MB",
                        settings.MaxFileSizeInBytes / (1024 * 1024),
                        filePath,
                        fileInfo.Length / (1024 * 1024));
                    return (false, $"File exceeds maximum size of {settings.MaxFileSizeInBytes / (1024 * 1024)}MB");
                }

                // Verify PDF header - checks for the %PDF- signature at the start of the file
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
                {
                    byte[] buffer = new byte[5];
                    await stream.ReadAsync(buffer, 0, 5);

                    string header = System.Text.Encoding.ASCII.GetString(buffer);
                    if (!header.StartsWith("%PDF-"))
                    {
                        logger.LogWarning("File does not have a valid PDF header: {FilePath}", filePath);
                        return (false, $"File does not have a valid PDF header: {filePath}");
                    }
                }

                logger.LogInformation("File validated successfully: {FilePath}", filePath);
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error validating file: {FilePath}", filePath);
                return (false, $"Error validating file: {ex.Message}");
            }
        }
    }
}