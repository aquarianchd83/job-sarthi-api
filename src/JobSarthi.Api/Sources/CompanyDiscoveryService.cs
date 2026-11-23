using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Classification;
using JobSarthi.Api.Common;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;

namespace JobSarthi.Api.Sources;

public record DiscoverySummary(int JobsScanned, int NewSuggestions, int AutoAdded);

/// <summary>
/// Greenhouse, Lever and Ashby can only be read one company board at a time, so they need company slugs.
/// This finds those slugs in the apply links of jobs from every other source and suggests them (or adds them, when auto-add is on).
/// </summary>
public class CompanyDiscoveryService(JobSarthiDbContext db, ILogger<CompanyDiscoveryService> log)
{
    public static readonly string[] SlugSources = { "greenhouse", "lever", "ashby" };
    private static readonly Regex ValidSlug = new("^[a-z0-9][a-z0-9_-]{0,98}$", RegexOptions.Compiled);
    // Path words that are never company slugs.
    private static readonly HashSet<string> NotSlugs = new(StringComparer.OrdinalIgnoreCase) { "embed", "jobs", "job", "v1", "api", "boards", "posting-api", "careers" };

    /// <summary>(source, slug) from an ATS link, or null. Greenhouse: boards / job-boards / boards.eu, and embeds with ?for=slug.</summary>
    public static (string Source, string Slug)? Extract(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return null;
        var ats = ApplyClassifier.AtsFor(url);
        if (ats is null || !SlugSources.Contains(ats)) return null;
        var host = u.Host.ToLowerInvariant();
        var segs = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        string? slug = null;
        if (ats == "greenhouse")
        {
            if (!(host.StartsWith("boards") || host.StartsWith("job-boards"))) return null;   // company sites with ?gh_jid carry no slug
            var forParam = Regex.Match(u.Query, @"[?&]for=([^&]+)", RegexOptions.IgnoreCase);
            slug = forParam.Success ? Uri.UnescapeDataString(forParam.Groups[1].Value) : segs.FirstOrDefault();
        }
        else if (ats == "lever" && host.StartsWith("jobs.")) slug = segs.FirstOrDefault();
        else if (ats == "ashby" && host == "jobs.ashbyhq.com") slug = segs.FirstOrDefault();

        slug = slug?.Trim().ToLowerInvariant();
        if (slug is null || NotSlugs.Contains(slug) || !ValidSlug.IsMatch(slug)) return null;
        return (ats, slug);
    }

    public async Task<DiscoverySummary> DiscoverAsync(bool autoAdd, CancellationToken ct)
    {
        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => !SlugSources.Contains(j.SourceKey))
            .Select(j => new { j.Title, j.Company, j.Url, j.ApplyUrl, j.Description })
            .ToListAsync(ct);

        var found = new Dictionary<(string, string), (int Count, string Title, string Company)>();
        foreach (var j in jobs)
        {
            // Apply link first, then the job link, then a single ATS link in the description.
            var hit = Extract(j.ApplyUrl) ?? Extract(j.Url) ?? Extract(ApplyClassifier.FindAtsLink(j.Description)?.Url);
            if (hit is null) continue;
            found[hit.Value] = found.TryGetValue(hit.Value, out var f) ? (f.Count + 1, f.Title, f.Company) : (1, j.Title, j.Company);
        }

        var existing = (await db.CompanySlugs.AsNoTracking().Select(s => new { s.SourceKey, s.Slug }).ToListAsync(ct))
            .Select(s => (s.SourceKey, s.Slug)).ToHashSet();
        var suggestions = await db.CompanySuggestions.ToListAsync(ct);
        var now = DateTime.UtcNow;
        int created = 0, added = 0;

        foreach (var ((source, slug), info) in found)
        {
            var s = suggestions.FirstOrDefault(x => x.SourceKey == source && x.Slug == slug);
            if (s is null)
            {
                s = new CompanySuggestion { SourceKey = source, Slug = slug, FirstSeenUtc = now, Status = SuggestionStatus.Pending };
                db.CompanySuggestions.Add(s);
                created++;
            }
            s.SeenCount = info.Count;
            s.LastSeenUtc = now;
            s.ExampleTitle = TextUtil.Truncate(info.Title, 300);
            s.ExampleCompany = TextUtil.Truncate(info.Company, 200);

            if (existing.Contains((source, slug))) { s.Status = SuggestionStatus.Added; continue; }
            if (autoAdd && s.Status == SuggestionStatus.Pending)
            {
                db.CompanySlugs.Add(new CompanySlug { SourceKey = source, Slug = slug });
                existing.Add((source, slug));
                s.Status = SuggestionStatus.Added;
                added++;
            }
        }
        await db.SaveChangesAsync(ct);
        if (created > 0 || added > 0) log.LogInformation("Company discovery: {New} new suggestions, {Added} auto-added", created, added);
        return new DiscoverySummary(jobs.Count, created, added);
    }

    /// <summary>Adds a suggestion to the company list of its source.</summary>
    public async Task<bool> AcceptAsync(CompanySuggestion s, CancellationToken ct)
    {
        if (!await db.CompanySlugs.AnyAsync(x => x.SourceKey == s.SourceKey && x.Slug == s.Slug, ct))
            db.CompanySlugs.Add(new CompanySlug { SourceKey = s.SourceKey, Slug = s.Slug });
        s.Status = SuggestionStatus.Added;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
