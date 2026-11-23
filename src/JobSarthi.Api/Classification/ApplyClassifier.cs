using System.Text.RegularExpressions;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Classification;

/// <param name="Ats">greenhouse, lever, ashby or workable for API apply. Null otherwise.</param>
/// <param name="ApplyUrl">The best link to apply on (an ATS link found in the description beats an aggregator page).</param>
/// <param name="ApplyEmail">Where to send the resume, for email apply.</param>
public record ApplyClassification(ApplyCategory Category, string? Ats, string ApplyUrl, string? ApplyEmail, string Reason);

/// <summary>
/// Decides how a job is applied to, from the apply link's domain and the job description. Pure, no network.
/// Order: ATS apply link, then a "send your resume to x@y" instruction, then an ATS link inside the description, else manual.
/// </summary>
public static class ApplyClassifier
{
    // host suffix -> ATS key
    private static readonly (string Host, string Ats)[] AtsHosts =
    {
        ("greenhouse.io", "greenhouse"),          // boards.greenhouse.io, job-boards.greenhouse.io, boards.eu.greenhouse.io
        ("lever.co", "lever"),                    // jobs.lever.co, jobs.eu.lever.co
        ("ashbyhq.com", "ashby"),                 // jobs.ashbyhq.com
        ("workable.com", "workable"),             // apply.workable.com, <company>.workable.com
    };

    private static readonly Regex UrlInText = new(@"https?://[^\s""'<>)\]]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Email = new(@"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MailTo = new(@"mailto:([A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "send / email / share / mail / forward your resume|CV ... to x@y", "apply by email", "drop your CV at x@y".
    private static readonly Regex EmailApplyContext = new(
        @"(?:send|email|e-mail|mail|share|forward|submit|drop|shoot)\b[^.\n]{0,60}\b(?:resume|cv|curriculum vitae|profile|application|portfolio)s?\b"
        + @"|\b(?:resume|cv|application)s?\b[^.\n]{0,40}\b(?:to|at|on)\s*[:\-]?\s*[A-Z0-9._%+\-]+@"
        + @"|apply (?:by|via|through|over) (?:e-?mail|mail)"
        + @"|interested candidates[^.\n]{0,80}@",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Addresses that are never where applications go.
    private static readonly string[] IgnoredMailboxes = { "noreply", "no-reply", "donotreply", "do-not-reply", "privacy", "support", "abuse", "unsubscribe", "example" };

    public static string? AtsFor(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.ToLowerInvariant();
        foreach (var (h, ats) in AtsHosts)
            if (host == h || host.EndsWith("." + h)) return ats;
        // Greenhouse embeds on company sites carry ?gh_jid=123.
        if (uri.Query.Contains("gh_jid=", StringComparison.OrdinalIgnoreCase)) return "greenhouse";
        return null;
    }

    public static ApplyClassification Classify(string url, string? description)
    {
        var text = description ?? "";

        if (AtsFor(url) is { } ats)
            return new(ApplyCategory.Api, ats, url, null, $"Apply link is on {Pretty(ats)}");

        if (FindApplyEmail(text) is { } email)
            return new(ApplyCategory.Email, null, url, email, $"Posting asks for the resume at {email}");

        if (FindAtsLink(text) is { } inner)
            return new(ApplyCategory.Api, inner.Ats, inner.Url, null, $"Description links to a {Pretty(inner.Ats)} application");

        return new(ApplyCategory.Manual, null, url, null, "No ATS or apply-by-email found. Apply on the site");
    }

    private static readonly Regex IframeSrc = new(@"<iframe\b[^>]*\bsrc\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The ATS link that IS this job's application, in a description or page HTML. Safe against "related jobs" lists:
    /// an embedded application iframe wins; otherwise there must be exactly one distinct ATS posting link. Several → null (stays Manual).
    /// </summary>
    public static (string Ats, string Url)? FindAtsLink(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var frames = IframeSrc.Matches(text).Select(m => Decode(m.Groups[1].Value))
            .Where(u => AtsFor(u) is not null).Select(Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (frames.Count == 1) return (AtsFor(frames[0])!, frames[0]);
        if (frames.Count > 1) return null;

        var links = UrlInText.Matches(text).Select(m => Decode(m.Value).TrimEnd('.', ',', ';'))
            .Where(u => AtsFor(u) is not null && !IsBoardHome(u)).Select(Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return links.Count == 1 ? (AtsFor(links[0])!, links[0]) : null;
    }

    private static string Decode(string s) => System.Net.WebUtility.HtmlDecode(s);

    /// <summary>One posting = one key: drops tracking query, fragment and the /apply or /application suffix. Keeps gh_jid / token queries.</summary>
    private static string Key(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return url;
        var path = u.AbsolutePath.TrimEnd('/');
        foreach (var suffix in new[] { "/apply", "/application" })
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) path = path[..^suffix.Length];
        var keep = u.Query.Contains("gh_jid=", StringComparison.OrdinalIgnoreCase) || u.Query.Contains("token=", StringComparison.OrdinalIgnoreCase) || u.Query.Contains("for=", StringComparison.OrdinalIgnoreCase);
        return $"{u.Scheme}://{u.Host}{path}{(keep ? u.Query : "")}";
    }

    /// <summary>A company's board home page (boards.greenhouse.io/acme) is not a posting.</summary>
    private static bool IsBoardHome(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Length <= 1
        && !u.Query.Contains("gh_jid=", StringComparison.OrdinalIgnoreCase) && !u.Query.Contains("token=", StringComparison.OrdinalIgnoreCase);

    /// <summary>An address the posting tells you to send your resume to. A contact address alone is not enough.</summary>
    public static string? FindApplyEmail(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var mailto = MailTo.Matches(text).Select(m => m.Groups[1].Value).FirstOrDefault(Usable);
        foreach (Match ctx in EmailApplyContext.Matches(text))
        {
            // Look for the address near the instruction: within the same sentence or the next 160 characters.
            var start = Math.Max(0, ctx.Index - 80);
            var window = text.Substring(start, Math.Min(text.Length - start, ctx.Length + 240));
            var near = Email.Matches(window).Select(m => m.Value.TrimEnd('.')).FirstOrDefault(Usable);
            if (near is not null) return near.ToLowerInvariant();
        }
        // A mailto link with no instruction still counts when the posting talks about applying.
        if (mailto is not null && Regex.IsMatch(text, @"\b(apply|application|resume|cv)\b", RegexOptions.IgnoreCase)) return mailto.ToLowerInvariant();
        return null;
    }

    private static bool Usable(string email)
    {
        var local = email.Split('@')[0].ToLowerInvariant();
        return !IgnoredMailboxes.Any(local.Contains) && !email.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !email.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);
    }

    private static string Pretty(string ats) => char.ToUpperInvariant(ats[0]) + ats[1..];
}
