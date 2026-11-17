namespace JobSarthi.Api.Tailoring;

public static class TailorExtensions
{
    public static IServiceCollection AddTailoring(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<GeminiOptions>(config.GetSection("Gemini"));
        services.Configure<OllamaOptions>(config.GetSection("Ollama"));
        services.AddHttpClient<GeminiTailor>(c => c.Timeout = TimeSpan.FromSeconds(60));
        services.AddHttpClient<OllamaTailor>(c => c.Timeout = TimeSpan.FromSeconds(180));
        services.AddTransient<ITailorService>(sp => sp.GetRequiredService<GeminiTailor>());
        services.AddTransient<ITailorService>(sp => sp.GetRequiredService<OllamaTailor>());
        services.AddSingleton<RuleBasedTailor>();
        services.AddScoped<TailorOrchestrator>();
        services.AddScoped<TailoringService>();
        return services;
    }
}
