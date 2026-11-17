using System.Text.RegularExpressions;
using JobSarthi.Api.Resume;

namespace JobSarthi.Api.Tailoring;

/// <summary>
/// The "never invent anything" check. Every engine's output, and every edit you save, passes through here.
/// It blocks added or dropped skills, skills named in the letter that are not in your resume, and inflated years.
/// </summary>
public static class TailorSafetyGuard
{
    private static readonly Regex Years = new(@"(\d{1,2})\s*\+?\s*(?:years?|yrs?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<string> Validate(TailoredDocs docs, TailorInput input)
    {
        var problems = new List<string>();

        var mine = input.Profile.Skills.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ordered = docs.OrderedSkills.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var added in ordered.Where(s => !mine.Contains(s)))
            problems.Add($"Skill \"{added}\" is not in your profile");
        foreach (var dropped in mine.Where(s => !ordered.Contains(s)))
            problems.Add($"Skill \"{dropped}\" was dropped from your list");

        problems.AddRange(ValidateLetter(docs.CoverLetter, input));
        return problems;
    }

    public static List<string> ValidateLetter(string letter, TailorInput input)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(letter)) { problems.Add("Cover letter is empty"); return problems; }
        if (letter.Length > 3000) problems.Add("Cover letter is longer than 3000 characters");

        // Skills named in the letter must come from your resume.
        // Your own headline counts too, since it is your text.
        var allowed = SkillCatalog.Find(input.ResumeText + " " + input.Profile.Headline)
            .Concat(input.Profile.Skills).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var named in SkillCatalog.Find(letter).Where(s => !allowed.Contains(s)))
            problems.Add($"Letter mentions \"{named}\", which is not in your resume");

        // A years figure may not exceed your own.
        var mineYears = input.Profile.TotalExperienceYears;
        foreach (Match m in Years.Matches(letter))
            if (int.Parse(m.Groups[1].Value) > mineYears)
                problems.Add($"Letter claims {m.Groups[1].Value} years, your profile says {mineYears}");

        return problems;
    }
}
