using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace JobSarthi.Api.Applying.Playwright;

/// <summary>One shared Chromium for the whole app. Each application gets its own isolated browser context.</summary>
public sealed class PlaywrightHost(IOptions<ApplyOptions> options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IPlaywright? _pw;
    private IBrowser? _browser;

    public async Task<IBrowser> GetBrowserAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_browser is { IsConnected: true }) return _browser;
            _pw ??= await Microsoft.Playwright.Playwright.CreateAsync();
            var launch = new BrowserTypeLaunchOptions { Headless = options.Value.Headless };
            if (!string.IsNullOrWhiteSpace(options.Value.BrowserChannel)) launch.Channel = options.Value.BrowserChannel;
            _browser = await _pw.Chromium.LaunchAsync(launch);
            return _browser;
        }
        finally { _lock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.CloseAsync();
        _pw?.Dispose();
    }
}
