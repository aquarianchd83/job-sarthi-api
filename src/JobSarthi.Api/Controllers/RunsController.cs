using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Data;
using JobSarthi.Api.Filtering;
using JobSarthi.Api.Scheduling;
using JobSarthi.Api.Sources;

namespace JobSarthi.Api.Controllers;

public record FetchRunResponse(List<SourceRunResult> Sources, ScoringSummary Scoring);
public record DailyRunDto(int Id, string Trigger, DateTime StartedAtUtc, DateTime? FinishedAtUtc, int Fetched, int NewJobs,
    int Shortlisted, int Queued, string? Error, List<string> Notes);
public record RunStatus(bool Running);

[ApiController]
[Route("api/[controller]")]
public class RunsController(JobFetchService fetch, JobScoringService scoring, JobSarthiDbContext db, DailyRunLauncher launcher) : ControllerBase
{
    /// <summary>Fetch now, then filter and score. Without ?source= it runs every enabled source.</summary>
    [HttpPost("fetch")]
    public async Task<FetchRunResponse> Fetch([FromQuery] string? source, CancellationToken ct)
    {
        var results = await fetch.RunAsync(source, ct);
        return new FetchRunResponse(results, await scoring.ScoreAllAsync(ct));
    }

    /// <summary>Re-apply the saved rules to every stored job.</summary>
    [HttpPost("score")]
    public async Task<ScoringSummary> Score(CancellationToken ct) => await scoring.ScoreAllAsync(ct);

    /// <summary>Run the whole daily routine now: fetch, filter, score, tailor and queue applications. Returns at once.</summary>
    [HttpPost("daily")]
    public IActionResult RunDaily() =>
        launcher.TryStart() ? Accepted(new RunStatus(true)) : Conflict("A run is already in progress.");

    [HttpGet("status")]
    public RunStatus Status() => new(DailyRunPipeline.IsRunning);

    /// <summary>The latest runs, newest first.</summary>
    [HttpGet]
    public async Task<IEnumerable<DailyRunDto>> History([FromQuery] int take = 14)
    {
        var rows = await db.DailyRuns.AsNoTracking().OrderByDescending(r => r.StartedAtUtc).Take(Math.Clamp(take, 1, 60)).ToListAsync();
        return rows.Select(r => new DailyRunDto(r.Id, r.Trigger, r.StartedAtUtc, r.FinishedAtUtc, r.Fetched, r.NewJobs, r.Shortlisted,
            r.Queued, r.Error, (r.Notes ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList()));
    }
}
