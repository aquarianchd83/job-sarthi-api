namespace JobSarthi.Api.Scheduling;

public static class SchedulingExtensions
{
    /// <param name="backgroundScheduler">False for the cron-style --run-daily mode, where Task Scheduler decides when to run.</param>
    public static IServiceCollection AddScheduling(this IServiceCollection services, bool backgroundScheduler = true)
    {
        services.AddScoped<DailyRunPipeline>();
        services.AddSingleton<DailyRunLauncher>();
        if (backgroundScheduler) services.AddHostedService<DailyRunHostedService>();
        return services;
    }
}
