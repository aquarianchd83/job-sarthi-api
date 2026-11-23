using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Data;

namespace JobSarthi.Api.Controllers;

public record ShortlistJobDto(int Id, string SourceKey, string Title, string Company, string Location,
    string WorkMode, string Url, bool CanAutoApply, int FitScore, string? ScoreNotes, DateTime FetchedAtUtc,
    string? ApplyCategory, string? AtsKey, string? ApplyUrl, string? ApplyEmail, string? CategoryReason);
public record RejectReasonCount(string Reason, int Count);

[ApiController]
[Route("api/[controller]")]
public class ShortlistController(JobSarthiDbContext db) : ControllerBase
{
    /// <summary>Jobs that passed your rules, best fit first.</summary>
    [HttpGet]
    /// <param name="category">Api, Email or Manual. Closed jobs are never on the shortlist.</param>
    public async Task<IEnumerable<ShortlistJobDto>> List([FromQuery] int take = 100, [FromQuery] int minScore = 0, [FromQuery] int maxScore = 100,
        [FromQuery] JobSarthi.Api.Domain.Enums.ApplyCategory? category = null) =>
        await db.Jobs.AsNoTracking()
            .Where(j => j.Shortlisted && !j.IsClosed && j.FitScore >= minScore && j.FitScore <= maxScore)
            .Where(j => category == null || j.ApplyCategory == category)
            .OrderByDescending(j => j.FitScore).ThenByDescending(j => j.FetchedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .Select(j => new ShortlistJobDto(j.Id, j.SourceKey, j.Title, j.Company, j.Location, j.WorkMode.ToString(),
                j.Url, j.CanAutoApply, j.FitScore ?? 0, j.ScoreNotes, j.FetchedAtUtc,
                j.ApplyCategory == null ? null : j.ApplyCategory.ToString(), j.AtsKey, j.ApplyUrl, j.ApplyEmail, j.CategoryReason))
            .ToListAsync();

    /// <summary>Why jobs were dropped. Useful when tuning rules.</summary>
    [HttpGet("stats")]
    public async Task<object> Stats()
    {
        var total = await db.Jobs.CountAsync();
        var shortlisted = await db.Jobs.CountAsync(j => j.Shortlisted);
        // Group on the text before "(" or "," so numbers do not split reasons.
        var reasons = (await db.Jobs.Where(j => j.RejectReason != null).Select(j => j.RejectReason!).ToListAsync())
            .GroupBy(r => r.Split('(', ',')[0].Trim().TrimEnd('.'))
            .Select(g => new RejectReasonCount(g.Key, g.Count())).OrderByDescending(x => x.Count).Take(10).ToList();
        return new { total, shortlisted, rejected = total - shortlisted, topRejectReasons = reasons };
    }
}
