using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JobSarthi.Api.Mcp;

/// <summary>
/// A Model Context Protocol client for one server: initialize handshake, tools/list and tools/call over JSON-RPC 2.0.
/// The app is the client; it uses tools that external servers (e.g. Playwright MCP) offer. It is never a server itself.
/// </summary>
public sealed class McpClient : IAsyncDisposable
{
    public const string ProtocolVersion = "2025-06-18";
    private readonly IMcpTransport _transport;
    private readonly TimeSpan _timeout;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private long _nextId;

    public string Name { get; }
    public string? ServerName { get; private set; }
    public string? ServerVersion { get; private set; }
    public bool IsAlive => _transport.IsAlive;

    private readonly IReadOnlyList<string> _roots;

    /// <param name="roots">Folders the server may use (answered to roots/list). Playwright MCP only uploads files from these.</param>
    public McpClient(string name, IMcpTransport transport, TimeSpan timeout, IReadOnlyList<string>? roots = null)
    {
        Name = name;
        _roots = roots ?? Array.Empty<string>();
        _transport = transport;
        _timeout = timeout;
        _transport.OnMessage = OnMessage;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        await _transport.StartAsync(ct);
        var init = await RequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject { ["roots"] = new JsonObject { ["listChanged"] = false } },
            ["clientInfo"] = new JsonObject { ["name"] = "job-sarthi", ["version"] = "1.0" },
        }, ct);
        if (init.TryGetProperty("serverInfo", out var info))
        {
            ServerName = info.TryGetProperty("name", out var n) ? n.GetString() : null;
            ServerVersion = info.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        if (_transport is HttpTransport http)
            http.ProtocolVersion = init.TryGetProperty("protocolVersion", out var pv) ? pv.GetString() : ProtocolVersion;
        await NotifyAsync("notifications/initialized", ct);
    }

    private List<McpTool>? _tools;

    /// <summary>The tool list, read once per connection.</summary>
    public async Task<IReadOnlyList<McpTool>> GetToolsAsync(CancellationToken ct) => _tools ??= await ListToolsAsync(ct);

    public async Task<List<McpTool>> ListToolsAsync(CancellationToken ct)
    {
        var tools = new List<McpTool>();
        string? cursor = null;
        do
        {
            var p = new JsonObject();
            if (cursor is not null) p["cursor"] = cursor;
            var res = await RequestAsync("tools/list", p, ct);
            if (res.TryGetProperty("tools", out var arr))
                foreach (var t in arr.EnumerateArray())
                    tools.Add(new McpTool(t.GetProperty("name").GetString() ?? "",
                        t.TryGetProperty("description", out var d) ? d.GetString() : null,
                        t.TryGetProperty("inputSchema", out var s) ? s.Clone() : null));
            cursor = res.TryGetProperty("nextCursor", out var nc) && nc.ValueKind == JsonValueKind.String ? nc.GetString() : null;
        } while (cursor is not null);
        return tools;
    }

    public async Task<McpToolResult> CallToolAsync(string tool, JsonObject? arguments, CancellationToken ct)
    {
        var res = await RequestAsync("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = arguments ?? new JsonObject() }, ct);
        var text = new StringBuilder();
        var images = 0;
        if (res.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            foreach (var c in content.EnumerateArray())
            {
                var type = c.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "text" && c.TryGetProperty("text", out var tx)) text.AppendLine(tx.GetString());
                else if (type == "image") images++;
            }
        var isError = res.TryGetProperty("isError", out var e) && e.ValueKind == JsonValueKind.True;
        return new McpToolResult(isError, text.ToString().TrimEnd(), images, res);
    }

    private async Task<JsonElement> RequestAsync(string method, JsonObject @params, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_timeout);
            try
            {
                await _transport.SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = @params }, timeout.Token);
                return await tcs.Task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Tell the server to stop working on it.
                _ = NotifyAsync("notifications/cancelled", CancellationToken.None, new JsonObject { ["requestId"] = id, ["reason"] = "timeout" });
                throw new McpException($"MCP server '{Name}' did not answer {method} within {_timeout.TotalSeconds:0}s.");
            }
        }
        finally { _pending.TryRemove(id, out _); }
    }

    private Task NotifyAsync(string method, CancellationToken ct, JsonObject? @params = null)
    {
        var msg = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };
        if (@params is not null) msg["params"] = @params;
        return _transport.SendAsync(msg, ct);
    }

    private void OnMessage(JsonElement m)
    {
        var hasId = m.TryGetProperty("id", out var idEl);
        var hasMethod = m.TryGetProperty("method", out var method);

        if (hasId && !hasMethod)   // a reply to one of our requests
        {
            if (!idEl.TryGetInt64(out var id) && !long.TryParse(idEl.ToString(), out id)) return;
            if (!_pending.TryGetValue(id, out var tcs)) return;
            if (m.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var em) ? em.GetString() : err.ToString();
                tcs.TrySetException(new McpException($"MCP server '{Name}': {msg}"));
            }
            else tcs.TrySetResult(m.TryGetProperty("result", out var r) ? r.Clone() : default);
            return;
        }

        if (hasId && hasMethod)    // the server asks us something: answer ping, decline the rest
        {
            var reply = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = JsonNode.Parse(idEl.GetRawText()) };
            if (method.GetString() == "ping") reply["result"] = new JsonObject();
            else if (method.GetString() == "roots/list")
                reply["result"] = new JsonObject
                {
                    ["roots"] = new JsonArray(_roots.Select(r => (JsonNode)new JsonObject
                        { ["uri"] = new Uri(Path.GetFullPath(r)).AbsoluteUri, ["name"] = Path.GetFileName(Path.TrimEndingDirectorySeparator(r)) }).ToArray()),
                };
            else reply["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Method not supported by this client" };
            _ = _transport.SendAsync(reply, CancellationToken.None);
        }
        // Notifications (logging, progress, list_changed) need no answer.
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var p in _pending.Values) p.TrySetException(new McpException($"MCP server '{Name}' was closed."));
        await _transport.DisposeAsync();
    }
}
