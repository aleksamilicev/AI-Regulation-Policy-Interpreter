using RetrievalService.Models;
using RetrievalService.Models.RetrievalService.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace RetrievalService.Services
{
    public class VectorSearchService
    {
        private readonly string _storageRoot;

        public VectorSearchService(string storageRoot)
        {
            _storageRoot = storageRoot;
            Console.WriteLine($"[VectorSearch] Using storage: {_storageRoot}");
        }

        /// <summary>
        /// Glavni search metod - pronalazi najsličnije chunk-ove
        /// </summary>
        public async Task<List<SearchResult>> SearchAsync(string query, string documentId, string versionId, int topK)
        {
            Console.WriteLine($"[Search] Query: {query}");

            // 1. Generiši embedding
            var queryEmbedding = GenerateQueryEmbedding(query);

            // 2. Učitaj chunk embeddings
            var chunkEmbeddings = await LoadAllChunkEmbeddingsAsync(documentId, versionId);

            // 3. Extract query keywords
            var queryLower = query.ToLower();
            var queryKeywords = queryLower
                .Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3)
                .ToHashSet();

            // 4. Detect query type
            bool isDefinitionQuery = queryLower.Contains("šta") || queryLower.Contains("sta") ||
                                     queryLower.Contains("defini") || queryLower.Contains("znači") ||
                                     queryLower.Contains("what") || queryLower.Contains("define");

            var results = new List<SearchResult>();

            foreach (var (chunkIndex, embedding, text) in chunkEmbeddings)
            {
                var textLower = text.ToLower();
                var score = CosineSimilarity(queryEmbedding, embedding);

                // KEYWORD MATCHING BOOST
                var keywordMatches = queryKeywords.Count(kw => textLower.Contains(kw));
                var keywordBoost = keywordMatches * 0.15f; // 15% per keyword

                // DEFINITION DETECTION BOOST
                float definitionBoost = 0f;
                if (isDefinitionQuery)
                {
                    // Proveri da li chunk sadrži definiciju
                    var hasDefinitionPattern =
                        textLower.Contains(" je ") ||
                        textLower.Contains(" su ") ||
                        textLower.Contains("predstavlja") ||
                        textLower.Contains("obuhvata") ||
                        textLower.Contains("označava") ||
                        System.Text.RegularExpressions.Regex.IsMatch(textLower, @"\d+\)\s+\w+.*\s+je\s+");

                    // Dodatni boost ako chunk počinje sa "Član 2." (često definicije)
                    var isDefinitionChapter = textLower.Contains("član 2.") || textLower.Contains("član 3.");

                    if (hasDefinitionPattern)
                        definitionBoost += 0.3f; // 30% boost

                    if (isDefinitionChapter)
                        definitionBoost += 0.2f; // dodatnih 20% boost

                    // Specifična detekcija za "IKT sistem"
                    if (queryKeywords.Contains("ikt") || queryKeywords.Contains("sistem"))
                    {
                        if (textLower.Contains("informaciono-komunikacioni sistem") ||
                            textLower.Contains("ikt sistem") && textLower.Contains("celina"))
                        {
                            definitionBoost += 0.5f; // Vrlo jak boost za IKT definiciju
                        }
                    }
                }

                // COMBINED SCORE
                var finalScore = Math.Min(1.0f, score + keywordBoost + definitionBoost);

                Console.WriteLine($"[Search] Chunk {chunkIndex}: base={score:F3}, kw={keywordBoost:F3}, def={definitionBoost:F3}, final={finalScore:F3}");

                results.Add(new SearchResult
                {
                    ChunkIndex = chunkIndex,
                    Text = text,
                    Score = finalScore
                });
            }

            // Sortiraj i vrati top K
            var topResults = results
                .OrderByDescending(r => r.Score)
                .Take(topK)
                .ToList();

            Console.WriteLine($"\n[Search] TOP {topK} RESULTS:");
            foreach (var r in topResults)
            {
                Console.WriteLine($"  Chunk #{r.ChunkIndex}: {r.Score:F3}");
            }

            return topResults;
        }

        /// <summary>
        /// Učitaj sve embeddings za dati dokument verziju
        /// </summary>
        private async Task<List<(int ChunkIndex, float[] Embedding, string Text)>> LoadAllChunkEmbeddingsAsync(string documentId, string versionId)
        {
            var results = new List<(int, float[], string)>();

            var embeddingsFolder = Path.Combine(_storageRoot, "embeddings", documentId, versionId);
            var parsedFolder = Path.Combine(_storageRoot, "parsed");

            Console.WriteLine($"[Search] Looking for embeddings in: {embeddingsFolder}");
            Console.WriteLine($"[Search] Directory exists: {Directory.Exists(embeddingsFolder)}");

            if (!Directory.Exists(embeddingsFolder))
            {
                Console.WriteLine($"[Search] ERROR: Embeddings folder not found!");
                return results;
            }

            // REFRESH: Učitaj fajlove svaki put iznova (bez cache-a)
            var embeddingFiles = Directory.GetFiles(embeddingsFolder, "chunk_*.json")
                .OrderBy(f => f)
                .ToList();

            Console.WriteLine($"[Search] Found {embeddingFiles.Count} embedding files");

            if (embeddingFiles.Count == 0)
            {
                Console.WriteLine($"[Search] ERROR: No chunk files found in folder!");
                return results;
            }

            // Učitaj parsed data
            var parsedFiles = Directory.GetFiles(parsedFolder, $"{versionId}.json");

            if (parsedFiles.Length == 0)
            {
                Console.WriteLine($"[Search] ERROR: Parsed file {versionId}.json not found in {parsedFolder}");
                return results;
            }

            var parsedFile = parsedFiles[0];
            Console.WriteLine($"[Search] Loading parsed data from: {parsedFile}");

            var parsedJson = await File.ReadAllTextAsync(parsedFile);
            var parsedData = JsonSerializer.Deserialize<ParsedData>(parsedJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            Console.WriteLine($"[Search] Parsed data loaded: {parsedData?.Chunks?.Length ?? 0} chunks");

            // Učitaj embeddings
            foreach (var file in embeddingFiles)
            {
                Console.WriteLine($"[Search] Reading embedding file: {Path.GetFileName(file)}");

                var json = await File.ReadAllTextAsync(file);
                var data = JsonSerializer.Deserialize<JsonElement>(json);

                var chunkIndex = data.GetProperty("ChunkIndex").GetInt32();
                var embeddingArray = data.GetProperty("Embedding");
                var embedding = JsonSerializer.Deserialize<float[]>(embeddingArray.GetRawText());

                var text = parsedData?.Chunks?.FirstOrDefault(c => c.Index == chunkIndex)?.Text ?? "";

                Console.WriteLine($"[Search] Chunk {chunkIndex}: {text.Length} chars, embedding: {embedding?.Length ?? 0} dims");

                if (embedding != null && !string.IsNullOrEmpty(text))
                {
                    results.Add((chunkIndex, embedding, text));
                }
            }

            Console.WriteLine($"[Search] Total loaded: {results.Count} chunks");
            return results;
        }

        /// <summary>
        /// Generiši embedding za query (mock implementacija)
        /// </summary>
        private float[] GenerateQueryEmbedding(string query)
        {
            // TODO: Kasnije integrisati sa pravim embedding modelom
            // Za sada koristimo mock koji je konzistentan sa DocumentService
            var hash = GetSimpleHash(query);
            var random = new Random(hash);

            var embedding = new float[384];
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] = (float)(random.NextDouble() * 2 - 1);
            }

            // Normalize
            var magnitude = (float)Math.Sqrt(embedding.Sum(x => x * x));
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= magnitude;
            }

            return embedding;
        }

        /// <summary>
        /// Cosine similarity između dva vektora
        /// </summary>
        private float CosineSimilarity(float[] a, float[] b)
        {
            if (a.Length != b.Length)
                throw new ArgumentException("Vectors must have same dimensions");

            float dotProduct = 0;
            float magnitudeA = 0;
            float magnitudeB = 0;

            for (int i = 0; i < a.Length; i++)
            {
                dotProduct += a[i] * b[i];
                magnitudeA += a[i] * a[i];
                magnitudeB += b[i] * b[i];
            }

            magnitudeA = (float)Math.Sqrt(magnitudeA);
            magnitudeB = (float)Math.Sqrt(magnitudeB);

            if (magnitudeA == 0 || magnitudeB == 0)
                return 0;

            return dotProduct / (magnitudeA * magnitudeB);
        }

        private int GetSimpleHash(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            int hash = 17;
            foreach (char c in text)
            {
                hash = hash * 31 + c;
            }
            return Math.Abs(hash);
        }
    }
}