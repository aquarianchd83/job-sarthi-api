using System.Text.RegularExpressions;
using JobSarthi.Api.Common;

namespace JobSarthi.Api.Sources.Alerts;

/// <summary>Shared helper: pulls (href, visible text) pairs out of an alert email's HTML.</summary>
public static class AlertLinkParser
{
    private static readonly Regex Anchor = new(
        @"<a\b[^>]*?href\s*=\s*[""']([^""']+)[""'][^>]*>(.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly string[] GenericText =
        { "view all", "see all", "apply", "unsubscribe", "view job", "view more", "manage", "settings", "edit" };

    public static IEnumerable<(string Href, string Text)> Links(string html)
    {
        foreach (Match m in Anchor.Matches(html))
        {
            var href = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
            var text = TextUtil.StripHtml(m.Groups[2].Value);
            yield return (href, text);
        }
    }

    public static bool IsGeneric(string text) =>
        text.Length < 4 || GenericText.Any(g => text.Equals(g, StringComparison.OrdinalIgnoreCase));
}
