using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Options;

namespace JobSarthi.Api.Sources.Alerts;

public class ImapOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 993;
    public string User { get; set; } = "";
    /// <summary>For Gmail use an App Password, not your normal password.</summary>
    public string AppPassword { get; set; } = "";
    public string Folder { get; set; } = "INBOX";
    public int LookbackDays { get; set; } = 3;
}

public record AlertMail(string From, string Subject, string Html, DateTime DateUtc);

/// <summary>Reads job-alert emails read-only. Never marks, moves or deletes mail.</summary>
public class ImapAlertReader(IOptions<ImapOptions> options)
{
    private readonly ImapOptions _o = options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_o.Host) && !string.IsNullOrWhiteSpace(_o.User) && !string.IsNullOrWhiteSpace(_o.AppPassword);

    public async Task<List<AlertMail>> ReadAsync(string fromContains, CancellationToken ct)
    {
        var result = new List<AlertMail>();
        using var client = new ImapClient();
        await client.ConnectAsync(_o.Host, _o.Port, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(_o.User, _o.AppPassword, ct);
        var folder = string.Equals(_o.Folder, "INBOX", StringComparison.OrdinalIgnoreCase)
            ? client.Inbox
            : await client.GetFolderAsync(_o.Folder, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        var since = DateTime.UtcNow.AddDays(-Math.Max(1, _o.LookbackDays));
        var uids = await folder.SearchAsync(SearchQuery.DeliveredAfter(since).And(SearchQuery.FromContains(fromContains)), ct);
        foreach (var uid in uids)
        {
            var msg = await folder.GetMessageAsync(uid, ct);
            var body = msg.HtmlBody ?? msg.TextBody ?? "";
            result.Add(new AlertMail(msg.From.ToString(), msg.Subject ?? "", body, msg.Date.UtcDateTime));
        }
        await client.DisconnectAsync(true, ct);
        return result;
    }
}
