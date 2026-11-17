using System.Text;
using JobSarthi.Api.Common;
using JobSarthi.Api.Domain.Entities;

namespace JobSarthi.Api.Notifications;

public record SummaryJob(string Title, string Company, string SourceKey, int Score, string Url, bool CanAutoApply);
public record SummaryApp(string Title, string Company);

public record SummaryData(DailyRun Run, string? AppUrl, List<SummaryJob> TopNew, List<SummaryApp> AwaitingApproval,
    List<SummaryApp> NeedsInput, List<SummaryApp> SubmittedToday, List<SummaryApp> FailedToday, int OpenAlerts);

/// <summary>Turns a finished run into the plain-text message you get on Telegram or email. Pure, so it is unit-tested.</summary>
public static class DailySummaryBuilder
{
    private const int ListLimit = 8;

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["greenhouse"] = "Greenhouse", ["lever"] = "Lever", ["ashby"] = "Ashby", ["remoteok"] = "RemoteOK",
        ["weworkremotely"] = "WeWorkRemotely", ["naukri"] = "Naukri alert", ["linkedin"] = "LinkedIn alert",
    };

    public static string Subject(DailyRun run) =>
        run.Error is not null ? "Job Sarthi: the daily run FAILED"
        : $"Job Sarthi: {run.Shortlisted} matches, {run.Queued} application{(run.Queued == 1 ? "" : "s")} queued";

    public static string Build(SummaryData d)
    {
        var r = d.Run;
        var sb = new StringBuilder();
        var when = IstTime.ToIst(r.StartedAtUtc).ToString("d MMM yyyy, h:mm tt") + " IST";
        sb.AppendLine($"Job Sarthi daily summary");
        sb.AppendLine($"{when} ({(r.Trigger == "schedule" ? "scheduled run" : "manual run")})");
        sb.AppendLine();

        if (r.Error is not null)
        {
            sb.AppendLine($"The run FAILED: {r.Error}");
            sb.AppendLine();
        }
        sb.AppendLine($"Fetched {r.Fetched} jobs ({r.NewJobs} new). {r.Shortlisted} match your rules. {r.Queued} application{(r.Queued == 1 ? "" : "s")} queued.");

        AppSection(sb, "Waiting for your approval", d.AwaitingApproval);
        AppSection(sb, "Needs your answer", d.NeedsInput);
        AppSection(sb, "Submitted today", d.SubmittedToday);
        AppSection(sb, "Failed today", d.FailedToday);
        if (d.OpenAlerts > 0) { sb.AppendLine(); sb.AppendLine($"{d.OpenAlerts} open alert{(d.OpenAlerts == 1 ? "" : "s")} in the app."); }

        sb.AppendLine();
        if (d.TopNew.Count == 0)
        {
            sb.AppendLine("No new matches in this run.");
        }
        else
        {
            sb.AppendLine("Top new matches");
            var i = 1;
            foreach (var j in d.TopNew)
            {
                var company = string.IsNullOrWhiteSpace(j.Company) ? "" : ", " + j.Company;
                var how = j.CanAutoApply ? "can auto-apply" : "apply yourself";
                sb.AppendLine($"{i++}. {j.Score}  {j.Title}{company} ({Label(j.SourceKey)}, {how})");
                sb.AppendLine($"   {j.Url}");
            }
        }

        if (!string.IsNullOrWhiteSpace(d.AppUrl))
        {
            sb.AppendLine();
            sb.AppendLine($"Open Job Sarthi: {d.AppUrl}");
        }
        return sb.ToString().TrimEnd();
    }

    private static void AppSection(StringBuilder sb, string title, List<SummaryApp> items)
    {
        if (items.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine($"{title} ({items.Count})");
        foreach (var a in items.Take(ListLimit))
            sb.AppendLine($"- {a.Title}{(string.IsNullOrWhiteSpace(a.Company) ? "" : ", " + a.Company)}");
        if (items.Count > ListLimit) sb.AppendLine($"- and {items.Count - ListLimit} more");
    }

    private static string Label(string key) => Labels.GetValueOrDefault(key, key);
}
