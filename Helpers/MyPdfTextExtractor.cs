using Aspose.Pdf;
using Aspose.Pdf.Text;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf;
using PdfProcessingService.Models;
using System.Text;
using iText.Kernel.Pdf.Canvas.Parser;



namespace PdfProcessingService.Helpers
{
    /// <summary>
    /// Extracts text content from PDF files and organizes it into manageable chunks.
    /// </summary>
    public class MyPdfTextExtractor
    {
        private readonly ILogger _logger;
        private readonly PdfProcessingSettings _settings;

        /// <summary>
        /// Initializes a new instance of the PdfTextExtractor class.
        /// </summary>
        /// <param name="logger">Logger for tracking extraction operations.</param>
        /// <param name="settings">Settings that control processing behavior.</param>
        public MyPdfTextExtractor(
            ILogger logger,
            PdfProcessingSettings settings)
        {
            _logger = logger;
            _settings = settings;
        }

        /// <summary>
        /// Extracts text from a PDF file and splits it into manageable chunks.
        /// </summary>
        /// <param name="filePath">Path to the PDF file.</param>
        /// <param name="fileIdentifier">Unique identifier for the file.</param>
        /// <param name="benchmarks">Dictionary to store performance metrics.</param>
        /// <returns>
        /// A tuple containing: list of document chunks, total page count, success indicator, and any error message.
        /// </returns>
        public async Task<(List<DocumentChunk> Chunks, int PageCount, bool Success, string ErrorMessage)> ExtractTextAsync(
            string filePath,
            string fileIdentifier,
            Dictionary<string, double> benchmarks)
        {
            _logger.LogInformation("Beginning text extraction from PDF file: {FilePath}", filePath);

            // Run extraction on a background thread to keep the API responsive
            return await Task.Run(() =>
            {
                var chunks = new List<DocumentChunk>();
                var chunkBuilder = new StringBuilder();
                int totalPages = 0;
                int currentChunk = 1;
                int chunkStartPage = 1;

                try
                {


                    chunks = GetPages3Parallel(filePath)
                    .Select((content, index) =>
                    {
                        return new DocumentChunk()
                        {
                            Id = Guid.NewGuid().ToString(),
                            OriginalFilePath = filePath,
                            FileName = Path.GetFileName(filePath),
                            FileIdentifier = fileIdentifier,
                            SequenceNumber = index + 1,
                            StartPage = index + 1,
                            EndPage = index + 1,
                            TotalPages = totalPages,
                            Content = content,
                            ProcessedAt = DateTime.UtcNow,
                            FileSizeInBytes = new FileInfo(filePath).Length,
                            TotalChunks = totalPages
                        };
                    }).ToList();


                    var count = chunks.Sum(c => c.Content.Length);

              

                    return (chunks, chunks.Count, true, string.Empty);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error extracting text from PDF file: {FilePath}", filePath);
                    return ([], 0, false, ex.Message);
                }
            });
        }


        public IEnumerable<string> GetPagesAsposePdf(string pdfPath)
        {
            using (FileStream fs = new FileStream(pdfPath, FileMode.Open, FileAccess.Read))
            {
                // Load the document from the stream
                Document pdfDocument = new Document(fs);


                // Process each page individually ("chunk by page")
                foreach (Page page in pdfDocument.Pages)
                {
                    // Extract text from the page
                    TextAbsorber absorber = new TextAbsorber();
                    page.Accept(absorber);

                    Console.WriteLine($"Page {page.Number}:\n{absorber.Text}\n");

                    // Yield the text for the current page
                    yield return absorber.Text; 
                }
            }
        }

        public IEnumerable<string> GetPagesWithIText(string pdfPath)
        {
            using (var reader = new PdfReader(pdfPath))
            using (var pdfDoc = new PdfDocument(reader))
            {
                int numPages = pdfDoc.GetNumberOfPages();
                for (int pageNum = 1; pageNum <= numPages; pageNum++)
                {
                    // Use a simple text extraction strategy (you can choose others if needed)
                    var strategy = new SimpleTextExtractionStrategy();
                    string pageText = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(pageNum), strategy);
                    yield return pageText;
                }
            }           
        }

        public IEnumerable<string> GetPages3Parallel(string pdfPath)
        {
            using (var reader = new PdfReader(pdfPath))
            using (var pdfDoc = new PdfDocument(reader))
            {
                int numPages = pdfDoc.GetNumberOfPages();
                object pdfLock = new object(); // Lock to ensure thread-safe access to pdfDoc

                // Use PLINQ to process page numbers in parallel, preserving the original order.
                var texts = Enumerable.Range(1, numPages)
                    .AsParallel()
                    .AsOrdered()
                    .Select(pageNum =>
                    {
                        var strategy = new SimpleTextExtractionStrategy();
                        string pageText;
                        // Locking around GetPage call to avoid concurrency issues.
                        lock (pdfLock)
                        {
                            pageText = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(pageNum), strategy);
                        }
                        return pageText;
                    })
                    .ToList();

                // Yield the extracted texts sequentially.
                foreach (var text in texts)
                {
                    yield return text;
                }
            }
        }

    }



}