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
            Console.WriteLine($"[Search] DocumentId: {documentId}");
            Console.WriteLine($"[Search] VersionId: {versionId}");
            Console.WriteLine($"[Search] Storage root: {_storageRoot}");

            // 1. Generiši embedding za query
            var queryEmbedding = GenerateQueryEmbedding(query);
            Console.WriteLine($"[Search] Query embedding generated: {queryEmbedding.Length} dims");

            // 2. Učitaj sve chunk embeddings
            var chunkEmbeddings = await LoadAllChunkEmbeddingsAsync(documentId, versionId);
            Console.WriteLine($"[Search] Loaded {chunkEmbeddings.Count} chunk embeddings");

            // 3. Izračunaj cosine similarity
            var results = new List<SearchResult>();

            foreach (var (chunkIndex, embedding, text) in chunkEmbeddings)
            {
                var score = CosineSimilarity(queryEmbedding, embedding);

                results.Add(new SearchResult
                {
                    ChunkIndex = chunkIndex,
                    Text = text,
                    Score = score
                });
            }

            // 4. Sortiraj po score-u i vrati top K
            return results
                .OrderByDescending(r => r.Score)
                .Take(topK)
                .ToList();
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

        // Helper classes za deserijalizaciju
        private class ParsedData
        {
            public ParsedChunk[] Chunks { get; set; }
        }

        private class ParsedChunk
        {
            public int Index { get; set; }
            public string Text { get; set; }
        }
    }
}