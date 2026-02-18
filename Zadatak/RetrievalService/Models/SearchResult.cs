namespace RetrievalService.Models
{
    namespace RetrievalService.Models
    {
        public class SearchResult
        {
            public int ChunkIndex { get; set; }
            public string Text { get; set; }
            public float Score { get; set; } // Cosine similarity score [0, 1]
        }
    }
}
