using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DocumentService.Services
{
    public class EmbeddingService
    {
        private readonly HttpClient _httpClient;

        public EmbeddingService()
        {
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Генерише embedding vektor za dati tekst koristeći lokalni embedding model
        /// </summary>
        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            // TODO: Ovo će kasnije biti poziv ka lokalnom embedding servisu
            // Za sada vraćamo mock embedding (384 dimenzije - tipično za sentence-transformers)
            return GenerateMockEmbedding(text);
        }

        // Mock embedding generator (privremeno rešenje)
        private float[] GenerateMockEmbedding(string text)
        {
            // Generiši konzistentan embedding na osnovu teksta (za testiranje)
            var hash = GetSimpleHash(text);
            var random = new Random(hash);

            var embedding = new float[384]; // Standard dimenzija za sentence-transformers
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] = (float)(random.NextDouble() * 2 - 1); // [-1, 1]
            }

            // Normalize (optional, ali preporučljivo)
            var magnitude = (float)Math.Sqrt(embedding.Sum(x => x * x));
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= magnitude;
            }

            return embedding;
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

        // TODO: Kasnije implementirati sa pravim embedding modelom
        /*
        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            // Poziv ka lokalnom embedding serveru (npr. sentence-transformers preko HTTP API)
            var request = new
            {
                text = text
            };

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync("http://localhost:5000/embed", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<EmbeddingResponse>(json);
            
            return result.Embedding;
        }

        private class EmbeddingResponse
        {
            public float[] Embedding { get; set; }
        }
        */
    }
}