namespace JobSarthi.Api.Domain.Enums;

public enum ApplicationStatus
{
    Shortlisted,
    AwaitingApproval,
    Tailoring,
    Applying,
    PausedNeedsInput,
    Submitted,
    Failed,
    ManualApply,
    /// <summary>Draft is ready (browser pre-filled, or email drafted). You review and submit yourself.</summary>
    DraftReady,
    Cancelled
}
