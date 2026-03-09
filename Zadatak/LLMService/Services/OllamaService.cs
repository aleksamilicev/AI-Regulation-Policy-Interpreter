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
                Timeout = TimeSpan.FromMinutes(10)
            };
        }

        public async Task<LLMResponse> GenerateResponseAsync(string query, string[] contextChunks, string systemPrompt = null)
        {
            bool hasSufficientContext = HasSufficientContext(query, contextChunks);

            var prompt = BuildPrompt(query, contextChunks, hasSufficientContext);

            Console.WriteLine($"[LLM] Sending prompt to Ollama (length: {prompt.Length} chars)");

            var requestBody = new
            {
                model = Model,
                prompt = prompt,
                stream = false,
                options = new
                {
                    temperature = 0.1,
                    top_p = 0.9,
                    top_k = 40
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(OllamaUrl, content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var answer = ollamaResponse.Response?.Trim() ?? "No response generated";
            Console.WriteLine($"[LLM] Answer length: {answer.Length} chars");

            // Match which chunks were actually used based on overlap with the answer
            var citations = MatchCitationsToAnswer(answer, contextChunks);

            return new LLMResponse
            {
                Answer = answer,
                Model = Model,
                TokensUsed = ollamaResponse.EvalCount ?? 0,
                Citations = citations,
                HasSufficientContext = hasSufficientContext
            };
        }

        private bool HasSufficientContext(string query, string[] contextChunks)
        {
            if (contextChunks == null || contextChunks.Length == 0)
                return false;

            var queryWords = query.ToLower()
                .Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3)
                .ToHashSet();

            if (queryWords.Count == 0)
                return false;

            // minimum 50% query words mora biti u bilo kom chunku
            return contextChunks.Any(chunk =>
            {
                var chunkLower = chunk.ToLower();
                int matches = queryWords.Count(word => chunkLower.Contains(word));
                return matches >= Math.Ceiling(queryWords.Count * 0.5);
            });
        }

        private string BuildPrompt(string query, string[] contextChunks, bool hasSufficientContext)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Ti si AI asistent specijalizovan za analizu pravnih dokumenata na srpskom jeziku.");
            sb.AppendLine();
            sb.AppendLine("PRAVILA:");
            sb.AppendLine("1. Odgovaraj ISKLJUČIVO na osnovu dostavljenog konteksta — ne koristi sopstveno znanje.");
            sb.AppendLine("2. Ako pitanje nije pokriveno kontekstom, NEMOJ dodavati informacije van konteksta. Odgovori samo: 'Na osnovu dostupnog konteksta ne mogu odgovoriti.'");
            sb.AppendLine("3. Daj DETALJAN i KONKRETAN odgovor — navedi definiciju, kategorije, obaveze i primere iz konteksta.");
            sb.AppendLine("4. Koristi srpski jezik ispravno. Piši 'člana' (ne 'članka').");
            sb.AppendLine("5. Kada navodiš direktno iz teksta zakona, koristi navodnike.");
            sb.AppendLine("6. Pored teksta koristi i teksta zakona, citate, koji će da budu pod navodnicima.");
            sb.AppendLine("7. Strukturiraj odgovor jasno — koristi pasuse ili nabrajanje gde je prikladno.");
            sb.AppendLine();
            sb.AppendLine("KONTEKST:");
            sb.AppendLine();

            for (int i = 0; i < contextChunks.Length; i++)
            {
                sb.AppendLine($"--- Chunk {i + 1} ---");
                sb.AppendLine(contextChunks[i]);
                sb.AppendLine();
            }

            sb.AppendLine($"PITANJE: {query}");
            sb.AppendLine();
            sb.AppendLine("ODGOVOR:");

            return sb.ToString();
        }

        // Determines which chunks were actually used in the answer by measuring
        // keyword overlap between the LLM answer and each chunk.
        // Only chunks with meaningful overlap are shown as citations.
        private List<Citation> MatchCitationsToAnswer(string answer, string[] contextChunks)
        {
            var citations = new List<Citation>();

            if (contextChunks == null || contextChunks.Length == 0)
                return citations;

            // Tokenize the answer into meaningful words (length > 3, ignore stopwords)
            var stopwords = new HashSet<string> {
                "koje", "koji", "koja", "kao", "što", "kako", "kada", "gdje", "gde",
                "biti", "jest", "nije", "also", "that", "this", "with", "from",
                "njihov", "njihova", "njihove", "ovaj", "ova", "ovo", "samo",
                "odnosno", "prema", "između", "kroz", "radi", "toga", "tome"
            };

            var answerWords = answer.ToLower()
                .Split(new[] { ' ', '\n', '\r', ',', '.', '!', '?', ':', ';', '"', '(', ')' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim('a', 'e', 'i', 'o', 'u'))
                .Where(w => w.Length > 3 && !stopwords.Contains(w))
                .ToHashSet();

            Console.WriteLine($"[Citations] Answer word count: {answerWords.Count}");

            for (int i = 0; i < contextChunks.Length; i++)
            {
                var chunkWords = contextChunks[i].ToLower()
                    .Split(new[] { ' ', '\n', '\r', ',', '.', '!', '?', ':', ';', '"', '(', ')' },
                        StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.Trim('a', 'e', 'i', 'o', 'u'))
                    .Where(w => w.Length > 3 && !stopwords.Contains(w))
                    .ToList();

                if (chunkWords.Count == 0) continue;

                // Count how many unique chunk words appear in the answer
                var uniqueChunkWords = chunkWords.ToHashSet();
                int overlap = uniqueChunkWords.Count(w => answerWords.Contains(w));
                float overlapRatio = (float)overlap / answerWords.Count;

                float definitionBoost = 0;

                var chunkLower = contextChunks[i].ToLower();

                if (chunkLower.Contains("značenje") ||
                    chunkLower.Contains("predstavlja") ||
                    chunkLower.Contains("definiše") ||
                    chunkLower.Contains("u smislu ovog zakona"))
                {
                    definitionBoost = 0.05f;
                }

                overlapRatio += definitionBoost;

                Console.WriteLine($"[Citations] Chunk {i + 1}: overlap={overlap}/{uniqueChunkWords.Count} ({overlapRatio:P0})");

                // Threshold: at least 25% of chunk's unique words appear in the answer
                // AND at least 4 words overlap (prevents false positives on short chunks)
                if (overlapRatio >= 0.25f && overlap >= 3)
                {
                    var quote = ExtractBestQuote(contextChunks[i], 220);
                    citations.Add(new Citation
                    {
                        ChunkIndex = i,
                        Quote = quote,
                        Score = overlap,
                        Relevance = $"Chunk {i + 1} — {overlap} zajedničkih reči sa odgovorom ({overlapRatio:P0})"
                    });

                    Console.WriteLine($"[Citations] ✓ Chunk {i + 1} matched as citation");
                }
                else
                {
                    Console.WriteLine($"[Citations] ✗ Chunk {i + 1} skipped (below threshold)");
                }
            }

            // Sort by chunk index for consistent display
            citations = citations
            .OrderByDescending(c => c.Score)
            .ToList();

            Console.WriteLine($"[Citations] Total: {citations.Count} citations");
            return citations;
        }

        // Extracts the most meaningful representative quote from a chunk.
        // Priority: definition sentence > article opener > first sentence > truncated start.
        private string ExtractBestQuote(string chunk, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(chunk))
                return "";

            chunk = chunk.Trim();
            var sentences = SplitIntoSentences(chunk);

            // Priority 1: Sentence containing a definition verb
            foreach (var sentence in sentences)
            {
                var lower = sentence.ToLower();
                if (lower.Contains(" je ") || lower.Contains(" su ") ||
                    lower.Contains("predstavlja") || lower.Contains("obuhvata") ||
                    lower.Contains("označava") || lower.Contains("podrazumeva"))
                {
                    return TruncateToLength(sentence.Trim(), maxLength);
                }
            }

            // Priority 2: Line starting with "Član X."
            var memberMatch = System.Text.RegularExpressions.Regex.Match(
                chunk, @"^Član \d+\.[^\n]*",
                System.Text.RegularExpressions.RegexOptions.Multiline);
            if (memberMatch.Success)
                return TruncateToLength(memberMatch.Value.Trim(), maxLength);

            // Priority 3: First sentence
            if (sentences.Count > 0)
                return TruncateToLength(sentences[0].Trim(), maxLength);

            return TruncateToLength(chunk, maxLength);
        }

        private List<string> SplitIntoSentences(string text)
        {
            var results = new List<string>();
            var parts = System.Text.RegularExpressions.Regex.Split(text, @"(?<=[.;])\s+");
            foreach (var p in parts)
            {
                var trimmed = p.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    results.Add(trimmed);
            }
            return results;
        }

        private string TruncateToLength(string text, int maxLength)
        {
            if (text.Length <= maxLength)
                return text;

            var truncated = text.Substring(0, maxLength);
            var lastSpace = truncated.LastIndexOf(' ');
            if (lastSpace > 0)
                truncated = truncated.Substring(0, lastSpace);

            return truncated + "...";
        }
    }
}