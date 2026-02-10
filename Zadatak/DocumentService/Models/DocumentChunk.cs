// Models/DocumentChunk.cs
using System;

namespace DocumentService.Models
{
    public class DocumentChunk
    {
        public Guid ChunkId { get; set; }
        public int ChunkIndex { get; set; }
        public string Text { get; set; }
        public int StartPosition { get; set; }
        public int EndPosition { get; set; }
    }
}