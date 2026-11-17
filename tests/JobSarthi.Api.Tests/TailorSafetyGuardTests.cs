using Microsoft.Extensions.Logging.Abstractions;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Tailoring;
using Xunit;

namespace JobSarthi.Api.Tests;

public class TailorTests
{
    private static TailorInput Input(string desc = "We use C#, Angular, Kafka and Kubernetes. 5+ years of experience.") => new(
        new UserProfile
        {
            FullName = "Asha Verma", Email = "asha@example.com", Phone = "+91 98765 43210",
            Headline = "Senior .NET Developer", TotalExperienceYears = 8,
            Skills = { "SQL Server", "C#", "Azure", "Angular" },
        },
        "Asha Verma. Built C# APIs on SQL Server and Azure, Angular front ends.",
        new Job { Title = "Senior .NET Engineer", Company = "Acme", Description = desc });

    // ---- rule-based engine ----

    [Fact]
    public async Task Rule_engine_moves_matching_skills_first_and_keeps_the_same_set()
    {
        var docs = await new RuleBasedTailor().TailorAsync(Input(), default);
        Assert.Equal(new[] { "C#", "Angular", "SQL Server", "Azure" }.OrderBy(x => x), docs.OrderedSkills.OrderBy(x => x));
        Assert.Equal("C#", docs.OrderedSkills[0]);                 // mentioned in the posting
        Assert.Equal("Azure", docs.OrderedSkills[^1]);             // not mentioned, goes last
        Assert.Contains("Kafka", docs.MissingKeywords);            // in posting, not yours: reported only
        Assert.DoesNotContain("Kafka", docs.OrderedSkills);
    }

    [Fact]
    public async Task Rule_letter_never_mentions_a_skill_you_lack_and_passes_the_guard()
    {
        var input = Input();
        var docs = await new RuleBasedTailor().TailorAsync(input, default);
        Assert.DoesNotContain("Kafka", docs.CoverLetter);
        Assert.DoesNotContain("Kubernetes", docs.CoverLetter);
        Assert.Contains("Acme", docs.CoverLetter);
        Assert.Contains("8 years", docs.CoverLetter);
        Assert.Empty(TailorSafetyGuard.Validate(docs, input));
    }

    // ---- safety guard ----

    private static TailoredDocs Docs(string letter, params string[] skills) =>
        new(skills.Length == 0 ? new[] { "SQL Server", "C#", "Azure", "Angular" } : skills, letter, "gemini", Array.Empty<string>(), Array.Empty<string>());

    [Fact]
    public void Guard_blocks_an_added_skill() =>
        Assert.Contains(TailorSafetyGuard.Validate(Docs("Hi.", "SQL Server", "C#", "Azure", "Angular", "Kafka"), Input()),
            p => p.Contains("Kafka") && p.Contains("not in your profile"));

    [Fact]
    public void Guard_blocks_a_dropped_skill() =>
        Assert.Contains(TailorSafetyGuard.Validate(Docs("Hi.", "C#", "Azure", "Angular"), Input()), p => p.Contains("dropped"));

    [Fact]
    public void Guard_blocks_a_letter_naming_a_skill_not_in_the_resume() =>
        Assert.Contains(TailorSafetyGuard.Validate(Docs("I have shipped Kubernetes clusters."), Input()), p => p.Contains("Kubernetes"));

    [Fact]
    public void Guard_blocks_inflated_years() =>
        Assert.Contains(TailorSafetyGuard.Validate(Docs("I bring 12 years of C# experience."), Input()), p => p.Contains("12 years"));

    [Fact]
    public void Guard_accepts_honest_years_and_known_skills() =>
        Assert.Empty(TailorSafetyGuard.Validate(Docs("I bring 8 years of C# and Angular experience."), Input()));

    [Fact]
    public void Guard_rejects_an_empty_letter() =>
        Assert.Contains(TailorSafetyGuard.ValidateLetter("  ", Input()), p => p.Contains("empty"));

    // ---- LLM reply parsing ----

    [Fact]
    public void Llm_reply_parses_with_and_without_code_fences()
    {
        const string json = """{"orderedSkills":["C#","Azure"],"coverLetter":"Dear team"}""";
        var (s1, l1) = LlmPrompt.Parse(json);
        var (s2, l2) = LlmPrompt.Parse("```json\n" + json + "\n```");
        Assert.Equal(new[] { "C#", "Azure" }, s1);
        Assert.Equal("Dear team", l1);
        Assert.Equal(s1, s2);
        Assert.Equal(l1, l2);
    }

    // ---- fallback ----

    private class FakeEngine(string name, Func<TailorInput, TailoredDocs> run) : ITailorService
    {
        public string Name => name;
        public Task<TailoredDocs> TailorAsync(TailorInput input, CancellationToken ct) => Task.FromResult(run(input));
    }

    private static TailorOrchestrator Orch(params ITailorService[] engines) =>
        new(engines, new RuleBasedTailor(), NullLogger<TailorOrchestrator>.Instance);

    [Fact]
    public async Task Switched_off_engines_are_never_called()
    {
        var called = false;
        var o = Orch(new FakeEngine("gemini", _ => { called = true; return Docs("x"); }));
        var docs = await o.TailorAsync(Input(), new AppSetting { UseGemini = false }, default);
        Assert.False(called);
        Assert.Equal("rule", docs.Engine);
        Assert.Null(docs.Note);
    }

    [Fact]
    public async Task Gemini_output_that_breaks_the_guard_falls_back_to_rule_based()
    {
        var o = Orch(new FakeEngine("gemini", _ => Docs("I know Kubernetes well.")));
        var docs = await o.TailorAsync(Input(), new AppSetting { UseGemini = true }, default);
        Assert.Equal("rule", docs.Engine);
        Assert.Contains("rejected", docs.Note);
    }

    [Fact]
    public async Task Good_gemini_output_is_used()
    {
        var o = Orch(new FakeEngine("gemini", _ => Docs("I bring 8 years of C# experience.")));
        var docs = await o.TailorAsync(Input(), new AppSetting { UseGemini = true }, default);
        Assert.Equal("gemini", docs.Engine);
    }

    [Fact]
    public async Task Rate_limit_falls_back_then_skips_the_engine_for_a_while()
    {
        var calls = 0;
        var gemini = new ThrowingEngine("gemini", () => { calls++; return new TailorUnavailableException("Gemini free limit reached", true); });
        var o = Orch(gemini);
        var s = new AppSetting { UseGemini = true };
        var first = await o.TailorAsync(Input(), s, default);
        var second = await o.TailorAsync(Input(), s, default);
        Assert.Equal("rule", first.Engine);
        Assert.Contains("limit reached", first.Note);
        Assert.Equal("rule", second.Engine);
        Assert.Equal(1, calls); // second run did not even try
    }

    private class ThrowingEngine(string name, Func<Exception> ex) : ITailorService
    {
        public string Name => name;
        public Task<TailoredDocs> TailorAsync(TailorInput input, CancellationToken ct) => throw ex();
    }
}
