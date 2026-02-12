using System;

namespace DocumentService.Models
{
    public class DocumentMetadata
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string FilePath { get; set; }
        public string Extension { get; set; }
        public DateTime UploadedAt { get; set; }
        public bool IsParsed { get; set; }
        public int CurrentVersion { get; set; }  // Trenutna verzija
        public DateTime CreatedAt { get; set; }  // Kada je dokument kreiran
    }
}