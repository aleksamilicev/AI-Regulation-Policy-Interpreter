using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LLMService.Models;

namespace LLMService.Services
{
    public class OllamaService
    {
        private readonly HttpClient _httpClient;
        private const string OllamaUrl = "http://localhost:11434/api/generate";
        private const string Model = "llama3.1:8b";

        public OllamaService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
        }

        public async Task<LLMResponse> GenerateResponseAsync(string query, string[] contextChunks, string systemPrompt = null)
        {
            // 1. Proveri da li ima dovoljno konteksta
            bool hasSufficientContext = HasSufficientContext(query, contextChunks);

            // 2. Sastavi enhanced prompt sa uputstvima za citiranje
            var prompt = BuildEnhancedPrompt(query, contextChunks, systemPrompt, hasSufficientContext);

            Console.WriteLine($"[LLM] Sending prompt to Ollama (length: {prompt.Length} chars)");
            Console.WriteLine($"[LLM] Sufficient context: {hasSufficientContext}");

            // 3. Pozovi Ollama API
            var requestBody = new
            {
                model = Model,
                prompt = prompt,
                stream = false
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(OllamaUrl, content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[LLM] Received response from Ollama");

            var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var answer = ollamaResponse.Response?.Trim() ?? "No response generated";

            // 4. Ekstraktuj citate iz odgovora
            var citations = ExtractCitations(answer, contextChunks);

            return new LLMResponse
            {
                Answer = answer,
                Model = Model,
                TokensUsed = ollamaResponse.EvalCount ?? 0,
                Citations = citations,
                HasSufficientContext = hasSufficientContext
            };
        }

        /// <summary>
        /// Proverava da li kontekst sadrži dovoljno informacija za odgovor
        /// </summary>
        private bool HasSufficientContext(string query, string[] contextChunks)
        {
            if (contextChunks == null || contextChunks.Length == 0)
                return false;

            // Proveri da li bar jedan chunk sadrži neke ključne reči iz query-ja
            var queryWords = query.ToLower()
                .Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3) // Ignoriši kratke reči
                .ToHashSet();

            if (queryWords.Count == 0)
                return true; // Ako nema ključnih reči, smatramo da je OK

            int chunksWithMatches = 0;
            foreach (var chunk in contextChunks)
            {
                var chunkLower = chunk.ToLower();
                int matches = queryWords.Count(word => chunkLower.Contains(word));

                if (matches >= Math.Max(1, queryWords.Count / 3)) // Bar 1/3 ključnih reči
                {
                    chunksWithMatches++;
                }
            }

            return chunksWithMatches > 0;
        }

        /// <summary>
        /// Pravi enhanced prompt sa instrukcijama za citiranje
        /// </summary>
        private string BuildEnhancedPrompt(string query, string[] contextChunks, string systemPrompt, bool hasSufficientContext)
        {
            var sb = new StringBuilder();

            // System prompt
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                sb.AppendLine(systemPrompt);
            }
            else
            {
                sb.AppendLine("You are a helpful AI assistant specialized in analyzing regulatory and policy documents.");
            }

            sb.AppendLine();
            sb.AppendLine("IMPORTANT INSTRUCTIONS:");
            sb.AppendLine("1. Answer the question ONLY based on the provided context.");
            sb.AppendLine("2. When you use information from a chunk, CITE it using [Chunk X] notation.");
            sb.AppendLine("3. If you quote directly, use quotation marks and cite the source.");

            if (!hasSufficientContext)
            {
                sb.AppendLine("4. WARNING: The context may not contain sufficient information. If you cannot answer confidently, explicitly state: 'Based on the provided context, I cannot provide a complete answer because [reason].'");
            }
            else
            {
                sb.AppendLine("4. Provide a clear and complete answer.");
            }

            sb.AppendLine();

            // Kontekst sa brojevima
            if (contextChunks != null && contextChunks.Length > 0)
            {
                sb.AppendLine("=== CONTEXT ===");
                for (int i = 0; i < contextChunks.Length; i++)
                {
                    sb.AppendLine($"[Chunk {i + 1}]");
                    sb.AppendLine(contextChunks[i]);
                    sb.AppendLine();
                }
                sb.AppendLine("=== END CONTEXT ===");
                sb.AppendLine();
            }

            // Query
            sb.AppendLine($"Question: {query}");
            sb.AppendLine();
            sb.AppendLine("Answer (with citations):");

            return sb.ToString();
        }

        /// <summary>
        /// Ekstraktuje citacije iz LLM odgovora
        /// </summary>
        private List<Citation> ExtractCitations(string answer, string[] contextChunks)
        {
            var citations = new List<Citation>();

            if (contextChunks == null || contextChunks.Length == 0)
                return citations;

            // Traži [Chunk X] reference u odgovoru
            for (int i = 0; i < contextChunks.Length; i++)
            {
                var chunkNum = i + 1;
                var citationPattern = $"[Chunk {chunkNum}]";

                if (answer.Contains(citationPattern, StringComparison.OrdinalIgnoreCase))
                {
                    // Pronađi relevantan citat iz chunk-a
                    var quote = ExtractRelevantQuote(contextChunks[i], 150);

                    citations.Add(new Citation
                    {
                        ChunkIndex = i,
                        Quote = quote,
                        Relevance = $"Referenced as [Chunk {chunkNum}] in answer"
                    });
                }
            }

            return citations;
        }

        /// <summary>
        /// Ekstraktuje relevantan citat iz chunk-a (prvih N karaktera)
        /// </summary>
        private string ExtractRelevantQuote(string chunk, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(chunk))
                return "";

            chunk = chunk.Trim();

            if (chunk.Length <= maxLength)
                return chunk;

            // Uzmi prvih maxLength karaktera i završi na celoj reči
            var truncated = chunk.Substring(0, maxLength);
            var lastSpace = truncated.LastIndexOf(' ');

            if (lastSpace > 0)
                truncated = truncated.Substring(0, lastSpace);

            return truncated + "...";
        }

        private class OllamaResponse
        {
            public string Response { get; set; }
            public int? EvalCount { get; set; }
        }
    }
}