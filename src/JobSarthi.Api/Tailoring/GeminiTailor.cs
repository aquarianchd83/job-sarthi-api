using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using JobSarthi.Api.Common;

namespace JobSarthi.Api.Tailoring;

public class GeminiOptions
{
    /// <summary>Free key from Google AI Studio. Put it in appsettings.Local.json, never in git.</summary>
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-2.0-flash";
}

/// <summary>Optional. Uses the free Gemini tier. Sends your skills, headline and the job text to Google when switched on.</summary>
public class GeminiTailor(HttpClient http, IOptions<GeminiOptions> options) : ITailorService
{
    private readonly GeminiOptions _o = options.Value;
    public string Name => "gemini";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_o.ApiKey);

    public async Task<TailoredDocs> TailorAsync(TailorInput input, CancellationToken ct)
    {
        if (!IsConfigured) throw new TailorUnavailableException("No Gemini API key configured");

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_o.Model}:generateContent";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                contents = new[] { new { parts = new[] { new { text = LlmPrompt.Build(input) } } } },
                generationConfig = new { responseMimeType = "application/json", temperature = 0.3 },
            }),
        };
        req.Headers.Add("x-goog-api-key", _o.ApiKey); // header, so the key never appears in a URL or log

        using var res = await http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.TooManyRequests)
            throw new TailorUnavailableException("Gemini free limit reached", rateLimited: true);
        if (res.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new TailorUnavailableException($"Gemini rejected the request ({(int)res.StatusCode}). Check the API key and model name");
        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var text = doc.RootElement.TryGetProperty("candidates", out var c) && c.GetArrayLength() > 0
            && c[0].TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0
            ? parts[0].Str("text") : "";
        if (text.Length == 0) throw new TailorUnavailableException("Gemini returned no text");
        return LlmPrompt.ToDocs(Name, text, input);
    }
}
