using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LLMService.Models
{
    public class OllamaResponse
    {
        public string Response { get; set; }
        public int? EvalCount { get; set; }
    }
}
