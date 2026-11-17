using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Common;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;
using JobSarthi.Api.Notifications;

namespace JobSarthi.Api.Applying;

/// <summary>
/// Runs one application end to end: checks the daily limit and the gap between applies, picks the applier for the source,
/// then turns the outcome into a status, a log and (when needed) questions for you.
/// </summary>
public class ApplyOrchestrator(JobSarthiDbContext db, IEnumerable<IJobApplier> appliers, AlertService alerts, ILogger<ApplyOrchestrator> log)
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

        var applier = appliers.FirstOrDefault(a => a.SourceKey == job.SourceKey);
        if (applier is null) { await Set(ApplicationStatus.ManualApply, $"{job.SourceKey} cannot be applied to automatically. Apply on the site."); return; }

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
            case OutcomeKind.Submitted:
                app.SubmittedAtUtc = DateTime.UtcNow;
                await Set(ApplicationStatus.Submitted, "Application submitted", outcome.ScreenshotFile);
                break;
            case OutcomeKind.Filled:
                await Set(ApplicationStatus.AwaitingApproval, "Ready for your approval", outcome.ScreenshotFile);
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
}
