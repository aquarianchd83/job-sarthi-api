using System.Xml.Linq;
using JobSarthi.Api.Common;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Sources.Providers;

/// <summary>WeWorkRemotely public RSS feed. Fetch only: you apply on their site.</summary>
public class WeWorkRemotelySource(HttpClient http) : IJobSource
{
    public string Key => "weworkremotely";
    public string DisplayName => "WeWorkRemotely";
    public SourceCapability Capability => SourceCapability.FetchOnly;

    public async Task<FetchResult> FetchAsync(IReadOnlyList<string> companySlugs, CancellationToken ct)
    {
        try
        {
            var xml = await http.GetStringAsync("https://weworkremotely.com/remote-jobs.rss", ct);
            return new FetchResult(Parse(xml), Array.Empty<string>());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            return FetchResult.Empty(ex.Message);
        }
    }

    public static List<Job> Parse(string xml)
    {
        var list = new List<Job>();
        var doc = XDocument.Parse(xml);
        foreach (var item in doc.Descendants("item"))
        {
            var raw = item.Element("title")?.Value ?? "";
            var link = item.Element("link")?.Value ?? "";
            if (raw.Length == 0 || link.Length == 0) continue;
            // Titles look like "Company: Role".
            var split = raw.IndexOf(':');
            var company = split > 0 ? raw[..split].Trim() : "";
            var title = split > 0 ? raw[(split + 1)..].Trim() : raw.Trim();
            var region = item.Element("region")?.Value ?? "";
            list.Add(new Job
            {
                ExternalId = item.Element("guid")?.Value is { Length: > 0 } g ? g : link,
                Title = title,
                Company = company,
                Location = region.Length > 0 ? region : "Remote",
                WorkMode = WorkMode.Remote,
                Url = link,
                Description = TextUtil.StripHtml(item.Element("description")?.Value),
            });
        }
        return list;
    }
}
