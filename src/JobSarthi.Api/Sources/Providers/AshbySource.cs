using System.Text.Json;
using JobSarthi.Api.Common;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Sources.Providers;

/// <summary>Ashby public job board API (no key needed).</summary>
public class AshbySource(HttpClient http) : IJobSource
{
    public string Key => "ashby";
    public string DisplayName => "Ashby";
    public SourceCapability Capability => SourceCapability.FetchAndApply;

    public async Task<FetchResult> FetchAsync(IReadOnlyList<string> companySlugs, CancellationToken ct)
    {
        var jobs = new List<Job>();
        var errors = new List<string>();
        foreach (var slug in companySlugs)
        {
            try
            {
                var json = await http.GetStringAsync($"https://api.ashbyhq.com/posting-api/job-board/{slug}", ct);
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
            var location = j.Str("location");
            var title = j.Str("title");
            var isRemote = j.TryGetProperty("isRemote", out var r) && r.ValueKind == JsonValueKind.True;
            list.Add(new Job
            {
                ExternalId = $"{slug}:{j.Str("id")}",
                Title = title,
                Company = TextUtil.PrettySlug(slug),
                Location = location,
                WorkMode = isRemote ? WorkMode.Remote : TextUtil.DetectWorkMode(j.Str("workplaceType"), location, title),
                Url = j.Str("jobUrl"),
                Description = j.Str("descriptionPlain") is { Length: > 0 } p ? p : TextUtil.StripHtml(j.Str("descriptionHtml")),
            });
        }
        return list;
    }
}
