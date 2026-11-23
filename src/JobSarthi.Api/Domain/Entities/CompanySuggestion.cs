namespace JobSarthi.Api.Domain.Entities;

public enum SuggestionStatus { Pending, Added, Dismissed }

/// <summary>A Greenhouse/Lever/Ashby company found in the apply link of a job from another source (RemoteOK, WWR, alerts...).</summary>
public class CompanySuggestion
{
    public int Id { get; set; }
    /// <summary>greenhouse, lever or ashby.</summary>
    public string SourceKey { get; set; } = "";
    public string Slug { get; set; } = "";
    /// <summary>How many fetched jobs linked to this company board.</summary>
    public int SeenCount { get; set; }
    public string? ExampleTitle { get; set; }
    public string? ExampleCompany { get; set; }
    public SuggestionStatus Status { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
}
