using System.Text.RegularExpressions;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Filtering;

/// <summary>Hard rules. Returns a reason when a job must be dropped, or null when it passes.</summary>
public static class RuleFilter
{
    private static readonly string[] GenericRemote = { "anywhere", "worldwide", "global", "apac", "asia", "earth" };

    private static readonly Regex ExpA = new(@"(\d{1,2})\s*\+?\s*(?:years?|yrs?)[^.\n]{0,40}experience", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExpB = new(@"experience[^.\n]{0,40}?(\d{1,2})\s*\+?\s*(?:years?|yrs?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Lpa = new(@"(\d{1,3}(?:\.\d+)?)\s*(?:-|to)\s*(\d{1,3}(?:\.\d+)?)\s*(?:lpa|lakhs?|lacs?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Alert-only jobs (Naukri/LinkedIn) carry just a title and link, so location, mode and experience cannot be judged.</summary>
    public static bool HasLimitedData(Job job) =>
        string.IsNullOrWhiteSpace(job.Description) && string.IsNullOrWhiteSpace(job.Location);

    public static string? Check(Job job, Rule rule)
    {
        var company = job.Company.ToLowerInvariant();
        foreach (var bad in rule.BlacklistedCompanies.Where(b => !string.IsNullOrWhiteSpace(b)))
            if (company.Contains(bad.Trim().ToLowerInvariant())) return $"Blacklisted company ({bad.Trim()})";

        if (rule.Roles.Count > 0 && RoleMatch(job.Title, rule.Roles) < 0.5)
            return "Title does not match your roles";

        if (HasLimitedData(job)) return null;

        if (rule.WorkMode is { } wanted && job.WorkMode != wanted)
            return $"Work mode is {job.WorkMode}, you want {wanted}";

        if (!LocationOk(job, rule.Locations)) return "Location not in your list";

        var required = RequiredYears(job);
        if (required is { } r && rule.MaxExperience > 0 && r > rule.MaxExperience)
            return $"Needs {r}+ years, your max is {rule.MaxExperience}";

        if (rule.MinSalaryLpa is { } min && MaxSalaryLpa(job) is { } max && max < (double)min)
            return $"Salary up to {max} LPA is below your {min} LPA";

        return null;
    }

    public static bool LocationOk(Job job, List<string> locations)
    {
        var locs = locations.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim().ToLowerInvariant()).ToList();
        if (locs.Count == 0) return true;
        var jl = job.Location.Trim().ToLowerInvariant();
        if (jl.Length == 0) return true;
        if (locs.Any(l => l != "remote" && jl.Contains(l))) return true;
        if (job.WorkMode == WorkMode.Remote && locs.Contains("remote"))
            return jl == "remote" || GenericRemote.Any(g => jl.Contains(g));
        return false;
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
