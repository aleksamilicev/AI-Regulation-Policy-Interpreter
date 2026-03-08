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
                stream = false,
                options = new
                {
                    temperature = 0.1,  // NISKA temperatura = manje kreativnosti, više preciznosti
                    top_p = 0.9,
                    top_k = 40
                }
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

            sb.AppendLine("Ti si AI asistent specijalizovan za analizu pravnih dokumenata.");
            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine("STROGA PRAVILA - OBAVEZNO POŠTUJ:");
            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine("1. ODGOVARAJ ISKLJUČIVO NA OSNOVU DOSTAVLJENOG KONTEKSTA");
            sb.AppendLine("   - NE koristi svoje predznanje");
            sb.AppendLine("   - NE izmišljaj informacije");
            sb.AppendLine("   - Ako odgovor nije u kontekstu: 'Na osnovu dostupnog konteksta ne mogu odgovoriti.'");
            sb.AppendLine();
            sb.AppendLine("2. CITIRANJE:");
            sb.AppendLine("   - UVEK navedi [Chunk X] odmah nakon informacije");
            sb.AppendLine("   - Primer: IKT sistem je tehnološka celina [Chunk 1].");
            sb.AppendLine();
            sb.AppendLine("3. CITATI:");
            sb.AppendLine("   - Kada citiraš direktno, koristi navodnike");
            sb.AppendLine("   - Drži citate kratkim (max 15-20 reči)");
            sb.AppendLine("   - Primer: Prema zakonu, IKT sistem je \"tehnološko-organizaciona celina\" [Chunk 1].");
            sb.AppendLine();
            sb.AppendLine("4. ZA PITANJA SA 'ŠTA JE' ili 'ŠTA SU':");
            sb.AppendLine("   - Prvo pronađi DEFINICIJU u kontekstu");
            sb.AppendLine("   - Citiraj TAČNU definiciju iz propisa");
            sb.AppendLine("   - Ne parafraziraj ako postoji direktna definicija");
            sb.AppendLine();
            sb.AppendLine("5. ODGOVARAJ NA SRPSKOM JEZIKU");
            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine("KONTEKST (sortirano po relevantnosti):");
            sb.AppendLine();

            for (int i = 0; i < contextChunks.Length; i++)
            {
                sb.AppendLine($"[Chunk {i + 1}]");
                sb.AppendLine("───────────────────────────────────────────────────────");
                sb.AppendLine(contextChunks[i]);
                sb.AppendLine("───────────────────────────────────────────────────────");
                sb.AppendLine();
            }

            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine($"PITANJE: {query}");
            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine("ODGOVOR (sa citatima i referencama):");

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

            // Pattern 1: Ako chunk sadrži definiciju (npr. "je" ili "predstavlja")
            var definitionPatterns = new[]
            {
        @"(\w+\s+(?:je|predstavlja|su|označava|obuhvata))\s+([^.;]+)",
        @"(\d+\)\s*\w+[^-]+-[^-]+-\w+\s+\([^\)]+\))\s+je\s+([^;]+)"
    };

            foreach (var pattern in definitionPatterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(chunk, pattern);
                if (match.Success && match.Length <= maxLength)
                {
                    // Vraća definiciju (npr. "IKT sistem je tehnološko-organizaciona celina koja...")
                    var quote = match.Value.Trim();
                    if (quote.Length > maxLength)
                    {
                        quote = quote.Substring(0, maxLength);
                        var lastSpace = quote.LastIndexOf(' ');
                        if (lastSpace > 0)
                            quote = quote.Substring(0, lastSpace);
                        quote += "...";
                    }
                    return quote;
                }
            }

            // Pattern 2: Ako chunk počinje sa "Član X."
            var memberPattern = new System.Text.RegularExpressions.Regex(@"^Član \d+\.\s*");
            var memberMatch = memberPattern.Match(chunk);

            if (memberMatch.Success)
            {
                // Uzmi prvu rečenicu nakon "Član X."
                var afterMember = chunk.Substring(memberMatch.Length).Trim();

                // Pronađi prvu tačku
                var firstPeriod = afterMember.IndexOf('.');
                if (firstPeriod > 0 && firstPeriod <= maxLength)
                {
                    return memberMatch.Value.TrimEnd() + " " + afterMember.Substring(0, firstPeriod + 1);
                }

                // Ako je prva rečenica predugačka, uzmi maxLength
                var preview = afterMember.Length <= maxLength
                    ? afterMember
                    : afterMember.Substring(0, maxLength);

                var lastSpace = preview.LastIndexOf(' ');
                if (lastSpace > 0)
                    preview = preview.Substring(0, lastSpace);

                return $"{memberMatch.Value.TrimEnd()} {preview}...";
            }

            // Fallback: Prva rečenica ili prvih maxLength karaktera
            var firstSentence = chunk.IndexOf('.');
            if (firstSentence > 0 && firstSentence <= maxLength)
            {
                return chunk.Substring(0, firstSentence + 1);
            }

            if (chunk.Length <= maxLength)
                return chunk;

            var truncated = chunk.Substring(0, maxLength);
            var lastSpaceIndex = truncated.LastIndexOf(' ');

            if (lastSpaceIndex > 0)
                truncated = truncated.Substring(0, lastSpaceIndex);

            return truncated + "...";
        }

        private class OllamaResponse
        {
            public string Response { get; set; }
            public int? EvalCount { get; set; }
        }
    }
}