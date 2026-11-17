namespace JobSarthi.Api.Domain.Entities;

/// <summary>Something an application is waiting on. "question" can be answered here. "captcha" and "otp" need you on the real site.</summary>
public class PendingQuestion
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public string Kind { get; set; } = "question"; // question | captcha | otp
    public string Prompt { get; set; } = "";
    public string? Answer { get; set; }
    public bool Resolved { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
