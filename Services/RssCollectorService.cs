using CodeHollow.FeedReader;
using Microsoft.Extensions.Logging;
using NewsScraping.Models;
using NewsScraping.Utils;

namespace NewsScraping.Services;

public class RssCollectorService
{
    private readonly ILogger<RssCollectorService> _logger;
    private readonly HttpClient _httpClient;

    public RssCollectorService(ILogger<RssCollectorService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        );
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    /// <summary>
    /// 指定されたRSS/AtomフィードURLから記事一覧を収集する
    /// </summary>
    public async Task<List<ScrapedArticle>> CollectFromRssAsync(
        string rssUrl,
        string category,
        string sourceName,
        int limit = 5,
        CancellationToken ct = default)
    {
        var articles = new List<ScrapedArticle>();
        _logger.LogInformation("[RSS] フィード取得中: {Name} ({Url})", sourceName, rssUrl);

        try
        {
            // 1. HTTP 経由でフィード文字列を取得
            var content = await _httpClient.GetStringAsync(rssUrl, ct);
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("[RSS] フィード内容が空です: {Url}", rssUrl);
                return articles;
            }

            // 2. FeedReader でパース
            var feed = FeedReader.ReadFromString(content);
            if (feed == null || feed.Items == null || !feed.Items.Any())
            {
                _logger.LogWarning("[RSS] 記事が見つかりませんでした: {Url}", rssUrl);
                return articles;
            }

            int rank = 1;
            foreach (var item in feed.Items.Take(limit))
            {
                var title = HtmlSanitizer.Clean(item.Title);
                if (string.IsNullOrWhiteSpace(title) || title == "No Title")
                {
                    continue;
                }

                var url = item.Link ?? string.Empty;
                var rawSummary = item.Description ?? item.Content ?? string.Empty;
                var summary = HtmlSanitizer.Clean(rawSummary);

                articles.Add(new ScrapedArticle
                {
                    Title = title,
                    Url = url,
                    Summary = summary,
                    Category = category,
                    SourceSite = sourceName,
                    RankInGenre = rank++
                });
            }

            _logger.LogInformation("[RSS] 取得成功: {Name} ({Count} 件)", sourceName, articles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RSS] フィード取得/パースエラー ({Name}: {Url})", sourceName, rssUrl);
        }

        return articles;
    }
}
