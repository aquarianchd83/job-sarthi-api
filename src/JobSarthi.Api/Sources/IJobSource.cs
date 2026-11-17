using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Sources;

public record FetchResult(IReadOnlyList<Job> Jobs, IReadOnlyList<string> Errors)
{
    public static FetchResult Empty(string? error = null) =>
        new(Array.Empty<Job>(), error is null ? Array.Empty<string>() : new[] { error });
}

/// <summary>Plugin contract. Add a new portal = add one class implementing this + register in JobSourceExtensions.</summary>
public interface IJobSource
{
    string Key { get; }
    string DisplayName { get; }
    SourceCapability Capability { get; }
    Task<FetchResult> FetchAsync(IReadOnlyList<string> companySlugs, CancellationToken ct);
}
