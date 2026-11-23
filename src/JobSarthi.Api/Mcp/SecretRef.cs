using System.Text.RegularExpressions;

namespace JobSarthi.Api.Mcp;

/// <summary>Expands ${NAME} to the environment variable NAME, so tokens live in the environment and not in config files.</summary>
public static class SecretRef
{
    private static readonly Regex Ref = new(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    public static string Expand(string value, Func<string, string?>? lookup = null)
    {
        lookup ??= Environment.GetEnvironmentVariable;
        return Ref.Replace(value, m => lookup(m.Groups[1].Value) ?? throw new InvalidOperationException(
            $"Environment variable {m.Groups[1].Value} is not set (referenced in the MCP settings)."));
    }
}
