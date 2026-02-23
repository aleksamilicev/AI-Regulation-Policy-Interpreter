namespace Client.Models
{
    public class LLMRequest
    {
        public string Query { get; set; }
        public List<string> Context { get; set; }
        public string SystemPrompt { get; set; }
    }
}
