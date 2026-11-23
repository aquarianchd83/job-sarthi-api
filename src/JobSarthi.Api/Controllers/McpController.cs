using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using JobSarthi.Api.Mcp;

namespace JobSarthi.Api.Controllers;

public record McpToolDto(string Name, string? Description, JsonElement? InputSchema);
public record McpCallResultDto(bool IsError, string Text, int ImageCount);

/// <summary>
/// See and try the external MCP servers this app uses as a client. Personal tool: answers only calls from this computer
/// unless Mcp:AllowRemoteCalls is true. Tool arguments and results are never logged.
/// </summary>
[ApiController]
[Route("api/mcp")]
[LocalOnly]
public class McpController(McpClientManager mcp) : ControllerBase
{
    [HttpGet("servers")]
    public object Servers() => new { enabled = mcp.Enabled, browserServer = mcp.Options.BrowserServer, servers = mcp.List() };

    [HttpGet("servers/{name}/tools")]
    public async Task<ActionResult<IEnumerable<McpToolDto>>> Tools(string name, CancellationToken ct)
    {
        try { return (await (await mcp.GetAsync(name, ct)).ListToolsAsync(ct)).Select(t => new McpToolDto(t.Name, t.Description, t.InputSchema)).ToList(); }
        catch (Exception ex) when (ex is McpException or InvalidOperationException or System.ComponentModel.Win32Exception) { return BadRequest(ex.Message); }
    }

    /// <summary>Calls one tool. Body = the tool's arguments as a JSON object.</summary>
    [HttpPost("servers/{name}/tools/{tool}")]
    public async Task<ActionResult<McpCallResultDto>> Call(string name, string tool, [FromBody] JsonObject? args, CancellationToken ct)
    {
        try
        {
            var r = await mcp.CallAsync(name, tool, args, ct);
            return new McpCallResultDto(r.IsError, r.Text, r.ImageCount);
        }
        catch (Exception ex) when (ex is McpException or InvalidOperationException or System.ComponentModel.Win32Exception) { return BadRequest(ex.Message); }
    }

    /// <summary>Stops the server (closes the browser it opened). It starts again on next use.</summary>
    [HttpPost("servers/{name}/stop")]
    public async Task<object> Stop(string name) => new { stopped = await mcp.StopAsync(name) };
}

/// <summary>Refuses requests that do not come from this computer, unless Mcp:AllowRemoteCalls is on.</summary>
public sealed class LocalOnlyAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var mcp = context.HttpContext.RequestServices.GetRequiredService<McpClientManager>();
        var remote = context.HttpContext.Connection.RemoteIpAddress;
        var local = remote is null || IPAddress.IsLoopback(remote)
                    || remote.Equals(context.HttpContext.Connection.LocalIpAddress);
        if (!local && !mcp.Options.AllowRemoteCalls)
            context.Result = new ObjectResult("MCP endpoints work only from this computer.") { StatusCode = StatusCodes.Status403Forbidden };
    }
}
