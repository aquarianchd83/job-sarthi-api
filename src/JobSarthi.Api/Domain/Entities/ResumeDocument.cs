using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Domain.Entities;

public class ResumeDocument
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public string StoragePath { get; set; } = "";
    public string ParsedText { get; set; } = "";
    public DateTime UploadedAtUtc { get; set; }
}
