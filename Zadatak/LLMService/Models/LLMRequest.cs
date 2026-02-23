using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LLMService.Models
{
    public class LLMRequest
    {
        public string Query { get; set; }
        public List<string> Context { get; set; } // Chunk-ovi iz Retrieval Service-a
        public string SystemPrompt { get; set; } // Opciono
    }
}
