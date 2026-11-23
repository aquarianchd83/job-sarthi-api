using System.Net;
using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Common;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Classification;

public record PageCheckSummary(int Checked, int Closed, int Upgraded, int Errors);

/// <summary>
/// Sorts jobs into API / Email / Manual apply, and drops jobs that no longer take applications.
/// The listing check is pure and runs on every scoring pass. The apply-page check reads the live page and runs for shortlisted jobs.
/// </summary>
public class JobClassificationService(JobSarthiDbContext db, IHttpClientFactory httpFactory, ILogger<JobClassificationService> log)
{
    public const string HttpClientName = "apply-page";
    /// <summary>ATS that have a Playwright applier (Applying/Playwright). Workable is API-category but filled through the MCP draft flow.</summary>
    public static readonly HashSet<string> NativeAts = new(StringComparer.OrdinalIgnoreCase) { "greenhouse", "lever", "ashby" };
    private static readonly TimeSpan RecheckAfter = TimeSpan.FromHours(20);
    private const int MaxPageChars = 1_500_000;

    /// <summary>Listing check and category, from the job's own text. No network.</summary>
    public static void ClassifyListing(Job job, DateTime nowUtc)
    {
        job.ClassifiedAtUtc = nowUtc;
        if (ClosedJobDetector.Find(job.Title, job.Description) is { } phrase)
        {
            MarkClosed(job, $"Listing says \"{phrase}\"");
            return;
        }
        if (job.IsClosed) return;   // closed earlier by the apply-page check: stays closed
        Apply(job, ApplyClassifier.Classify(job.Url, job.Description));
    }

    public static void MarkClosed(Job job, string reason)
    {
        job.IsClosed = true;
        job.ClosedReason = TextUtil.Truncate(reason, 300);
        // Ignored completely: no category, never shortlisted, never applied to.
        job.ApplyCategory = null;
        job.AtsKey = null;
        job.ApplyEmail = null;
        job.CategoryReason = null;
        job.CanAutoApply = false;
        job.Shortlisted = false;
        job.RejectReason = "No longer accepting applications";
    }

    private static void Apply(Job job, ApplyClassification c)
    {
        job.ApplyCategory = c.Category;
        job.AtsKey = c.Ats;
        job.ApplyUrl = TextUtil.Truncate(c.ApplyUrl, 1000);
        job.ApplyEmail = c.ApplyEmail is null ? null : TextUtil.Truncate(c.ApplyEmail, 254);
        job.CategoryReason = TextUtil.Truncate(c.Reason, 300);
        job.CanAutoApply = c.Category == ApplyCategory.Api && c.Ats is not null && NativeAts.Contains(c.Ats);
    }

    /// <summary>Reclassifies every job from its listing (use after changing the rules here).</summary>
    public async Task<int> ReclassifyAllAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var jobs = await db.Jobs.ToListAsync(ct);
        foreach (var j in jobs) ClassifyListing(j, now);
        await db.SaveChangesAsync(ct);
        return jobs.Count;
    }

    /// <summary>
    /// Opens the apply page of the best shortlisted jobs and drops the ones that say they are closed.
    /// A Manual job whose page embeds an ATS form is moved to API apply.
    /// </summary>
    public async Task<PageCheckSummary> CheckApplyPagesAsync(int max, CancellationToken ct, IReadOnlyCollection<int>? onlyJobIds = null)
    {
        var since = DateTime.UtcNow - RecheckAfter;
        var q = db.Jobs.Where(j => !j.IsClosed && j.ApplyCategory != null);
        q = onlyJobIds is { Count: > 0 }
            ? q.Where(j => onlyJobIds.Contains(j.Id))
            : q.Where(j => j.Shortlisted && (j.PageCheckedAtUtc == null || j.PageCheckedAtUtc < since));
        var jobs = await q.OrderByDescending(j => j.FitScore).Take(Math.Clamp(max, 1, 200)).ToListAsync(ct);

        int closed = 0, upgraded = 0, errors = 0;
        var http = httpFactory.CreateClient(HttpClientName);
        foreach (var job in jobs)
        {
            var url = job.ApplyUrl ?? job.Url;
            var page = await ReadPageAsync(http, url, ct);
            job.PageCheckedAtUtc = DateTime.UtcNow;
            switch (page)
            {
                case { Gone: true }:
                    MarkClosed(job, $"Apply page is gone (HTTP {page.Status})");
                    closed++;
                    break;
                case { Error: not null }:
                    errors++;
                    log.LogInformation("Could not read apply page for job {Id}: {Error}", job.Id, page.Error);
                    break;
                default:
                    if (ClosedJobDetector.Find(page.Text) is { } phrase)
                    {
                        MarkClosed(job, $"Apply page says \"{phrase}\"");
                        closed++;
                    }
                    else if (job.ApplyCategory == ApplyCategory.Manual && ApplyClassifier.FindAtsLink(page.Html) is { } ats)
                    {
                        // A company careers page that embeds a Greenhouse/Lever/Ashby/Workable form.
                        Apply(job, new ApplyClassification(ApplyCategory.Api, ats.Ats, ats.Url, null, $"Apply page embeds a {ats.Ats} form"));
                        upgraded++;
                    }
                    break;
            }
            await db.SaveChangesAsync(ct);
        }
        return new PageCheckSummary(jobs.Count, closed, upgraded, errors);
    }

    private static readonly System.Text.RegularExpressions.Regex NotVisible = new(
        @"<(script|style|template|noscript)\b[^>]*>[\s\S]*?</\1\s*>|<!--[\s\S]*?-->",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private record PageRead(int Status, string Html, string Text, bool Gone, string? Error);

    private static async Task<PageRead> ReadPageAsync(HttpClient http, string url, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            var status = (int)resp.StatusCode;
            if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return new(status, "", "", true, null);
            if (!resp.IsSuccessStatusCode) return new(status, "", "", false, $"HTTP {status}");
            var html = await resp.Content.ReadAsStringAsync(ct);
            if (html.Length > MaxPageChars) html = html[..MaxPageChars];
            // Scripts and styles hold translation strings ("this job is no longer available") that are not shown. Only visible text counts.
            var visible = NotVisible.Replace(html, " ");
            return new(status, html, TextUtil.StripHtml(visible), false, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException && !ct.IsCancellationRequested)
        {
            return new(0, "", "", false, ex.Message.Split('\n')[0]);
        }
    }
}

public static class ClassificationExtensions
{
    public static IServiceCollection AddClassification(this IServiceCollection services)
    {
        services.AddHttpClient(JobClassificationService.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(20);
            // A normal browser agent: many career sites answer bots with 403.
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
            c.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
        });
        services.AddScoped<JobClassificationService>();
        return services;
    }
}
