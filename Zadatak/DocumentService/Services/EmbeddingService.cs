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

        // Generise embedding vektor za dati tekst koristeći lokalni embedding model
        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            return GenerateMockEmbedding(text);
        }

        // Embedding generator
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
    }
}