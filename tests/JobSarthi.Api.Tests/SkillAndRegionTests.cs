using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;
using JobSarthi.Api.Filtering;
using Xunit;

namespace JobSarthi.Api.Tests;

public class SkillAndRegionTests
{
    // A .NET developer in India, with no roles or keywords set in Rules (the situation that let marketing jobs through).
    private static readonly RuleFilter.FilterContext Me = new(
        new[] { "C#", ".NET", "ASP.NET Core", "Angular", "SQL Server", "Azure", "HTML", "CSS", "Git" }, "Pune");

    private static Job J(string title, string desc, string loc = "Anywhere in the World", WorkMode mode = WorkMode.Remote) =>
        new() { Title = title, Company = "Acme", Description = desc, Location = loc, WorkMode = mode };

    [Fact]
    public void Marketing_and_design_jobs_are_dropped_by_the_skills_check()
    {
        var rule = new Rule();
        Assert.StartsWith("Only 0 of your skills", RuleFilter.Check(J("Director of Content Strategy", "Lead our content team. 3+ years of experience."), rule, Me));
        Assert.StartsWith("Only 0 of your skills", RuleFilter.Check(J("Paid Ads Specialist (Meta & Google)", "PPC campaigns, HTML emails and CSS tweaks."), rule, Me)); // HTML and CSS are generic
    }

    [Fact]
    public void A_job_mentioning_two_or_more_of_your_skills_passes()
    {
        var rule = new Rule();
        Assert.Null(RuleFilter.Check(J("Backend Engineer", "We build with C# and ASP.NET Core on Azure."), rule, Me));
    }

    [Fact]
    public void One_matching_skill_is_not_enough_until_you_lower_the_minimum()
    {
        var job = J("Software Engineer", "Our stack is mostly Python with some Azure.");
        Assert.NotNull(RuleFilter.Check(job, new Rule(), Me));
        Assert.Null(RuleFilter.Check(job, new Rule { MinSkillMatches = 1 }, Me));
        Assert.Null(RuleFilter.Check(job, new Rule { MinSkillMatches = 0 }, Me));
    }

    [Fact]
    public void Without_a_profile_skill_list_the_check_is_skipped()
    {
        var job = J("Director of Content Strategy", "Lead our content team.");
        Assert.Null(RuleFilter.Check(job, new Rule(), new RuleFilter.FilterContext(Array.Empty<string>(), "")));
        Assert.Null(RuleFilter.Check(job, new Rule(), null));
    }

    [Fact]
    public void Alert_jobs_with_only_a_title_skip_the_skills_check()
    {
        var alert = new Job { Title = "Lead .NET Developer", Company = "", Location = "", Description = "" };
        Assert.Null(RuleFilter.Check(alert, new Rule(), Me));
    }

    [Theory]
    [InlineData("USA Only")]
    [InlineData("Americas Only")]
    [InlineData("California")]
    [InlineData("Europe")]
    [InlineData("Canada Only")]
    public void Jobs_restricted_to_another_region_are_dropped(string location) =>
        Assert.StartsWith("Location restricted", RuleFilter.Check(J("Backend Engineer", "C# and .NET and Azure", location), new Rule(), Me));

    [Theory]
    [InlineData("Anywhere in the World")]
    [InlineData("Worldwide")]
    [InlineData("Remote")]
    [InlineData("India")]
    [InlineData("Asia Only")]
    [InlineData("Pune")]
    public void Open_or_local_locations_pass(string location) =>
        Assert.Null(RuleFilter.Check(J("Backend Engineer", "C# and .NET and Azure", location), new Rule(), Me));

    [Fact]
    public void A_region_you_list_yourself_is_allowed()
    {
        var job = J("Backend Engineer", "C# and .NET and Azure", "Europe");
        Assert.NotNull(RuleFilter.Check(job, new Rule(), Me));
        Assert.Null(RuleFilter.Check(job, new Rule { LocationPrefs = { new LocationPref { Place = "Europe", Modes = { "Remote", "Hybrid", "Onsite" } } } }, Me));
    }

    [Fact]
    public void Skill_matches_ignore_generic_skills_and_use_whole_words()
    {
        var matches = RuleFilter.SkillMatches(J("Dev", "HTML CSS Git and angularjs and C# daily"), Me.Skills);
        Assert.Equal(new[] { "C#" }, matches);          // "angularjs" is not "Angular", HTML/CSS/Git are generic
    }

    [Fact]
    public void More_matching_skills_score_higher_and_are_named_in_the_notes()
    {
        var rule = new Rule();
        var strong = JobScorer.Score(J("Backend Engineer", "C# .NET ASP.NET Core Angular SQL Server Azure"), rule, 16, Me.Skills);
        var weak = JobScorer.Score(J("Backend Engineer", "C# only here and a bit of Azure"), rule, 16, Me.Skills);
        Assert.True(strong.Score > weak.Score, $"{strong.Score} vs {weak.Score}");
        Assert.Contains("Skills 6", strong.Notes);
        Assert.InRange(strong.Score, 0, 100);
    }
}
