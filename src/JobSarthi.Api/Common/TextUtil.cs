using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Common;

public static class TextUtil
{
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Turns HTML (even entity-encoded HTML, as Greenhouse sends) into plain text.</summary>
    public static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var s = WebUtility.HtmlDecode(html);
        s = Tags.Replace(s, " ");
        s = WebUtility.HtmlDecode(s);
        return Spaces.Replace(s, " ").Trim();
    }

    public static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);

    public static WorkMode DetectWorkMode(params string?[] texts)
    {
        var t = string.Join(" ", texts).ToLowerInvariant();
        if (t.Contains("hybrid")) return WorkMode.Hybrid;
        if (t.Contains("remote") || t.Contains("work from home") || t.Contains("anywhere")) return WorkMode.Remote;
        return WorkMode.Onsite;
    }

    /// <summary>"acme-corp" becomes "Acme Corp".</summary>
    public static string PrettySlug(string slug) =>
        string.Join(' ', slug.Split('-', '_').Where(p => p.Length > 0).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));

    /// <summary>Reads a string or number property as text. Missing or other kinds give "".</summary>
    public static string Str(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString() ?? "", JsonValueKind.Number => v.GetRawText(), _ => "" }
            : "";
}
