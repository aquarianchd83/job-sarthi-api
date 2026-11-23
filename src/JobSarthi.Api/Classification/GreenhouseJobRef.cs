using System.Text.RegularExpressions;
using JobSarthi.Api.Domain.Entities;

namespace JobSarthi.Api.Classification;

/// <summary>
/// A Greenhouse posting = board token (company slug) + numeric job id. With both, Greenhouse's own hosted form can be opened
/// even when the company shows the job on its own careers site (stripe.com/careers/apply/..., acme.com/jobs?gh_jid=...),
/// whose custom form our filler cannot read.
/// </summary>
public sealed record GreenhouseJobRef(string Board, string JobId)
{
    private static readonly Regex Digits = new(@"^\d{3,}$", RegexOptions.Compiled);
    private static readonly Regex BoardSlug = new(@"^[a-z0-9][a-z0-9_\-]*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex JobsPath = new(@"^/([^/]+)/jobs/(\d{3,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Greenhouse's embeddable application form. Renders the plain Greenhouse form for any board, custom careers site or not.</summary>
    public string EmbedFormUrl => $"https://job-boards.greenhouse.io/embed/job_app?for={Uri.EscapeDataString(Board)}&token={JobId}";
    /// <summary>The hosted posting page on Greenhouse.</summary>
    public string HostedUrl => $"https://job-boards.greenhouse.io/{Uri.EscapeDataString(Board)}/jobs/{JobId}";
    /// <summary>Public Job Board API: 404 here means the job is closed.</summary>
    public string ApiUrl => $"https://boards-api.greenhouse.io/v1/boards/{Uri.EscapeDataString(Board)}/jobs/{JobId}";

    /// <summary>From the job: a Greenhouse-sourced job carries "board:id" in ExternalId; otherwise the apply link or posting URL is parsed.</summary>
    public static GreenhouseJobRef? From(Job job)
    {
        string? board = null, id = null;
        if (string.Equals(job.SourceKey, "greenhouse", StringComparison.OrdinalIgnoreCase))
        {
            var parts = job.ExternalId.Split(':', 2);
            if (parts.Length == 2 && BoardSlug.IsMatch(parts[0]) && Digits.IsMatch(parts[1])) return new(parts[0], parts[1]);
            if (parts.Length == 2 && BoardSlug.IsMatch(parts[0])) board = parts[0];
        }
        foreach (var url in new[] { job.ApplyUrl, job.Url })
        {
            if (FromUrl(url) is { } r) return r;
            // gh_jid on a company site gives the id; the board comes from the source when we have it.
            if (id is null && GhJid(url) is { } jid) id = jid;
        }
        return board is not null && id is not null ? new(board, id) : null;
    }

    /// <summary>boards.greenhouse.io/acme/jobs/123, job-boards.greenhouse.io/acme/jobs/123, .../embed/job_app?for=acme&amp;token=123.</summary>
    public static GreenhouseJobRef? FromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return null;
        var host = u.Host.ToLowerInvariant();
        if (host != "greenhouse.io" && !host.EndsWith(".greenhouse.io")) return null;

        var q = Query(u);
        if (q.TryGetValue("for", out var f) && q.TryGetValue("token", out var t) && BoardSlug.IsMatch(f) && Digits.IsMatch(t)) return new(f, t);

        var m = JobsPath.Match(u.AbsolutePath);
        if (m.Success && BoardSlug.IsMatch(m.Groups[1].Value)) return new(m.Groups[1].Value, m.Groups[2].Value);
        return null;
    }

    private static string? GhJid(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return null;
        return Query(u).TryGetValue("gh_jid", out var v) && Digits.IsMatch(v) ? v : null;
    }

    private static Dictionary<string, string> Query(Uri u) =>
        u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .GroupBy(p => Uri.UnescapeDataString(p[0]).ToLowerInvariant())
            .ToDictionary(g => g.Key, g => Uri.UnescapeDataString(g.First()[1]));
}
