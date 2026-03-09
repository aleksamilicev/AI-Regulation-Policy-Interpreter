using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LLMService.Models
{
    public class Citation
    {
        public int ChunkIndex { get; set; }
        public string Quote { get; set; }
        public string Relevance { get; set; }
        public int Score { get; set; }
    }
}
