using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Analysis;
using JobSarthi.Api.Data;

namespace JobSarthi.Api.Controllers;

[ApiController]
[Route("api/skills")]
public class SkillGapController(JobSarthiDbContext db) : ControllerBase
{
    /// <summary>
    /// Which skills the fetched jobs ask for, and which of them are missing from your profile.
    /// scope=field (default) looks at jobs that mention at least one of your skills; scope=all looks at every fetched job.
    /// title=.net keeps only jobs with that text in the title.
    /// </summary>
    [HttpGet("market")]
    public async Task<SkillGapResult> Market([FromQuery] string scope = "field", [FromQuery] string? title = null, CancellationToken ct = default)
    {
        var skills = (await db.Profiles.AsNoTracking().Select(p => p.Skills).FirstOrDefaultAsync(ct)) ?? new List<string>();
        var jobs = (await db.Jobs.AsNoTracking().Select(j => new { j.Title, j.Description }).ToListAsync(ct))
            .Select(j => (j.Title, j.Description)).ToList();
        return SkillGapAnalyzer.Analyze(jobs, skills, scope, title);
    }
}
