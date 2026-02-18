using System.Collections.Generic;

namespace Client.Models
{
    public class SearchResponse
    {
        public string Query { get; set; }
        public string DocumentId { get; set; }
        public string VersionId { get; set; }
        public int TopK { get; set; }
        public int ResultsCount { get; set; }
        public List<SearchResult> Results { get; set; }
    }
}