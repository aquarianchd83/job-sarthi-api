namespace JobSarthi.Api.Domain.Entities;

public class UserProfile
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Headline { get; set; } = "";
    public string Location { get; set; } = "";
    public int TotalExperienceYears { get; set; }
    public List<string> Skills { get; set; } = new();
}
