using System.Text;
using JobSarthi.Api.Resume;

namespace JobSarthi.Api.Tailoring;

public record JobAnalysis(List<string> OrderedSkills, List<string> Matched, List<string> Missing);

/// <summary>
/// Free, offline tailoring. Pulls skills out of the posting, moves the ones you have to the top and fills a plain cover letter.
/// It only ever uses skills, years and contact details already in your profile.
/// </summary>
public class RuleBasedTailor : ITailorService
{
    public string Name => "rule";

    public Task<TailoredDocs> TailorAsync(TailorInput input, CancellationToken ct)
    {
        var a = Analyze(input);
        return Task.FromResult(new TailoredDocs(a.OrderedSkills, BuildLetter(input, a.Matched), Name, a.Matched, a.Missing));
    }

    /// <summary>Shared by every engine, so keyword lists are always computed the same way.</summary>
    public static JobAnalysis Analyze(TailorInput input)
    {
        var jd = (input.Job.Title + " " + input.Job.Description).ToLowerInvariant();
        var mine = input.Profile.Skills;

        // Skills you have that the posting mentions, most mentioned first.
        var scored = mine.Select((s, i) => (Skill: s, Index: i, Count: SkillCatalog.Count(jd, SkillCatalog.TermFor(s)))).ToList();
        var matched = scored.Where(x => x.Count > 0).OrderByDescending(x => x.Count).ThenBy(x => x.Index).Select(x => x.Skill).ToList();
        var rest = scored.Where(x => x.Count == 0).OrderBy(x => x.Index).Select(x => x.Skill).ToList();

        var have = mine.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = SkillCatalog.Find(jd).Where(s => !have.Contains(s)).ToList();
        return new JobAnalysis(matched.Concat(rest).ToList(), matched, missing);
    }

    public static string BuildLetter(TailorInput input, IReadOnlyList<string> matched)
    {
        var p = input.Profile;
        var job = input.Job;
        var company = string.IsNullOrWhiteSpace(job.Company) ? "your company" : job.Company;
        var who = string.IsNullOrWhiteSpace(p.Headline) ? "professional" : p.Headline.Trim();
        var sb = new StringBuilder();

        sb.AppendLine($"Dear Hiring Team at {company},");
        sb.AppendLine();
        sb.Append($"I am writing to apply for the {job.Title} position. I am a {who}");
        sb.AppendLine(p.TotalExperienceYears > 0 ? $" with {p.TotalExperienceYears} years of experience." : ".");

        var top = matched.Take(4).ToList();
        if (top.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Your posting mentions {Join(top)}, which {(top.Count == 1 ? "is" : "are")} in my skill set.");
        }
        else if (p.Skills.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"My skills include {Join(p.Skills.Take(4).ToList())}.");
        }

        sb.AppendLine();
        sb.AppendLine($"I would welcome the chance to discuss how I can contribute to {company}.");
        sb.AppendLine();
        sb.AppendLine("Best regards,");
        sb.AppendLine(p.FullName);
        var contact = string.Join(" | ", new[] { p.Email, p.Phone }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (contact.Length > 0) sb.AppendLine(contact);
        return sb.ToString().TrimEnd();
    }

    private static string Join(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };
}
