using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Scheduling;

/// <param name="AppStatus">Null when the job has no application yet.</param>
public record ApplyCandidate(int JobId, int FitScore, ApplicationStatus? AppStatus);

/// <summary>Which shortlisted jobs the daily run should apply to. Pure logic.</summary>
public static class AutoApplyPlanner
{
    /// <summary>Best scores first, only jobs never attempted, only above the minimum score, no more than the budget.</summary>
    public static List<int> Select(IEnumerable<ApplyCandidate> candidates, int budget, int minScore) =>
        candidates
            .Where(c => c.FitScore >= minScore && (c.AppStatus is null or ApplicationStatus.Shortlisted))
            .OrderByDescending(c => c.FitScore).ThenBy(c => c.JobId)
            .Take(Math.Max(0, budget))
            .Select(c => c.JobId)
            .ToList();
}
