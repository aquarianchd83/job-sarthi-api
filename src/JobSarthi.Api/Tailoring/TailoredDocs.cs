using JobSarthi.Api.Domain.Entities;

namespace JobSarthi.Api.Tailoring;

/// <summary>What an engine gets: your profile, your resume text and the job. Nothing else.</summary>
public record TailorInput(UserProfile Profile, string ResumeText, Job Job);

/// <param name="OrderedSkills">Your own skills, reordered. Always the same set as your profile.</param>
/// <param name="Engine">"rule", "gemini" or "ollama".</param>
/// <param name="MatchedKeywords">Skills in the posting that you have.</param>
/// <param name="MissingKeywords">Skills in the posting that are not in your resume. Information only, never added.</param>
public record TailoredDocs(
    IReadOnlyList<string> OrderedSkills,
    string CoverLetter,
    string Engine,
    IReadOnlyList<string> MatchedKeywords,
    IReadOnlyList<string> MissingKeywords,
    string? Note = null);

/// <summary>An engine cannot run right now (no key, limit reached, server down). The orchestrator moves on to the next engine.</summary>
public class TailorUnavailableException(string message, bool rateLimited = false) : Exception(message)
{
    public bool RateLimited { get; } = rateLimited;
}
