namespace JobSarthi.Api.Domain.Entities;

/// <summary>Something that needs your attention: a source is failing, a site changed, a run failed or was missed.</summary>
public class Alert
{
    public int Id { get; set; }
    /// <summary>source | apply-failed | run-failed | run-missed</summary>
    public string Kind { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public bool Acknowledged { get; set; }
}
