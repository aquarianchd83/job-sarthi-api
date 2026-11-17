using JobSarthi.Api.Domain.Enums;
using JobSarthi.Api.Sources.Alerts;

namespace JobSarthi.Api.Sources.Providers;

/// <summary>Email alerts only. No scraping, no auto-apply.</summary>
public class LinkedInAlertSource(ImapAlertReader reader) : IJobSource
{
    public string Key => "linkedin";
    public string DisplayName => "LinkedIn (email alerts)";
    public SourceCapability Capability => SourceCapability.AlertsOnly;

    public async Task<FetchResult> FetchAsync(IReadOnlyList<string> companySlugs, CancellationToken ct)
    {
        if (!reader.IsConfigured) return FetchResult.Empty("Email (IMAP) is not configured. See Imap section in appsettings.");
        try
        {
            var mails = await reader.ReadAsync("linkedin.com", ct);
            var jobs = mails.SelectMany(m => LinkedInEmailParser.Parse(m.Html))
                            .GroupBy(j => j.ExternalId).Select(g => g.First()).ToList();
            return new FetchResult(jobs, Array.Empty<string>());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FetchResult.Empty("IMAP: " + ex.Message);
        }
    }
}
