using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using JobSarthi.Api.Common;

namespace JobSarthi.Api.Tailoring;

public class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3.1";
}

/// <summary>Optional. Uses a local Ollama model, so nothing leaves your server.</summary>
public class OllamaTailor(HttpClient http, IOptions<OllamaOptions> options) : ITailorService
{
    private readonly OllamaOptions _o = options.Value;
    public string Name => "ollama";

    public async Task<TailoredDocs> TailorAsync(TailorInput input, CancellationToken ct)
    {
        HttpResponseMessage res;
        try
        {
            res = await http.PostAsJsonAsync($"{_o.BaseUrl.TrimEnd('/')}/api/chat", new
            {
                model = _o.Model,
                stream = false,
                format = "json",
                messages = new[] { new { role = "user", content = LlmPrompt.Build(input) } },
                options = new { temperature = 0.3 },
            }, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new TailorUnavailableException($"Ollama is not reachable at {_o.BaseUrl} ({ex.Message})");
        }

        using (res)
        {
            if (!res.IsSuccessStatusCode)
                throw new TailorUnavailableException($"Ollama returned {(int)res.StatusCode}. Is model '{_o.Model}' pulled?");
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var text = doc.RootElement.TryGetProperty("message", out var m) ? m.Str("content") : "";
            if (text.Length == 0) throw new TailorUnavailableException("Ollama returned no text");
            return LlmPrompt.ToDocs(Name, text, input);
        }
    }
}
