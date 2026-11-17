namespace JobSarthi.Api.Notifications;

public static class NotificationExtensions
{
    /// <summary>The one place a new channel is registered.</summary>
    public static IServiceCollection AddNotifications(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<TelegramOptions>(config.GetSection("Telegram"));
        services.Configure<EmailOptions>(config.GetSection("Email"));
        // RemoveAllLoggers: Telegram's API puts the bot token in the URL, so requests must not be logged.
        services.AddHttpClient<TelegramNotifier>(c => c.Timeout = TimeSpan.FromSeconds(20)).RemoveAllLoggers();
        services.AddTransient<INotifier>(sp => sp.GetRequiredService<TelegramNotifier>());
        services.AddTransient<INotifier, EmailNotifier>();
        services.AddScoped<AlertService>();
        services.AddScoped<SummaryService>();
        return services;
    }
}
