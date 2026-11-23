using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;
using Xunit;

namespace JobSarthi.Api.Tests;

/// <summary>Rules saved before per-place modes existed must keep their meaning.</summary>
public class LocationUpgradeTests
{
    private static async Task<Rule> Upgrade(Rule old)
    {
        var db = new JobSarthiDbContext(new DbContextOptionsBuilder<JobSarthiDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Rules.Add(old);
        await db.SaveChangesAsync();
        await DbSeeder.UpgradeOldLocationRulesAsync(db);
        return await db.Rules.AsNoTracking().SingleAsync();
    }

    [Fact]
    public async Task Old_places_with_remote_listed_become_places_plus_remote_elsewhere()
    {
        var r = await Upgrade(new Rule { Locations = { "Bengaluru", "Pune", "Remote" } });
        Assert.Equal(new[] { "Bengaluru", "Pune" }, r.LocationPrefs.Select(p => p.Place));
        Assert.All(r.LocationPrefs, p => Assert.Equal(new[] { "Remote", "Hybrid", "Onsite" }, p.Modes));
        Assert.Equal(new[] { "Remote" }, r.OtherModes);
        Assert.Empty(r.Locations);
    }

    [Fact]
    public async Task Old_places_without_remote_accept_nothing_elsewhere()
    {
        var r = await Upgrade(new Rule { Locations = { "Pune" } });
        Assert.Empty(r.OtherModes);
    }

    [Fact]
    public async Task An_old_work_mode_alone_becomes_the_everywhere_mode()
    {
        var r = await Upgrade(new Rule { WorkMode = WorkMode.Remote });
        Assert.Empty(r.LocationPrefs);
        Assert.Equal(new[] { "Remote" }, r.OtherModes);
        Assert.Null(r.WorkMode);
    }

    [Fact]
    public async Task Empty_rules_and_already_upgraded_rules_are_left_alone()
    {
        var empty = await Upgrade(new Rule());
        Assert.Equal(3, empty.OtherModes.Count);
        var done = await Upgrade(new Rule { LocationPrefs = { new LocationPref { Place = "Pune", Modes = { "Onsite" } } }, OtherModes = new() { "Remote" } });
        Assert.Equal("Pune", Assert.Single(done.LocationPrefs).Place);
        Assert.Equal(new[] { "Remote" }, done.OtherModes);
    }

    [Fact]
    public async Task Per_place_preferences_survive_a_save_and_reload()
    {
        var r = await Upgrade(new Rule { LocationPrefs = { new LocationPref { Place = "Pune", Modes = { "Hybrid", "Onsite" } } }, OtherModes = new() { "Remote" } });
        Assert.Equal(new[] { "Hybrid", "Onsite" }, r.LocationPrefs[0].Modes);
    }
}
