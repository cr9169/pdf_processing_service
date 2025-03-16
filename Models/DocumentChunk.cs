namespace PdfProcessingService.Models
{
    /// <summary>
    /// Represents a chunk of text content extracted from a PDF document.
    /// </summary>
    public class DocumentChunk
    {
        /// <summary>Unique identifier for this chunk.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Identifier for the source PDF file this chunk belongs to.</summary>
        public string FileIdentifier { get; set; } = string.Empty;

        /// <summary>Original file path of the source PDF.</summary>
        public string OriginalFilePath { get; set; } = string.Empty;

        /// <summary>Name of the source PDF file.</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>Ordinal position of this chunk in the sequence of chunks from the document.</summary>
        public int SequenceNumber { get; set; }

        /// <summary>Total number of chunks the document was split into.</summary>
        public int TotalChunks { get; set; }

        /// <summary>First page number included in this chunk.</summary>
        public int StartPage { get; set; }

        /// <summary>Last page number included in this chunk.</summary>
        public int EndPage { get; set; }

        /// <summary>Total number of pages in the original document.</summary>
        public int TotalPages { get; set; }

        /// <summary>Extracted text content contained in this chunk.</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>Timestamp when this chunk was processed.</summary>
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Size of the original PDF file in bytes.</summary>
        public long FileSizeInBytes { get; set; }
    }
}