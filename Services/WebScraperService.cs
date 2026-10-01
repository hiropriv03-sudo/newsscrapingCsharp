using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using NewsScraping.Models;
using NewsScraping.Utils;
using System.Text.RegularExpressions;

namespace NewsScraping.Services;

public class WebScraperService
{
    private readonly ILogger<WebScraperService> _logger;
    private readonly HttpClient _httpClient;

    public WebScraperService(ILogger<WebScraperService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        );
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    /// <summary>
    /// HTML ページから記事一覧を抽出する (HtmlAgilityPack による高速パース)
    /// </summary>
    public async Task<List<ScrapedArticle>> ScrapeFromWebAsync(
        string url,
        string category,
        string sourceName,
        int limit = 5,
        CancellationToken ct = default)
    {
        var articles = new List<ScrapedArticle>();
        _logger.LogInformation("[Web] HTML 抽出開始: {Name} ({Url})", sourceName, url);

        try
        {
            var html = await _httpClient.GetStringAsync(url, ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // 1. 主要な記事コンテナやリンクの候補を検索
            var linkNodes = doc.DocumentNode.SelectNodes("//a[@href and (h1 or h2 or h3 or string-length(normalize-space(text())) > 15)]");
            if (linkNodes == null || !linkNodes.Any())
            {
                linkNodes = doc.DocumentNode.SelectNodes("//article//a[@href]");
            }

            var baseUri = new Uri(url);
            var seenUrls = new HashSet<string>();
            int rank = 1;

            if (linkNodes != null)
            {
                foreach (var node in linkNodes)
                {
                    if (articles.Count >= limit) break;

                    var href = node.GetAttributeValue("href", string.Empty);
                    if (string.IsNullOrWhiteSpace(href) || href.StartsWith("#") || href.StartsWith("javascript:"))
                    {
                        continue;
                    }

                    if (!Uri.TryCreate(baseUri, href, out var absoluteUri))
                    {
                        continue;
                    }

                    var articleUrl = absoluteUri.ToString();
                    if (!seenUrls.Add(articleUrl))
                    {
                        continue;
                    }

                    var rawTitle = node.InnerText;
                    var title = HtmlSanitizer.Clean(rawTitle);
                    if (string.IsNullOrWhiteSpace(title) || title.Length < 6)
                    {
                        continue;
                    }

                    articles.Add(new ScrapedArticle
                    {
                        Title = title,
                        Url = articleUrl,
                        Summary = string.Empty, // 後続で必要に応じて取得またはLLM要約
                        Category = category,
                        SourceSite = sourceName,
                        RankInGenre = rank++
                    });
                }
            }

            _logger.LogInformation("[Web] 抽出完了: {Name} ({Count} 件)", sourceName, articles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Web] スクレイピングエラー ({Name}: {Url})", sourceName, url);
        }
        return articles;
    }

    /// <summary>
    /// 記事詳細ページから本文テキストを抽出
    /// </summary>
    public async Task<string> FetchArticleTextAsync(string url, CancellationToken ct = default)
    {
        try
        {
            var html = await _httpClient.GetStringAsync(url, ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var noiseNodes = doc.DocumentNode.SelectNodes("//script|//style|//nav|//header|//footer|//aside|//form");
            if (noiseNodes != null)
            {
                foreach (var node in noiseNodes)
                {
                    node.Remove();
                }
            }

            var paragraphs = doc.DocumentNode.SelectNodes("//article//p") 
                          ?? doc.DocumentNode.SelectNodes("//main//p") 
                          ?? doc.DocumentNode.SelectNodes("//p");
            if (paragraphs != null)
            {
                var texts = paragraphs
                    .Select(p => HtmlSanitizer.Clean(p.InnerText))
                    .Where(t => t.Length > 20);
                var combined = string.Join("\n", texts);
                if (!string.IsNullOrWhiteSpace(combined))
                {
                    return combined.Length > 2000 ? combined[..2000] : combined;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Web] 記事本文取得スキップ ({Url}): {Message}", url, ex.Message);
        }
        return string.Empty;
    }
}
