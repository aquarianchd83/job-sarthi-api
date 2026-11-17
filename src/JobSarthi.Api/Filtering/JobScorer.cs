using JobSarthi.Api.Domain.Entities;

namespace JobSarthi.Api.Filtering;

public record ScoreResult(int Score, string Notes);

/// <summary>
/// Fit score 0 to 100, no LLM. Role match 40, keywords 30, location and mode 15, experience 15.
/// Call only for jobs that passed <see cref="RuleFilter"/>.
/// </summary>
public static class JobScorer
{
    public static ScoreResult Score(Job job, Rule rule, int userYears)
    {
        var notes = new List<string>();

        double role = 20;
        if (rule.Roles.Count > 0)
        {
            var m = RuleFilter.RoleMatch(job.Title, rule.Roles);
            role = m * 40;
            notes.Add($"Title {m:P0}");
        }

        double kw = 15;
        var keywords = rule.Keywords.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim().ToLowerInvariant()).Distinct().ToList();
        if (keywords.Count > 0)
        {
            var hay = (job.Title + " " + job.Description).ToLowerInvariant();
            var hit = keywords.Count(hay.Contains);
            kw = hit / (double)keywords.Count * 30;
            notes.Add($"Keywords {hit}/{keywords.Count}");
        }

        double loc = 8;
        if (!RuleFilter.HasLimitedData(job) && (rule.WorkMode is not null || rule.Locations.Count > 0))
        {
            loc = 15; // it passed the filter, so any stated preference is met
            notes.Add("Location fits");
        }

        double exp = 8;
        if (RuleFilter.RequiredYears(job) is { } req)
        {
            exp = userYears >= req ? 15 : Math.Max(0, 15 - 3 * (req - userYears));
            notes.Add($"Needs {req}y");
        }

        return new ScoreResult((int)Math.Round(role + kw + loc + exp), string.Join(" · ", notes));
    }
}
