using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;

namespace JobSarthi.Api.Controllers;

public record SourceDto(string Key, string DisplayName, string Capability, bool Enabled,
    DateTime? LastRunUtc, int LastJobCount, string? LastError, List<string> CompanySlugs);
public record ToggleRequest(bool Enabled);
public record SlugRequest(string Slug);
public record SuggestionDto(int Id, string SourceKey, string Slug, int SeenCount, string? ExampleTitle, string? ExampleCompany, DateTime LastSeenUtc);
public record AutoAddRequest(bool Enabled);

[ApiController]
[Route("api/[controller]")]
public class SourcesController : ControllerBase
{
    private static readonly string[] SlugSources = { "greenhouse", "lever", "ashby" };
    private readonly JobSarthiDbContext _db;
    private readonly JobSarthi.Api.Sources.CompanyDiscoveryService _discovery;
    public SourcesController(JobSarthiDbContext db, JobSarthi.Api.Sources.CompanyDiscoveryService discovery) { _db = db; _discovery = discovery; }

    /// <summary>Greenhouse/Lever/Ashby companies found in other sources' apply links, not yet in your list. Most seen first.</summary>
    [HttpGet("suggestions")]
    public async Task<object> Suggestions()
    {
        var items = await _db.CompanySuggestions.AsNoTracking().Where(s => s.Status == SuggestionStatus.Pending)
            .OrderByDescending(s => s.SeenCount).ThenBy(s => s.Slug)
            .Select(s => new SuggestionDto(s.Id, s.SourceKey, s.Slug, s.SeenCount, s.ExampleTitle, s.ExampleCompany, s.LastSeenUtc))
            .ToListAsync();
        var autoAdd = (await _db.Settings.AsNoTracking().FirstAsync()).AutoAddDiscoveredCompanies;
        return new { autoAdd, items };
    }

    /// <summary>Scan stored jobs now (the daily run does this too).</summary>
    [HttpPost("suggestions/scan")]
    public async Task<object> Scan(CancellationToken ct)
    {
        var autoAdd = (await _db.Settings.AsNoTracking().FirstAsync(ct)).AutoAddDiscoveredCompanies;
        return await _discovery.DiscoverAsync(autoAdd, ct);
    }

    [HttpPost("suggestions/{id:int}/accept")]
    public async Task<IActionResult> Accept(int id, CancellationToken ct)
    {
        var s = await _db.CompanySuggestions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound();
        await _discovery.AcceptAsync(s, ct);
        return NoContent();
    }

    [HttpPost("suggestions/accept-all")]
    public async Task<object> AcceptAll(CancellationToken ct)
    {
        var pending = await _db.CompanySuggestions.Where(x => x.Status == SuggestionStatus.Pending).ToListAsync(ct);
        foreach (var s in pending) await _discovery.AcceptAsync(s, ct);
        return new { added = pending.Count };
    }

    /// <summary>Hide a suggestion for good. It is not suggested again.</summary>
    [HttpPost("suggestions/{id:int}/dismiss")]
    public async Task<IActionResult> Dismiss(int id)
    {
        var s = await _db.CompanySuggestions.FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();
        s.Status = SuggestionStatus.Dismissed;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>On: companies found in job links are added to the list automatically during the daily run.</summary>
    [HttpPut("suggestions/auto-add")]
    public async Task<IActionResult> SetAutoAdd(AutoAddRequest req)
    {
        var s = await _db.Settings.FirstAsync();
        s.AutoAddDiscoveredCompanies = req.Enabled;
        await _db.SaveChangesAsync();
        return NoContent();
    }

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
        // Removed by you: never suggested or auto-added again.
        var sug = await _db.CompanySuggestions.FirstOrDefaultAsync(x => x.SourceKey == key && x.Slug == slug);
        if (sug is not null) sug.Status = SuggestionStatus.Dismissed;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
