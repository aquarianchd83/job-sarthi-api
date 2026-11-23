using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Data;

public static class DbSeeder
{
    private record SourceSeed(string Key, string DisplayName, SourceCapability Capability, bool Enabled);

    /// <summary>
    /// One-time conversion of the old "locations list + one work mode" rule into per-place preferences,
    /// so nobody's saved rule changes meaning. Old places become places with the old work mode (or all three).
    /// </summary>
    public static async Task UpgradeOldLocationRulesAsync(JobSarthiDbContext db)
    {
        var rule = await db.Rules.FirstAsync();
        if (rule.LocationPrefs.Count > 0 || (rule.Locations.Count == 0 && rule.WorkMode is null)) return;

        var all = new List<string> { "Remote", "Hybrid", "Onsite" };
        var modes = rule.WorkMode is { } m ? new List<string> { m.ToString() } : all;
        var places = rule.Locations.Where(l => !l.Equals("remote", StringComparison.OrdinalIgnoreCase)).ToList();
        var listedRemote = rule.Locations.Any(l => l.Equals("remote", StringComparison.OrdinalIgnoreCase));

        rule.LocationPrefs = places.Select(p => new LocationPref { Place = p, Modes = modes.ToList() }).ToList();
        // Old behaviour: with a list of places, remote jobs open to anywhere passed only if "Remote" was in the list.
        rule.OtherModes = places.Count == 0 ? modes
                        : listedRemote && modes.Contains("Remote") ? new List<string> { "Remote" }
                        : new List<string>();
        rule.Locations = new();
        rule.WorkMode = null;
        await db.SaveChangesAsync();
    }

    public static async Task SeedAsync(JobSarthiDbContext db, string contentRoot)
    {
        await db.Database.MigrateAsync();

        if (!await db.JobSources.AnyAsync())
        {
            var path = Path.Combine(contentRoot, "Data", "Seed", "sources.seed.json");
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
            var seeds = JsonSerializer.Deserialize<List<SourceSeed>>(await File.ReadAllTextAsync(path), opts) ?? new();
            db.JobSources.AddRange(seeds.Select(s => new JobSource
            {
                Key = s.Key, DisplayName = s.DisplayName, Capability = s.Capability, Enabled = s.Enabled
            }));
        }

        if (!await db.Rules.AnyAsync()) db.Rules.Add(new Rule());          // empty rules = nothing filtered out
        else await UpgradeOldLocationRulesAsync(db);
        if (!await db.Profiles.AnyAsync()) db.Profiles.Add(new UserProfile());

        if (!await db.Settings.AnyAsync())
            db.Settings.Add(new AppSetting()); // defaults: 09:00 IST, 10/day, 90s gap, Approval mode

        await db.SaveChangesAsync();
    }
}
