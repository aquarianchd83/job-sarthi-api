using System.Text.Json;
using JobSarthi.Api.Common;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Sources.Providers;

/// <summary>RemoteOK public JSON feed. Fetch only: you apply on their site.</summary>
public class RemoteOkSource(HttpClient http) : IJobSource
{
    public string Key => "remoteok";
    public string DisplayName => "RemoteOK";
    public SourceCapability Capability => SourceCapability.FetchOnly;

    public async Task<FetchResult> FetchAsync(IReadOnlyList<string> companySlugs, CancellationToken ct)
    {
        try
        {
            var json = await http.GetStringAsync("https://remoteok.com/api", ct);
            return new FetchResult(Parse(json), Array.Empty<string>());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return FetchResult.Empty(ex.Message);
        }
    }

    public static List<Job> Parse(string json)
    {
        var list = new List<Job>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var j in doc.RootElement.EnumerateArray())
        {
            var title = j.Str("position");
            if (title.Length == 0) continue; // first element is a legal notice
            list.Add(new Job
            {
                ExternalId = j.Str("id"),
                Title = title,
                Company = j.Str("company"),
                Location = j.Str("location") is { Length: > 0 } loc ? loc : "Worldwide",
                WorkMode = WorkMode.Remote,
                Url = j.Str("url"),
                Description = TextUtil.StripHtml(j.Str("description")),
            });
        }
        return list;
    }
}
