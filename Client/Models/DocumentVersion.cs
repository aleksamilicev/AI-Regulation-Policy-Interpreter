using System;

namespace Client.Models
{
    public class DocumentVersion
    {
        public string VersionId { get; set; }
        public string DocumentId { get; set; }
        public int VersionNumber { get; set; }
        public string Extension { get; set; }
        public DateTime UploadedAt { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public bool IsParsed { get; set; }
    }
}