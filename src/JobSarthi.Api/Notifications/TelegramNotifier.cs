using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using JobSarthi.Api.Common;

namespace JobSarthi.Api.Notifications;

public class TelegramOptions
{
    /// <summary>From @BotFather. Free. Keep it in appsettings.Local.json.</summary>
    public string BotToken { get; set; } = "";
    /// <summary>Your chat with the bot. Message the bot once, then open /api/notifications/telegram/chats to find it.</summary>
    public string ChatId { get; set; } = "";
}

/// <summary>Sends plain-text messages through a Telegram bot. Free.</summary>
public class TelegramNotifier(HttpClient http, IOptions<TelegramOptions> options) : INotifier
{
    private const int MaxLength = 4000; // Telegram's limit is 4096
    private readonly TelegramOptions _o = options.Value;

    public string Name => "telegram";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_o.BotToken) && !string.IsNullOrWhiteSpace(_o.ChatId);

    public async Task SendAsync(string subject, string body, CancellationToken ct)
    {
        var text = string.IsNullOrWhiteSpace(subject) ? body : subject + "\n\n" + body;
        foreach (var part in Split(text, MaxLength))
        {
            // The token is part of Telegram's URL by design, so HTTP request logging is switched off for this client.
            using var res = await http.PostAsJsonAsync($"https://api.telegram.org/bot{_o.BotToken}/sendMessage",
                new { chat_id = _o.ChatId, text = part, disable_web_page_preview = true }, ct);
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException("Telegram refused the message: " + await Describe(res, ct));
        }
    }

    /// <summary>Chats that have messaged the bot, so you can find your chat id without leaving the app.</summary>
    public async Task<List<(string ChatId, string Name)>> FindChatsAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_o.BotToken)) throw new InvalidOperationException("Set Telegram:BotToken first.");
        using var res = await http.GetAsync($"https://api.telegram.org/bot{_o.BotToken}/getUpdates", ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException("Telegram refused the request: " + await Describe(res, ct));

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var found = new Dictionary<string, string>();
        if (doc.RootElement.TryGetProperty("result", out var updates))
            foreach (var u in updates.EnumerateArray())
                if (u.TryGetProperty("message", out var m) && m.TryGetProperty("chat", out var chat))
                {
                    var id = chat.Str("id");
                    var name = string.Join(' ', new[] { chat.Str("first_name"), chat.Str("last_name") }.Where(x => x.Length > 0));
                    found[id] = name.Length > 0 ? name : chat.Str("title");
                }
        return found.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>Splits at line breaks so no message is cut mid-sentence.</summary>
    public static List<string> Split(string text, int max)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine;
            while (line.Length > max) { Flush(); parts.Add(line[..max]); line = line[max..]; }
            if (current.Length + line.Length + 1 > max) Flush();
            if (current.Length > 0) current.Append('\n');
            current.Append(line);
        }
        Flush();
        return parts;

        void Flush() { if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); } }
    }

    private static async Task<string> Describe(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            return doc.RootElement.Str("description") is { Length: > 0 } d ? d : $"HTTP {(int)res.StatusCode}";
        }
        catch (JsonException) { return $"HTTP {(int)res.StatusCode}"; }
    }
}
