namespace JobSarthi.Api.Applying;

public class ApplyOptions
{
    /// <summary>Run Chromium without a window. Set false on your own PC to watch it work.</summary>
    public bool Headless { get; set; } = true;
    /// <summary>Empty = the Chromium installed by "--install-browsers". Use "msedge" or "chrome" to drive an installed browser instead.</summary>
    public string BrowserChannel { get; set; } = "";
    /// <summary>Empty = data/screenshots next to the app.</summary>
    public string ScreenshotDir { get; set; } = "";
    public int NavigationTimeoutSeconds { get; set; } = 45;
}
