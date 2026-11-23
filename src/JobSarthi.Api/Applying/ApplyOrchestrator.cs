using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Classification;
using JobSarthi.Api.Common;
using JobSarthi.Api.Mcp;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;
using JobSarthi.Api.Notifications;

namespace JobSarthi.Api.Applying;

/// <summary>
/// Runs one application end to end: checks the daily limit and the gap between applies, picks the applier for the source,
/// then turns the outcome into a status, a log and (when needed) questions for you.
/// </summary>
public class ApplyOrchestrator(JobSarthiDbContext db, IEnumerable<IJobApplier> appliers, McpApplyDrafter drafter, AlertService alerts, ILogger<ApplyOrchestrator> log)
{
    public async Task RunAsync(int applicationId, bool submitRequested, CancellationToken ct)
    {
        var app = await db.Applications.FirstAsync(a => a.Id == applicationId, ct);
        var job = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == app.JobId, ct);
        var settings = await db.Settings.AsNoTracking().FirstAsync(ct);

        async Task Set(ApplicationStatus status, string message, string? shot = null)
        {
            app.Status = status;
            app.UpdatedAtUtc = DateTime.UtcNow;
            if (shot is not null) app.ScreenshotFile = shot;
            db.ApplicationLogs.Add(new ApplicationLog { ApplicationId = app.Id, AtUtc = DateTime.UtcNow, Message = message });
            await db.SaveChangesAsync(ct);
        }

        if (job.IsClosed) { await Set(ApplicationStatus.Cancelled, $"Job ignored: {job.ClosedReason ?? "no longer accepting applications"}"); return; }

        // API apply: the ATS of the apply link picks the applier (a RemoteOK job linking to Greenhouse uses the Greenhouse applier).
        // Jobs classified before categories existed fall back to their source.
        var key = job.AtsKey ?? (job.ApplyCategory is null ? job.SourceKey : null);
        var applier = key is null ? null : appliers.FirstOrDefault(a => a.SourceKey == key);
        if (applier is null)
        {
            await DraftAsync(app, job, Set, ct);
            return;
        }

        // Daily limit and gap only matter when we are about to submit for real.
        if (submitRequested)
        {
            var dayStart = IstTime.StartOfIstDayUtc(DateTime.UtcNow);
            var today = await db.Applications.CountAsync(a => a.SubmittedAtUtc != null && a.SubmittedAtUtc >= dayStart && a.Status == ApplicationStatus.Submitted, ct);
            if (today >= settings.DailyApplyLimit)
            {
                await Set(ApplicationStatus.AwaitingApproval, $"Daily limit of {settings.DailyApplyLimit} applications reached. It will not be submitted today. Approve again tomorrow.");
                return;
            }
            var last = await db.Applications.Where(a => a.SubmittedAtUtc != null).MaxAsync(a => a.SubmittedAtUtc, ct);
            if (last is not null && settings.GapBetweenAppliesSeconds > 0)
            {
                var wait = TimeSpan.FromSeconds(settings.GapBetweenAppliesSeconds) - (DateTime.UtcNow - last.Value);
                if (wait > TimeSpan.Zero) { await Set(ApplicationStatus.Applying, $"Waiting {(int)wait.TotalSeconds}s, the gap between applications"); await Task.Delay(wait, ct); }
            }
        }

        await Set(ApplicationStatus.Applying, submitRequested ? "Filling the form and submitting" : "Filling the form (will not submit until you approve)");

        var profile = await db.Profiles.AsNoTracking().FirstAsync(ct);
        var resumePath = await db.Resumes.AsNoTracking().OrderByDescending(r => r.UploadedAtUtc).Select(r => r.StoragePath).FirstOrDefaultAsync(ct);
        var answers = (await db.SavedAnswers.AsNoTracking().ToListAsync(ct)).Select(a => (a.Question, a.Answer)).ToList();
        // Answers you typed for this application count too, even if you did not save them for next time.
        answers.AddRange((await db.PendingQuestions.Where(q => q.ApplicationId == app.Id && q.Resolved && q.Answer != null).ToListAsync(ct))
            .Select(q => (q.Prompt, q.Answer!)));

        var ctx = new ApplyContext(app.Id, job, new PlanInput(profile, resumePath, app.CoverLetter ?? "", answers), submitRequested);

        ApplyOutcome outcome;
        try { outcome = await applier.ApplyAsync(ctx, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Applier crashed for application {Id}", app.Id);
            outcome = ApplyOutcome.Failed("Browser could not run: " + ex.Message.Split('\n')[0], null, new());
        }

        foreach (var line in outcome.Log) db.ApplicationLogs.Add(new ApplicationLog { ApplicationId = app.Id, AtUtc = DateTime.UtcNow, Message = line });

        // Replace any unanswered questions from the last run.
        db.PendingQuestions.RemoveRange(db.PendingQuestions.Where(q => q.ApplicationId == app.Id && !q.Resolved));
        foreach (var (kind, prompt) in outcome.Questions)
            db.PendingQuestions.Add(new PendingQuestion { ApplicationId = app.Id, Kind = kind, Prompt = prompt, CreatedAtUtc = DateTime.UtcNow });

        switch (outcome.Kind)
        {
            case OutcomeKind.Closed:
                await MarkJobClosedAsync(job.Id, outcome.Message, ct);
                await Set(ApplicationStatus.Cancelled, outcome.Message, outcome.ScreenshotFile);
                break;
            case OutcomeKind.Submitted:
                app.SubmittedAtUtc = DateTime.UtcNow;
                await Set(ApplicationStatus.Submitted, "Application submitted", outcome.ScreenshotFile);
                break;
            case OutcomeKind.Filled:
                await Set(ApplicationStatus.AwaitingApproval, "Ready for your approval", outcome.ScreenshotFile);
                break;
            case OutcomeKind.ManualOnly:
                // Open job, but this site cannot be auto-applied (custom form or bot check). Not a failure: it becomes a Manual job with a draft.
                var moved = await MoveToManualAsync(job.Id, outcome.Message, ct);
                if (outcome.ScreenshotFile is not null) app.ScreenshotFile = outcome.ScreenshotFile;
                await DraftAsync(app, moved, Set, ct, outcome.Message);
                break;
            case OutcomeKind.NeedsInput or OutcomeKind.Blocked:
                await Set(ApplicationStatus.PausedNeedsInput, "Paused: " + outcome.Message, outcome.ScreenshotFile);
                break;
            default:
                await Set(ApplicationStatus.Failed, "Failed: " + outcome.Message, outcome.ScreenshotFile);
                await alerts.RaiseAsync("apply-failed", $"{job.Title} at {(job.Company.Length > 0 ? job.Company : job.SourceKey)}: {outcome.Message}", ct);
                break;
        }
        }

    /// <summary>
    /// Email and Manual apply (and ATS without an applier, e.g. Workable). Nothing is ever submitted here.
    /// Manual: with Playwright MCP switched on, the apply page is opened and pre-filled in your browser.
    /// </summary>
    private async Task DraftAsync(JobApplication app, Job job, Func<ApplicationStatus, string, string?, Task> set, CancellationToken ct, string? whyManual = null)
    {
        if (whyManual is not null)
        {
            // Say why auto-apply did not run, then prepare the draft as for any Manual job.
            var inner = set;
            set = (status, message, shot) => inner(status, status == ApplicationStatus.DraftReady ? $"{whyManual} {message}" : message, shot);
        }
        if (job.ApplyCategory == ApplyCategory.Email)
        {
            await set(ApplicationStatus.DraftReady, $"Email draft ready for {job.ApplyEmail}. Attach your resume and send it from your mail app.", null);
            return;
        }
        if (!drafter.Enabled)
        {
            await set(ApplicationStatus.DraftReady, "Cover letter and link are ready. Open the link and apply (switch on Playwright MCP to pre-fill the form).", null);
            return;
        }

        var profile = await db.Profiles.AsNoTracking().FirstAsync(ct);
        var resumePath = await db.Resumes.AsNoTracking().OrderByDescending(r => r.UploadedAtUtc).Select(r => r.StoragePath).FirstOrDefaultAsync(ct);
        var answers = (await db.SavedAnswers.AsNoTracking().ToListAsync(ct)).Select(a => (a.Question, a.Answer)).ToList();
        var outcome = await drafter.PrepareAsync(new ApplyContext(app.Id, job, new PlanInput(profile, resumePath, app.CoverLetter ?? "", answers), Submit: false), ct);
        foreach (var line in outcome.Log) db.ApplicationLogs.Add(new ApplicationLog { ApplicationId = app.Id, AtUtc = DateTime.UtcNow, Message = line });
        switch (outcome.Kind)
        {
            case OutcomeKind.Closed:
                await MarkJobClosedAsync(job.Id, outcome.Message, ct);
                await set(ApplicationStatus.Cancelled, outcome.Message, null);
                break;
            case OutcomeKind.Filled:
                await set(ApplicationStatus.DraftReady, outcome.Message, null);
                break;
            default:
                await set(ApplicationStatus.DraftReady, "Browser pre-fill did not work (" + outcome.Message + "). The link and cover letter are ready, apply yourself.", null);
                break;
        }
    }

    private async Task<Job> MoveToManualAsync(int jobId, string reason, CancellationToken ct)
    {
        var job = await db.Jobs.FirstAsync(j => j.Id == jobId, ct);
        job.ApplyCategory = ApplyCategory.Manual;
        job.AtsKey = null;
        job.CanAutoApply = false;
        job.CategoryReason = TextUtil.Truncate(reason, 300);
        await db.SaveChangesAsync(ct);
        return job;
    }

    private async Task MarkJobClosedAsync(int jobId, string reason, CancellationToken ct)
    {
        var job = await db.Jobs.FirstAsync(j => j.Id == jobId, ct);
        JobClassificationService.MarkClosed(job, reason);
        job.PageCheckedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
