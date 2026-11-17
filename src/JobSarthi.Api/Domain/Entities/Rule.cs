using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Domain.Entities;

public class Rule
{
    public int Id { get; set; }
    public List<string> Roles { get; set; } = new();
    public int MinExperience { get; set; }
    public int MaxExperience { get; set; }
    public List<string> Locations { get; set; } = new();
    public WorkMode? WorkMode { get; set; }
    public decimal? MinSalaryLpa { get; set; }
    public List<string> Keywords { get; set; } = new();
    public List<string> BlacklistedCompanies { get; set; } = new();
}
