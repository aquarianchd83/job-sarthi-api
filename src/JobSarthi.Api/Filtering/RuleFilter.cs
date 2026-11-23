using System.Text.RegularExpressions;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;
using JobSarthi.Api.Resume;

namespace JobSarthi.Api.Filtering;

/// <summary>Hard rules. Returns a reason when a job must be dropped, or null when it passes.</summary>
public static class RuleFilter
{
    private static readonly Regex ExpA = new(@"(\d{1,2})\s*\+?\s*(?:years?|yrs?)[^.\n]{0,40}experience", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExpB = new(@"experience[^.\n]{0,40}?(\d{1,2})\s*\+?\s*(?:years?|yrs?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Lpa = new(@"(\d{1,3}(?:\.\d+)?)\s*(?:-|to)\s*(\d{1,3}(?:\.\d+)?)\s*(?:lpa|lakhs?|lacs?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Alert-only jobs (Naukri/LinkedIn) carry just a title and link, so location, mode and experience cannot be judged.</summary>
    public static bool HasLimitedData(Job job) =>
        string.IsNullOrWhiteSpace(job.Description) && string.IsNullOrWhiteSpace(job.Location);

    /// <param name="Skills">Your skills from the profile.</param>
    /// <param name="Location">Your own location from the profile, so a job restricted to your region is not dropped.</param>
    public record FilterContext(IReadOnlyList<string> Skills, string Location);

    // Common skills that appear in almost every tech posting. They never count towards "does this job fit my skills".
    private static readonly HashSet<string> GenericSkills = new(StringComparer.OrdinalIgnoreCase)
        { "HTML", "CSS", "SCSS", "Git", "Agile", "Scrum", "OOP", "SOLID", "Design Patterns", "REST APIs", "Unit Testing", "TDD", "CI/CD", "JWT", "OAuth" };

    public static bool IsGenericSkill(string skill) => GenericSkills.Contains(skill);

    /// <summary>The last check, after scoring: a reason when the fit score is outside your range, else null.</summary>
    public static string? FitRangeReason(int score, Rule rule) =>
        score >= rule.MinFitScore && score <= rule.MaxFitScore ? null
        : $"Fit score is outside your range ({score} not in {rule.MinFitScore}-{rule.MaxFitScore})";

    /// <summary>Your specific skills that the posting mentions (title and description).</summary>
    public static List<string> SkillMatches(Job job, IEnumerable<string> skills)
    {
        var text = (job.Title + " " + job.Description).ToLowerInvariant();
        return skills.Where(s => !GenericSkills.Contains(s) && SkillCatalog.Count(text, SkillCatalog.TermFor(s)) > 0).ToList();
    }

    private static readonly Regex OtherRegion = new(
        @"\b(usa|u\.s\.a?|us|united states|north america|americas|latam|latin america|canada|europe|emea|uk|united kingdom|germany|france|spain|netherlands|poland|australia|new zealand|brazil|mexico|argentina|california|new york|texas|florida|washington|massachusetts|colorado|illinois|georgia|ontario|london|berlin|toronto)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] OpenHints = { "india", "anywhere", "worldwide", "global", "apac", "asia", "earth" };

    /// <summary>"USA Only", "Americas Only", "California" and similar: a job you cannot take unless you listed that place.</summary>
    public static string? RegionRestriction(Job job, Rule rule, FilterContext? ctx)
    {
        var jl = job.Location.Trim().ToLowerInvariant();
        if (jl.Length == 0) return null;
        var restricted = OtherRegion.IsMatch(jl) || Regex.IsMatch(jl, @"\bonly\b");
        if (!restricted) return null;

        var mine = rule.LocationPrefs.Select(p => p.Place).Concat(new[] { ctx?.Location ?? "" })
            .Select(l => l.Trim().ToLowerInvariant()).Where(l => l.Length > 0 && l != "remote").ToList();
        if (OpenHints.Any(jl.Contains) || mine.Any(jl.Contains) || mine.Any(m => m.Length > 2 && jl.Split(',', '/', '-').Any(p => p.Trim() == m))) return null;
        return $"Location restricted to \"{job.Location}\"";
    }

    public static string? Check(Job job, Rule rule, FilterContext? ctx = null)
    {
        // "No longer accepting applications" and the like: ignored before anything else.
        if (job.IsClosed) return "No longer accepting applications";

        var company = job.Company.ToLowerInvariant();
        foreach (var bad in rule.BlacklistedCompanies.Where(b => !string.IsNullOrWhiteSpace(b)))
            if (company.Contains(bad.Trim().ToLowerInvariant())) return $"Blacklisted company ({bad.Trim()})";

        if (rule.Roles.Count > 0 && RoleMatch(job.Title, rule.Roles) < 0.5)
            return "Title does not match your roles";

        if (HasLimitedData(job)) return null;

        if (LocationModeReason(job, rule) is { } place) return place;
        if (RegionRestriction(job, rule, ctx) is { } region) return region;

        // Does the posting actually mention the skills you have? Needs your profile to have specific skills.
        if (ctx is { Skills.Count: > 0 } && rule.MinSkillMatches > 0)
        {
            var specific = ctx.Skills.Count(s => !GenericSkills.Contains(s));
            var needed = Math.Min(rule.MinSkillMatches, specific);
            var found = SkillMatches(job, ctx.Skills).Count;
            if (needed > 0 && found < needed) return $"Only {found} of your skills match (needs {needed})";
        }

        var required = RequiredYears(job);
        if (required is { } r && rule.MaxExperience > 0 && r > rule.MaxExperience)
            return $"Needs {r}+ years, your max is {rule.MaxExperience}";

        if (rule.MinSalaryLpa is { } min && MaxSalaryLpa(job) is { } max && max < (double)min)
            return $"Salary up to {max} LPA is below your {min} LPA";

        return null;
    }

    public static readonly string[] AllModes = { "Remote", "Hybrid", "Onsite" };

    /// <summary>True when you set any location preference. With none, every place and work mode is fine.</summary>
    public static bool HasLocationPreference(Rule rule) =>
        rule.LocationPrefs.Any(p => !string.IsNullOrWhiteSpace(p.Place)) || AllModes.Any(m => !rule.OtherModes.Contains(m, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Per-place work modes. A job in a place you listed must be in one of that place's modes (for example Pune: Hybrid or Onsite).
    /// A job anywhere else, including remote jobs open to anywhere, must be in the "everywhere else" modes (for example Remote).
    /// </summary>
    public static string? LocationModeReason(Job job, Rule rule)
    {
        if (!HasLocationPreference(rule)) return null;
        var jl = job.Location.Trim().ToLowerInvariant();
        if (jl.Length == 0) return null;
        var mode = job.WorkMode.ToString();

        var hits = rule.LocationPrefs.Where(p => !string.IsNullOrWhiteSpace(p.Place) && jl.Contains(p.Place.Trim().ToLowerInvariant())).ToList();
        if (hits.Count > 0)
        {
            if (hits.Any(p => p.Modes.Contains(mode, StringComparer.OrdinalIgnoreCase))) return null;
            var first = hits[0];
            var want = first.Modes.Count == 0 ? "nothing" : string.Join(" or ", first.Modes);
            return $"In {first.Place.Trim()} you want {want}, this job is {mode}";
        }

        if (rule.OtherModes.Contains(mode, StringComparer.OrdinalIgnoreCase)) return null;
        var elsewhere = rule.OtherModes.Count == 0 ? "nothing" : string.Join(" or ", rule.OtherModes);
        return $"Outside your listed places you want {elsewhere}, this job is {mode} ({job.Location})";
    }

    /// <summary>Best token overlap (0 to 1) between the job title and any wanted role.</summary>
    public static double RoleMatch(string title, IEnumerable<string> roles)
    {
        var t = Tokens(title);
        double best = 0;
        foreach (var role in roles)
        {
            var r = Tokens(role);
            if (r.Count == 0) continue;
            // The skill or domain words in a role (".net", "backend") must all be in the title.
            // "Senior Magento Developer" must not match "Senior .NET Developer" on level + "developer" alone.
            var core = r.Where(w => !Generic.Contains(w)).ToList();
            if (core.Count > 0 && !core.All(t.Contains)) continue;
            best = Math.Max(best, r.Count(t.Contains) / (double)r.Count);
        }
        return best;
    }

    private static readonly HashSet<string> Generic = new()
        { "senior", "junior", "lead", "staff", "principal", "mid", "associate", "developer" };

    private static readonly Dictionary<string, string> Synonyms = new()
    {
        ["engineer"] = "developer", ["dev"] = "developer", ["programmer"] = "developer",
        ["sr"] = "senior", ["jr"] = "junior", ["fullstack"] = "full-stack",
    };
    private static readonly HashSet<string> Stop = new() { "of", "and", "the", "a", "i", "ii", "iii", "-", "at", "for" };

    private static HashSet<string> Tokens(string s)
    {
        var set = new HashSet<string>();
        foreach (Match m in Regex.Matches(s.ToLowerInvariant(), @"[a-z0-9#+.]+"))
        {
            var w = m.Value.TrimEnd('.');
            if (w.Length == 0 || Stop.Contains(w)) continue;
            set.Add(Synonyms.GetValueOrDefault(w, w));
        }
        // "full stack" and "full-stack" should compare equal.
        if (set.Contains("full") && set.Contains("stack")) { set.Remove("full"); set.Remove("stack"); set.Add("full-stack"); }
        return set;
    }

    /// <summary>Smallest "N years ... experience" figure mentioned in the posting, or null.</summary>
    public static int? RequiredYears(Job job)
    {
        var text = job.Title + " " + job.Description;
        var found = ExpA.Matches(text).Concat(ExpB.Matches(text))
            .Select(m => int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)).ToList();
        return found.Count == 0 ? null : found.Min();
    }

    public static double? MaxSalaryLpa(Job job)
    {
        var ms = Lpa.Matches(job.Description);
        if (ms.Count == 0) return null;
        return ms.Max(m => double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
    }
}
