namespace JobSarthi.Api.Scheduling;

/// <summary>Starts a run in the background for the Run now button, so the request returns at once.</summary>
public class DailyRunLauncher(IServiceScopeFactory scopes, ILogger<DailyRunLauncher> log)
{
    public bool TryStart()
    {
        if (DailyRunPipeline.IsRunning) return false;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<DailyRunPipeline>().RunAsync("manual", CancellationToken.None);
            }
            catch (Exception ex) { log.LogError(ex, "Manual run failed"); }
        });
        return true;
    }
}
