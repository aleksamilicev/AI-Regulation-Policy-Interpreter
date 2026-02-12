using DocumentService.Models;
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

namespace DocumentService.Controllers
{
    [ApiController]
    [Route("api/documents")]
    public class DocumentController : ControllerBase
    {
        private readonly IReliableStateManager _stateManager;

        public DocumentController(IReliableStateManager stateManager)
        {
            _stateManager = stateManager;
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
            var uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploaded-docs");
            var filePath = Path.Combine(uploadFolder, $"{versionId}{extension}");

            // Save file to disk
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var documents = await _stateManager.GetOrAddAsync<IReliableDictionary<string, DocumentMetadata>>("documents");
            var versions = await _stateManager.GetOrAddAsync<IReliableDictionary<string, List<DocumentVersion>>>("versions");

            using (var tx = _stateManager.CreateTransaction())
            {
                // Create document metadata
                await documents.AddAsync(tx, docId, new DocumentMetadata
                {
                    Id = docId,
                    Title = title ?? file.FileName,
                    CurrentVersion = 1,
                    CreatedAt = DateTime.UtcNow
                });

                // Create first version
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
            }

            return Ok(new { DocumentId = docId, VersionId = versionId, Message = "Document uploaded successfully" });
        }

        // POST /api/documents/{id}/versions/upload
        [HttpPost("{id}/versions/upload")]
        public async Task<IActionResult> UploadNewVersion(
    string id,
    IFormFile file,
    [FromForm] DateTime validFrom,
    [FromForm] DateTime? validTo)
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

                // 1️⃣ Nađi trenutno aktivnu verziju (ona bez ValidTo)
                var currentActiveVersion = docVersions
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefault(v => !v.ValidTo.HasValue);

                // 2️⃣ Proveri overlap sa svim OSTALIM verzijama
                foreach (var v in docVersions)
                {
                    if (currentActiveVersion != null && v.VersionId == currentActiveVersion.VersionId)
                        continue;

                    bool overlap =
                        validFrom < (v.ValidTo ?? DateTime.MaxValue) &&
                        (validTo ?? DateTime.MaxValue) > v.ValidFrom;

                    if (overlap)
                        return BadRequest($"Version date overlaps with version {v.VersionNumber}");
                }

                // 3️⃣ Zatvori prethodnu aktivnu verziju
                if (currentActiveVersion != null)
                {
                    currentActiveVersion.ValidTo = validFrom;
                }

                // 4️⃣ Sačuvaj fajl
                var versionId = Guid.NewGuid().ToString();
                var uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploaded-docs");
                var filePath = Path.Combine(uploadFolder, $"{versionId}{extension}");

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // 5️⃣ Kreiraj novu verziju
                var newVersionNumber = doc.CurrentVersion + 1;

                var newVersion = new DocumentVersion
                {
                    VersionId = versionId,
                    DocumentId = id,
                    VersionNumber = newVersionNumber,
                    FilePath = filePath,
                    Extension = extension,
                    UploadedAt = DateTime.UtcNow,
                    ValidFrom = validFrom,
                    ValidTo = validTo,
                    IsParsed = false
                };

                docVersions.Add(newVersion);
                await versions.SetAsync(tx, id, docVersions);

                // 6️⃣ Update dokumenta
                doc.CurrentVersion = newVersionNumber;
                await documents.SetAsync(tx, id, doc);

                await tx.CommitAsync();

                return Ok(new
                {
                    VersionId = versionId,
                    VersionNumber = newVersionNumber,
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
            var chunks = await _stateManager.GetOrAddAsync<IReliableDictionary<string, List<DocumentChunk>>>("chunks");

            using (var tx = _stateManager.CreateTransaction())
            {
                DocumentVersion targetVersion = null;
                string documentId = null;

                // Find version
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
                if (targetVersion.Extension == ".txt")
                {
                    text = await System.IO.File.ReadAllTextAsync(targetVersion.FilePath);
                }
                else
                {
                    return BadRequest("Only TXT parsing supported for now");
                }

                // Chunk text
                var documentChunks = new List<DocumentChunk>();
                int chunkSize = 500;
                for (int i = 0; i < text.Length; i += chunkSize)
                {
                    var chunkText = text.Substring(i, Math.Min(chunkSize, text.Length - i));
                    documentChunks.Add(new DocumentChunk
                    {
                        ChunkId = Guid.NewGuid(),
                        ChunkIndex = documentChunks.Count,
                        Text = chunkText
                    });
                }

                // Save chunks with versionId as key
                await chunks.AddOrUpdateAsync(tx, versionId, documentChunks, (key, oldValue) => documentChunks);

                // Update version status
                var docVersions = (await versions.TryGetValueAsync(tx, documentId)).Value;
                targetVersion.IsParsed = true;
                await versions.SetAsync(tx, documentId, docVersions);

                await tx.CommitAsync();

                return Ok(new { ChunkCount = documentChunks.Count, Message = "Version parsed successfully" });
            }
        }

        // GET /api/documents/versions/{versionId}/chunks
        [HttpGet("versions/{versionId}/chunks")]
        public async Task<IActionResult> GetVersionChunks(string versionId)
        {
            var chunks = await _stateManager.GetOrAddAsync<IReliableDictionary<string, List<DocumentChunk>>>("chunks");

            using (var tx = _stateManager.CreateTransaction())
            {
                var result = await chunks.TryGetValueAsync(tx, versionId);
                if (!result.HasValue)
                    return NotFound("Version chunks not found. Parse the version first.");

                return Ok(result.Value);
            }
        }
    }
}