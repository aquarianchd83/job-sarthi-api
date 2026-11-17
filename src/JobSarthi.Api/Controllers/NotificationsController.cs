using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using JobSarthi.Api.Notifications;

namespace JobSarthi.Api.Controllers;

public record ChannelStatus(string Name, bool Configured, string Detail);
public record ChannelResult(string Channel, bool Ok, string Message);
public record TelegramChat(string ChatId, string Name);

[ApiController]
[Route("api/[controller]")]
public class NotificationsController(IEnumerable<INotifier> notifiers, TelegramNotifier telegram,
    IOptions<TelegramOptions> tg, IOptions<EmailOptions> email) : ControllerBase
{
    /// <summary>Which channels are set up. Secrets are never returned.</summary>
    [HttpGet("status")]
    public IEnumerable<ChannelStatus> Status() => new[]
    {
        new ChannelStatus("telegram", telegram.IsConfigured,
            telegram.IsConfigured ? "Bot token and chat id are set" :
            string.IsNullOrWhiteSpace(tg.Value.BotToken) ? "Set Telegram:BotToken and Telegram:ChatId in appsettings.Local.json"
                                                         : "Bot token is set. Message your bot, then find your chat id below"),
        new ChannelStatus("email", notifiers.OfType<EmailNotifier>().First().IsConfigured,
            notifiers.OfType<EmailNotifier>().First().IsConfigured ? $"Sends to {email.Value.To} via {email.Value.Host}"
                                                                    : "Set Email:Host, Email:User, Email:Password and Email:To in appsettings.Local.json"),
    };

    /// <summary>Sends a test message to every configured channel and reports each result. Ignores the Settings switches.</summary>
    [HttpPost("test")]
    public async Task<IEnumerable<ChannelResult>> Test(CancellationToken ct)
    {
        var results = new List<ChannelResult>();
        foreach (var n in notifiers)
        {
            if (!n.IsConfigured) { results.Add(new(n.Name, false, "Not set up")); continue; }
            try
            {
                await n.SendAsync("Job Sarthi test", "If you can read this, notifications from Job Sarthi work.", ct);
                results.Add(new(n.Name, true, "Sent"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new(n.Name, false, ex.Message));
            }
        }
        return results;
    }

    /// <summary>Chats that have messaged your bot. Send the bot any message first, then open this to find your chat id.</summary>
    [HttpGet("telegram/chats")]
    public async Task<ActionResult<IEnumerable<TelegramChat>>> TelegramChats(CancellationToken ct)
    {
        try { return (await telegram.FindChatsAsync(ct)).Select(c => new TelegramChat(c.ChatId, c.Name)).ToList(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }
}
