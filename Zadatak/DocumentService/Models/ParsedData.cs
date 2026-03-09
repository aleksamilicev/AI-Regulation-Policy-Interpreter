using DocumentService.Services;

namespace DocumentService.Models
{
    public class ParsedData
    {
        public string VersionId { get; set; }
        public ParsedChunk[] Chunks { get; set; }
    }
}
