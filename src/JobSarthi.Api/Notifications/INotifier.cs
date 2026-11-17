namespace JobSarthi.Api.Notifications;

/// <summary>A way to reach you. Add a new channel the same way: one class, one registration line in NotificationExtensions.</summary>
public interface INotifier
{
    /// <summary>"telegram" or "email".</summary>
    string Name { get; }
    /// <summary>False until its settings are filled in. Unconfigured channels are skipped silently.</summary>
    bool IsConfigured { get; }
    /// <summary>Throws when delivery fails, with a message you can act on.</summary>
    Task SendAsync(string subject, string body, CancellationToken ct);
}
