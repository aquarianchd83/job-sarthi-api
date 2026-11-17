using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Domain.Entities;

/// <summary>One application per job: tailoring output (Stage 4), apply status and screenshot (Stage 5).</summary>
public class JobApplication
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public ApplicationStatus Status { get; set; }
    /// <summary>File name inside the screenshot folder. Never a full path.</summary>
    public string? ScreenshotFile { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }

    // Tailoring
    public string? CoverLetter { get; set; }
    public List<string> TailoredSkills { get; set; } = new();
    public List<string> MatchedKeywords { get; set; } = new();
    public List<string> MissingKeywords { get; set; } = new();
    public string? TailorEngine { get; set; }
    public string? TailorNote { get; set; }
    public DateTime? TailoredAtUtc { get; set; }
}
