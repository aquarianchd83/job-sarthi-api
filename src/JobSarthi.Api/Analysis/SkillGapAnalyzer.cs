using JobSarthi.Api.Filtering;
using JobSarthi.Api.Resume;

namespace JobSarthi.Api.Analysis;

public record MarketSkill(string Name, int Jobs, double Percent, bool Have, bool Common);

/// <param name="TopN">How many of the most-requested skills the coverage figure looks at.</param>
public record SkillGapResult(string Scope, int TotalJobs, int JobsAnalysed, List<MarketSkill> Skills, int TopN, int TopHave,
    List<MarketSkill> ToAdd, List<MarketSkill> RarelyAsked, string? Note);

/// <summary>
/// Compares the skills in your profile with the skills the fetched job postings ask for.
/// "Market demand" here means the jobs this app has fetched from your enabled sources, not the whole market.
/// </summary>
public static class SkillGapAnalyzer
{
    public const int TopN = 15;

    // Having the left skill means you have the right ones too, so they are not reported as missing.
    private static readonly Dictionary<string, string[]> Implies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ASP.NET Core"] = new[] { "ASP.NET", ".NET", "C#" },
        ["ASP.NET"] = new[] { ".NET" },
        ["EF Core"] = new[] { "Entity Framework" },
        ["Entity Framework"] = new[] { "EF Core" },
        ["Azure DevOps"] = new[] { "CI/CD" },
        ["GitHub Actions"] = new[] { "CI/CD" },
        ["Jenkins"] = new[] { "CI/CD" },
        ["xUnit"] = new[] { "Unit Testing" }, ["NUnit"] = new[] { "Unit Testing" }, ["Jest"] = new[] { "Unit Testing" },
        ["Web API"] = new[] { "REST APIs" },
        ["T-SQL"] = new[] { "SQL Server" },
    };

    public static HashSet<string> ExpandSkills(IEnumerable<string> skills)
    {
        var have = new HashSet<string>(skills, StringComparer.OrdinalIgnoreCase);
        foreach (var s in have.ToList())
            if (Implies.TryGetValue(s, out var more)) foreach (var m in more) have.Add(m);
        return have;
    }

    /// <param name="scope">"field": only jobs that mention at least one of your specific skills. "all": every job with a description.</param>
    /// <param name="titleContains">Optional. Only jobs whose title contains this text, for example ".net" or "backend".</param>
    public static SkillGapResult Analyze(IReadOnlyList<(string Title, string Description)> jobs, IReadOnlyList<string> yourSkills,
        string scope, string? titleContains)
    {
        string? note = null;
        var withText = jobs.Where(j => !string.IsNullOrWhiteSpace(j.Description)).ToList();
        var total = withText.Count;

        var pool = withText;
        if (!string.IsNullOrWhiteSpace(titleContains))
        {
            var t = titleContains.Trim();
            pool = pool.Where(j => j.Title.Contains(t, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var have = ExpandSkills(yourSkills);
        var specificMine = yourSkills.Where(s => !RuleFilter.IsGenericSkill(s)).ToList();

        // Skills each job asks for, found once per job.
        var perJob = pool.Select(j => SkillCatalog.Find(j.Title + " " + j.Description)).ToList();

        var effectiveScope = scope == "all" ? "all" : "field";
        if (effectiveScope == "field")
        {
            if (specificMine.Count == 0)
            {
                effectiveScope = "all";
                note = "Your profile has no specific skills yet, so all jobs are used. Upload your resume in Resume & Profile to compare jobs in your field.";
            }
            else
            {
                var mine = new HashSet<string>(specificMine, StringComparer.OrdinalIgnoreCase);
                var keep = perJob.Select(s => s.Any(mine.Contains)).ToList();
                pool = pool.Where((_, i) => keep[i]).ToList();
                perJob = perJob.Where((_, i) => keep[i]).ToList();
            }
        }

        var n = pool.Count;
        if (n == 0)
            return new SkillGapResult(effectiveScope, total, 0, new(), TopN, 0, new(), new(),
                note ?? "No jobs to compare yet. Fetch jobs from Job Sources first, or clear the title filter.");
        if (n < 20) note ??= $"Only {n} job{(n == 1 ? "" : "s")} to compare, so treat this as a rough picture. Fetch more jobs for a reliable one.";

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var skills in perJob)
            foreach (var s in skills) counts[s] = counts.GetValueOrDefault(s) + 1;

        MarketSkill Make(string name, int jobsCount) =>
            new(name, jobsCount, Math.Round(jobsCount * 100.0 / n, 1), have.Contains(name), RuleFilter.IsGenericSkill(name));

        var market = counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => Make(kv.Key, kv.Value)).ToList();
        var top = market.Take(TopN).ToList();

        // Skills worth adding: asked for by at least 3% of jobs (and 2 jobs), you do not have it, most requested first.
        var toAdd = market.Where(s => !s.Have && s.Percent >= 3 && s.Jobs >= 2).Take(10).ToList();

        // Your skills that employers barely ask for, so you know what to push down the resume.
        var rare = yourSkills
            .Where(s => SkillCatalog.All.Any(c => c.Name.Equals(s, StringComparison.OrdinalIgnoreCase)))
            .Select(s => Make(SkillCatalog.All.First(c => c.Name.Equals(s, StringComparison.OrdinalIgnoreCase)).Name, counts.GetValueOrDefault(s)))
            .Where(s => s.Percent < 3).OrderBy(s => s.Jobs).ToList();

        return new SkillGapResult(effectiveScope, total, n, market, Math.Min(TopN, market.Count), top.Count(s => s.Have), toAdd, rare, note);
    }
}
