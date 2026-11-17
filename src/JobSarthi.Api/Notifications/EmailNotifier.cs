using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace JobSarthi.Api.Notifications;

public class EmailOptions
{
    /// <summary>For Gmail: smtp.gmail.com, with an App Password (not your normal password).</summary>
    public string Host { get; set; } = "";
    public int Port { get; set; } = 465;
    /// <summary>SslOnConnect (port 465, default), StartTls (port 587) or None (local testing only).</summary>
    public string Security { get; set; } = "SslOnConnect";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

/// <summary>Sends plain-text email over SMTP. Free with a normal mailbox, such as Gmail.</summary>
public class EmailNotifier(IOptions<EmailOptions> options) : INotifier
{
    private readonly EmailOptions _o = options.Value;

    public string Name => "email";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_o.Host) && !string.IsNullOrWhiteSpace(_o.To);

    public async Task SendAsync(string subject, string body, CancellationToken ct)
    {
        var from = string.IsNullOrWhiteSpace(_o.From) ? _o.User : _o.From;
        if (string.IsNullOrWhiteSpace(from)) throw new InvalidOperationException("Set Email:From or Email:User.");

        var msg = new MimeMessage();
        msg.From.Add(MailboxAddress.Parse(from));
        msg.To.Add(MailboxAddress.Parse(_o.To));
        msg.Subject = subject;
        msg.Body = new TextPart("plain") { Text = body };

        var security = _o.Security.ToLowerInvariant() switch
        {
            "starttls" => SecureSocketOptions.StartTls,
            "none" => SecureSocketOptions.None,
            _ => SecureSocketOptions.SslOnConnect,
        };

        using var smtp = new SmtpClient();
        try
        {
            await smtp.ConnectAsync(_o.Host, _o.Port, security, ct);
            if (!string.IsNullOrWhiteSpace(_o.User)) await smtp.AuthenticateAsync(_o.User, _o.Password, ct);
            await smtp.SendAsync(msg, ct);
            await smtp.DisconnectAsync(true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("Email failed: " + ex.Message, ex);
        }
    }
}
