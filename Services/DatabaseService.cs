using Dapper;
using Microsoft.Extensions.Logging;
using NewsScraping.Models;
using Npgsql;

namespace NewsScraping.Services;

public class DatabaseService : IDisposable
{
    private readonly DatabaseConfig _dbConfig;
    private readonly ILogger<DatabaseService> _logger;
    private readonly NpgsqlDataSource _dataSource;

    public DatabaseService(DatabaseConfig dbConfig, ILogger<DatabaseService> logger)
    {
        _dbConfig = dbConfig;
        _logger = logger;

        var connString = dbConfig.BuildConnectionString();
        var builder = new NpgsqlDataSourceBuilder(connString);
        _dataSource = builder.Build();
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            var version = await conn.ExecuteScalarAsync<string>("SELECT version();");
            _logger.LogInformation("[DB] 接続成功: {Version}", version);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DB] 接続失敗 (Host: {Host}, Port: {Port})", _dbConfig.Host, _dbConfig.Port);
            return false;
        }
    }

    public async Task InitializeSchemaAsync(string sqlFilePath, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var schema = _dbConfig.Schema;

        // 1. スキーマ作成
        await conn.ExecuteAsync($"CREATE SCHEMA IF NOT EXISTS {schema};");
        await conn.ExecuteAsync($"SET search_path TO {schema};");

        // 2. DDL 実行
        if (File.Exists(sqlFilePath))
        {
            var sql = await File.ReadAllTextAsync(sqlFilePath, ct);
            // schema 内でテーブルが作成されるように search_path または修飾子を考慮
            var script = $"SET search_path TO {schema};\n" + sql;
            await conn.ExecuteAsync(script);
            _logger.LogInformation("[DB] スキーマ・テーブル初期化完了 (Schema: {Schema})", schema);
        }
        else
        {
            _logger.LogWarning("[DB] 初期化SQLが見つかりません: {Path}", sqlFilePath);
        }
    }

    /// <summary>
    /// 本日の実行データが既に daily_digest に存在するかチェック (二重実行防止)
    /// </summary>
    public async Task<bool> CheckAlreadyExecutedTodayAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var schema = _dbConfig.Schema;

        var query = $@"
            SELECT COUNT(*) 
            FROM {schema}.daily_digest 
            WHERE run_timestamp::date = CURRENT_DATE;
        ";

        var count = await conn.ExecuteScalarAsync<int>(query);
        if (count > 0)
        {
            _logger.LogInformation("[DB] 本日の実行データが既に {Count} 件存在します。", count);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 同一記事が過去指定日数（デフォルト3日間）以内に存在するかチェック（重複投稿防止）
    /// タイトル一致またはURL一致のいずれかに該当する場合は重複とみなしてDB登録をスキップする
    /// </summary>
    public async Task<bool> IsArticleDuplicateAsync(string title, string? url = null, int duplicateDays = 3, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var schema = _dbConfig.Schema;

        var query = $@"
            SELECT 1 
            FROM {schema}.daily_digest 
            WHERE (
                article_title = @title 
                OR (@url IS NOT NULL AND @url <> '' AND source_url = @url)
            )
            AND run_timestamp >= NOW() - make_interval(days => @duplicateDays)
            LIMIT 1;
        ";

        var exists = await conn.ExecuteScalarAsync<int?>(query, new { title, url, duplicateDays });
        return exists.HasValue;
    }

    /// <summary>
    /// 互換用オーバーロード（カテゴリとタイトル指定）
    /// </summary>
    public Task<bool> IsArticleDuplicateAsync(string category, string title, CancellationToken ct = default)
        => IsArticleDuplicateAsync(title, null, 3, ct);

    /// <summary>
    /// 記事を daily_digest テーブルに挿入
    /// </summary>
    public async Task<bool> InsertArticleAsync(
        DateTime runTimestamp,
        string category,
        string title,
        string summary,
        string url,
        string sourceName,
        int rankInGenre,
        CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var schema = _dbConfig.Schema;

        var query = $@"
            INSERT INTO {schema}.daily_digest 
            (run_timestamp, genre_name, article_title, summary_text, source_url, source_site, rank_in_genre)
            VALUES (@runTimestamp, @category, @title, @summary, @url, @sourceName, @rankInGenre);
        ";

        var rows = await conn.ExecuteAsync(query, new
        {
            runTimestamp,
            category,
            title,
            summary,
            url,
            sourceName,
            rankInGenre
        });

        return rows > 0;
    }

    /// <summary>
    /// DBのサマリー統計（ジャンル別件数、最新記事情報など）を取得
    /// </summary>
    public async Task<List<(string Genre, int Count)>> GetStatisticsAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var schema = _dbConfig.Schema;

        var query = $@"
            SELECT genre_name, COUNT(*) 
            FROM {schema}.daily_digest 
            GROUP BY genre_name 
            ORDER BY count DESC;
        ";

        var results = await conn.QueryAsync<(string Genre, int Count)>(query);
        return results.ToList();
    }

    public void Dispose()
    {
        _dataSource.Dispose();
    }
}
