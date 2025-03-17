using PdfProcessingService.Models;
using System.Threading.Tasks;

namespace PdfProcessingService.Services
{
    /// <summary>
    /// Defines the contract for PDF processing services.
    /// </summary>
    /// <remarks>
    /// This interface represents the primary service for processing PDF documents,
    /// including extraction of text content and indexing in search systems.
    /// </remarks>
    public interface IExtractService
    {
        /// <summary>
        /// Processes a PDF file asynchronously.
        /// </summary>
        /// <param name="filePath">The full path to the PDF file to be processed.</param>
        /// <returns>
        /// A <see cref="ProcessingResponse"/> containing the processing results,
        /// including success status, metrics, and any error information.
        /// </returns>
        /// <remarks>
        /// The processing includes validating the file, extracting text content,
        /// splitting into searchable chunks, and indexing in the search system.
        /// Performance metrics are collected throughout the process.
        /// </remarks>
        Task<ProcessingResponse> ProcessFileAsync(string filePath);
    }
}