using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RetrievalService.Models;
using RetrievalService.Services;

namespace RetrievalService.Controllers
{
    [ApiController]
    [Route("api/search")]
    public class SearchController : ControllerBase
    {
        private readonly VectorSearchService _vectorSearchService;

        public SearchController(VectorSearchService vectorSearchService)
        {
            _vectorSearchService = vectorSearchService;
        }

        // POST /api/search
        [HttpPost]
        public async Task<IActionResult> Search([FromBody] SearchRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return BadRequest("Query is required");

            if (string.IsNullOrWhiteSpace(request.DocumentId) || string.IsNullOrWhiteSpace(request.VersionId))
                return BadRequest("DocumentId and VersionId are required");

            try
            {
                var results = await _vectorSearchService.SearchAsync(
                    request.Query,
                    request.DocumentId,
                    request.VersionId,
                    request.TopK);

                return Ok(new
                {
                    Query = request.Query,
                    DocumentId = request.DocumentId,
                    VersionId = request.VersionId,
                    TopK = request.TopK,
                    ResultsCount = results.Count,
                    Results = results
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }
    }
}