namespace Client.Models
{
    public class SearchResult
    {
        public int ChunkIndex { get; set; }
        public string Text { get; set; }
        public float Score { get; set; }
    }
}