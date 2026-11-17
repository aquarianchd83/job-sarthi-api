using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Data;

namespace JobSarthi.Api.Controllers;

public record JobDto(int Id, string SourceKey, string Title, string Company, string Location,
    string WorkMode, string Url, bool CanAutoApply, int? FitScore, DateTime FetchedAtUtc);

[ApiController]
[Route("api/[controller]")]
public class JobsController(JobSarthiDbContext db) : ControllerBase
{
    /// <summary>All fetched jobs, newest first. Filtering and scoring arrive in Stage 3.</summary>
    [HttpGet]
    public async Task<IEnumerable<JobDto>> List([FromQuery] string? source, [FromQuery] int take = 100)
    {
        var q = db.Jobs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(source)) q = q.Where(j => j.SourceKey == source);
        return await q.OrderByDescending(j => j.FetchedAtUtc).ThenByDescending(j => j.Id)
            .Take(Math.Clamp(take, 1, 500))
            .Select(j => new JobDto(j.Id, j.SourceKey, j.Title, j.Company, j.Location,
                j.WorkMode.ToString(), j.Url, j.CanAutoApply, j.FitScore, j.FetchedAtUtc))
            .ToListAsync();
    }

    [HttpGet("count")]
    public async Task<object> Count() => new { total = await db.Jobs.CountAsync() };
}
