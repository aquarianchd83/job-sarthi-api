using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace JobSarthi.Api.Mcp;

public record McpServerStatus(string Name, string Transport, bool Enabled, bool Connected, string? ServerName, string? ServerVersion);

/// <summary>Starts each configured MCP server on first use and keeps it running (so a browser it opened stays open), one client per server.</summary>
public sealed class McpClientManager(IOptionsMonitor<McpOptions> options, IHttpClientFactory httpFactory, ILoggerFactory logs) : IAsyncDisposable
{
    public const string HttpClientName = "mcp";
    private readonly ConcurrentDictionary<string, McpClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _connect = new(1, 1);
    private readonly ILogger _log = logs.CreateLogger<McpClientManager>();

    public McpOptions Options => options.CurrentValue;
    public bool Enabled => Options.Enabled;

    public bool IsConfigured(string name) =>
        Options.Enabled && Options.Servers.TryGetValue(name, out var s) && s.Enabled;

    public IReadOnlyList<McpServerStatus> List() =>
        Options.Servers.Select(kv =>
        {
            _clients.TryGetValue(kv.Key, out var c);
            return new McpServerStatus(kv.Key, kv.Value.IsHttp ? "http" : "stdio", Options.Enabled && kv.Value.Enabled,
                c is { IsAlive: true }, c?.ServerName, c?.ServerVersion);
        }).ToList();

    public async Task<McpClient> GetAsync(string name, CancellationToken ct)
    {
        if (_clients.TryGetValue(name, out var c) && c.IsAlive) return c;
        if (!Options.Enabled) throw new McpException("MCP is switched off. Set Mcp:Enabled to true.");
        if (!Options.Servers.TryGetValue(name, out var cfg)) throw new McpException($"No MCP server named '{name}' in settings.");
        if (!cfg.Enabled) throw new McpException($"MCP server '{name}' is switched off.");

        await _connect.WaitAsync(ct);
        try
        {
            if (_clients.TryGetValue(name, out c))
            {
                if (c.IsAlive) return c;
                _clients.TryRemove(name, out _);
                await c.DisposeAsync();
            }
            IMcpTransport transport = cfg.IsHttp
                ? new HttpTransport(name, cfg, httpFactory.CreateClient(HttpClientName))
                : new StdioTransport(name, cfg, _log);
            var client = new McpClient(name, transport, TimeSpan.FromSeconds(Math.Max(10, Options.CallTimeoutSeconds)), Options.Roots);
            try { await client.ConnectAsync(ct); }
            catch { await client.DisposeAsync(); throw; }
            _clients[name] = client;
            return client;
        }
        finally { _connect.Release(); }
    }

    public async Task<McpToolResult> CallAsync(string server, string tool, JsonObject? args, CancellationToken ct) =>
        await (await GetAsync(server, ct)).CallToolAsync(tool, args, ct);

    /// <summary>Stops a server (closes any browser it opened).</summary>
    public async Task<bool> StopAsync(string name)
    {
        if (!_clients.TryRemove(name, out var c)) return false;
        await c.DisposeAsync();
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _clients.Values)
            try { await c.DisposeAsync(); } catch (Exception) { }
        _clients.Clear();
    }
}

public static class McpExtensions
{
    public static IServiceCollection AddMcpClient(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<McpOptions>().Bind(config.GetSection("Mcp")).PostConfigure<IHostEnvironment>((o, env) =>
        {
            // The resume folder, so the browser server can attach your resume to a form.
            var resumes = config["Storage:ResumeDir"] is { Length: > 0 } d ? d : Path.Combine(env.ContentRootPath, "data", "resumes");
            if (!o.Roots.Contains(resumes, StringComparer.OrdinalIgnoreCase)) o.Roots.Add(resumes);
        });
        // Header values can hold tokens, so this client is never logged.
        services.AddHttpClient(McpClientManager.HttpClientName, c => c.Timeout = Timeout.InfiniteTimeSpan).RemoveAllLoggers();
        services.AddSingleton<McpClientManager>();
        services.AddScoped<McpApplyDrafter>();
        return services;
    }
}
