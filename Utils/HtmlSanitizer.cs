using System.Net;
using System.Text.RegularExpressions;

namespace NewsScraping.Utils;

public static class HtmlSanitizer
{
    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex MultipleSpacesRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// HTMLタグを除去し、HTML実体参照をデコードし、余分な空白を正規化する
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // 1. HTMLタグの除去
        var stripped = TagRegex.Replace(text, " ");

        // 2. HTML実体参照のデコード
        var decoded = WebUtility.HtmlDecode(stripped);

        // 3. 連続する改行やスペースの圧縮
        var normalized = MultipleSpacesRegex.Replace(decoded, " ").Trim();

        return normalized;
    }
}
