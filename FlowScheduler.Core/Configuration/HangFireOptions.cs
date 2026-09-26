namespace FlowScheduler.Core.Configuration {
    public class HangFireOptions {
        public string ConnectionString { get; set; }
        public string Version { get; set; }
        public OpenAI OpenAI { get; set; }
        public Telegram Telegram { get; set; }
        public Ollama Ollama { get; set; }
    }
    public class OpenAI {
        public string Provider { get; set; }
        public string ApiKey { get; set; }
        public bool GenerateContentEnabled { get; set; }
        public string GenerateContentModelId { get; set; }
        public string GenerateContentEndpoint { get; set; }
        public string EmbeddingModelId { get; set; }
        public string EmbeddingEndpoint { get; set; }
        public int EmbeddingDimension { get; set; }
        public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromSeconds(60);
        public int EvalMaxParallelism { get; set; } = 1;
    }
    public class Telegram {
        public string BotChatId { get; set; }
        public string BotToken { get; set; }
    }
    public class Ollama {
        public string Model { get; set; }
        public string Host { get; set; }
        public string Port { get; set; }
    }
}