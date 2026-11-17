using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Common;
using JobSarthi.Api.Data;
using JobSarthi.Api.Notifications;

namespace JobSarthi.Api.Scheduling;

/// <summary>
/// Starts the daily run at the IST time in Settings. It re-reads Settings every 30 seconds, so a changed time applies
/// without a restart. Runs once per IST day, catches up after downtime, and raises an alert if a day was missed.
/// </summary>
public class DailyRunHostedService(IServiceScopeFactory scopes, ILogger<DailyRunHostedService> log) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var warnedMissedFor = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<JobSarthiDbContext>();
                var settings = await db.Settings.AsNoTracking().FirstAsync(stoppingToken);

                if (settings.SchedulerEnabled)
                {
                    var last = await db.DailyRuns.AsNoTracking().Where(r => r.Trigger == "schedule")
                        .MaxAsync(r => (DateTime?)r.StartedAtUtc, stoppingToken);
                    var (decision, next) = SchedulePolicy.Decide(DateTime.UtcNow, settings.DailyRunTimeIst, last);

                    if (decision == ScheduleDecision.RunNow && !DailyRunPipeline.IsRunning)
                    {
                        log.LogInformation("Starting the daily run");
                        await scope.ServiceProvider.GetRequiredService<DailyRunPipeline>().RunAsync("schedule", stoppingToken);
                        continue;
                    }
                    if (decision == ScheduleDecision.Missed && warnedMissedFor < IstTime.StartOfIstDayUtc(DateTime.UtcNow))
                    {
                        warnedMissedFor = IstTime.StartOfIstDayUtc(DateTime.UtcNow);
                        await scope.ServiceProvider.GetRequiredService<AlertService>().RaiseAsync("run-missed",
                            "Today's daily run did not happen. The server was probably off at the run time. Use Run now in Settings.", stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogError(ex, "Scheduler loop error"); }

            try { await Task.Delay(Tick, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
