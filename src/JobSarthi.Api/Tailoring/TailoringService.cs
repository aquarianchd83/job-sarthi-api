using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Tailoring;

/// <summary>Tailors one job and saves the result on its application. Used by the Tailor button and by Apply.</summary>
public class TailoringService(JobSarthiDbContext db, TailorOrchestrator orchestrator)
{
    public async Task<TailorInput?> LoadInputAsync(int jobId, CancellationToken ct)
    {
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return null;
        var profile = await db.Profiles.AsNoTracking().FirstAsync(ct);
        var resumeText = await db.Resumes.AsNoTracking().OrderByDescending(r => r.UploadedAtUtc)
            .Select(r => r.ParsedText).FirstOrDefaultAsync(ct) ?? "";
        return new TailorInput(profile, resumeText, job);
    }

    /// <returns>The application, or an error message when the job is missing or the profile is empty.</returns>
    public async Task<(JobApplication? App, string? Error)> RunAsync(int jobId, CancellationToken ct)
    {
        var input = await LoadInputAsync(jobId, ct);
        if (input is null) return (null, "Job not found.");
        if (input.Profile.Skills.Count == 0 && string.IsNullOrWhiteSpace(input.Profile.FullName))
            return (null, "Upload your resume and save your profile first.");

        var settings = await db.Settings.AsNoTracking().FirstAsync(ct);
        var docs = await orchestrator.TailorAsync(input, settings, ct);

        var app = await db.Applications.FirstOrDefaultAsync(a => a.JobId == jobId, ct);
        if (app is null)
        {
            app = new JobApplication { JobId = jobId, Status = ApplicationStatus.Shortlisted, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
            db.Applications.Add(app);
        }
        app.CoverLetter = docs.CoverLetter;
        app.TailoredSkills = docs.OrderedSkills.ToList();
        app.MatchedKeywords = docs.MatchedKeywords.ToList();
        app.MissingKeywords = docs.MissingKeywords.ToList();
        app.TailorEngine = docs.Engine;
        app.TailorNote = docs.Note;
        app.TailoredAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        db.ApplicationLogs.Add(new ApplicationLog
        {
            ApplicationId = app.Id, AtUtc = DateTime.UtcNow,
            Message = $"Tailored resume and cover letter ({docs.Engine})" + (docs.Note is null ? "" : ". " + docs.Note),
        });
        await db.SaveChangesAsync(ct);
        return (app, null);
    }
}
