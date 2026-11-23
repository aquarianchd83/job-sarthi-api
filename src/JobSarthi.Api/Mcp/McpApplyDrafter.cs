using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using JobSarthi.Api.Applying;
using JobSarthi.Api.Applying.Playwright;
using JobSarthi.Api.Classification;

namespace JobSarthi.Api.Mcp;

/// <summary>
/// Manual apply through Playwright MCP: opens the apply page in the browser that the MCP server runs on your PC,
/// checks it is still open, pre-fills what is known (same planner as the ATS appliers) and tries to attach the resume.
/// It NEVER submits. The window stays open so you review and press Submit yourself.
/// Tools used: browser_navigate, browser_snapshot, browser_evaluate, browser_click, browser_file_upload.
/// </summary>
public class McpApplyDrafter(McpClientManager mcp, ILogger<McpApplyDrafter> log)
{
    private static readonly Regex B64 = new(@"JSB64:([A-Za-z0-9+/=]+):END", RegexOptions.Compiled);
    private static readonly Regex RefOf = new(@"\[ref=([^\]\s]+)\]", RegexOptions.Compiled);
    private static readonly Regex UploadLine = new(@"resume|\bcv\b|attach|upload|choose file|browse", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool Enabled => mcp.IsConfigured(mcp.Options.BrowserServer);

    public async Task<ApplyOutcome> PrepareAsync(ApplyContext ctx, CancellationToken ct)
    {
        var server = mcp.Options.BrowserServer;
        var log = new List<string>();
        var url = ctx.Job.ApplyUrl ?? ctx.Job.Url;
        try
        {
            var nav = await Call(server, "browser_navigate", new JsonObject { ["url"] = url }, ct);
            log.Add($"Opened {SafeHost(url)} in the MCP browser");

            // The page as you would read it (accessibility snapshot). Some servers include it in the navigate reply.
            var snap = await Call(server, "browser_snapshot", new JsonObject(), ct);
            if (ClosedJobDetector.Find(nav.Text, snap.Text) is { } phrase)
            {
                log.Add($"Page says \"{phrase}\"");
                return ApplyOutcome.Closed(phrase, null, log);
            }

            var fields = FormFieldMapper.Parse(await EvaluateJson(server, Wrap(FormFieldMapper.CollectScript), ct));
            log.Add(fields.Count == 0 ? "No form fields on this page yet. Click Apply on the page, the link and cover letter are ready"
                                      : $"Found {fields.Count} form fields");

            var plan = FieldPlanner.Plan(fields, ctx.Plan);
            var toRun = plan.Where(a => a is FillAction or SelectAction or ClickAction).ToList();
            if (toRun.Count > 0)
            {
                var results = await EvaluateJson(server, FillScript(toRun), ct);
                var ok = results.EnumerateArray().Where(r => r.GetProperty("ok").GetBoolean()).Select(r => r.GetProperty("idx").GetInt32()).ToHashSet();
                foreach (var a in toRun)
                    log.Add(ok.Contains(a.Idx) ? $"Filled \"{a.Label}\"" : $"Could not fill \"{a.Label}\", fill it yourself");
            }
            foreach (var a in plan.OfType<AskAction>()) log.Add($"Left for you: \"{a.Label}\"");

            if (plan.OfType<UploadAction>().FirstOrDefault() is { } upload)
                log.Add(await TryUploadAsync(server, upload.Path, ct)
                    ? "Attached your resume"
                    : "Attach your resume yourself (the upload button could not be used)");

            log.Add("Not submitted. Review the browser window and press Submit yourself");
            return new ApplyOutcome(OutcomeKind.Filled, "Draft ready in the browser. Review it and press Submit yourself.", null, new(), log);
        }
        catch (McpException ex)
        {
            log.Add(ex.Message);
            return ApplyOutcome.Failed(ex.Message, null, log);
        }
    }

    private async Task<McpToolResult> Call(string server, string tool, JsonObject args, CancellationToken ct)
    {
        var res = await mcp.CallAsync(server, tool, args, ct);
        if (res.IsError) throw new McpException($"{tool} failed: {FirstLine(res.Text)}");
        return res;
    }

    /// <summary>Runs a function in the page. It returns its JSON as base64 between markers, so it survives any result formatting.</summary>
    private async Task<JsonElement> EvaluateJson(string server, string function, CancellationToken ct)
    {
        var res = await Call(server, "browser_evaluate", new JsonObject { ["function"] = function }, ct);
        var m = B64.Match(res.Text);
        if (!m.Success) throw new McpException("browser_evaluate returned an unexpected result.");
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value)));
        return doc.RootElement.Clone();
    }

    private const string Encode = "v => 'JSB64:' + btoa(unescape(encodeURIComponent(JSON.stringify(v)))) + ':END'";

    public static string Wrap(string arrowFunction) =>
        $"() => {{ const enc = {Encode}; const f = ({arrowFunction.Trim()}); return enc(f()); }}";

    /// <summary>Sets values the way a person typing would (input/change events), so React/Vue forms notice.</summary>
    public static string FillScript(IEnumerable<PlannedAction> actions)
    {
        var list = actions.Select(a => a switch
        {
            FillAction f => new { idx = f.Idx, kind = "fill", value = f.Value },
            SelectAction s => new { idx = s.Idx, kind = "select", value = s.Option },
            _ => new { idx = a.Idx, kind = "click", value = "" },
        }).ToList();
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(list)));
        return $$"""
        () => {
          const enc = {{Encode}};
          const acts = JSON.parse(decodeURIComponent(escape(atob('{{b64}}'))));
          const clean = t => (t || '').replace(/\s+/g, ' ').replace(/\s*\*+\s*$/, '').replace(/\(required\)/i, '').trim();
          const setVal = (el, v) => {
            const proto = el.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : el.tagName === 'SELECT' ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
            Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, v);
            for (const t of ['input', 'change', 'blur']) el.dispatchEvent(new Event(t, { bubbles: true }));
          };
          return enc(acts.map(a => {
            const el = document.querySelector('[data-js-idx="' + a.idx + '"]');
            if (!el) return { idx: a.idx, ok: false };
            try {
              if (a.kind === 'fill') { el.focus(); setVal(el, a.value); }
              else if (a.kind === 'select') { const o = [...el.options].find(o => clean(o.text) === a.value); if (!o) return { idx: a.idx, ok: false }; setVal(el, o.value); }
              else if (!el.checked) el.click();
              return { idx: a.idx, ok: true };
            } catch (e) { return { idx: a.idx, ok: false }; }
          }));
        }
        """;
    }

    /// <summary>Clicks the resume upload control found in the snapshot, then hands the file to the file chooser.</summary>
    private async Task<bool> TryUploadAsync(string server, string path, CancellationToken ct)
    {
        try
        {
            var snap = await Call(server, "browser_snapshot", new JsonObject(), ct);
            var line = PickUploadLine(snap.Text);
            if (line is null) return false;
            // Newer Playwright MCP names the argument "target", older versions "ref".
            var refArg = await ArgNameAsync(server, "browser_click", new[] { "target", "ref" }, ct) ?? "ref";
            await Call(server, "browser_click", new JsonObject { ["element"] = line.Value.Label, [refArg] = line.Value.Ref }, ct);
            var up = await mcp.CallAsync(server, "browser_file_upload", new JsonObject { ["paths"] = new JsonArray(path) }, ct);
            if (up.IsError)
            {
                // Usually "outside allowed roots". Close the file chooser so the page stays usable.
                log.LogInformation("Resume upload through MCP was refused: {Message}", up.Text.Split('\n').LastOrDefault(l => l.Trim().Length > 0));
                await mcp.CallAsync(server, "browser_file_upload", new JsonObject(), ct);
            }
            return !up.IsError;
        }
        catch (McpException ex)
        {
            log.LogInformation("Resume upload through MCP did not work: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>The first of <paramref name="names"/> that the tool's input schema declares.</summary>
    private async Task<string?> ArgNameAsync(string server, string tool, string[] names, CancellationToken ct)
    {
        var t = (await (await mcp.GetAsync(server, ct)).GetToolsAsync(ct)).FirstOrDefault(x => x.Name == tool);
        if (t?.InputSchema is not { ValueKind: JsonValueKind.Object } schema || !schema.TryGetProperty("properties", out var props)) return null;
        return names.FirstOrDefault(n => props.TryGetProperty(n, out _));
    }

    /// <summary>The snapshot line of the resume upload button: prefers lines that mention resume/CV, then any upload button.</summary>
    public static (string Label, string Ref)? PickUploadLine(string snapshot)
    {
        var candidates = snapshot.Split('\n')
            .Where(l => RefOf.IsMatch(l) && UploadLine.IsMatch(l) && Regex.IsMatch(l, @"\b(button|textbox|generic|link)\b"))
            .OrderByDescending(l => Regex.IsMatch(l, @"resume|\bcv\b", RegexOptions.IgnoreCase))
            .ThenByDescending(l => l.Contains("button"))
            .ToList();
        if (candidates.Count == 0) return null;
        var best = candidates[0];
        var label = best.Trim().TrimStart('-', ' ');
        var cut = label.IndexOf("[ref=", StringComparison.Ordinal);
        return (cut > 0 ? label[..cut].Trim() : label, RefOf.Match(best).Groups[1].Value);
    }

    private static string FirstLine(string s) => s.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "error";
    private static string SafeHost(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "the page";
}
