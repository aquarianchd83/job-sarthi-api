using JobSarthi.Api.Sources.Providers;

namespace JobSarthi.Api.Sources;

public static class JobSourceExtensions
{
    /// <summary>The one place a new source plugin is registered.</summary>
    public static IServiceCollection AddJobSources(this IServiceCollection services)
    {
        AddHttpSource<GreenhouseSource>(services);
        AddHttpSource<LeverSource>(services);
        AddHttpSource<AshbySource>(services);
        AddHttpSource<RemoteOkSource>(services);
        AddHttpSource<WeWorkRemotelySource>(services);
        services.AddTransient<IJobSource, NaukriAlertSource>();
        services.AddTransient<IJobSource, LinkedInAlertSource>();
        services.AddScoped<JobSourceRegistry>();
        services.AddScoped<JobFetchService>();
        services.AddScoped<CompanyDiscoveryService>();
        services.AddScoped<JobSarthi.Api.Filtering.JobScoringService>();
        return services;
    }

    private static void AddHttpSource<T>(IServiceCollection services) where T : class, IJobSource
    {
        services.AddHttpClient<T>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("JobSarthi/1.0 (personal job search)");
        });
        services.AddTransient<IJobSource>(sp => sp.GetRequiredService<T>());
    }
}
