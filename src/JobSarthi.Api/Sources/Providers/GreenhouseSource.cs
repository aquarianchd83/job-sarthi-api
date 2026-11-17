using System.Text.Json;
using JobSarthi.Api.Common;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Sources.Providers;

/// <summary>Greenhouse public job board API (no key needed).</summary>
public class GreenhouseSource(HttpClient http) : IJobSource
{
    public string Key => "greenhouse";
    public string DisplayName => "Greenhouse";
    public SourceCapability Capability => SourceCapability.FetchAndApply;

    public async Task<FetchResult> FetchAsync(IReadOnlyList<string> companySlugs, CancellationToken ct)
    {
        var jobs = new List<Job>();
        var errors = new List<string>();
        foreach (var slug in companySlugs)
        {
            try
            {
                var json = await http.GetStringAsync($"https://boards-api.greenhouse.io/v1/boards/{slug}/jobs?content=true", ct);
                jobs.AddRange(Parse(json, slug));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                errors.Add($"{slug}: {ex.Message}");
            }
        }
        return new FetchResult(jobs, errors);
    }

    public static List<Job> Parse(string json, string slug)
    {
        var list = new List<Job>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("jobs", out var jobs)) return list;
        foreach (var j in jobs.EnumerateArray())
        {
            var location = j.TryGetProperty("location", out var l) ? l.Str("name") : "";
            var title = j.Str("title");
            list.Add(new Job
            {
                ExternalId = $"{slug}:{j.Str("id")}",
                Title = title,
                Company = TextUtil.PrettySlug(slug),
                Location = location,
                WorkMode = TextUtil.DetectWorkMode(location, title),
                Url = j.Str("absolute_url"),
                Description = TextUtil.StripHtml(j.Str("content")),
            });
        }
        return list;
    }
}
