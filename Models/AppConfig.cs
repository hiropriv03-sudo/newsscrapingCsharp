using YamlDotNet.Serialization;

namespace NewsScraping.Models;

public class AppConfig
{
    [YamlMember(Alias = "database")]
    public DatabaseConfig Database { get; set; } = new();

    [YamlMember(Alias = "llm")]
    public LlmConfig Llm { get; set; } = new();

    [YamlMember(Alias = "browser")]
    public BrowserConfig Browser { get; set; } = new();

    [YamlMember(Alias = "performance")]
    public PerformanceConfig Performance { get; set; } = new();

    [YamlMember(Alias = "news_sources")]
    public List<NewsSourceConfig> NewsSources { get; set; } = new();
}

public class DatabaseConfig
{
    [YamlMember(Alias = "host")]
    public string Host { get; set; } = "localhost";

    [YamlMember(Alias = "port")]
    public string Port { get; set; } = "5433";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "postgres";

    [YamlMember(Alias = "user")]
    public string User { get; set; } = "postgres";

    [YamlMember(Alias = "password")]
    public string Password { get; set; } = "kanon";

    [YamlMember(Alias = "schema")]
    public string Schema { get; set; } = "newslist";

    public string BuildConnectionString(string? overrideHost = null)
    {
        var h = overrideHost ?? Host;
        return $"Host={h};Port={Port};Database={Name};Username={User};Password={Password};Timeout=5;Command Timeout=30;";
    }
}

public class LlmConfig
{
    [YamlMember(Alias = "url")]
    public string Url { get; set; } = "http://localhost:11434";

    [YamlMember(Alias = "model")]
    public string Model { get; set; } = "gemma4-e4b:latest";

    [YamlMember(Alias = "num_ctx")]
    public int NumCtx { get; set; } = 4096;

    [YamlMember(Alias = "timeout_seconds")]
    public int TimeoutSeconds { get; set; } = 45;
}

public class BrowserConfig
{
    [YamlMember(Alias = "headless")]
    public bool Headless { get; set; } = true;

    [YamlMember(Alias = "wait_time")]
    public double WaitTime { get; set; } = 0.1;
}

public class PerformanceConfig
{
    [YamlMember(Alias = "parallel_limit")]
    public int ParallelLimit { get; set; } = 2;
}

public class NewsSourceConfig
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "rss")]
    public string Rss { get; set; } = string.Empty;

    [YamlMember(Alias = "category")]
    public string Category { get; set; } = string.Empty;
}
