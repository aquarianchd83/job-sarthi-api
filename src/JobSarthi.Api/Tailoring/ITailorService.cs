namespace JobSarthi.Api.Tailoring;

public interface ITailorService
{
    /// <summary>"rule", "gemini" or "ollama".</summary>
    string Name { get; }
    Task<TailoredDocs> TailorAsync(TailorInput input, CancellationToken ct);
}
