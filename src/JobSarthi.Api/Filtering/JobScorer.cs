using JobSarthi.Api.Domain.Entities;

namespace JobSarthi.Api.Filtering;

public record ScoreResult(int Score, string Notes);

/// <summary>
/// Fit score 0 to 100, no LLM. Out of 70: role match (40, only if you set roles), your keywords (20, only if you set any),
/// and the skills from your profile that the posting mentions (the rest). Plus location and mode (15) and experience (15).
/// Call only for jobs that passed <see cref="RuleFilter"/>.
/// </summary>
public static class JobScorer
{
    public static ScoreResult Score(Job job, Rule rule, int userYears, IReadOnlyList<string>? skills = null)
    {
        var notes = new List<string>();
        var keywords = rule.Keywords.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim().ToLowerInvariant()).Distinct().ToList();

        double roleW = rule.Roles.Count > 0 ? 40 : 0;
        double kwW = keywords.Count > 0 ? 20 : 0;
        double skillW = 70 - roleW - kwW;

        double role = 0;
        if (roleW > 0)
        {
            var m = RuleFilter.RoleMatch(job.Title, rule.Roles);
            role = m * roleW;
            notes.Add($"Title {m:P0}");
        }

        double kw = 0;
        if (kwW > 0)
        {
            var hay = (job.Title + " " + job.Description).ToLowerInvariant();
            var hit = keywords.Count(hay.Contains);
            kw = hit / (double)keywords.Count * kwW;
            notes.Add($"Keywords {hit}/{keywords.Count}");
        }

        double skill = skillW * 0.5; // no skills in the profile yet: neutral
        if (skills is { Count: > 0 })
        {
            var matched = RuleFilter.SkillMatches(job, skills);
            var specific = skills.Count(s => !RuleFilter.IsGenericSkill(s));
            skill = Math.Min(1.0, matched.Count / (double)Math.Max(1, Math.Min(specific, 5))) * skillW;
            notes.Add(matched.Count == 0 ? "Skills 0" : $"Skills {matched.Count}: {string.Join(", ", matched.Take(4))}");
        }

        double loc = 8;
        if (!RuleFilter.HasLimitedData(job) && RuleFilter.HasLocationPreference(rule))
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

        var text = string.Join(" · ", notes);
        return new ScoreResult((int)Math.Round(role + kw + skill + loc + exp), text.Length > 290 ? text[..290] : text);
    }
}
