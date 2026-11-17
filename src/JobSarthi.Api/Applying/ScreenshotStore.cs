using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace JobSarthi.Api.Applying;

/// <summary>Saves page screenshots. Only the file name is stored in the database, and reads are confined to this folder.</summary>
public class ScreenshotStore(IOptions<ApplyOptions> options, IWebHostEnvironment env)
{
    public string Dir
    {
        get
        {
            var d = string.IsNullOrWhiteSpace(options.Value.ScreenshotDir)
                ? Path.Combine(env.ContentRootPath, "data", "screenshots")
                : options.Value.ScreenshotDir;
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public async Task<string?> SaveAsync(IPage page, int applicationId, string stage)
    {
        try
        {
            var name = $"app{applicationId}-{DateTime.UtcNow:yyyyMMddHHmmss}-{stage}.png";
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(Dir, name), FullPage = true });
            return name;
        }
        catch (PlaywrightException) { return null; }
    }

    /// <summary>Full path for a stored file name, or null if the name tries to leave the screenshot folder.</summary>
    public string? Resolve(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName)) return null;
        var full = Path.GetFullPath(Path.Combine(Dir, fileName));
        return full.StartsWith(Path.GetFullPath(Dir), StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? full : null;
    }
}
