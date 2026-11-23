using System.Text.Json;
using System.Text.Json.Nodes;

namespace JobSarthi.Api.Mcp;

public record McpTool(string Name, string? Description, JsonElement? InputSchema);

/// <param name="Text">All text content joined. Images are counted, not returned.</param>
public record McpToolResult(bool IsError, string Text, int ImageCount, JsonElement Raw);

public class McpException(string message) : Exception(message);

/// <summary>Moves JSON-RPC messages to and from one MCP server.</summary>
public interface IMcpTransport : IAsyncDisposable
{
    Task StartAsync(CancellationToken ct);
    /// <summary>Sends a message. For HTTP the reply may come back on the same call, so incoming messages are handed to <see cref="OnMessage"/>.</summary>
    Task SendAsync(JsonObject message, CancellationToken ct);
    Action<JsonElement>? OnMessage { get; set; }
    bool IsAlive { get; }
}
