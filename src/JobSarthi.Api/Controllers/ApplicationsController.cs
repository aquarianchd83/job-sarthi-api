using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Applying;
using JobSarthi.Api.Common;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;
using JobSarthi.Api.Tailoring;

namespace JobSarthi.Api.Controllers;

public record ApplicationListItem(int Id, int JobId, string Title, string Company, string SourceKey, string Status,
    DateTime UpdatedAtUtc, string Url, string? ApplyCategory);
public record LogLine(DateTime AtUtc, string Message);
/// <param name="Input">text | textarea | select | radio | multi | yesno.</param>
/// <param name="Options">The form's own choices for select / radio / multi.</param>
public record PendingQuestionDto(int Id, string Kind, string Prompt, string Input, List<string> Options);
public record ApplicationDetail(int Id, int JobId, string Title, string Company, string SourceKey, string Status, string Url,
    DateTime UpdatedAtUtc, DateTime? SubmittedAtUtc, string? CoverLetter, bool HasScreenshot,
    List<LogLine> Log, List<PendingQuestionDto> Pending,
    string? ApplyCategory, string? ApplyUrl, string? ApplyEmail, string? EmailSubject, string? CategoryReason);
public record AnswerRequest(int QuestionId, string Answer, bool Save);

[ApiController]
[Route("api/[controller]")]
public class ApplicationsController(JobSarthiDbContext db, ApplyQueue queue, ApplyStarter starter, TailoringService tailoring, ScreenshotStore shots) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<ApplicationListItem>> List() =>
        await (from a in db.Applications.AsNoTracking()
               join j in db.Jobs.AsNoTracking() on a.JobId equals j.Id
               where a.Status != ApplicationStatus.Shortlisted || a.TailoredAtUtc == null
               orderby a.UpdatedAtUtc descending
               select new ApplicationListItem(a.Id, a.JobId, j.Title, j.Company, j.SourceKey, a.Status.ToString(), a.UpdatedAtUtc, j.ApplyUrl ?? j.Url,
                   j.ApplyCategory == null ? null : j.ApplyCategory.ToString()))
            .ToListAsync();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApplicationDetail>> Get(int id) =>
        await Detail(id) is { } d ? d : NotFound();

    /// <summary>Tailor if needed, then fill the form in the background. In approval mode it stops before submitting.</summary>
    [HttpPost("apply/{jobId:int}")]
    public async Task<ActionResult<ApplicationDetail>> Apply(int jobId, CancellationToken ct)
    {
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return NotFound("Job not found.");
        if (job.IsClosed) return Conflict("This job no longer accepts applications. It is ignored.");
        if (!job.CanAutoApply) return BadRequest("This job cannot be applied to automatically. Use Prepare draft (email or manual apply).");
        if (!await db.Resumes.AnyAsync(ct)) return BadRequest("Upload your resume first (Resume & Profile).");

        var existing = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.JobId == jobId, ct);
        if (existing?.Status == ApplicationStatus.Submitted) return Conflict("You already applied to this job.");
        if (existing is not null && queue.IsActive(existing.Id)) return Conflict("This application is already running.");

        // Keep an existing tailored letter, including your edits. Tailor only when there is none.
        var app = existing is { TailoredAtUtc: not null } ? await db.Applications.FirstAsync(a => a.Id == existing.Id, ct) : null;
        if (app is null)
        {
            var (made, error) = await tailoring.RunAsync(jobId, ct);
            if (made is null) return BadRequest(error);
            app = made;
        }
        return await Start(app, ct);
    }

    /// <summary>
    /// Email and Manual apply: tailor a cover letter and get everything ready, never submit.
    /// Email: subject and body ready for your mail app. Manual: with Playwright MCP on, the apply page opens pre-filled in your browser.
    /// </summary>
    [HttpPost("draft/{jobId:int}")]
    public async Task<ActionResult<ApplicationDetail>> Draft(int jobId, CancellationToken ct)
    {
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return NotFound("Job not found.");
        if (job.IsClosed) return Conflict("This job no longer accepts applications. It is ignored.");

        var existing = await db.Applications.FirstOrDefaultAsync(a => a.JobId == jobId, ct);
        if (existing?.Status == ApplicationStatus.Submitted) return Conflict("You already applied to this job.");
        if (existing is not null && queue.IsActive(existing.Id)) return Conflict("This application is already running.");
        var app = existing is { TailoredAtUtc: not null } ? existing : null;
        if (app is null)
        {
            var (made, error) = await tailoring.RunAsync(jobId, ct);
            if (made is null) return BadRequest(error);
            app = made;
        }
        // Never submits: the orchestrator only drafts for Email/Manual, and only fills for an ATS.
        return await Start(app, ct, submit: false, note: "Preparing a draft (will not submit)");
    }

    [HttpPost("{id:int}/approve")]
    public async Task<ActionResult<ApplicationDetail>> Approve(int id, CancellationToken ct)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (app is null) return NotFound();
        if (app.Status != ApplicationStatus.AwaitingApproval) return Conflict("Only applications waiting for approval can be approved.");
        return await Start(app, ct, submit: true, note: "You approved the application");
    }

    [HttpPost("{id:int}/retry")]
    public async Task<ActionResult<ApplicationDetail>> Retry(int id, CancellationToken ct)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (app is null) return NotFound();
        if (app.Status is ApplicationStatus.Submitted or ApplicationStatus.Applying) return Conflict("This application cannot be retried now.");
        return await Start(app, ct, note: "Retrying");
    }

    [HttpPost("{id:int}/reject")]
    public Task<ActionResult<ApplicationDetail>> Reject(int id) => SetStatus(id, ApplicationStatus.Cancelled, "You cancelled this application");

    [HttpPost("{id:int}/manual")]
    public Task<ActionResult<ApplicationDetail>> Manual(int id) => SetStatus(id, ApplicationStatus.ManualApply, "You will apply manually on the site");

    /// <summary>For jobs you applied to yourself (Naukri, LinkedIn, RemoteOK...). Keeps the tracker complete.</summary>
    [HttpPost("mark-applied/{jobId:int}")]
    public async Task<ActionResult<ApplicationDetail>> MarkApplied(int jobId)
    {
        if (!await db.Jobs.AnyAsync(j => j.Id == jobId)) return NotFound("Job not found.");
        var app = await db.Applications.FirstOrDefaultAsync(a => a.JobId == jobId);
        if (app is null)
        {
            app = new JobApplication { JobId = jobId, CreatedAtUtc = DateTime.UtcNow };
            db.Applications.Add(app);
        }
        app.Status = ApplicationStatus.Submitted;
        app.SubmittedAtUtc = DateTime.UtcNow;
        app.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        AddLog(app.Id, "You marked this job as applied");
        await db.SaveChangesAsync();
        return await Detail(app.Id) ?? (ActionResult<ApplicationDetail>)NotFound();
    }

    /// <summary>Answer a question the form asked. The run resumes once every question has an answer.</summary>
    [HttpPost("{id:int}/answer")]
    public async Task<ActionResult<ApplicationDetail>> Answer(int id, AnswerRequest req, CancellationToken ct)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (app is null) return NotFound();
        var q = await db.PendingQuestions.FirstOrDefaultAsync(x => x.Id == req.QuestionId && x.ApplicationId == id, ct);
        if (q is null) return NotFound("Question not found.");
        if (q.Kind != "question") return BadRequest("CAPTCHAs and codes cannot be answered here. Open the job link and apply yourself.");
        if (string.IsNullOrWhiteSpace(req.Answer)) return BadRequest("Type an answer.");

        // A choice must be one of the form's own options, so the form can be filled exactly.
        var options = ParseOptions(q.OptionsJson);
        string answer;
        if (q.InputType == "multi" && options.Count > 0)
        {
            var picked = Question.SplitMulti(req.Answer);
            var bad = picked.Where(p => !options.Contains(p)).ToList();
            if (picked.Count == 0) return BadRequest("Pick at least one option.");
            if (bad.Count > 0) return BadRequest($"Not an option on the form: {string.Join(", ", bad)}");
            answer = string.Join(Question.MultiSeparator, options.Where(picked.Contains)); // form order
        }
        else if (q.InputType is "select" or "radio" && options.Count > 0)
        {
            answer = req.Answer.Trim();
            if (!options.Contains(answer)) return BadRequest($"Not an option on the form: {answer}");
        }
        else answer = req.Answer.Trim();

        q.Answer = answer;
        q.Resolved = true;
        if (req.Save)
        {
            var key = TextUtil.Truncate(q.Prompt, 300);
            var saved = await db.SavedAnswers.FirstOrDefaultAsync(a => a.Question == key, ct);
            if (saved is null) db.SavedAnswers.Add(new SavedAnswer { Question = key, Answer = answer });
            else saved.Answer = answer; // your newest answer wins
        }
        await db.SaveChangesAsync(ct);

        var stillOpen = await db.PendingQuestions.AnyAsync(x => x.ApplicationId == id && !x.Resolved && x.Kind == "question", ct);
        if (stillOpen) return await Detail(id) ?? (ActionResult<ApplicationDetail>)NotFound();
        return await Start(app, ct, note: "Resuming with your answers");
    }

    [HttpGet("{id:int}/screenshot")]
    public async Task<IActionResult> Screenshot(int id)
    {
        var file = await db.Applications.AsNoTracking().Where(a => a.Id == id).Select(a => a.ScreenshotFile).FirstOrDefaultAsync();
        var path = file is null ? null : shots.Resolve(file);
        return path is null ? NotFound() : PhysicalFile(path, "image/png");
    }

    // ---- helpers ----

    private static List<string> ParseOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch (System.Text.Json.JsonException) { return new(); }
    }

    private async Task<ActionResult<ApplicationDetail>> Start(JobApplication app, CancellationToken ct, bool? submit = null, string? note = null)
    {
        if (!await starter.StartAsync(app, submit, note ?? "Queued", ct)) return Conflict("This application is already running.");
        return AcceptedAtAction(nameof(Get), new { id = app.Id }, await Detail(app.Id));
    }

    private async Task<ActionResult<ApplicationDetail>> SetStatus(int id, ApplicationStatus status, string message)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == id);
        if (app is null) return NotFound();
        if (queue.IsActive(id)) return Conflict("This application is running. Wait for it to finish.");
        app.Status = status;
        app.UpdatedAtUtc = DateTime.UtcNow;
        AddLog(id, message);
        await db.SaveChangesAsync();
        return await Detail(id) ?? (ActionResult<ApplicationDetail>)NotFound();
    }

    private void AddLog(int applicationId, string message) =>
        db.ApplicationLogs.Add(new ApplicationLog { ApplicationId = applicationId, AtUtc = DateTime.UtcNow, Message = message });

    private async Task<ApplicationDetail?> Detail(int id)
    {
        var row = await (from a in db.Applications.AsNoTracking()
                         join j in db.Jobs.AsNoTracking() on a.JobId equals j.Id
                         where a.Id == id
                         select new { a, j }).FirstOrDefaultAsync();
        if (row is null) return null;
        var log = await db.ApplicationLogs.AsNoTracking().Where(l => l.ApplicationId == id).OrderBy(l => l.AtUtc).ThenBy(l => l.Id)
            .Select(l => new LogLine(l.AtUtc, l.Message)).ToListAsync();
        var pending = await db.PendingQuestions.AsNoTracking().Where(q => q.ApplicationId == id && !q.Resolved)
            .Select(q => new { q.Id, q.Kind, q.Prompt, q.InputType, q.OptionsJson }).ToListAsync();
        var pendingDtos = pending.Select(q => new PendingQuestionDto(q.Id, q.Kind, q.Prompt, q.InputType, ParseOptions(q.OptionsJson))).ToList();
        var name = await db.Profiles.AsNoTracking().Select(p => p.FullName).FirstOrDefaultAsync();
        var subject = row.j.ApplyCategory == ApplyCategory.Email
            ? $"Application for {row.j.Title}" + (string.IsNullOrWhiteSpace(name) ? "" : $" - {name}")
            : null;
        return new ApplicationDetail(row.a.Id, row.a.JobId, row.j.Title, row.j.Company, row.j.SourceKey, row.a.Status.ToString(), row.j.Url,
            row.a.UpdatedAtUtc, row.a.SubmittedAtUtc, row.a.CoverLetter, row.a.ScreenshotFile is not null, log, pendingDtos,
            row.j.ApplyCategory?.ToString(), row.j.ApplyUrl ?? row.j.Url, row.j.ApplyEmail, subject, row.j.CategoryReason);
    }
}
