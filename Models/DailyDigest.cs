namespace NewsScraping.Models;

public class DailyDigest
{
    public int Id { get; set; }
    public DateTime RunTimestamp { get; set; }
    public string? GenreName { get; set; }
    public string? ArticleTitle { get; set; }
    public string? SummaryText { get; set; }
    public string? SourceUrl { get; set; }
    public string? SourceSite { get; set; }
    public int? RankInGenre { get; set; }
    public bool SendFlag { get; set; }
    public DateTime CreateDateTime { get; set; }
    public DateTime UpdateDateTime { get; set; }
}
