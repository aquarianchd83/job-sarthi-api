using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JobSarthi.Api.Mcp;

/// <summary>Starts the server as a child process and speaks newline-delimited JSON-RPC over its stdin/stdout.</summary>
public sealed class StdioTransport(string name, McpServerOptions options, ILogger log) : IMcpTransport
{
    private Process? _proc;
    private readonly SemaphoreSlim _write = new(1, 1);
    public Action<JsonElement>? OnMessage { get; set; }
    public bool IsAlive => _proc is { HasExited: false };

    public Task StartAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Command)) throw new McpException($"MCP server '{name}' has no Command.");
        var (file, args) = Resolve(options.Command, options.Args);
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false), StandardInputEncoding = new UTF8Encoding(false),
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (!string.IsNullOrWhiteSpace(options.WorkingDirectory)) psi.WorkingDirectory = options.WorkingDirectory;
        foreach (var (k, v) in options.Env) psi.Environment[k] = SecretRef.Expand(v);

        _proc = Process.Start(psi) ?? throw new McpException($"Could not start MCP server '{name}'.");
        _ = Task.Run(ReadLoopAsync);
        _ = Task.Run(async () =>
        {
            // Server logs go to stderr. Logged at debug level: they can echo arguments.
            while (_proc is { HasExited: false } && await _proc.StandardError.ReadLineAsync() is { } line)
                log.LogDebug("[mcp:{Name}] {Line}", name, line);
        });
        log.LogInformation("Started MCP server {Name} ({Command})", name, options.Command);
        return Task.CompletedTask;
    }

    /// <summary>On Windows, "npx", "npm" and other commands without an extension are .cmd scripts that only cmd.exe can run.</summary>
    public static (string File, List<string> Args) Resolve(string command, List<string> args)
    {
        if (OperatingSystem.IsWindows() && Path.GetExtension(command).Length == 0)
            return ("cmd.exe", new List<string> { "/d", "/s", "/c", command }.Concat(args).ToList());
        return (command, args.ToList());
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (_proc is { HasExited: false } && await _proc.StandardOutput.ReadLineAsync() is { } line)
            {
                if (line.Length == 0 || line[0] != '{') continue;   // some servers print banners
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    OnMessage?.Invoke(doc.RootElement.Clone());
                }
                catch (JsonException) { log.LogDebug("[mcp:{Name}] ignored non-JSON line", name); }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
        log.LogInformation("MCP server {Name} stopped", name);
    }

    public async Task SendAsync(JsonObject message, CancellationToken ct)
    {
        if (!IsAlive) throw new McpException($"MCP server '{name}' is not running.");
        await _write.WaitAsync(ct);
        try
        {
            await _proc!.StandardInput.WriteLineAsync(message.ToJsonString().AsMemory(), ct);
            await _proc.StandardInput.FlushAsync(ct);
        }
        finally { _write.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_proc is null) return;
        try
        {
            _proc.StandardInput.Close();   // polite shutdown per the MCP spec
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _proc.WaitForExitAsync(cts.Token);
        }
        catch (Exception) { /* fall through to kill */ }
        try { if (!_proc.HasExited) _proc.Kill(entireProcessTree: true); } catch (Exception) { }
        _proc.Dispose();
    }
}
