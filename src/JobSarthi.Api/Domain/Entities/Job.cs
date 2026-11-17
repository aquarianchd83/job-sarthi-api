using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Domain.Entities;

public class Job
{
    public int Id { get; set; }
    public string SourceKey { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public string Location { get; set; } = "";
    public WorkMode WorkMode { get; set; }
    public string Url { get; set; } = "";
    public string Description { get; set; } = "";
    public bool CanAutoApply { get; set; }
    public DateTime FetchedAtUtc { get; set; }

    // Set by scoring (Stage 3)
    public bool Shortlisted { get; set; }
    public int? FitScore { get; set; }
    public string? ScoreNotes { get; set; }
    public string? RejectReason { get; set; }
    public DateTime? ScoredAtUtc { get; set; }
}
