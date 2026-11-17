using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;

namespace JobSarthi.Api.Controllers;

public record SourceDto(string Key, string DisplayName, string Capability, bool Enabled,
    DateTime? LastRunUtc, int LastJobCount, string? LastError, List<string> CompanySlugs);
public record ToggleRequest(bool Enabled);
public record SlugRequest(string Slug);

[ApiController]
[Route("api/[controller]")]
public class SourcesController : ControllerBase
{
    private static readonly string[] SlugSources = { "greenhouse", "lever", "ashby" };
    private readonly JobSarthiDbContext _db;
    public SourcesController(JobSarthiDbContext db) => _db = db;

    [HttpGet]
    public async Task<IEnumerable<SourceDto>> GetAll()
    {
        var sources = await _db.JobSources.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
        var slugs = await _db.CompanySlugs.AsNoTracking().ToListAsync();
        return sources.Select(s => new SourceDto(s.Key, s.DisplayName, s.Capability.ToString(), s.Enabled,
            s.LastRunUtc, s.LastJobCount, s.LastError, slugs.Where(x => x.SourceKey == s.Key).Select(x => x.Slug).ToList()));
    }

    [HttpPut("{key}/enabled")]
    public async Task<IActionResult> Toggle(string key, ToggleRequest req)
    {
        var s = await _db.JobSources.FirstOrDefaultAsync(x => x.Key == key);
        if (s is null) return NotFound();
        s.Enabled = req.Enabled;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{key}/companies")]
    public async Task<IActionResult> AddCompany(string key, SlugRequest req)
    {
        if (!SlugSources.Contains(key)) return BadRequest("This source does not use a company list.");
        var slug = req.Slug.Trim().ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9][a-z0-9_-]{0,98}$"))
            return BadRequest("Slug may only use letters, numbers, dashes and underscores.");
        if (await _db.CompanySlugs.AnyAsync(x => x.SourceKey == key && x.Slug == slug)) return Conflict();
        _db.CompanySlugs.Add(new CompanySlug { SourceKey = key, Slug = slug });
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{key}/companies/{slug}")]
    public async Task<IActionResult> RemoveCompany(string key, string slug)
    {
        var row = await _db.CompanySlugs.FirstOrDefaultAsync(x => x.SourceKey == key && x.Slug == slug);
        if (row is null) return NotFound();
        _db.CompanySlugs.Remove(row);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
