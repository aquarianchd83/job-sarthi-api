using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Applying;
using JobSarthi.Api.Common;
using JobSarthi.Api.Data;

namespace JobSarthi.Api.Scheduling;

/// <summary>
/// "dotnet JobSarthi.Api.dll --run-daily": the cron-style way to run Job Sarthi. Starts the app without a web address,
/// runs the daily routine once, waits for queued applications to finish filling, sends the summary, then exits.
/// </summary>
public static class OneShotRunner
{
    private static readonly TimeSpan ApplyTimeout = TimeSpan.FromMinutes(90);

    public static async Task<int> RunAsync(WebApplication app)
    {
        await app.StartAsync(); // starts the apply worker that processes the queue
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<JobSarthiDbContext>();

            // One scheduled run per IST day, even if Task Scheduler fires twice or the in-app scheduler also ran.
            var last = await db.DailyRuns.AsNoTracking().Where(r => r.Trigger == "schedule").MaxAsync(r => (DateTime?)r.StartedAtUtc);
            if (last is { } l && l >= IstTime.StartOfIstDayUtc(DateTime.UtcNow))
            {
                Console.WriteLine($"Skipped: the daily run already happened today ({IstTime.ToIst(l):d MMM yyyy, h:mm tt} IST).");
                return 2;
            }

            var pipeline = scope.ServiceProvider.GetRequiredService<DailyRunPipeline>();
            var run = await pipeline.RunAsync("schedule", CancellationToken.None, sendSummary: false);

            var queue = app.Services.GetRequiredService<ApplyQueue>();
            var deadline = DateTime.UtcNow + ApplyTimeout;
            while (!queue.IsIdle && DateTime.UtcNow < deadline) await Task.Delay(TimeSpan.FromSeconds(2));
            if (!queue.IsIdle) Console.WriteLine("Warning: some applications were still running after 90 minutes. They will be marked as interrupted.");

            // Sent after the fills finish, so it reports what is really waiting for your approval.
            var body = await pipeline.SendSummaryAsync(run);
            if (body is not null) Console.WriteLine(body);
            return run.Error is null ? 0 : 1;
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
