using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;
using JobSarthi.Api.Filtering;
using JobSarthi.Api.Resume;
using Xunit;

namespace JobSarthi.Api.Tests;

public class JobScorerTests
{
    private static Rule R() => new()
    {
        Roles = { "Senior .NET Developer" }, Keywords = { "C#", "Angular", "Azure", "Kafka" },
        Locations = { "Pune" }, MinExperience = 5, MaxExperience = 12,
    };

    [Fact]
    public void Better_match_scores_higher()
    {
        var good = new Job { Title = "Senior .NET Engineer", Location = "Pune", Description = "C# Angular Azure Kafka. 4+ years of experience." };
        var weak = new Job { Title = "Senior .NET Engineer", Location = "Pune", Description = "Java shop. 9+ years of experience." };
        var g = JobScorer.Score(good, R(), 8);
        var w = JobScorer.Score(weak, R(), 6);
        Assert.True(g.Score > w.Score, $"{g.Score} should beat {w.Score}");
        Assert.InRange(g.Score, 0, 100);
        Assert.Contains("Keywords 4/4", g.Notes);
    }

    [Fact]
    public void Score_never_exceeds_100()
    {
        var j = new Job { Title = "Senior .NET Developer", Location = "Pune", Description = "C# Angular Azure Kafka 3 years of experience" };
        Assert.True(JobScorer.Score(j, R(), 10).Score <= 100);
    }

    [Fact]
    public void Resume_parser_finds_contact_skills_and_years()
    {
        const string text = "Asha Verma\nSenior .NET Developer\nasha@example.com | +91 98765 43210\n8 years of experience building C# and ASP.NET Core APIs with SQL Server, Angular and Azure DevOps.";
        var p = ResumeParser.Parse(text);
        Assert.Equal("Asha Verma", p.FullName);
        Assert.Equal("Senior .NET Developer", p.Headline);
        Assert.Equal("asha@example.com", p.Email);
        Assert.Contains("98765", p.Phone.Replace(" ", ""));
        Assert.Equal(8, p.ExperienceYears);
        Assert.Contains("C#", p.Skills);
        Assert.Contains("Angular", p.Skills);
        Assert.Contains("SQL Server", p.Skills);
        Assert.DoesNotContain("Java", p.Skills); // "JavaScript"/"Java" must not match inside other words
    }
}
