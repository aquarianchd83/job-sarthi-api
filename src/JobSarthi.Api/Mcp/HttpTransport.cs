using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JobSarthi.Api.Mcp;

/// <summary>MCP "Streamable HTTP": every message is a POST; the reply is JSON or a short server-sent-event stream.</summary>
public sealed class HttpTransport(string name, McpServerOptions options, HttpClient http) : IMcpTransport
{
    private string? _sessionId;
    public string? ProtocolVersion { get; set; }
    public Action<JsonElement>? OnMessage { get; set; }
    public bool IsAlive { get; private set; }

    public Task StartAsync(CancellationToken ct)
    {
        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out _)) throw new McpException($"MCP server '{name}' has an invalid Url.");
        IsAlive = true;
        return Task.CompletedTask;
    }

    public async Task SendAsync(JsonObject message, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, options.Url)
        {
            Content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (_sessionId is not null) req.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        if (ProtocolVersion is not null) req.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
        foreach (var (k, v) in options.Headers) req.Headers.TryAddWithoutValidation(k, SecretRef.Expand(v));

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.Headers.TryGetValues("Mcp-Session-Id", out var sid)) _sessionId = sid.FirstOrDefault();
        if ((int)resp.StatusCode == 202) return;   // notification accepted
        if (!resp.IsSuccessStatusCode)
            throw new McpException($"MCP server '{name}' answered HTTP {(int)resp.StatusCode}.");

        var media = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (media.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
        {
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            var data = new StringBuilder();
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (line.StartsWith("data:")) { data.Append(line.AsSpan(5).TrimStart()); continue; }
                if (line.Length == 0 && data.Length > 0)
                {
                    var done = Dispatch(data.ToString(), message);
                    data.Clear();
                    if (done) return;   // our reply arrived, stop reading
                }
            }
            if (data.Length > 0) Dispatch(data.ToString(), message);
        }
        else
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(body)) Dispatch(body, message);
        }
    }

    /// <summary>Hands each message to the client. True once the reply to <paramref name="sent"/> was seen.</summary>
    private bool Dispatch(string json, JsonObject sent)
    {
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new() { doc.RootElement };
        var done = false;
        foreach (var m in items)
        {
            OnMessage?.Invoke(m.Clone());
            if (m.TryGetProperty("id", out var id) && sent["id"] is { } sentId && id.ToString() == sentId.ToString()
                && (m.TryGetProperty("result", out _) || m.TryGetProperty("error", out _))) done = true;
        }
        return done;
    }

    public async ValueTask DisposeAsync()
    {
        IsAlive = false;
        if (_sessionId is null) return;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete, options.Url);
            req.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await http.SendAsync(req, cts.Token);
        }
        catch (Exception) { /* server may not support explicit close */ }
    }
}
