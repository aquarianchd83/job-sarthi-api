using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;

namespace JobSarthi.Api.Filtering;

public record ScoringSummary(int Total, int Shortlisted, int Rejected);

/// <summary>Applies the saved rules to every stored job and records shortlist state, score and reason.</summary>
public class JobScoringService(JobSarthiDbContext db)
{
    public async Task<ScoringSummary> ScoreAllAsync(CancellationToken ct)
    {
        var rule = await db.Rules.AsNoTracking().FirstOrDefaultAsync(ct) ?? new Rule();
        var userYears = (await db.Profiles.AsNoTracking().Select(p => (int?)p.TotalExperienceYears).FirstOrDefaultAsync(ct))
                        ?? 0;
        if (userYears == 0) userYears = rule.MinExperience;

        var now = DateTime.UtcNow;
        var shortlisted = 0;
        var jobs = await db.Jobs.ToListAsync(ct);
        foreach (var job in jobs)
        {
            var reason = RuleFilter.Check(job, rule);
            job.ScoredAtUtc = now;
            if (reason is null)
            {
                var s = JobScorer.Score(job, rule, userYears);
                job.Shortlisted = true;
                job.FitScore = s.Score;
                job.ScoreNotes = s.Notes;
                job.RejectReason = null;
                shortlisted++;
            }
            else
            {
                job.Shortlisted = false;
                job.FitScore = null;
                job.ScoreNotes = null;
                job.RejectReason = reason;
            }
        }
        await db.SaveChangesAsync(ct);
        return new ScoringSummary(jobs.Count, shortlisted, jobs.Count - shortlisted);
    }
}
