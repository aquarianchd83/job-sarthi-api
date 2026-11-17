namespace JobSarthi.Api.Applying;

/// <summary>One implementation per supported ATS (Greenhouse, Lever, Ashby). Add a new one the same way as a job source.</summary>
public interface IJobApplier
{
    string SourceKey { get; }
    Task<ApplyOutcome> ApplyAsync(ApplyContext ctx, CancellationToken ct);
}
