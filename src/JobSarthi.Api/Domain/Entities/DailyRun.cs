namespace JobSarthi.Api.Domain.Entities;

public class DailyRun
{
    public int Id { get; set; }
    /// <summary>"schedule" for the automatic run, "manual" for Run now.</summary>
    public string Trigger { get; set; } = "schedule";
    public DateTime StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public int Fetched { get; set; }
    public int NewJobs { get; set; }
    public int Shortlisted { get; set; }
    /// <summary>Applications prepared and queued by this run.</summary>
    public int Queued { get; set; }
    public string? Error { get; set; }
    public string? Notes { get; set; }
}
