namespace Client.Models
{
    public class LLMResponse
    {
        public string Answer { get; set; }
        public string Model { get; set; }
        public int TokensUsed { get; set; }
        public List<Citation> Citations { get; set; }
        public bool HasSufficientContext { get; set; }
    }
}
