using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocumentService.Models
{
    public class DocumentMetadata
    {
        public Guid DocumentId { get; set; }
        public string Title { get; set; }
        public string Type { get; set; } // "law" or "policy"
        public DateTime CreatedAt { get; set; }
    }
}
