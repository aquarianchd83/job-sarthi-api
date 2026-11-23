using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Filtering;

namespace JobSarthi.Api.Controllers;

public record LocationPrefDto(string Place, List<string> Modes);

/// <param name="LocationPrefs">Work modes you accept in each place you list, e.g. Pune: Hybrid and Onsite.</param>
/// <param name="OtherModes">Work modes you accept everywhere else, e.g. Remote.</param>
public record RuleDto(List<string> Roles, int MinExperience, int MaxExperience, List<LocationPrefDto> LocationPrefs,
    List<string> OtherModes, decimal? MinSalaryLpa, List<string> Keywords, List<string> BlacklistedCompanies, int MinSkillMatches,
    int MinFitScore, int MaxFitScore);

[ApiController]
[Route("api/[controller]")]
public class RulesController(JobSarthiDbContext db, JobScoringService scoring) : ControllerBase
{
    [HttpGet]
    public async Task<RuleDto> Get()
    {
        var r = await db.Rules.AsNoTracking().FirstAsync();
        return new RuleDto(r.Roles, r.MinExperience, r.MaxExperience,
            r.LocationPrefs.Select(p => new LocationPrefDto(p.Place, p.Modes)).ToList(), r.OtherModes,
            r.MinSalaryLpa, r.Keywords, r.BlacklistedCompanies, r.MinSkillMatches, r.MinFitScore, r.MaxFitScore);
    }

    /// <summary>Saves the rules and immediately re-filters every stored job.</summary>
    [HttpPut]
    public async Task<IActionResult> Put(RuleDto dto, CancellationToken ct)
    {
        if (dto.MinExperience < 0 || dto.MaxExperience < 0 || (dto.MaxExperience > 0 && dto.MaxExperience < dto.MinExperience))
            return BadRequest("Max experience must be at least the min.");
        if (dto.MinFitScore is < 0 or > 100 || dto.MaxFitScore is < 0 or > 100) return BadRequest("Fit scores must be between 0 and 100.");
        if (dto.MinFitScore > dto.MaxFitScore) return BadRequest("The lowest fit score cannot be above the highest.");

        static List<string> Clean(IEnumerable<string> xs) =>
            xs.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Keep only real modes, in a fixed order, without repeats.
        static List<string>? Modes(IEnumerable<string> xs)
        {
            var list = xs.Select(x => x.Trim()).ToList();
            if (list.Any(x => !RuleFilter.AllModes.Contains(x, StringComparer.OrdinalIgnoreCase))) return null;
            return RuleFilter.AllModes.Where(m => list.Contains(m, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        var prefs = new List<LocationPref>();
        foreach (var p in dto.LocationPrefs)
        {
            var place = p.Place.Trim();
            if (place.Length == 0) continue;
            if (place.Length > 100) return BadRequest("A place name is too long.");
            if (prefs.Any(x => x.Place.Equals(place, StringComparison.OrdinalIgnoreCase))) return BadRequest($"\"{place}\" is listed twice.");
            var modes = Modes(p.Modes);
            if (modes is null) return BadRequest("Work mode must be Remote, Hybrid or Onsite.");
            prefs.Add(new LocationPref { Place = place, Modes = modes });
        }
        var other = Modes(dto.OtherModes);
        if (other is null) return BadRequest("Work mode must be Remote, Hybrid or Onsite.");

        var r = await db.Rules.FirstAsync(ct);
        r.Roles = Clean(dto.Roles);
        r.MinExperience = dto.MinExperience;
        r.MaxExperience = dto.MaxExperience;
        r.LocationPrefs = prefs;
        r.OtherModes = other;
        r.Locations = new();     // old fields are no longer used
        r.WorkMode = null;
        r.MinSalaryLpa = dto.MinSalaryLpa;
        r.Keywords = Clean(dto.Keywords);
        r.BlacklistedCompanies = Clean(dto.BlacklistedCompanies);
        r.MinSkillMatches = Math.Clamp(dto.MinSkillMatches, 0, 10);
        r.MinFitScore = dto.MinFitScore;
        r.MaxFitScore = dto.MaxFitScore;
        await db.SaveChangesAsync(ct);

        return Ok(await scoring.ScoreAllAsync(ct));
    }
}
