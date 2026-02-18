namespace RetrievalService.Models
{
    public class SearchRequest
    {
        public string Query { get; set; }
        public string DocumentId { get; set; }
        public string VersionId { get; set; }
        public int TopK { get; set; } = 5; // Default: vrati top 5 chunks
    }
}
