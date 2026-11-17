using JobSarthi.Api.Applying.Playwright;

namespace JobSarthi.Api.Applying;

public static class ApplyExtensions
{
    /// <summary>The one place a new ATS applier is registered.</summary>
    public static IServiceCollection AddApplying(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<ApplyOptions>(config.GetSection("Apply"));
        services.AddSingleton<PlaywrightHost>();
        services.AddSingleton<ApplyQueue>();
        services.AddScoped<ScreenshotStore>();
        services.AddScoped<IJobApplier, GreenhouseApplier>();
        services.AddScoped<IJobApplier, LeverApplier>();
        services.AddScoped<IJobApplier, AshbyApplier>();
        services.AddScoped<ApplyOrchestrator>();
        services.AddScoped<ApplyStarter>();
        services.AddHostedService<ApplyWorker>();
        return services;
    }
}
