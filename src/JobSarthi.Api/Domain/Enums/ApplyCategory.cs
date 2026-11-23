namespace JobSarthi.Api.Domain.Enums;

/// <summary>How a job can be applied to. Closed jobs get no category at all.</summary>
public enum ApplyCategory
{
    /// <summary>A structured ATS (Greenhouse, Lever, Ashby, Workable). The form can be filled by the system.</summary>
    Api,
    /// <summary>The posting asks for the resume by email. A draft email is prepared, you send it.</summary>
    Email,
    /// <summary>Everything else. The link and a draft are kept ready, you submit in one click. Never auto-submitted.</summary>
    Manual
}
