using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocumentService.Models
{
    public class DocumentVersion
    {
        public Guid VersionId { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public string RawText { get; set; }
        public DateTime UploadedAt { get; set; }
    }
}
