using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Common;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Sources;

public record SourceRunResult(string Key, int Fetched, int Added, string? Error);

/// <summary>Runs enabled sources, saves new jobs, records last run, count and error per source.</summary>
public class JobFetchService(JobSarthiDbContext db, JobSourceRegistry registry, ILogger<JobFetchService> log)
{
    private static readonly string[] SlugSources = { "greenhouse", "lever", "ashby" };

    /// <param name="onlyKey">Run just this source (even if switched off). Null runs every enabled source.</param>
    public async Task<List<SourceRunResult>> RunAsync(string? onlyKey, CancellationToken ct)
    {
        var sources = await db.JobSources
            .Where(s => onlyKey == null ? s.Enabled : s.Key == onlyKey)
            .OrderBy(s => s.Id).ToListAsync(ct);
        var results = new List<SourceRunResult>();

        foreach (var source in sources)
        {
            var now = DateTime.UtcNow;
            try
            {
                var impl = registry.Get(source.Key);
                if (impl is null)
                {
                    source.LastError = "No plugin is registered for this source.";
                    results.Add(new(source.Key, 0, 0, source.LastError));
                    continue;
                }

                var slugs = await db.CompanySlugs.Where(x => x.SourceKey == source.Key).Select(x => x.Slug).ToListAsync(ct);
                FetchResult fetched;
                if (SlugSources.Contains(source.Key) && slugs.Count == 0)
                    fetched = FetchResult.Empty("Add at least one company slug.");
                else
                    fetched = await impl.FetchAsync(slugs, ct);

                var added = await SaveNewAsync(impl, fetched.Jobs, now, ct);
                source.LastRunUtc = now;
                source.LastJobCount = fetched.Jobs.Count;
                source.LastError = fetched.Errors.Count == 0 ? null : TextUtil.Truncate(string.Join("; ", fetched.Errors), 1000);
                results.Add(new(source.Key, fetched.Jobs.Count, added, source.LastError));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Fetch failed for {Source}", source.Key);
                source.LastRunUtc = now;
                source.LastError = TextUtil.Truncate(ex.Message, 1000);
                results.Add(new(source.Key, 0, 0, source.LastError));
            }
            await db.SaveChangesAsync(ct);
        }
        return results;
    }

    private async Task<int> SaveNewAsync(IJobSource impl, IReadOnlyList<Job> jobs, DateTime now, CancellationToken ct)
    {
        var existing = (await db.Jobs.Where(j => j.SourceKey == impl.Key).Select(j => j.ExternalId).ToListAsync(ct)).ToHashSet();
        var added = 0;
        foreach (var j in jobs)
        {
            if (string.IsNullOrWhiteSpace(j.ExternalId) || string.IsNullOrWhiteSpace(j.Url) || !existing.Add(j.ExternalId)) continue;
            j.SourceKey = impl.Key;
            j.ExternalId = TextUtil.Truncate(j.ExternalId, 200);
            j.Title = TextUtil.Truncate(j.Title, 300);
            j.Company = TextUtil.Truncate(j.Company, 200);
            j.Location = TextUtil.Truncate(j.Location, 300);
            j.Url = TextUtil.Truncate(j.Url, 1000);
            j.Description = TextUtil.Truncate(j.Description, 30000);
            j.CanAutoApply = impl.Capability == SourceCapability.FetchAndApply;
            j.FetchedAtUtc = now;
            db.Jobs.Add(j);
            added++;
        }
        return added;
    }
}
