using JobSarthi.Api.Analysis;
using Xunit;

namespace JobSarthi.Api.Tests;

public class SkillGapTests
{
    private static List<(string Title, string Description)> Jobs()
    {
        var list = new List<(string, string)>();
        // 30 backend jobs: all C#, 24 also want Kubernetes, 12 want Docker, 3 want Terraform
        for (int i = 0; i < 30; i++)
            list.Add(("Backend Engineer", "C# and .NET. " + (i < 24 ? "Kubernetes. " : "") + (i < 12 ? "Docker. " : "") + (i < 3 ? "Terraform. " : "") + (i == 0 ? "Redis." : "")));
        // 20 marketing jobs that mention none of the tech skills
        for (int i = 0; i < 20; i++) list.Add(("Content Marketing Lead", "Write blog posts and run campaigns."));
        // 5 alert-style jobs with no description at all
        for (int i = 0; i < 5; i++) list.Add(("Lead .NET Developer", ""));
        return list;
    }

    private static readonly string[] Mine = { "C#", ".NET", "Angular", "SQL Server" };

    [Fact]
    public void Field_scope_ignores_unrelated_jobs_and_alert_jobs_without_text()
    {
        var r = SkillGapAnalyzer.Analyze(Jobs(), Mine, "field", null);
        Assert.Equal("field", r.Scope);
        Assert.Equal(50, r.TotalJobs);        // alert jobs with no description are not counted
        Assert.Equal(30, r.JobsAnalysed);     // marketing jobs mention none of your skills
    }

    [Fact]
    public void All_scope_uses_every_job_with_text_so_percentages_are_diluted()
    {
        var r = SkillGapAnalyzer.Analyze(Jobs(), Mine, "all", null);
        Assert.Equal(50, r.JobsAnalysed);
        Assert.Equal(60.0, r.Skills.First(s => s.Name == "C#").Percent);          // 30 of 50
    }

    [Fact]
    public void Missing_skills_are_ranked_by_demand_and_skills_you_have_are_flagged()
    {
        var r = SkillGapAnalyzer.Analyze(Jobs(), Mine, "field", null);
        Assert.Equal(new[] { "Kubernetes", "Docker" }, r.ToAdd.Select(s => s.Name).Take(2));
        Assert.Equal(80.0, r.ToAdd[0].Percent);                                   // 24 of 30
        Assert.Equal(40.0, r.ToAdd[1].Percent);
        Assert.DoesNotContain(r.ToAdd, s => s.Name == "C#");
        Assert.True(r.Skills.First(s => s.Name == "C#").Have);
        Assert.False(r.Skills.First(s => s.Name == "Kubernetes").Have);
    }

    [Fact]
    public void A_skill_in_a_single_job_is_not_worth_adding_but_one_in_a_tenth_of_jobs_is()
    {
        var r = SkillGapAnalyzer.Analyze(Jobs(), Mine, "field", null);
        Assert.DoesNotContain(r.ToAdd, s => s.Name == "Redis");       // 1 of 30 jobs
        Assert.Contains(r.ToAdd, s => s.Name == "Terraform");         // 3 of 30 jobs = 10%
    }

    [Fact]
    public void Coverage_counts_how_many_of_the_top_skills_you_have()
    {
        var r = SkillGapAnalyzer.Analyze(Jobs(), Mine, "field", null);
        // Market has C#, .NET, Kubernetes, Docker, Terraform, Redis = 6 skills; you have C# and .NET.
        Assert.Equal(6, r.TopN);
        Assert.Equal(2, r.TopHave);
    }

    [Fact]
    public void Your_skills_that_nobody_asks_for_are_listed_as_rarely_asked()
    {
        var r = SkillGapAnalyzer.Analyze(Jobs(), Mine, "field", null);
        Assert.Contains(r.RarelyAsked, s => s.Name == "Angular" && s.Jobs == 0);
        Assert.Contains(r.RarelyAsked, s => s.Name == "SQL Server");
        Assert.DoesNotContain(r.RarelyAsked, s => s.Name == "C#");
    }

    [Fact]
    public void Title_filter_narrows_the_jobs()
    {
        var r = SkillGapAnalyzer.Analyze(Jobs(), Mine, "all", "marketing");
        Assert.Equal(20, r.JobsAnalysed);
        Assert.Empty(r.Skills);
    }

    [Fact]
    public void Having_aspnet_core_also_covers_aspnet_and_dotnet()
    {
        var jobs = Enumerable.Range(0, 25).Select(_ => ("Dev", "We use ASP.NET and .NET daily.")).ToList();
        var r = SkillGapAnalyzer.Analyze(jobs, new[] { "ASP.NET Core" }, "all", null);
        Assert.All(r.Skills, s => Assert.True(s.Have, s.Name));
        Assert.Empty(r.ToAdd);
    }

    [Fact]
    public void Profile_without_specific_skills_falls_back_to_all_jobs_with_an_explanation()
    {
        var r = SkillGapAnalyzer.Analyze(Jobs(), Array.Empty<string>(), "field", null);
        Assert.Equal("all", r.Scope);
        Assert.Contains("no specific skills", r.Note);
        Assert.Equal(0, r.TopHave);
    }

    [Fact]
    public void Common_skills_are_flagged_and_small_samples_are_called_rough()
    {
        var jobs = new List<(string, string)> { ("Dev", "HTML and C#"), ("Dev", "HTML and Kubernetes") };
        var r = SkillGapAnalyzer.Analyze(jobs, new[] { "C#" }, "all", null);
        Assert.True(r.Skills.First(s => s.Name == "HTML").Common);
        Assert.Contains("rough picture", r.Note);
    }

    [Fact]
    public void No_jobs_gives_an_empty_result_with_guidance()
    {
        var r = SkillGapAnalyzer.Analyze(new List<(string, string)>(), Mine, "field", null);
        Assert.Equal(0, r.JobsAnalysed);
        Assert.Contains("Fetch jobs", r.Note);
    }
}
