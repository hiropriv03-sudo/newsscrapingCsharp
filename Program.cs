using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewsScraping.Models;
using NewsScraping.Services;
using NewsScraping.Utils;
using Serilog;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NewsScraping;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // 1. ログディレクトリ & Serilog の設定
        var baseDir = AppContext.BaseDirectory;
        var logsDir = Path.Combine(baseDir, "logs");
        Directory.CreateDirectory(logsDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(logsDir, "newsbot.log"),
                rollingInterval: RollingInterval.Day,
                encoding: System.Text.Encoding.UTF8,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();

        try
        {
            Log.Information("=== ニュース集約エージェント (C#版) 起動 ===");

            // 2. 引数解析
            bool isInitDb = args.Contains("--init-db", StringComparer.OrdinalIgnoreCase);
            bool isStatus = args.Contains("--status", StringComparer.OrdinalIgnoreCase);
            bool isTest = args.Contains("--test", StringComparer.OrdinalIgnoreCase);
            bool isDemo = args.Contains("--demo", StringComparer.OrdinalIgnoreCase) || args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
            bool isForce = args.Contains("--force", StringComparer.OrdinalIgnoreCase);
            bool showHelp = args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase);

            if (showHelp)
            {
                PrintHelp();
                return 0;
            }

            // 3. 多重プロセス起動防止 (Windows Mutex)
            using var singleInstance = new SingleInstanceMutex("NewsScrapingBot");
            if (!singleInstance.IsOnlyInstance && !isStatus && !isDemo && !isTest)
            {
                Log.Warning("警告: NewsScraping が既に実行中です。重複実行を防ぐため終了します。");
                return 0;
            }

            // 4. 設定ファイルの読み込み (config.yaml)
            var configPath = Path.Combine(baseDir, "config.yaml");
            if (!File.Exists(configPath))
            {
                // カレントディレクトリも探す
                configPath = Path.Combine(Directory.GetCurrentDirectory(), "config.yaml");
            }

            if (!File.Exists(configPath))
            {
                Log.Error("設定ファイルが見つかりません: config.yaml");
                return 1;
            }

            var yamlContent = await File.ReadAllTextAsync(configPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var config = deserializer.Deserialize<AppConfig>(yamlContent);

            // 5. DI コンテナの構築
            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(dispose: true);
            });

            services.AddSingleton(config);
            services.AddSingleton(config.Database);
            services.AddSingleton(config.Llm);
            services.AddSingleton(config.Browser);
            services.AddSingleton<DatabaseService>();
            services.AddSingleton<LlmSummarizerService>();
            services.AddSingleton<RssCollectorService>();
            services.AddSingleton<WebScraperService>();
            services.AddSingleton<NewsAggregatorService>();

            using var serviceProvider = services.BuildServiceProvider();

            var dbService = serviceProvider.GetRequiredService<DatabaseService>();
            var llmService = serviceProvider.GetRequiredService<LlmSummarizerService>();

            // 6. 診断テスト (--test)
            if (isTest)
            {
                Log.Information("--- [自己診断テスト開始] ---");
                bool dbOk = await dbService.TestConnectionAsync();
                bool llmOk = await llmService.CheckOllamaAsync();

                Log.Information("[Config] 登録ニュースソース件数: {Count}", config.NewsSources.Count);
                foreach (var s in config.NewsSources)
                {
                    Log.Information("  - {Name}: {Rss} ({Category})", s.Name, s.Rss, s.Category);
                }

                var rssCollector = serviceProvider.GetRequiredService<RssCollectorService>();
                int totalArticlesFound = 0;
                foreach (var source in config.NewsSources)
                {
                    try
                    {
                        var articles = await rssCollector.CollectFromRssAsync(source.Rss, source.Category, source.Name, limit: 1);
                        Log.Information("  [テスト] {Name}: {Count} 件取得成功", source.Name, articles.Count);
                        totalArticlesFound += articles.Count;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "  [テスト] {Name} 取得エラー: {Message}", source.Name, ex.Message);
                    }
                }

                Log.Information("--- [診断結果サマリー] ---");
                Log.Information("  PostgreSQL 接続: {Status}", dbOk ? "正常 (OK)" : "失敗 (NG)");
                Log.Information("  Ollama 接続:     {Status}", llmOk ? "正常 (OK)" : "未起動または応答なし (フォールバック動作)");
                Log.Information("  RSS 全体取得:    {Status}", totalArticlesFound > 0 ? $"正常 (合計 {totalArticlesFound} 件確認)" : "取得なし");
                return dbOk ? 0 : 1;
            }

            // 6.5 デモ要約実行 (--demo / --dry-run) - DB保存なしで数件取得しOllamaで要約を出力
            if (isDemo)
            {
                Log.Information("--- [デモ要約実行開始 (DB保存なし)] ---");
                bool llmOk = await llmService.CheckOllamaAsync();
                if (!llmOk)
                {
                    Log.Error("Ollama サーバーに接続できません。Ollama を起動してから再実行してください。");
                    return 1;
                }

                var rssCollector = serviceProvider.GetRequiredService<RssCollectorService>();
                var webScraper = serviceProvider.GetRequiredService<WebScraperService>();

                int targetCount = 2;
                var demoArticles = new List<ScrapedArticle>();

                foreach (var source in config.NewsSources)
                {
                    if (demoArticles.Count >= targetCount) break;
                    var articles = await rssCollector.CollectFromRssAsync(source.Rss, source.Category, source.Name, limit: 1);
                    demoArticles.AddRange(articles.Take(targetCount - demoArticles.Count));
                }

                if (!demoArticles.Any())
                {
                    Log.Warning("記事が取得できませんでした。");
                    return 1;
                }

                Console.WriteLine("\n" + new string('=', 65));
                Console.WriteLine($"★ AI要約デモ (使用モデル: {config.Llm.Model}) ★");
                Console.WriteLine(new string('=', 65));

                int idx = 1;
                foreach (var article in demoArticles)
                {
                    Console.WriteLine($"\n【記事 {idx++}】");
                    Console.WriteLine($"ソース:   {article.SourceSite} ({article.Category})");
                    Console.WriteLine($"タイトル: {article.Title}");
                    Console.WriteLine($"URL:      {article.Url}");

                    // RSS概要が短い場合は詳細ページ本文も取得してみる
                    string contentToSummarize = article.Summary;
                    if (string.IsNullOrWhiteSpace(contentToSummarize) || contentToSummarize.Length < 100)
                    {
                        var body = await webScraper.FetchArticleTextAsync(article.Url);
                        if (!string.IsNullOrWhiteSpace(body))
                        {
                            contentToSummarize = body;
                        }
                    }

                    Console.WriteLine($"元テキスト文字数: {contentToSummarize.Length} 文字");
                    Console.WriteLine("Ollama で要約生成中...");

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var summary = await llmService.SummarizeArticleAsync(article.Title, contentToSummarize, forceLlm: true);
                    sw.Stop();

                    Console.WriteLine($"所要時間: {sw.Elapsed.TotalSeconds:F1} 秒");
                    Console.WriteLine($"\n[AI要約結果]:\n{summary}\n");
                    Console.WriteLine(new string('-', 65));
                }

                Log.Information("=== デモ要約完了 ===");
                return 0;
            }

            // 7. DB 初期化 (--init-db)
            if (isInitDb)
            {
                var sqlPath = Path.Combine(baseDir, "db_init.sql");
                if (!File.Exists(sqlPath))
                {
                    sqlPath = Path.Combine(Directory.GetCurrentDirectory(), "db_init.sql");
                }
                await dbService.InitializeSchemaAsync(sqlPath);
                Log.Information("=== DB 初期化完了 ===");
                return 0;
            }

            // 8. ステータス集計表示 (--status)
            if (isStatus)
            {
                var stats = await dbService.GetStatisticsAsync();
                Console.WriteLine("\n=== daily_digest 保存ステータス ===");
                if (!stats.Any())
                {
                    Console.WriteLine("  保存されているニュースデータはありません。");
                }
                else
                {
                    int total = 0;
                    foreach (var s in stats)
                    {
                        Console.WriteLine($"  {s.Genre,-15}: {s.Count,5} 件");
                        total += s.Count;
                    }
                    Console.WriteLine($"  ---------------------------");
                    Console.WriteLine($"  合計件数:       {total,5} 件\n");
                }
                return 0;
            }

            // 9. 通常実行フロー
            // (1) DB準備チェック & 自動初期化
            var sqlFile = Path.Combine(baseDir, "db_init.sql");
            if (!File.Exists(sqlFile))
            {
                sqlFile = Path.Combine(Directory.GetCurrentDirectory(), "db_init.sql");
            }
            await dbService.InitializeSchemaAsync(sqlFile);

            // (2) 重複防止ルールの確認
            var prohibitionDays = config.Duplication?.ProhibitionDays ?? 3;
            Log.Information("重複防止ルール: 過去 {Days} 日間に登録された同一記事はスキップします (1日の登録件数・実行回数制限なし)", prohibitionDays);

            // (3) Ollama 稼働チェック
            await llmService.CheckOllamaAsync();

            // (4) ニュース集約実行
            var aggregator = serviceProvider.GetRequiredService<NewsAggregatorService>();
            await aggregator.RunAsync();

            Log.Information("=== 処理完了 ===");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "処理中に致命的なエラーが発生しました");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"
ニュース集約エージェント (NewsScraping C#版)
使用方法:
  dotnet run                       : 通常のニュース収集とDB保存 (1日件数制限なし、過去3日間重複スキップ)
  dotnet run -- --force            : 強制実行 (通常実行と同様に処理)
  dotnet run -- --demo             : DB保存なしで記事を2件取得しAI要約のデモを実行
  dotnet run -- --test             : DB/Ollama/RSS の自己診断テストを実行
  dotnet run -- --init-db          : PostgreSQL のテーブルおよびスキーマを初期化
  dotnet run -- --status           : 保存されているニュースの件数と統計を表示
  dotnet run -- --help             : このヘルプを表示
");
    }
}
