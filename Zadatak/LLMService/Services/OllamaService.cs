using System;
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
                Timeout = TimeSpan.FromMinutes(5) // Llama može biti spor
            };
        }

        /// <summary>
        /// Generiše odgovor koristeći Llama model preko Ollama API-ja
        /// </summary>
        public async Task<LLMResponse> GenerateResponseAsync(string query, string[] contextChunks, string systemPrompt = null)
        {
            // 1. Sastavi prompt
            var prompt = BuildPrompt(query, contextChunks, systemPrompt);

            Console.WriteLine($"[LLM] Sending prompt to Ollama (length: {prompt.Length} chars)");

            // 2. Pozovi Ollama API
            var requestBody = new
            {
                model = Model,
                prompt = prompt,
                stream = false // Ne koristimo streaming za sada
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(OllamaUrl, content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[LLM] Received response from Ollama");

            // 3. Parsiraj odgovor
            var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return new LLMResponse
            {
                Answer = ollamaResponse.Response?.Trim() ?? "No response generated",
                Model = Model,
                TokensUsed = ollamaResponse.EvalCount ?? 0
            };
        }

        /// <summary>
        /// Pravi strukturisan prompt sa kontekstom
        /// </summary>
        private string BuildPrompt(string query, string[] contextChunks, string systemPrompt)
        {
            var sb = new StringBuilder();

            // System prompt (default ili custom)
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                sb.AppendLine(systemPrompt);
            }
            else
            {
                sb.AppendLine("You are a helpful AI assistant specialized in analyzing regulatory and policy documents.");
                sb.AppendLine("Answer the user's question based ONLY on the provided context.");
                sb.AppendLine("If the answer is not in the context, say 'I cannot answer based on the provided context.'");
            }

            sb.AppendLine();

            // Kontekst (chunk-ovi iz retrieval service-a)
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

            // Korisnikov query
            sb.AppendLine($"Question: {query}");
            sb.AppendLine();
            sb.AppendLine("Answer:");

            return sb.ToString();
        }

        // Helper klasa za deserijalizaciju Ollama response-a
        private class OllamaResponse
        {
            public string Response { get; set; }
            public int? EvalCount { get; set; }
        }
    }
}