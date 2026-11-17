using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Notifications;
using Xunit;

namespace JobSarthi.Api.Tests;

public class NotificationTests
{
    // ---------- summary text ----------

    private static DailyRun Run(string? error = null) => new()
    {
        Trigger = "schedule", StartedAtUtc = new DateTime(2026, 11, 17, 3, 30, 0, DateTimeKind.Utc),
        Fetched = 1254, NewJobs = 12, Shortlisted = 55, Queued = 3, Error = error,
    };

    private static SummaryData Data(DailyRun run, string? url = "http://server/jobsarthi") => new(run, url,
        new() { new SummaryJob("Senior .NET Engineer", "Stripe", "greenhouse", 91, "https://x/1", true),
                new SummaryJob("Lead .NET Developer", "", "naukri", 66, "https://x/2", false) },
        new() { new SummaryApp("Backend Developer", "Netflix") }, new(), new() { new SummaryApp("Platform Engineer", "Airbnb") }, new(), 2);

    [Fact]
    public void Summary_shows_ist_time_counts_sections_and_links()
    {
        var text = DailySummaryBuilder.Build(Data(Run()));
        Assert.Contains("17 Nov 2026, 9:00 AM IST (scheduled run)", text);        // 03:30 UTC shown as IST, not as UTC digits
        Assert.DoesNotContain("3:30", text);
        Assert.Contains("Fetched 1254 jobs (12 new). 55 match your rules. 3 applications queued.", text);
        Assert.Contains("Waiting for your approval (1)", text);
        Assert.Contains("- Backend Developer, Netflix", text);
        Assert.DoesNotContain("Needs your answer", text);                         // empty sections are left out
        Assert.Contains("Submitted today (1)", text);
        Assert.Contains("2 open alerts in the app.", text);
        Assert.Contains("1. 91  Senior .NET Engineer, Stripe (Greenhouse, can auto-apply)", text);
        Assert.Contains("2. 66  Lead .NET Developer (Naukri alert, apply yourself)", text);   // no company: no stray comma
        Assert.Contains("https://x/2", text);
        Assert.Contains("Open Job Sarthi: http://server/jobsarthi", text);
    }

    [Fact]
    public void Summary_of_a_failed_run_says_so_and_has_a_failure_subject()
    {
        var run = Run("Database is unreachable");
        Assert.Contains("The run FAILED: Database is unreachable", DailySummaryBuilder.Build(Data(run)));
        Assert.Contains("FAILED", DailySummaryBuilder.Subject(run));
        Assert.Equal("Job Sarthi: 55 matches, 3 applications queued", DailySummaryBuilder.Subject(Run()));
    }

    [Fact]
    public void Summary_without_new_matches_or_public_url_stays_clean()
    {
        var d = Data(Run()) with { TopNew = new(), AppUrl = "" };
        var text = DailySummaryBuilder.Build(d);
        Assert.Contains("No new matches in this run.", text);
        Assert.DoesNotContain("Open Job Sarthi", text);
    }

    [Fact]
    public void Long_lists_are_cut_with_a_count()
    {
        var many = Enumerable.Range(1, 12).Select(i => new SummaryApp($"Job {i}", "Co")).ToList();
        var text = DailySummaryBuilder.Build(Data(Run()) with { AwaitingApproval = many });
        Assert.Contains("Waiting for your approval (12)", text);
        Assert.Contains("- and 4 more", text);
    }

    // ---------- telegram ----------

    [Fact]
    public void Split_keeps_every_part_within_the_limit_and_loses_nothing()
    {
        var text = string.Join("\n", Enumerable.Range(1, 400).Select(i => $"line number {i} with some padding text"));
        var parts = TelegramNotifier.Split(text, 4000);
        Assert.True(parts.Count > 1);
        Assert.All(parts, p => Assert.True(p.Length <= 4000));
        Assert.Equal(text, string.Join("\n", parts));
        Assert.Equal(2, TelegramNotifier.Split(new string('x', 5000), 4000).Count); // a single huge line is still cut
    }

    private class FakeHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        public List<(string Url, string Body)> Calls { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls.Add((r.RequestUri!.ToString(), r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static TelegramNotifier Tg(FakeHandler h, string token = "123:ABC", string chat = "42") =>
        new(new HttpClient(h), Options.Create(new TelegramOptions { BotToken = token, ChatId = chat }));

    [Fact]
    public async Task Telegram_posts_subject_and_body_to_the_chat()
    {
        var h = new FakeHandler(HttpStatusCode.OK, """{"ok":true}""");
        await Tg(h).SendAsync("Job Sarthi alert", "greenhouse is failing", default);
        var call = Assert.Single(h.Calls);
        Assert.Equal("https://api.telegram.org/bot123:ABC/sendMessage", call.Url);
        Assert.Contains("\"chat_id\":\"42\"", call.Body);
        Assert.Contains("Job Sarthi alert\\n\\ngreenhouse is failing", call.Body);
    }

    [Fact]
    public async Task Telegram_error_carries_telegrams_own_explanation()
    {
        var h = new FakeHandler(HttpStatusCode.BadRequest, """{"ok":false,"description":"Bad Request: chat not found"}""");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Tg(h).SendAsync("s", "b", default));
        Assert.Contains("chat not found", ex.Message);
        Assert.DoesNotContain("123:ABC", ex.Message);                     // the token never appears in errors
    }

    [Fact]
    public async Task Telegram_finds_chat_ids_from_updates()
    {
        var h = new FakeHandler(HttpStatusCode.OK,
            """{"ok":true,"result":[{"message":{"chat":{"id":987654,"first_name":"Asha","last_name":"V"}}},{"message":{"chat":{"id":987654,"first_name":"Asha","last_name":"V"}}}]}""");
        var chats = await Tg(h).FindChatsAsync(default);
        var c = Assert.Single(chats);
        Assert.Equal(("987654", "Asha V"), c);
    }

    [Fact]
    public void Channels_are_unconfigured_until_filled_in()
    {
        Assert.False(Tg(new FakeHandler(HttpStatusCode.OK, "{}"), token: "", chat: "").IsConfigured);
        Assert.False(Tg(new FakeHandler(HttpStatusCode.OK, "{}"), chat: "").IsConfigured);
        Assert.True(Tg(new FakeHandler(HttpStatusCode.OK, "{}")).IsConfigured);
        Assert.False(new EmailNotifier(Options.Create(new EmailOptions())).IsConfigured);
        Assert.True(new EmailNotifier(Options.Create(new EmailOptions { Host = "smtp.x", To = "me@x.com" })).IsConfigured);
    }

    // ---------- email, against a tiny local SMTP server ----------

    [Fact]
    public async Task Email_is_delivered_over_smtp_with_subject_and_body()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            var data = new StringBuilder();
            await writer.WriteLineAsync("220 test ESMTP");
            while (await reader.ReadLineAsync() is { } line)
            {
                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase)) { await writer.WriteLineAsync("250-test"); await writer.WriteLineAsync("250 8BITMIME"); }
                else if (line.StartsWith("MAIL") || line.StartsWith("RCPT")) await writer.WriteLineAsync("250 OK");
                else if (line == "DATA")
                {
                    await writer.WriteLineAsync("354 go on");
                    while (await reader.ReadLineAsync() is { } d && d != ".") data.AppendLine(d);
                    await writer.WriteLineAsync("250 queued");
                }
                else if (line == "QUIT") { await writer.WriteLineAsync("221 bye"); break; }
                else await writer.WriteLineAsync("250 OK");
            }
            return data.ToString();
        });

        var notifier = new EmailNotifier(Options.Create(new EmailOptions
        { Host = "127.0.0.1", Port = port, Security = "None", From = "bot@example.com", To = "me@example.com" }));
        await notifier.SendAsync("Job Sarthi test", "Hello from the daily run.", default);

        var mail = await received.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("Subject: Job Sarthi test", mail);
        Assert.Contains("To: me@example.com", mail);
        Assert.Contains("Hello from the daily run.", mail);
    }

    // ---------- when messages are sent ----------

    private class Spy(string name, bool configured, bool fails = false) : INotifier
    {
        public string Name => name;
        public bool IsConfigured => configured;
        public List<string> Sent { get; } = new();
        public Task SendAsync(string subject, string body, CancellationToken ct)
        {
            if (fails) throw new InvalidOperationException("boom");
            Sent.Add(body);
            return Task.CompletedTask;
        }
    }

    private static JobSarthiDbContext Db(bool summary = true, bool alerts = true)
    {
        var db = new JobSarthiDbContext(new DbContextOptionsBuilder<JobSarthiDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Settings.Add(new AppSetting { NotifySummary = summary, NotifyAlerts = alerts });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Alerts_go_to_configured_channels_only_and_are_saved_for_the_app()
    {
        var db = Db();
        var tg = new Spy("telegram", true); var mail = new Spy("email", false);
        await new AlertService(db, new INotifier[] { tg, mail }, NullLogger<AlertService>.Instance).RaiseAsync("source", "greenhouse is down");
        Assert.Equal(new[] { "greenhouse is down" }, tg.Sent);
        Assert.Empty(mail.Sent);
        Assert.Equal("source", Assert.Single(db.Alerts).Kind);
    }

    [Fact]
    public async Task Switching_alerts_off_still_records_them_but_sends_nothing()
    {
        var db = Db(alerts: false);
        var tg = new Spy("telegram", true);
        await new AlertService(db, new INotifier[] { tg }, NullLogger<AlertService>.Instance).RaiseAsync("run-failed", "x");
        Assert.Empty(tg.Sent);
        Assert.Single(db.Alerts);
    }

    [Fact]
    public async Task The_summary_switch_is_separate_from_the_alert_switch()
    {
        var db = Db(summary: false, alerts: true);
        var tg = new Spy("telegram", true);
        var svc = new AlertService(db, new INotifier[] { tg }, NullLogger<AlertService>.Instance);
        await svc.NotifyAsync("s", "daily summary", NotifyKind.Summary);
        await svc.NotifyAsync("s", "an alert", NotifyKind.Alert);
        Assert.Equal(new[] { "an alert" }, tg.Sent);
    }

    [Fact]
    public async Task A_broken_channel_never_stops_the_others_or_the_run()
    {
        var db = Db();
        var bad = new Spy("telegram", true, fails: true); var good = new Spy("email", true);
        await new AlertService(db, new INotifier[] { bad, good }, NullLogger<AlertService>.Instance).NotifyAsync("s", "hello", NotifyKind.Summary);
        Assert.Equal(new[] { "hello" }, good.Sent);
    }
}
