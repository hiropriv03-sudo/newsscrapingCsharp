namespace NewsScraping.Models;

public class ScrapedArticle
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string SourceSite { get; set; } = string.Empty;
    public int RankInGenre { get; set; } = 1;
}
