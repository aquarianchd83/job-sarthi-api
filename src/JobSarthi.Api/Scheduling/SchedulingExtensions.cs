namespace JobSarthi.Api.Scheduling;

public static class SchedulingExtensions
{
    public static IServiceCollection AddScheduling(this IServiceCollection services)
    {
        services.AddScoped<DailyRunPipeline>();
        services.AddSingleton<DailyRunLauncher>();
        services.AddHostedService<DailyRunHostedService>();
        return services;
    }
}
