using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Enums;
using JobSarthi.Api.Filtering;

namespace JobSarthi.Api.Controllers;

public record RuleDto(List<string> Roles, int MinExperience, int MaxExperience, List<string> Locations,
    string? WorkMode, decimal? MinSalaryLpa, List<string> Keywords, List<string> BlacklistedCompanies);

[ApiController]
[Route("api/[controller]")]
public class RulesController(JobSarthiDbContext db, JobScoringService scoring) : ControllerBase
{
    [HttpGet]
    public async Task<RuleDto> Get()
    {
        var r = await db.Rules.AsNoTracking().FirstAsync();
        return new RuleDto(r.Roles, r.MinExperience, r.MaxExperience, r.Locations,
            r.WorkMode?.ToString(), r.MinSalaryLpa, r.Keywords, r.BlacklistedCompanies);
    }

    /// <summary>Saves the rules and immediately re-filters every stored job.</summary>
    [HttpPut]
    public async Task<IActionResult> Put(RuleDto dto, CancellationToken ct)
    {
        WorkMode? mode = null;
        if (!string.IsNullOrWhiteSpace(dto.WorkMode))
        {
            if (!Enum.TryParse<WorkMode>(dto.WorkMode, out var m)) return BadRequest("Unknown work mode.");
            mode = m;
        }
        if (dto.MinExperience < 0 || dto.MaxExperience < 0 || (dto.MaxExperience > 0 && dto.MaxExperience < dto.MinExperience))
            return BadRequest("Max experience must be at least the min.");

        static List<string> Clean(IEnumerable<string> xs) =>
            xs.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var r = await db.Rules.FirstAsync(ct);
        r.Roles = Clean(dto.Roles);
        r.MinExperience = dto.MinExperience;
        r.MaxExperience = dto.MaxExperience;
        r.Locations = Clean(dto.Locations);
        r.WorkMode = mode;
        r.MinSalaryLpa = dto.MinSalaryLpa;
        r.Keywords = Clean(dto.Keywords);
        r.BlacklistedCompanies = Clean(dto.BlacklistedCompanies);
        await db.SaveChangesAsync(ct);

        return Ok(await scoring.ScoreAllAsync(ct));
    }
}
