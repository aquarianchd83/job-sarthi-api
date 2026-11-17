using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Domain.Entities;

public class JobSource
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public SourceCapability Capability { get; set; }
    public bool Enabled { get; set; }
    public DateTime? LastRunUtc { get; set; }
    public int LastJobCount { get; set; }
    public string? LastError { get; set; }
}
