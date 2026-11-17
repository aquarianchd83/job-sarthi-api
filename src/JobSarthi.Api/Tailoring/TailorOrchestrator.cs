using System.Collections.Concurrent;
using JobSarthi.Api.Domain.Entities;

namespace JobSarthi.Api.Tailoring;

/// <summary>
/// Picks the engine. Order: Gemini (if switched on), Ollama (if switched on), then rule-based, which cannot fail.
/// Any engine that errors, hits its limit or produces output the safety guard rejects is skipped, and the reason is recorded.
/// </summary>
public class TailorOrchestrator(IEnumerable<ITailorService> engines, RuleBasedTailor ruleEngine, ILogger<TailorOrchestrator> log)
{
    private static readonly ConcurrentDictionary<string, DateTime> Cooldown = new();
    private static readonly TimeSpan LimitCooldown = TimeSpan.FromMinutes(30);

    public async Task<TailoredDocs> TailorAsync(TailorInput input, AppSetting settings, CancellationToken ct)
    {
        var notes = new List<string>();
        var wanted = new List<string>();
        if (settings.UseGemini) wanted.Add("gemini");
        if (settings.UseOllama) wanted.Add("ollama");

        foreach (var name in wanted)
        {
            if (Cooldown.TryGetValue(name, out var until) && until > DateTime.UtcNow)
            {
                notes.Add($"{Pretty(name)} skipped, limit reached earlier");
                continue;
            }
            var engine = engines.FirstOrDefault(e => e.Name == name);
            if (engine is null) continue;

            try
            {
                var docs = await engine.TailorAsync(input, ct);
                var problems = TailorSafetyGuard.Validate(docs, input);
                if (problems.Count == 0) return docs with { Note = Join(notes) };
                notes.Add($"{Pretty(name)} output rejected: {problems[0]}");
                log.LogWarning("{Engine} output rejected by safety guard: {Problems}", name, string.Join("; ", problems));
            }
            catch (TailorUnavailableException ex)
            {
                if (ex.RateLimited) Cooldown[name] = DateTime.UtcNow + LimitCooldown;
                notes.Add($"{Pretty(name)}: {ex.Message}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "{Engine} failed", name);
                notes.Add($"{Pretty(name)} failed: {ex.Message}");
            }
        }

        var rule = await ruleEngine.TailorAsync(input, ct);
        if (notes.Count > 0) notes.Add("Used rule-based tailoring");
        return rule with { Note = Join(notes) };
    }

    private static string? Join(List<string> notes) => notes.Count == 0 ? null : string.Join(". ", notes);
    private static string Pretty(string name) => char.ToUpperInvariant(name[0]) + name[1..];
}
