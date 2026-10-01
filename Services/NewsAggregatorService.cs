using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NewsScraping.Models;

namespace NewsScraping.Services;

public class NewsAggregatorService
{
    private readonly AppConfig _config;
    private readonly DatabaseService _dbService;
    private readonly RssCollectorService _rssCollector;
    private readonly WebScraperService _webScraper;
    private readonly LlmSummarizerService _llmService;
    private readonly ILogger<NewsAggregatorService> _logger;

    public NewsAggregatorService(
        AppConfig config,
        DatabaseService dbService,
        RssCollectorService rssCollector,
        WebScraperService webScraper,
        LlmSummarizerService llmService,
        ILogger<NewsAggregatorService> logger)
    {
        _config = config;
        _dbService = dbService;
        _rssCollector = rssCollector;
        _webScraper = webScraper;
        _llmService = llmService;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("=== ニュース集約処理 開始 (C#版) ===");

        var sources = _config.NewsSources;
        if (sources == null || !sources.Any())
        {
            _logger.LogWarning("ニュースソースが設定されていません。config.yaml を確認してください。");
            return;
        }

        var parallelLimit = Math.Max(1, _config.Performance.ParallelLimit);
        using var semaphore = new SemaphoreSlim(parallelLimit, parallelLimit);

        var tasks = sources.Select(async source =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await ProcessSourceAsync(source, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        sw.Stop();
        _logger.LogInformation("=== 全工程終了: {Elapsed:F1}秒 ===", sw.Elapsed.TotalSeconds);
    }

    private async Task ProcessSourceAsync(NewsSourceConfig source, CancellationToken ct)
    {
        var name = source.Name;
        var url = source.Rss;
        var category = source.Category;
        var runTimestamp = DateTime.UtcNow;

        _logger.LogInformation("== 処理開始: {Name} ({Category}) ==", name, category);

        // 1. 記事収集 (RSS優先、必要に応じてWebスクレイピングにフォールバック)
        List<ScrapedArticle> articles;
        bool isFeedUrl = url.Contains(".rss", StringComparison.OrdinalIgnoreCase)
                      || url.Contains(".xml", StringComparison.OrdinalIgnoreCase)
                      || url.Contains("feed", StringComparison.OrdinalIgnoreCase)
                      || url.Contains("/rss", StringComparison.OrdinalIgnoreCase)
                      || url.Contains(".rdf", StringComparison.OrdinalIgnoreCase);

        if (isFeedUrl)
        {
            articles = await _rssCollector.CollectFromRssAsync(url, category, name, limit: 5, ct);
            if (!articles.Any())
            {
                _logger.LogInformation("  {Name}: RSS取得件数0。Webフォールバックを試行します。", name);
                articles = await _webScraper.ScrapeFromWebAsync(url, category, name, limit: 5, ct);
            }
        }
        else
        {
            articles = await _webScraper.ScrapeFromWebAsync(url, category, name, limit: 5, ct);
        }

        if (!articles.Any())
        {
            _logger.LogWarning("  {Name}: 記事取得なし", name);
            return;
        }

        // 2. 記事ごとの重複チェック・要約・保存
        int savedCount = 0;
        int skippedCount = 0;

        foreach (var article in articles)
        {
            try
            {
                // 3日間の重複防止チェック (タイトルまたはURLで過去N日以内の重複を検知しDB登録スキップ)
                var prohibitionDays = _config.Duplication?.ProhibitionDays ?? 3;
                var isDup = await _dbService.IsArticleDuplicateAsync(article.Title, article.Url, prohibitionDays, ct);
                if (isDup)
                {
                    _logger.LogInformation("  {Name}: スキップ({Days}日以内重複) - {Title}", name, prohibitionDays, Truncate(article.Title, 30));
                    skippedCount++;
                    continue;
                }

                // 要約生成
                var summary = await _llmService.SummarizeArticleAsync(article.Title, article.Summary, ct);

                // DB 保存
                var inserted = await _dbService.InsertArticleAsync(
                    runTimestamp,
                    category,
                    article.Title,
                    summary,
                    article.Url,
                    name,
                    article.RankInGenre,
                    ct
                );

                if (inserted)
                {
                    _logger.LogInformation("  {Name}: 保存成功 - {Title}", name, Truncate(article.Title, 25));
                    savedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "  {Name}: 記事処理エラー ({Title})", name, article.Title);
            }
        }

        _logger.LogInformation("== 処理完了: {Name} (保存: {Saved}件, スキップ: {Skipped}件) ==", name, savedCount, skippedCount);
    }

    private static string Truncate(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLen) return text;
        return text[..maxLen] + "...";
    }
}
