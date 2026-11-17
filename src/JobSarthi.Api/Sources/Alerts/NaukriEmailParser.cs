using System.Text.RegularExpressions;
using JobSarthi.Api.Common;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Sources.Alerts;

/// <summary>
/// Reads Naukri job-alert emails. Heuristic: job links contain "naukri.com/job-listings-".
/// Company is not reliably present in the link text, so it stays blank. The link is what matters, you apply manually.
/// </summary>
public static class NaukriEmailParser
{
    private static readonly Regex JobId = new(@"-(\d{6,})(?:\?|$)", RegexOptions.Compiled);

    public static List<Job> Parse(string html)
    {
        var seen = new HashSet<string>();
        var jobs = new List<Job>();
        foreach (var (href, text) in AlertLinkParser.Links(html))
        {
            if (!href.Contains("naukri.com/job-listings-", StringComparison.OrdinalIgnoreCase)) continue;
            if (AlertLinkParser.IsGeneric(text)) continue;
            var url = href.Split('?')[0];
            var id = JobId.Match(href).Groups[1].Value;
            if (id.Length == 0) id = url[(url.LastIndexOf('/') + 1)..];
            if (!seen.Add(id)) continue;
            jobs.Add(new Job
            {
                ExternalId = id,
                Title = text,
                Company = "",
                Location = "",
                WorkMode = WorkMode.Onsite,
                Url = url,
                Description = "",
            });
        }
        return jobs;
    }
}
