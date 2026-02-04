using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocumentService.Models
{
    public class CreateDocumentRequest
    {
        public string Title { get; set; }
        public string Type { get; set; }
        public string RawText { get; set; }
        public DateTime ValidFrom { get; set; }
    }
}
