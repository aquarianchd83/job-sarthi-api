using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Domain.Entities;

public class CompanySlug
{
    public int Id { get; set; }
    public string SourceKey { get; set; } = "";
    public string Slug { get; set; } = "";
}
