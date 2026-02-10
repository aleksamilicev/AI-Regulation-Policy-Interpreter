using DocumentService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
            var uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploaded-docs");
            var filePath = Path.Combine(uploadFolder, $"{docId}{extension}");

            // Save file to disk
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Save metadata to Reliable Dictionary
            var documents = await _stateManager.GetOrAddAsync<IReliableDictionary<string, DocumentMetadata>>("documents");

            using (var tx = _stateManager.CreateTransaction())
            {
                await documents.AddAsync(tx, docId, new DocumentMetadata
                {
                    Id = docId,
                    Title = title ?? file.FileName,
                    FilePath = filePath,
                    Extension = extension,
                    UploadedAt = DateTime.UtcNow,
                    IsParsed = false
                });

                await tx.CommitAsync();
            }

            return Ok(new { DocumentId = docId, Message = "Document uploaded successfully" });
        }

        // GET /api/documents
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var documents = await _stateManager.GetOrAddAsync<IReliableDictionary<string, DocumentMetadata>>("documents");
            var result = new List<DocumentMetadata>();

            using (var tx = _stateManager.CreateTransaction())
            {
                var enumerable = await documents.CreateEnumerableAsync(tx);
                var enumerator = enumerable.GetAsyncEnumerator();

                while (await enumerator.MoveNextAsync(CancellationToken.None))
                {
                    result.Add(enumerator.Current.Value);
                }
            }

            return Ok(result);
        }

        // GET /api/documents/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var documents = await _stateManager.GetOrAddAsync<IReliableDictionary<string, DocumentMetadata>>("documents");

            using (var tx = _stateManager.CreateTransaction())
            {
                var result = await documents.TryGetValueAsync(tx, id);
                if (!result.HasValue)
                    return NotFound("Document not found");

                return Ok(result.Value);
            }
        }

        // POST /api/documents/{id}/parse
        [HttpPost("{id}/parse")]
        public async Task<IActionResult> Parse(string id)
        {
            var documents = await _stateManager.GetOrAddAsync<IReliableDictionary<string, DocumentMetadata>>("documents");
            var chunks = await _stateManager.GetOrAddAsync<IReliableDictionary<string, List<DocumentChunk>>>("chunks");

            using (var tx = _stateManager.CreateTransaction())
            {
                var docResult = await documents.TryGetValueAsync(tx, id);
                if (!docResult.HasValue)
                    return NotFound("Document not found");

                var doc = docResult.Value;

                if (!System.IO.File.Exists(doc.FilePath))
                    return NotFound("Document file not found on disk");

                // Simple text extraction (for now just TXT files)
                string text;
                if (doc.Extension == ".txt")
                {
                    text = await System.IO.File.ReadAllTextAsync(doc.FilePath);
                }
                else
                {
                    return BadRequest("Only TXT parsing supported for now");
                }

                // Chunk text (simple: 500 chars per chunk)
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

                // Save chunks
                await chunks.AddOrUpdateAsync(tx, id, documentChunks, (key, oldValue) => documentChunks);

                // Update metadata
                doc.IsParsed = true;
                await documents.SetAsync(tx, id, doc);

                await tx.CommitAsync();

                return Ok(new { ChunkCount = documentChunks.Count, Message = "Document parsed successfully" });
            }
        }

        // GET /api/documents/{id}/chunks
        [HttpGet("{id}/chunks")]
        public async Task<IActionResult> GetChunks(string id)
        {
            var chunks = await _stateManager.GetOrAddAsync<IReliableDictionary<string, List<DocumentChunk>>>("chunks");

            using (var tx = _stateManager.CreateTransaction())
            {
                var result = await chunks.TryGetValueAsync(tx, id);
                if (!result.HasValue)
                    return NotFound("Document chunks not found. Parse the document first.");

                return Ok(result.Value);
            }
        }
    }
}