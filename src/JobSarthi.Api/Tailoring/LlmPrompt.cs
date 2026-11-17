using System.Text.Json;
using JobSarthi.Api.Common;

namespace JobSarthi.Api.Tailoring;

/// <summary>Prompt and reply handling shared by the Gemini and Ollama engines.</summary>
public static class LlmPrompt
{
    public static string Build(TailorInput input)
    {
        var p = input.Profile;
        var job = input.Job;
        return $$"""
        You help a job seeker re-frame their EXISTING resume for one job. You must not invent anything.

        HARD RULES
        - "orderedSkills" must contain exactly the skills in MY SKILLS, same spelling, reordered so the ones most relevant to the job come first. Do not add, remove or rename any.
        - The cover letter may only use facts in MY PROFILE. Do not mention any technology, employer, project, degree, certification or number that is not listed there.
        - Years of experience must be exactly {{p.TotalExperienceYears}} or not mentioned.
        - Plain text, 120 to 180 words, no placeholders, no markdown.
        - Reply with JSON only: {"orderedSkills":["..."],"coverLetter":"..."}

        MY PROFILE
        Name: {{p.FullName}}
        Headline: {{p.Headline}}
        Years of experience: {{p.TotalExperienceYears}}
        Email: {{p.Email}}
        Phone: {{p.Phone}}
        MY SKILLS: {{string.Join(", ", p.Skills)}}

        THE JOB
        Title: {{job.Title}}
        Company: {{job.Company}}
        Description:
        {{TextUtil.Truncate(job.Description, 6000)}}
        """;
    }

    /// <summary>Reads {"orderedSkills":[],"coverLetter":""}, tolerating markdown code fences around it.</summary>
    public static (List<string> Skills, string Letter) Parse(string reply)
    {
        var s = reply.Trim();
        if (s.StartsWith("```"))
        {
            var first = s.IndexOf('\n');
            var last = s.LastIndexOf("```", StringComparison.Ordinal);
            if (first >= 0 && last > first) s = s[(first + 1)..last].Trim();
        }
        using var doc = JsonDocument.Parse(s);
        var root = doc.RootElement;
        var skills = root.TryGetProperty("orderedSkills", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!.Trim()).ToList()
            : new List<string>();
        return (skills, root.Str("coverLetter").Trim());
    }

    public static TailoredDocs ToDocs(string engine, string reply, TailorInput input)
    {
        var (skills, letter) = Parse(reply);
        var a = RuleBasedTailor.Analyze(input);
        return new TailoredDocs(skills, letter, engine, a.Matched, a.Missing);
    }
}
