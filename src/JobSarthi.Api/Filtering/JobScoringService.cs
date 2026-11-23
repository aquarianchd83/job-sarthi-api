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
        var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(ct) ?? new UserProfile();
        var userYears = profile.TotalExperienceYears;
        if (userYears == 0) userYears = rule.MinExperience;
        // Your skills and location from the profile make the filter and score about YOU, not just about the rule words.
        var ctx = new RuleFilter.FilterContext(profile.Skills, profile.Location);

        var now = DateTime.UtcNow;
        var shortlisted = 0;
        var jobs = await db.Jobs.ToListAsync(ct);
        foreach (var job in jobs)
        {
            var reason = RuleFilter.Check(job, rule, ctx);
            job.ScoredAtUtc = now;
            if (reason is null)
            {
                var s = JobScorer.Score(job, rule, userYears, profile.Skills);
                var fitReason = RuleFilter.FitRangeReason(s.Score, rule);
                job.Shortlisted = fitReason is null;
                job.FitScore = s.Score;          // kept even when outside the range, so you can see why
                job.ScoreNotes = s.Notes;
                job.RejectReason = fitReason;
                if (fitReason is null) shortlisted++;
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
