using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Classification;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Enums;
using JobSarthi.Api.Filtering;

namespace JobSarthi.Api.Controllers;

/// <param name="ApplyCategory">Api, Email or Manual. Null for closed jobs (and jobs not classified yet).</param>
public record JobDto(int Id, string SourceKey, string Title, string Company, string Location,
    string WorkMode, string Url, bool CanAutoApply, int? FitScore, DateTime FetchedAtUtc,
    string? ApplyCategory, string? AtsKey, string? ApplyUrl, string? ApplyEmail, string? CategoryReason,
    bool IsClosed, string? ClosedReason);

public record CategoryCounts(int Api, int Email, int Manual, int Unclassified, int Closed);
public record ClassifyResponse(int Reclassified, ScoringSummary Scoring, PageCheckSummary? Pages, CategoryCounts Shortlist);

[ApiController]
[Route("api/[controller]")]
public class JobsController(JobSarthiDbContext db, JobClassificationService classification, JobScoringService scoring) : ControllerBase
{
    /// <summary>Fetched jobs, newest first. Jobs that no longer accept applications are hidden unless includeClosed=true.</summary>
    [HttpGet]
    public async Task<IEnumerable<JobDto>> List([FromQuery] string? source, [FromQuery] ApplyCategory? category,
        [FromQuery] bool includeClosed = false, [FromQuery] int take = 100)
    {
        var q = db.Jobs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(source)) q = q.Where(j => j.SourceKey == source);
        if (!includeClosed) q = q.Where(j => !j.IsClosed);
        if (category is not null) q = q.Where(j => j.ApplyCategory == category);
        return await q.OrderByDescending(j => j.FetchedAtUtc).ThenByDescending(j => j.Id)
            .Take(Math.Clamp(take, 1, 500))
            .Select(j => new JobDto(j.Id, j.SourceKey, j.Title, j.Company, j.Location,
                j.WorkMode.ToString(), j.Url, j.CanAutoApply, j.FitScore, j.FetchedAtUtc,
                j.ApplyCategory == null ? null : j.ApplyCategory.ToString(), j.AtsKey, j.ApplyUrl, j.ApplyEmail, j.CategoryReason,
                j.IsClosed, j.ClosedReason))
            .ToListAsync();
    }

    [HttpGet("count")]
    public async Task<object> Count() => new { total = await db.Jobs.CountAsync(), closed = await db.Jobs.CountAsync(j => j.IsClosed) };

    /// <summary>How the shortlist splits into API / Email / Manual apply, plus how many jobs were ignored as closed.</summary>
    [HttpGet("categories")]
    public async Task<CategoryCounts> Categories([FromQuery] bool shortlistedOnly = true) => await CountsAsync(shortlistedOnly);

    /// <summary>
    /// Re-sorts every job into API / Email / Manual apply, re-scores, then (checkPages=true) opens the apply pages
    /// of the best shortlisted jobs to drop the ones that no longer accept applications.
    /// </summary>
    [HttpPost("classify")]
    public async Task<ClassifyResponse> Classify([FromQuery] bool checkPages = true, [FromQuery] int max = 40, CancellationToken ct = default)
    {
        var n = await classification.ReclassifyAllAsync(ct);
        var s = await scoring.ScoreAllAsync(ct);
        var pages = checkPages ? await classification.CheckApplyPagesAsync(max, ct) : null;
        return new ClassifyResponse(n, s, pages, await CountsAsync(true));
    }

    /// <summary>Reads one job's apply page now, to see whether it still takes applications.</summary>
    [HttpPost("{id:int}/check-page")]
    public async Task<ActionResult<JobDto>> CheckPage(int id, CancellationToken ct)
    {
        if (!await db.Jobs.AnyAsync(j => j.Id == id, ct)) return NotFound();
        await classification.CheckApplyPagesAsync(1, ct, new[] { id });
        return (await List(null, null, includeClosed: true, take: 500)).FirstOrDefault(j => j.Id == id) is { } dto ? dto : NotFound();
    }

    private async Task<CategoryCounts> CountsAsync(bool shortlistedOnly)
    {
        var q = db.Jobs.AsNoTracking();
        var closed = await q.CountAsync(j => j.IsClosed);
        if (shortlistedOnly) q = q.Where(j => j.Shortlisted);
        var groups = await q.Where(j => !j.IsClosed).GroupBy(j => j.ApplyCategory).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        int C(ApplyCategory? c) => groups.FirstOrDefault(x => x.Key == c)?.Count ?? 0;
        return new CategoryCounts(C(ApplyCategory.Api), C(ApplyCategory.Email), C(ApplyCategory.Manual), C(null), closed);
    }
}
