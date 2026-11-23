namespace JobSarthi.Api.Mcp;

/// <summary>
/// External MCP servers this app connects to as a client (section "Mcp" in appsettings).
/// Secrets never go here as plain text: write ${ENV_VAR_NAME} and set the variable on the machine.
/// </summary>
public class McpOptions
{
    /// <summary>Off by default. Nothing is started until you switch it on.</summary>
    public bool Enabled { get; set; }
    /// <summary>The server used to open apply pages and pre-fill forms for Manual apply jobs. Must be a Playwright MCP server.</summary>
    public string BrowserServer { get; set; } = "playwright";
    /// <summary>The /api/mcp endpoints answer only calls from this computer unless this is true.</summary>
    public bool AllowRemoteCalls { get; set; }
    public int CallTimeoutSeconds { get; set; } = 90;
    /// <summary>
    /// Folders the servers may read (MCP "roots"). Playwright MCP uploads files only from these.
    /// The resume folder is added automatically.
    /// </summary>
    public List<string> Roots { get; set; } = new();
    public Dictionary<string, McpServerOptions> Servers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class McpServerOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>stdio: the program to start, e.g. "npx". On Windows a command without extension runs through cmd.exe.</summary>
    public string Command { get; set; } = "";
    public List<string> Args { get; set; } = new();
    /// <summary>Extra environment variables for the server process. Values may be ${ENV_VAR} references.</summary>
    public Dictionary<string, string> Env { get; set; } = new();
    public string? WorkingDirectory { get; set; }
    /// <summary>Streamable HTTP transport instead of stdio, e.g. http://localhost:8931/mcp.</summary>
    public string? Url { get; set; }
    /// <summary>HTTP headers (e.g. Authorization). Values may be ${ENV_VAR} references.</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    public bool IsHttp => !string.IsNullOrWhiteSpace(Url);
}
