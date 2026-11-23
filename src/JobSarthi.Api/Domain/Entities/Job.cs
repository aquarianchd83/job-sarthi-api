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

    // Set by classification (after search). A closed job has IsClosed = true and no category.
    public ApplyCategory? ApplyCategory { get; set; }
    /// <summary>greenhouse, lever, ashby or workable when the category is Api.</summary>
    public string? AtsKey { get; set; }
    /// <summary>Best link to apply on. Can differ from Url when the description links to an ATS.</summary>
    public string? ApplyUrl { get; set; }
    /// <summary>Where the resume goes, for email apply.</summary>
    public string? ApplyEmail { get; set; }
    public string? CategoryReason { get; set; }
    public DateTime? ClassifiedAtUtc { get; set; }
    /// <summary>"No longer accepting applications", position filled, expired... Such jobs are ignored.</summary>
    public bool IsClosed { get; set; }
    public string? ClosedReason { get; set; }
    /// <summary>Last time the apply page itself was read to see if it is still open.</summary>
    public DateTime? PageCheckedAtUtc { get; set; }
}
