using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Domain.Entities;

public class ApplicationLog
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public string Message { get; set; } = "";
    public DateTime AtUtc { get; set; }
}
