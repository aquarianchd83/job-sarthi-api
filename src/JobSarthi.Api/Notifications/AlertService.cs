using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;

namespace JobSarthi.Api.Notifications;

public enum NotifyKind { Alert, Summary }

/// <summary>Records an alert (shown in the app) and sends alerts and summaries to every configured channel.</summary>
public class AlertService(JobSarthiDbContext db, IEnumerable<INotifier> notifiers, ILogger<AlertService> log)
{
    public async Task RaiseAsync(string kind, string message, CancellationToken ct = default)
    {
        db.Alerts.Add(new Alert { Kind = kind, Message = message.Length > 1000 ? message[..1000] : message, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        await NotifyAsync("Job Sarthi alert", message, NotifyKind.Alert, ct);
    }

    /// <summary>Best effort. A broken channel must never break a run. Respects the two switches in Settings.</summary>
    public async Task NotifyAsync(string subject, string body, NotifyKind kind, CancellationToken ct = default)
    {
        log.LogInformation("{Subject}: {Body}", subject, body);

        var s = await db.Settings.AsNoTracking().FirstAsync(ct);
        if (kind == NotifyKind.Alert && !s.NotifyAlerts) return;
        if (kind == NotifyKind.Summary && !s.NotifySummary) return;

        foreach (var n in notifiers.Where(n => n.IsConfigured))
        {
            try { await n.SendAsync(subject, body, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("{Channel} notification failed: {Message}", n.Name, ex.Message); }
        }
    }
}
