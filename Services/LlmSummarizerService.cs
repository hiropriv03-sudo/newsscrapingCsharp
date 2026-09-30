using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NewsScraping.Models;
using NewsScraping.Utils;

namespace NewsScraping.Services;

public class LlmSummarizerService
{
    private readonly LlmConfig _llmConfig;
    private readonly ILogger<LlmSummarizerService> _logger;
    private readonly HttpClient _httpClient;

    public LlmSummarizerService(LlmConfig llmConfig, ILogger<LlmSummarizerService> logger)
    {
        _llmConfig = llmConfig;
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(llmConfig.Url.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(llmConfig.TimeoutSeconds);
    }

    /// <summary>
    /// Ollama サーバーの死活確認 (3秒タイムアウト)
    /// </summary>
    public async Task<bool> CheckOllamaAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var response = await _httpClient.GetAsync("api/version", cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cts.Token);
                _logger.LogInformation("[LLM] Ollama 接続成功: {Content}", content.Trim());
                return true;
            }
            _logger.LogWarning("[LLM] Ollama ステータスコード異常: {StatusCode}", response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[LLM] Ollama 接続スキップ (タイムアウトまたは未起動): {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// ニュース記事の要約を生成
    /// </summary>
    public async Task<string> SummarizeArticleAsync(string title, string rawSummary, CancellationToken ct = default)
    {
        var cleanedSummary = HtmlSanitizer.Clean(rawSummary);

        // 1. 内容が空の場合はタイトルをそのまま要約とする
        if (string.IsNullOrWhiteSpace(cleanedSummary))
        {
            return title;
        }

        // 2. 310文字未満ならそのまま使う (Python版互換の高速化)
        if (cleanedSummary.Length < 310)
        {
            return cleanedSummary;
        }

        // 3. 310文字以上の場合は Ollama で200〜300文字に要約
        var prompt = $@"以下のニュース内容を200〜300文字程度で詳細に要約してください。日本語で、重要な事実を網羅的に伝えてください。
タイトル: {title}
内容: {cleanedSummary}";

        var requestBody = new
        {
            model = _llmConfig.Model,
            prompt = prompt,
            stream = false,
            options = new
            {
                temperature = 0,
                num_ctx = _llmConfig.NumCtx
            }
        };

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_llmConfig.TimeoutSeconds));

            var response = await _httpClient.PostAsJsonAsync("api/generate", requestBody, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: cts.Token);
                if (!string.IsNullOrWhiteSpace(result?.Response))
                {
                    return HtmlSanitizer.Clean(result.Response);
                }
            }
            _logger.LogWarning("[LLM] 要約API呼び出し失敗または空返却。フォールバックします。");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[LLM] 要約中にエラーまたはタイムアウト: {Message}。元テキストの切り出しを使用します。", ex.Message);
        }

        // フォールバック: 元テキストの先頭1000文字
        return cleanedSummary.Length > 1000 ? cleanedSummary[..1000] : cleanedSummary;
    }

    private class OllamaGenerateResponse
    {
        [JsonPropertyName("response")]
        public string? Response { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }
    }
}
