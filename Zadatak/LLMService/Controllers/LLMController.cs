using LLMService.Models;
using LLMService.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LLMService.Controllers
{
    [ApiController]
    [Route("api/llm")]
    public class LLMController : ControllerBase
    {
        private readonly OllamaService _ollamaService;

        public LLMController(OllamaService ollamaService)
        {
            _ollamaService = ollamaService;
        }

        // POST /api/llm/generate
        [HttpPost("generate")]
        public async Task<IActionResult> Generate([FromBody] LLMRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return BadRequest("Query is required");

            if (request.Context == null || request.Context.Count == 0)
                return BadRequest("Context chunks are required");

            try
            {
                Console.WriteLine($"[LLM] Generating response for query: {request.Query}");
                Console.WriteLine($"[LLM] Context chunks: {request.Context.Count}");

                var response = await _ollamaService.GenerateResponseAsync(
                    request.Query,
                    request.Context.ToArray(),
                    request.SystemPrompt);

                return Ok(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LLM] Error: {ex.Message}");
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        // GET /api/llm/health
        [HttpGet("health")]
        public async Task<IActionResult> Health()
        {
            try
            {
                // Test Ollama connection
                var testResponse = await _ollamaService.GenerateResponseAsync(
                    "Hello",
                    new[] { "Test context" },
                    "You are a test assistant. Just say 'OK'.");

                return Ok(new
                {
                    Status = "Healthy",
                    OllamaConnected = true,
                    Model = "llama3.1:8b",
                    TestResponse = testResponse.Answer
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Status = "Unhealthy",
                    OllamaConnected = false,
                    Error = ex.Message
                });
            }
        }
    }
}