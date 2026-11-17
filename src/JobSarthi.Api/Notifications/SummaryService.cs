using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Common;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Notifications;

/// <summary>Gathers what the daily summary reports. Counts of new matches use this run's own fetch time.</summary>
public class SummaryService(JobSarthiDbContext db, IConfiguration config)
{
    public async Task<(string Subject, string Body)> BuildAsync(DailyRun run, CancellationToken ct)
    {
        var dayStart = IstTime.StartOfIstDayUtc(DateTime.UtcNow);

        var topNew = await db.Jobs.AsNoTracking()
            .Where(j => j.Shortlisted && j.FetchedAtUtc >= run.StartedAtUtc)
            .OrderByDescending(j => j.FitScore).Take(10)
            .Select(j => new SummaryJob(j.Title, j.Company, j.SourceKey, j.FitScore ?? 0, j.Url, j.CanAutoApply)).ToListAsync(ct);

        async Task<List<SummaryApp>> Apps(Func<IQueryable<JobApplication>, IQueryable<JobApplication>> filter) =>
            await (from a in filter(db.Applications.AsNoTracking())
                   join j in db.Jobs.AsNoTracking() on a.JobId equals j.Id
                   orderby a.UpdatedAtUtc descending
                   select new SummaryApp(j.Title, j.Company)).ToListAsync(ct);

        var data = new SummaryData(run, config["App:PublicUrl"], topNew,
            await Apps(q => q.Where(a => a.Status == ApplicationStatus.AwaitingApproval)),
            await Apps(q => q.Where(a => a.Status == ApplicationStatus.PausedNeedsInput)),
            await Apps(q => q.Where(a => a.Status == ApplicationStatus.Submitted && a.SubmittedAtUtc >= dayStart)),
            await Apps(q => q.Where(a => a.Status == ApplicationStatus.Failed && a.UpdatedAtUtc >= dayStart)),
            await db.Alerts.CountAsync(a => !a.Acknowledged, ct));

        return (DailySummaryBuilder.Subject(run), DailySummaryBuilder.Build(data));
    }
}
