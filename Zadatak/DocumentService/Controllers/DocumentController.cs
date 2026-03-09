using DocumentService.Models;
using DocumentService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace DocumentService.Controllers
{
    [ApiController]
    [Route("api/documents")]
    public class DocumentController : ControllerBase
    {
        private readonly IReliableStateManager _stateManager;
        private readonly EmbeddingService _embeddingService;


        public DocumentController(IReliableStateManager stateManager, EmbeddingService embeddingService)
        {
            _stateManager = stateManager;
            _embeddingService = embeddingService;
        }

        // POST /api/documents/upload
        [HttpPost("upload")]
        public async Task<IActionResult> Upload(IFormFile file, [FromForm] string title)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            var allowedExtensions = new[] { ".pdf", ".txt", ".docx" };
            var extension = Path.GetExtension(file.FileName).ToLower();

            if (!allowedExtensions.Contains(extension))
                return BadRequest("Only PDF, TXT, and DOCX files allowed");

            var docId = Guid.NewGuid().ToString();
            var versionId = Guid.NewGuid().ToString();
            var docTitle = title ?? file.FileName;

            // Save to storage/documents/Title_abc123/v1.ext
            var filePath = StorageHelper.GetVersionFilePath(docId, docTitle, 1, extension);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var documents = await _stateManager.GetOrAddAsync<IReliableDictionary<string, DocumentMetadata>>("documents");
            var versions = await _stateManager.GetOrAddAsync<IReliableDictionary<string, List<DocumentVersion>>>("versions");

            using (var tx = _stateManager.CreateTransaction())
            {
                await documents.AddAsync(tx, docId, new DocumentMetadata
                {
                    Id = docId,
                    Title = docTitle,
                    CurrentVersion = 1,
                    CreatedAt = DateTime.UtcNow
                });

                var version = new DocumentVersion
                {
                    VersionId = versionId,
                    DocumentId = docId,
                    VersionNumber = 1,
                    FilePath = filePath,
                    Extension = extension,
                    UploadedAt = DateTime.UtcNow,
                    ValidFrom = DateTime.UtcNow,
                    ValidTo = null,
                    IsParsed = false
                };

                await versions.AddAsync(tx, docId, new List<DocumentVersion> { version });
                await tx.CommitAsync();
                await StorageHelper.SaveVersionMetadataAsync(docId, docTitle, version);
            }

            return Ok(new { DocumentId = docId, VersionId = versionId, Message = "Document uploaded successfully" });
        }

        // POST /api/documents/{id}/versions/upload
        [HttpPost("{id}/versions/upload")]
        public async Task<IActionResult> UploadNewVersion(
            string id,
            IFormFile file,
            [FromForm] DateTime validFrom)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            var allowedExtensions = new[] { ".pdf", ".txt", ".docx" };
            var extension = Path.GetExtension(file.FileName).ToLower();

            if (!allowedExtensions.Contains(extension))
                return BadRequest("Only PDF, TXT, and DOCX files allowed");

            var documents = await _stateManager.GetOrAddAsync<IReliableDictionary<string, DocumentMetadata>>("documents");
            var versions = await _stateManager.GetOrAddAsync<IReliableDictionary<string, List<DocumentVersion>>>("versions");

            using (var tx = _stateManager.CreateTransaction())
            {
                var docResult = await documents.TryGetValueAsync(tx, id);
                if (!docResult.HasValue)
                    return NotFound("Document not found");

                var versionsResult = await versions.TryGetValueAsync(tx, id);
                if (!versionsResult.HasValue)
                    return NotFound("Document versions not found");

                var doc = docResult.Value;
                var docVersions = versionsResult.Value;

                // Find current active version (ValidTo == null)
                var currentActiveVersion = docVersions
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefault(v => v.ValidTo == null);

                // Close previous active version automatically
                if (currentActiveVersion != null)
                {
                    if (validFrom <= currentActiveVersion.ValidFrom)
                        return BadRequest("New version ValidFrom must be after current version ValidFrom");

                    currentActiveVersion.ValidTo = validFrom;
                }

                // Create new version
                var versionId = Guid.NewGuid().ToString();
                var newVersionNumber = doc.CurrentVersion + 1;

                var filePath = StorageHelper.GetVersionFilePath(id, doc.Title, newVersionNumber, extension);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var newVersion = new DocumentVersion
                {
                    VersionId = versionId,
                    DocumentId = id,
                    VersionNumber = newVersionNumber,
                    FilePath = filePath,
                    Extension = extension,
                    UploadedAt = DateTime.UtcNow,
                    ValidFrom = validFrom,
                    ValidTo = null, // newest version is active
                    IsParsed = false
                };

                docVersions.Add(newVersion);

                // Save updated versions list
                await versions.SetAsync(tx, id, docVersions);

                // Update document current version
                doc.CurrentVersion = newVersionNumber;
                await documents.SetAsync(tx, id, doc);

                await tx.CommitAsync();
                await StorageHelper.SaveVersionMetadataAsync(id, doc.Title, newVersion);

                return Ok(new
                {
                    VersionId = versionId,
                    VersionNumber = newVersionNumber,
                    ValidFrom = newVersion.ValidFrom,
                    Message = "New version uploaded successfully"
                });
            }
        }


        // GET /api/documents
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var documents = await _stateManager.GetOrAddAsync<IReliableDictionary<string, DocumentMetadata>>("documents");
            var versions = await _stateManager.GetOrAddAsync<IReliableDictionary<string, List<DocumentVersion>>>("versions");
            var result = new List<object>();

            using (var tx = _stateManager.CreateTransaction())
            {
                var enumerable = await documents.CreateEnumerableAsync(tx);
                var enumerator = enumerable.GetAsyncEnumerator();

                while (await enumerator.MoveNextAsync(CancellationToken.None))
                {
                    var doc = enumerator.Current.Value;
                    var versionsResult = await versions.TryGetValueAsync(tx, doc.Id);

                    var latestVersion = versionsResult.HasValue
                        ? versionsResult.Value.OrderByDescending(v => v.VersionNumber).FirstOrDefault()
                        : null;

                    result.Add(new
                    {
                        doc.Id,
                        doc.Title,
                        doc.CurrentVersion,
                        Status = latestVersion?.IsParsed == true ? "Parsed" : "Not Parsed"
                    });
                }
            }

            return Ok(result);
        }

        // GET /api/documents/{id}/versions
        [HttpGet("{id}/versions")]
        public async Task<IActionResult> GetVersions(string id)
        {
            var versions = await _stateManager.GetOrAddAsync<IReliableDictionary<string, List<DocumentVersion>>>("versions");

            using (var tx = _stateManager.CreateTransaction())
            {
                var result = await versions.TryGetValueAsync(tx, id);
                if (!result.HasValue)
                    return NotFound("Document versions not found");

                return Ok(result.Value.OrderByDescending(v => v.VersionNumber));
            }
        }

        // POST /api/documents/versions/{versionId}/parse
        [HttpPost("versions/{versionId}/parse")]
        public async Task<IActionResult> ParseVersion(string versionId)
        {
            var versions = await _stateManager.GetOrAddAsync<IReliableDictionary<string, List<DocumentVersion>>>("versions");

            using (var tx = _stateManager.CreateTransaction())
            {
                DocumentVersion targetVersion = null;
                string documentId = null;

                var enumerable = await versions.CreateEnumerableAsync(tx);
                var enumerator = enumerable.GetAsyncEnumerator();

                while (await enumerator.MoveNextAsync(CancellationToken.None))
                {
                    documentId = enumerator.Current.Key;
                    targetVersion = enumerator.Current.Value.FirstOrDefault(v => v.VersionId == versionId);
                    if (targetVersion != null) break;
                }

                if (targetVersion == null)
                    return NotFound("Version not found");

                if (!System.IO.File.Exists(targetVersion.FilePath))
                    return NotFound("Version file not found on disk");

                string text;

                // Parse based on extension
                switch (targetVersion.Extension.ToLower())
                {
                    case ".txt":
                        text = await System.IO.File.ReadAllTextAsync(targetVersion.FilePath);
                        break;

                    case ".pdf":
                        text = ExtractTextFromPdf(targetVersion.FilePath);
                        break;

                    case ".docx":
                        return BadRequest("DOCX parsing not yet implemented");

                    default:
                        return BadRequest($"Unsupported file type: {targetVersion.Extension}");
                }

                // Chunk text
                text = CleanText(text);

                // SEMANTIC CHUNKING
                var chunks = CreateSemanticChunks(text, 1200, 200);

                var parsedChunks = chunks
                    .Select((chunkText, index) => new ParsedChunk
                    {
                        Index = index,
                        Text = chunkText
                    })
                    .ToList();



                // Save to storage/parsed/versionId.json
                var parsedData = new ParsedData
                {
                    VersionId = versionId,
                    Chunks = parsedChunks.ToArray()

                };

                var documents = await _stateManager.GetOrAddAsync<IReliableDictionary<string, DocumentMetadata>>("documents");
                string docTitle;
                using (var docTx = _stateManager.CreateTransaction())
                {
                    var docResult = await documents.TryGetValueAsync(docTx, documentId);
                    docTitle = docResult.HasValue ? docResult.Value.Title : "unknown";
                }

                await StorageHelper.SaveParsedDataAsync(versionId, parsedData, docTitle, targetVersion.VersionNumber);

                // Generiši i sačuvaj embeddings
                for (int i = 0; i < parsedChunks.Count; i++)
                {
                    var embedding = await _embeddingService.GenerateEmbeddingAsync(parsedChunks[i].Text);
                    await StorageHelper.SaveChunkEmbeddingAsync(documentId, versionId, i, embedding);
                }

                // Update version status
                var docVersions = (await versions.TryGetValueAsync(tx, documentId)).Value;
                targetVersion.IsParsed = true;
                await versions.SetAsync(tx, documentId, docVersions);

                await tx.CommitAsync();

                return Ok(new
                {
                    ChunkCount = parsedChunks.Count,
                    ParsedFilePath = StorageHelper.GetParsedFilePathById(versionId),
                    Message = "Version parsed successfully"
                });
            }
        }

        // GET /api/documents/versions/{versionId}/embeddings/{chunkIndex}
        [HttpGet("versions/{versionId}/embeddings/{chunkIndex}")]
        public async Task<IActionResult> GetChunkEmbedding(string versionId, int chunkIndex)
        {
            var versions = await _stateManager.GetOrAddAsync<IReliableDictionary<string, List<DocumentVersion>>>("versions");

            using (var tx = _stateManager.CreateTransaction())
            {
                DocumentVersion targetVersion = null;
                string documentId = null;

                var enumerable = await versions.CreateEnumerableAsync(tx);
                var enumerator = enumerable.GetAsyncEnumerator();

                while (await enumerator.MoveNextAsync(CancellationToken.None))
                {
                    documentId = enumerator.Current.Key;
                    targetVersion = enumerator.Current.Value.FirstOrDefault(v => v.VersionId == versionId);
                    if (targetVersion != null) break;
                }

                if (targetVersion == null)
                    return NotFound("Version not found");

                var embedding = await StorageHelper.LoadChunkEmbeddingAsync(documentId, versionId, chunkIndex);

                if (embedding == null)
                    return NotFound("Embedding not found for this chunk");

                return Ok(new
                {
                    DocumentId = documentId,
                    VersionId = versionId,
                    ChunkIndex = chunkIndex,
                    Embedding = embedding,
                    Dimension = embedding.Length
                });
            }
        }

        private string CleanText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Normalize line endings
            text = text.Replace("\r\n", "\n");

            // Remove excessive whitespace
            text = System.Text.RegularExpressions.Regex.Replace(text, @"[ \t]+", " ");

            // Remove multiple empty lines
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");

            // Remove page numbers (common PDF artifact)
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"^\s*\d+\s*$",
                "",
                System.Text.RegularExpressions.RegexOptions.Multiline);

            // Fix broken words from PDF line breaks
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"(\w)-\n(\w)",
                "$1$2");

            // Remove single-letter line breaks (very common PDF issue)
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"(\w)\n(\w)",
                "$1 $2");

            return text.Trim();
        }

        private List<string> CreateSemanticChunks(
            string text,
            int maxChunkSize = 1200,
            int minChunkSize = 200)
        {
            var chunks = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
                return chunks;

            // Split by article OR paragraph
            var parts = System.Text.RegularExpressions.Regex.Split(
                text,
                @"(?=Član\s+\d+\.?)|\n\n");

            var currentChunk = new System.Text.StringBuilder();

            foreach (var part in parts)
            {
                var trimmed = part.Trim();

                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                // If adding part exceeds max size, finalize current chunk
                if (currentChunk.Length + trimmed.Length > maxChunkSize)
                {
                    if (currentChunk.Length >= minChunkSize)
                    {
                        chunks.Add(currentChunk.ToString().Trim());
                        currentChunk.Clear();
                    }
                }

                currentChunk.AppendLine(trimmed);
                currentChunk.AppendLine();
            }

            // Add last chunk
            if (currentChunk.Length > 0)
                chunks.Add(currentChunk.ToString().Trim());

            return chunks;
        }


        // Helper method za PDF parsing
        private string ExtractTextFromPdf(string pdfPath)
        {
            var sb = new System.Text.StringBuilder();

            using (var reader = new iText.Kernel.Pdf.PdfReader(pdfPath))
            using (var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader))
            {
                for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                {
                    var page = pdfDoc.GetPage(i);
                    var strategy = new iText.Kernel.Pdf.Canvas.Parser.Listener.SimpleTextExtractionStrategy();
                    string pageText = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(page, strategy);
                    sb.AppendLine(pageText);
                }
            }

            return sb.ToString();
        }

        // GET /api/documents/versions/{versionId}/chunks
        [HttpGet("versions/{versionId}/chunks")]
        public async Task<IActionResult> GetVersionChunks(string versionId)
        {
            var parsedData = await StorageHelper.LoadParsedDataAsync(versionId);

            if (parsedData == null)
                return NotFound("Version chunks not found. Parse the version first.");

            return Ok(parsedData.Chunks);
        }
    }
}