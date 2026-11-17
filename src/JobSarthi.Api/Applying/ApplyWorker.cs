using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Applying;

/// <summary>Background worker that runs queued applications. Also survives restarts: it marks interrupted ones as failed instead of leaving them "Applying".</summary>
public class ApplyWorker(ApplyQueue queue, IServiceScopeFactory scopes, ILogger<ApplyWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedAsync(stoppingToken);

        await foreach (var req in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ApplyOrchestrator>().RunAsync(req.ApplicationId, req.Submit, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                log.LogError(ex, "Application {Id} crashed", req.ApplicationId);
                await MarkFailedAsync(req.ApplicationId, "Unexpected error: " + ex.Message);
            }
            finally { queue.Done(req.ApplicationId); }
        }
    }

    private async Task RecoverInterruptedAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JobSarthiDbContext>();
        var stuck = await db.Applications.Where(a => a.Status == ApplicationStatus.Applying).ToListAsync(ct);
        foreach (var a in stuck)
        {
            a.Status = ApplicationStatus.Failed;
            a.UpdatedAtUtc = DateTime.UtcNow;
            db.ApplicationLogs.Add(new ApplicationLog { ApplicationId = a.Id, AtUtc = DateTime.UtcNow, Message = "Interrupted by an app restart. Press Retry." });
        }
        if (stuck.Count > 0) await db.SaveChangesAsync(ct);
    }

    private async Task MarkFailedAsync(int applicationId, string message)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JobSarthiDbContext>();
        var a = await db.Applications.FindAsync(applicationId);
        if (a is null) return;
        a.Status = ApplicationStatus.Failed;
        a.UpdatedAtUtc = DateTime.UtcNow;
        db.ApplicationLogs.Add(new ApplicationLog { ApplicationId = a.Id, AtUtc = DateTime.UtcNow, Message = message });
        await db.SaveChangesAsync();
    }
}
