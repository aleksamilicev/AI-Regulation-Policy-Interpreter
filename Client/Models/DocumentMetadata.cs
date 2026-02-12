namespace Client.Models
{
    public class DocumentMetadata
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public int CurrentVersion { get; set; }
        public string Status { get; set; }
        public string FilePath { get; set; }
        public string Extension { get; set; }
        public DateTime UploadedAt { get; set; }
        public bool IsParsed { get; set; }
    }
}
