using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Applying;
using JobSarthi.Api.Classification;
using JobSarthi.Api.Common;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;
using JobSarthi.Api.Filtering;
using JobSarthi.Api.Notifications;
using JobSarthi.Api.Sources;
using JobSarthi.Api.Tailoring;

namespace JobSarthi.Api.Scheduling;

/// <summary>
/// The daily routine: fetch from enabled sources, filter and score, then tailor and queue applications for the best
/// auto-apply jobs. Approval mode stops before submitting. Only one run at a time.
/// </summary>
public class DailyRunPipeline(JobSarthiDbContext db, JobFetchService fetch, JobScoringService scoring, JobClassificationService classification, TailoringService tailoring,
    ApplyStarter starter, AlertService alerts, SummaryService summary, ILogger<DailyRunPipeline> log)
{
    /// <summary>Held for the whole run.</summary>
    public static readonly SemaphoreSlim Gate = new(1, 1);
    public static bool IsRunning => Gate.CurrentCount == 0;

    /// <param name="sendSummary">False when the caller sends the summary itself later, after queued applications finish.</param>
    public async Task<DailyRun> RunAsync(string trigger, CancellationToken ct, bool sendSummary = true)
    {
        if (!await Gate.WaitAsync(0, ct)) throw new InvalidOperationException("A run is already in progress.");
        var run = new DailyRun { Trigger = trigger, StartedAtUtc = DateTime.UtcNow };
        var notes = new List<string>();
        try
        {
            db.DailyRuns.Add(run);
            await db.SaveChangesAsync(ct);

            // 1. Fetch. A failing source is reported, the others carry on.
            var results = await fetch.RunAsync(null, ct);
            run.Fetched = results.Sum(r => r.Fetched);
            run.NewJobs = results.Sum(r => r.Added);
            notes.Add($"Fetched {run.Fetched} jobs from {results.Count} source(s), {run.NewJobs} new");
            foreach (var bad in results.Where(r => r.Error is not null))
            {
                notes.Add($"{bad.Key}: {bad.Error}");
                await alerts.RaiseAsync("source", $"{bad.Key} has a problem. The site may have changed or be down. {bad.Error}", ct);
            }

            // 2. Filter and score against your rules.
            var s = await scoring.ScoreAllAsync(ct);
            run.Shortlisted = s.Shortlisted;
            notes.Add($"{s.Shortlisted} of {s.Total} jobs match your rules");

            // 2b. Open the apply pages of the shortlist and drop jobs that no longer take applications.
            var pages = await classification.CheckApplyPagesAsync(40, ct);
            run.Shortlisted -= pages.Closed;
            if (pages.Checked > 0)
                notes.Add($"Checked {pages.Checked} apply page(s): {pages.Closed} closed and ignored, {pages.Upgraded} moved to API apply");
            notes.Add(await CategoryNoteAsync(ct));

            // 3. Tailor and queue applications.
            run.Queued = await QueueApplicationsAsync(notes, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Daily run failed");
            run.Error = TextUtil.Truncate(ex.Message, 1000);
            await alerts.RaiseAsync("run-failed", "The daily run failed: " + ex.Message, CancellationToken.None);
        }
        finally
        {
            run.FinishedAtUtc = DateTime.UtcNow;
            run.Notes = TextUtil.Truncate(string.Join("\n", notes), 4000);
            try { await db.SaveChangesAsync(CancellationToken.None); } finally { Gate.Release(); }
        }

        // Applications queued by this run are still filling in the background, so their state may change after this message.
        if (sendSummary) await SendSummaryAsync(run);
        return run;
    }

    public async Task<string?> SendSummaryAsync(DailyRun run)
    {
        try
        {
            var (subject, body) = await summary.BuildAsync(run, CancellationToken.None);
            await alerts.NotifyAsync(subject, body, NotifyKind.Summary, CancellationToken.None);
            return body;
        }
        catch (Exception ex) { log.LogWarning(ex, "Could not send the daily summary"); return null; }
    }

    private async Task<string> CategoryNoteAsync(CancellationToken ct)
    {
        var counts = await db.Jobs.Where(j => j.Shortlisted && !j.IsClosed && j.ApplyCategory != null)
            .GroupBy(j => j.ApplyCategory).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int C(ApplyCategory c) => counts.FirstOrDefault(x => x.Key == c)?.Count ?? 0;
        return $"Shortlist by apply type: {C(ApplyCategory.Api)} API, {C(ApplyCategory.Email)} email, {C(ApplyCategory.Manual)} manual";
    }

    private async Task<int> QueueApplicationsAsync(List<string> notes, CancellationToken ct)
    {
        var settings = await db.Settings.AsNoTracking().FirstAsync(ct);
        if (!await db.Resumes.AnyAsync(ct)) { notes.Add("Applications skipped: upload your resume first"); return 0; }

        var dayStart = IstTime.StartOfIstDayUtc(DateTime.UtcNow);
        var preparedToday = await db.Applications.CountAsync(a => a.UpdatedAtUtc >= dayStart &&
            (a.Status == ApplicationStatus.Applying || a.Status == ApplicationStatus.AwaitingApproval
             || a.Status == ApplicationStatus.PausedNeedsInput || a.Status == ApplicationStatus.Submitted), ct);
        var budget = settings.DailyApplyLimit - preparedToday;
        if (budget <= 0) { notes.Add($"Applications skipped: today's limit of {settings.DailyApplyLimit} is used"); return 0; }

        var candidates = await (from j in db.Jobs.AsNoTracking()
                                where j.Shortlisted && j.CanAutoApply && !j.IsClosed && j.ApplyCategory == ApplyCategory.Api
                                join a in db.Applications.AsNoTracking() on j.Id equals a.JobId into apps
                                from a in apps.DefaultIfEmpty()
                                select new ApplyCandidate(j.Id, j.FitScore ?? 0, a == null ? null : a.Status)).ToListAsync(ct);
        var picked = AutoApplyPlanner.Select(candidates, budget, settings.AutoApplyMinScore);
        if (picked.Count == 0) { notes.Add($"No new auto-apply jobs scored {settings.AutoApplyMinScore} or more"); return 0; }

        var queued = 0;
        foreach (var jobId in picked)
        {
            var existing = await db.Applications.FirstOrDefaultAsync(a => a.JobId == jobId, ct);
            var app = existing is { TailoredAtUtc: not null } ? existing : (await tailoring.RunAsync(jobId, ct)).App;
            if (app is null) { notes.Add("Tailoring failed. Check your profile"); break; }
            if (await starter.StartAsync(app, null, "Queued by the daily run", ct)) queued++;
        }
        notes.Add($"Queued {queued} application(s) ({(settings.ApplyMode == ApplyMode.FullAuto ? "full auto, will submit" : "approval mode, will wait for you")})");
        return queued;
    }

}
