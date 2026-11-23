using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;
using JobSarthi.Api.Filtering;
using Xunit;

namespace JobSarthi.Api.Tests;

public class FitRangeTests
{
    [Theory]
    [InlineData(60, 60, 100, true)]
    [InlineData(100, 60, 100, true)]
    [InlineData(59, 60, 100, false)]
    [InlineData(80, 0, 70, false)]
    [InlineData(0, 0, 100, true)]
    public void Range_is_inclusive_at_both_ends(int score, int min, int max, bool inside) =>
        Assert.Equal(inside, RuleFilter.FitRangeReason(score, new Rule { MinFitScore = min, MaxFitScore = max }) is null);

    [Fact]
    public void Reason_names_the_score_and_the_range() =>
        Assert.Equal("Fit score is outside your range (45 not in 60-100)", RuleFilter.FitRangeReason(45, new Rule { MinFitScore = 60 }));

    private static JobSarthiDbContext Db(Rule rule)
    {
        var db = new JobSarthiDbContext(new DbContextOptionsBuilder<JobSarthiDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Rules.Add(rule);
        db.Profiles.Add(new UserProfile { TotalExperienceYears = 10, Skills = { "C#", ".NET", "Angular", "Azure", "SQL Server" } });
        // Strong: many skills. Weak: two skills only.
        db.Jobs.Add(new Job { Title = "Backend Engineer", Company = "A", Location = "Worldwide", WorkMode = WorkMode.Remote, ExternalId = "1", SourceKey = "t", Url = "u",
            Description = "C# .NET Angular Azure SQL Server daily" });
        db.Jobs.Add(new Job { Title = "Backend Engineer", Company = "B", Location = "Worldwide", WorkMode = WorkMode.Remote, ExternalId = "2", SourceKey = "t", Url = "u",
            Description = "We use C# and Azure." });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Jobs_below_the_minimum_fit_are_not_shortlisted_but_keep_their_score()
    {
        var db = Db(new Rule { MinFitScore = 70 });
        var summary = await new JobScoringService(db).ScoreAllAsync(default);
        var strong = db.Jobs.Single(j => j.Company == "A");
        var weak = db.Jobs.Single(j => j.Company == "B");
        Assert.True(strong.Shortlisted);
        Assert.False(weak.Shortlisted);
        Assert.NotNull(weak.FitScore);                                   // still scored
        Assert.StartsWith("Fit score is outside your range", weak.RejectReason);
        Assert.Equal(1, summary.Shortlisted);
    }

    [Fact]
    public async Task A_maximum_fit_hides_the_best_matches()
    {
        var db = Db(new Rule { MinFitScore = 0, MaxFitScore = 70 });
        await new JobScoringService(db).ScoreAllAsync(default);
        Assert.False(db.Jobs.Single(j => j.Company == "A").Shortlisted);
        Assert.True(db.Jobs.Single(j => j.Company == "B").Shortlisted);
    }

    [Fact]
    public async Task Default_range_keeps_everything_that_passes_the_other_rules()
    {
        var db = Db(new Rule());
        var summary = await new JobScoringService(db).ScoreAllAsync(default);
        Assert.Equal(2, summary.Shortlisted);
    }
}
