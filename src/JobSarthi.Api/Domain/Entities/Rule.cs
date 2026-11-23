using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Domain.Entities;

/// <summary>Work modes you accept in one place, for example Place "Pune", Modes Hybrid and Onsite.</summary>
public class LocationPref
{
    public string Place { get; set; } = "";
    /// <summary>Any of Remote, Hybrid, Onsite.</summary>
    public List<string> Modes { get; set; } = new();
}

public class Rule
{
    public int Id { get; set; }
    public List<string> Roles { get; set; } = new();
    public int MinExperience { get; set; }
    public int MaxExperience { get; set; }

    /// <summary>Per-place work modes. Empty list means no preference.</summary>
    public List<LocationPref> LocationPrefs { get; set; } = new();
    /// <summary>Work modes you accept everywhere that is not in LocationPrefs. Default: all three.</summary>
    public List<string> OtherModes { get; set; } = new() { "Remote", "Hybrid", "Onsite" };

    /// <summary>Old single list of places. Only read once to convert into LocationPrefs, then cleared.</summary>
    public List<string> Locations { get; set; } = new();
    /// <summary>Old single work mode. Only read once to convert, then cleared.</summary>
    public WorkMode? WorkMode { get; set; }

    public decimal? MinSalaryLpa { get; set; }
    public List<string> Keywords { get; set; } = new();
    public List<string> BlacklistedCompanies { get; set; } = new();
    /// <summary>A job must mention at least this many of your specific skills (from your profile). 0 turns the check off.</summary>
    public int MinSkillMatches { get; set; } = 2;
    /// <summary>Only jobs whose fit score (0 to 100) is inside this range are shortlisted.</summary>
    public int MinFitScore { get; set; } = 0;
    public int MaxFitScore { get; set; } = 100;
}
