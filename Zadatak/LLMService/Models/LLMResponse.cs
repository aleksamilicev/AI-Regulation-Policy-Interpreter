using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LLMService.Models
{
    public class LLMResponse
    {
        public string Answer { get; set; }
        public string Model { get; set; }
        public int TokensUsed { get; set; }
    }
}
