using System.Text.RegularExpressions;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Sources.Alerts;

/// <summary>
/// Reads LinkedIn job-alert emails. Heuristic: job links contain "/jobs/view/{id}" (also "/comm/jobs/view/").
/// The tracking query string is dropped. You apply manually.
/// </summary>
public static class LinkedInEmailParser
{
    private static readonly Regex JobId = new(@"/jobs/view/(?:[^/?]*-)?(\d{6,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<Job> Parse(string html)
    {
        var seen = new HashSet<string>();
        var jobs = new List<Job>();
        foreach (var (href, text) in AlertLinkParser.Links(html))
        {
            var m = JobId.Match(href);
            if (!m.Success || !href.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase)) continue;
            if (AlertLinkParser.IsGeneric(text)) continue;
            var id = m.Groups[1].Value;
            if (!seen.Add(id)) continue;
            jobs.Add(new Job
            {
                ExternalId = id,
                Title = text,
                Company = "",
                Location = "",
                WorkMode = WorkMode.Onsite,
                Url = $"https://www.linkedin.com/jobs/view/{id}",
                Description = "",
            });
        }
        return jobs;
    }
}
