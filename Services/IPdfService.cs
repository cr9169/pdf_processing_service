using PdfProcessingService.Models;
using System.Threading.Tasks;

namespace PdfProcessingService.Services
{
    public interface IPdfService
    {
        Task<PdfProcessingResponse> ProcessPdfFileAsync(string filePath);
    }
}