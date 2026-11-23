using System.Text.Json;
using System.Text.Json.Nodes;
using JobSarthi.Api.Mcp;
using Xunit;

namespace JobSarthi.Api.Tests;

/// <summary>An in-memory MCP server: answers initialize, tools/list and tools/call like Playwright MCP would.</summary>
internal sealed class FakeTransport : IMcpTransport
{
    public List<JsonObject> Sent { get; } = new();
    public Action<JsonElement>? OnMessage { get; set; }
    public bool IsAlive { get; private set; }
    public Func<string, JsonObject?, JsonNode?> Handler { get; set; } = (_, _) => new JsonObject();

    public Task StartAsync(CancellationToken ct) { IsAlive = true; return Task.CompletedTask; }

    public Task SendAsync(JsonObject message, CancellationToken ct)
    {
        Sent.Add(message);
        if (message["id"] is null || message["method"] is null) return Task.CompletedTask;   // notification or our reply
        var method = message["method"]!.GetValue<string>();
        var reply = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = message["id"]!.DeepClone() };
        if (method == "fail") reply["error"] = new JsonObject { ["code"] = -1, ["message"] = "boom" };
        else reply["result"] = Handler(method, message["params"] as JsonObject);
        var json = JsonDocument.Parse(reply.ToJsonString()).RootElement.Clone();
        _ = Task.Run(() => OnMessage?.Invoke(json));   // replies arrive on another thread, like a real pipe
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() { IsAlive = false; return ValueTask.CompletedTask; }
}

public class McpClientTests
{
    private static FakeTransport PlaywrightLike() => new()
    {
        Handler = (method, p) => method switch
        {
            "initialize" => new JsonObject { ["protocolVersion"] = "2025-06-18", ["serverInfo"] = new JsonObject { ["name"] = "Playwright", ["version"] = "0.0.40" }, ["capabilities"] = new JsonObject() },
            "tools/list" => new JsonObject { ["tools"] = new JsonArray(
                new JsonObject { ["name"] = "browser_navigate", ["description"] = "Navigate", ["inputSchema"] = new JsonObject { ["type"] = "object" } },
                new JsonObject { ["name"] = "browser_snapshot" }) },
            "tools/call" => new JsonObject { ["content"] = new JsonArray(
                new JsonObject { ["type"] = "text", ["text"] = $"called {p!["name"]} with {p["arguments"]!.ToJsonString()}" },
                new JsonObject { ["type"] = "image", ["data"] = "AAAA", ["mimeType"] = "image/png" }) },
            _ => new JsonObject(),
        },
    };

    [Fact]
    public async Task Handshake_then_list_and_call_tools()
    {
        var t = PlaywrightLike();
        await using var c = new McpClient("playwright", t, TimeSpan.FromSeconds(5));
        await c.ConnectAsync(CancellationToken.None);

        Assert.Equal("Playwright", c.ServerName);
        Assert.Equal("initialize", t.Sent[0]["method"]!.GetValue<string>());
        Assert.Equal("notifications/initialized", t.Sent[1]["method"]!.GetValue<string>());
        Assert.Null(t.Sent[1]["id"]);   // a notification carries no id

        var tools = await c.ListToolsAsync(CancellationToken.None);
        Assert.Equal(new[] { "browser_navigate", "browser_snapshot" }, tools.Select(x => x.Name));

        var r = await c.CallToolAsync("browser_navigate", new JsonObject { ["url"] = "https://example.com" }, CancellationToken.None);
        Assert.False(r.IsError);
        Assert.Contains("called browser_navigate", r.Text);
        Assert.Contains("https://example.com", r.Text);
        Assert.Equal(1, r.ImageCount);
    }

    [Fact]
    public async Task Server_answers_ping_requests_from_server()
    {
        var t = PlaywrightLike();
        await using var c = new McpClient("x", t, TimeSpan.FromSeconds(5));
        await c.ConnectAsync(CancellationToken.None);
        t.OnMessage!(JsonDocument.Parse("""{"jsonrpc":"2.0","id":"s1","method":"ping"}""").RootElement);
        var reply = t.Sent.Last();
        Assert.Equal("s1", reply["id"]!.GetValue<string>());
        Assert.NotNull(reply["result"]);
    }

    [Fact]
    public async Task Silent_server_times_out()
    {
        var silent = new SilentTransport();
        await using var c = new McpClient("slow", silent, TimeSpan.FromMilliseconds(200));
        var ex = await Assert.ThrowsAsync<McpException>(() => c.ConnectAsync(CancellationToken.None));
        Assert.Contains("did not answer", ex.Message);
    }

    private sealed class SilentTransport : IMcpTransport
    {
        public Action<JsonElement>? OnMessage { get; set; }
        public bool IsAlive => true;
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task SendAsync(JsonObject message, CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void Secret_refs_come_from_the_environment()
    {
        var env = new Dictionary<string, string> { ["MY_TOKEN"] = "s3cr3t" };
        Assert.Equal("Bearer s3cr3t", SecretRef.Expand("Bearer ${MY_TOKEN}", k => env.GetValueOrDefault(k)));
        Assert.Throws<InvalidOperationException>(() => SecretRef.Expand("${MISSING_ONE}", _ => null));
    }

    [Fact]
    public void Upload_button_is_picked_from_the_snapshot()
    {
        const string snap = """
        - textbox "First name" [ref=e5]
        - button "Attach" [ref=e9]
        - button "Upload Resume/CV" [ref=e12]
        - button "Submit application" [ref=e20]
        """;
        var pick = McpApplyDrafter.PickUploadLine(snap);
        Assert.NotNull(pick);
        Assert.Equal("e12", pick!.Value.Ref);
    }
}
