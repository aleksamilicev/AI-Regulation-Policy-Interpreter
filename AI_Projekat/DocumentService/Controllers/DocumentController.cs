// Controllers/DocumentController.cs
using DocumentService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentService.Controllers
{
    [ApiController]
    [Route("documents")]
    public class DocumentController : ControllerBase
    {
        private readonly IReliableStateManager _stateManager;

        public DocumentController(IReliableStateManager stateManager)
        {
            _stateManager = stateManager;
        }

        // POST /documents
        [HttpPost]
        public async Task<IActionResult> CreateDocument([FromBody] CreateDocumentRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.RawText))
            {
                return BadRequest("Title and RawText are required");
            }

            var documentId = Guid.NewGuid();
            var versionId = Guid.NewGuid();

            var metadata = new DocumentMetadata
            {
                DocumentId = documentId,
                Title = request.Title,
                Type = request.Type ?? "law",
                CreatedAt = DateTime.UtcNow
            };

            var version = new DocumentVersion
            {
                VersionId = versionId,
                ValidFrom = request.ValidFrom,
                ValidTo = null,
                RawText = request.RawText,
                UploadedAt = DateTime.UtcNow
            };

            var documents = await _stateManager.GetOrAddAsync<IReliableDictionary<Guid, DocumentMetadata>>("Documents");
            var versions = await _stateManager.GetOrAddAsync<IReliableDictionary<Guid, List<DocumentVersion>>>("Versions");

            using (var tx = _stateManager.CreateTransaction())
            {
                await documents.AddAsync(tx, documentId, metadata);
                await versions.AddAsync(tx, documentId, new List<DocumentVersion> { version });
                await tx.CommitAsync();
            }

            return CreatedAtAction(nameof(GetDocument), new { id = documentId }, new
            {
                DocumentId = documentId,
                VersionId = versionId,
                Message = "Document created successfully"
            });
        }

        // POST /documents/{id}/versions
        [HttpPost("{id}/versions")]
        public async Task<IActionResult> AddVersion(Guid id, [FromBody] AddVersionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.RawText))
            {
                return BadRequest("RawText is required");
            }

            var documents = await _stateManager.GetOrAddAsync<IReliableDictionary<Guid, DocumentMetadata>>("Documents");
            var versions = await _stateManager.GetOrAddAsync<IReliableDictionary<Guid, List<DocumentVersion>>>("Versions");

            using (var tx = _stateManager.CreateTransaction())
            {
                var docExists = await documents.TryGetValueAsync(tx, id);
                if (!docExists.HasValue)
                {
                    return NotFound("Document not found");
                }

                var existingVersions = await versions.TryGetValueAsync(tx, id);
                if (!existingVersions.HasValue)
                {
                    return NotFound("Document versions not found");
                }

                var versionList = existingVersions.Value;

                // Validate date overlap
                foreach (var v in versionList)
                {
                    if (request.ValidFrom >= v.ValidFrom &&
                        (!v.ValidTo.HasValue || request.ValidFrom < v.ValidTo.Value))
                    {
                        return BadRequest($"Version date overlaps with existing version {v.VersionId}");
                    }

                    if (request.ValidTo.HasValue &&
                        v.ValidFrom >= request.ValidFrom && v.ValidFrom < request.ValidTo.Value)
                    {
                        return BadRequest($"Version date overlaps with existing version {v.VersionId}");
                    }
                }

                var newVersion = new DocumentVersion
                {
                    VersionId = Guid.NewGuid(),
                    ValidFrom = request.ValidFrom,
                    ValidTo = request.ValidTo,
                    RawText = request.RawText,
                    UploadedAt = DateTime.UtcNow
                };

                versionList.Add(newVersion);
                await versions.SetAsync(tx, id, versionList);
                await tx.CommitAsync();

                return Ok(new
                {
                    VersionId = newVersion.VersionId,
                    Message = "Version added successfully"
                });
            }
        }

        // GET /documents/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetDocument(Guid id)
        {
            var documents = await _stateManager.GetOrAddAsync<IReliableDictionary<Guid, DocumentMetadata>>("Documents");
            var versions = await _stateManager.GetOrAddAsync<IReliableDictionary<Guid, List<DocumentVersion>>>("Versions");

            using (var tx = _stateManager.CreateTransaction())
            {
                var docResult = await documents.TryGetValueAsync(tx, id);
                if (!docResult.HasValue)
                {
                    return NotFound("Document not found");
                }

                var versionResult = await versions.TryGetValueAsync(tx, id);
                var versionCount = versionResult.HasValue ? versionResult.Value.Count : 0;

                return Ok(new
                {
                    Metadata = docResult.Value,
                    VersionCount = versionCount
                });
            }
        }

        // GET /documents/{id}/version?date=2025-01-01
        [HttpGet("{id}/version")]
        public async Task<IActionResult> GetVersionByDate(Guid id, [FromQuery] DateTime date)
        {
            var versions = await _stateManager.GetOrAddAsync<IReliableDictionary<Guid, List<DocumentVersion>>>("Versions");

            using (var tx = _stateManager.CreateTransaction())
            {
                var versionResult = await versions.TryGetValueAsync(tx, id);
                if (!versionResult.HasValue)
                {
                    return NotFound("Document not found");
                }

                var validVersion = versionResult.Value
                    .Where(v => v.ValidFrom <= date && (!v.ValidTo.HasValue || v.ValidTo.Value > date))
                    .OrderByDescending(v => v.ValidFrom)
                    .FirstOrDefault();

                if (validVersion == null)
                {
                    return NotFound($"No version valid for date {date:yyyy-MM-dd}");
                }

                return Ok(validVersion);
            }
        }
    }
}