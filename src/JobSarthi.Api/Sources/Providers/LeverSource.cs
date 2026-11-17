using System.Text.Json;
using JobSarthi.Api.Common;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Sources.Providers;

/// <summary>Lever public postings API (no key needed).</summary>
public class LeverSource(HttpClient http) : IJobSource
{
    public string Key => "lever";
    public string DisplayName => "Lever";
    public SourceCapability Capability => SourceCapability.FetchAndApply;

    public async Task<FetchResult> FetchAsync(IReadOnlyList<string> companySlugs, CancellationToken ct)
    {
        var jobs = new List<Job>();
        var errors = new List<string>();
        foreach (var slug in companySlugs)
        {
            try
            {
                var json = await http.GetStringAsync($"https://api.lever.co/v0/postings/{slug}?mode=json", ct);
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
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var j in doc.RootElement.EnumerateArray())
        {
            var location = j.TryGetProperty("categories", out var c) ? c.Str("location") : "";
            var title = j.Str("text");
            list.Add(new Job
            {
                ExternalId = $"{slug}:{j.Str("id")}",
                Title = title,
                Company = TextUtil.PrettySlug(slug),
                Location = location,
                WorkMode = TextUtil.DetectWorkMode(j.Str("workplaceType"), location, title),
                Url = j.Str("hostedUrl"),
                Description = j.Str("descriptionPlain") is { Length: > 0 } p ? p : TextUtil.StripHtml(j.Str("description")),
            });
        }
        return list;
    }
}
