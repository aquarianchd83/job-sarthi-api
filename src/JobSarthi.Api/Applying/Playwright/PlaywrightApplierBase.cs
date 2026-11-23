using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace JobSarthi.Api.Applying.Playwright;

/// <summary>Opens the application page in an isolated browser context, finds the form (even inside an iframe) and runs <see cref="ApplyEngine"/>.</summary>
public abstract class PlaywrightApplierBase(PlaywrightHost host, ScreenshotStore shots, IOptions<ApplyOptions> options) : IJobApplier
{
    public abstract string SourceKey { get; }
    /// <summary>Where the form lives for a given posting URL.</summary>
    protected abstract string ApplyUrl(string jobUrl);
    protected virtual string[] SubmitSelectors => ApplyEngine.DefaultSubmit;

    public async Task<ApplyOutcome> ApplyAsync(ApplyContext ctx, CancellationToken ct)
    {
        var browser = await host.GetBrowserAsync();
        await using var bctx = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1280, Height = 1400 }, Locale = "en-IN" });
        var page = await bctx.NewPageAsync();
        var timeout = options.Value.NavigationTimeoutSeconds * 1000;
        page.SetDefaultTimeout(timeout);
        var log = new List<string>();
        try
        {
            var url = ApplyUrl(ctx.Job.ApplyUrl ?? ctx.Job.Url);
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = timeout });
            log.Add($"Opened {new Uri(url).Host}");

            if (await ClosedPhraseAsync(page) is { } closedNow)
            {
                log.Add($"Page says \"{closedNow}\"");
                return ApplyOutcome.Closed(closedNow, await shots.SaveAsync(page, ctx.ApplicationId, "closed"), log);
            }

            var frame = await FindFormFrameAsync(page);
            if (frame is null)
            {
                if (await ClosedPhraseAsync(page) is { } closed)
                {
                    log.Add($"Page says \"{closed}\"");
                    return ApplyOutcome.Closed(closed, await shots.SaveAsync(page, ctx.ApplicationId, "closed"), log);
                }
                var shot = await shots.SaveAsync(page, ctx.ApplicationId, "no-form");
                return ApplyOutcome.Failed("Application form not found on the page. The site layout may have changed, or the job was closed.", shot, log);
            }

            var outcome = await ApplyEngine.RunAsync(page, frame, ctx, SubmitSelectors, stage => shots.SaveAsync(page, ctx.ApplicationId, stage));
            outcome.Log.InsertRange(0, log);
            return outcome;
        }
        catch (PlaywrightException ex) when (!ct.IsCancellationRequested)
        {
            var shot = await shots.SaveAsync(page, ctx.ApplicationId, "error");
            log.Add(ex.Message.Split('\n')[0]);
            return ApplyOutcome.Failed("Browser error: " + ex.Message.Split('\n')[0], shot, log);
        }
    }

    /// <summary>"No longer accepting applications" and the like, in the visible text of the page or any frame.</summary>
    private static async Task<string?> ClosedPhraseAsync(IPage page)
    {
        foreach (var frame in page.Frames)
        {
            try
            {
                var text = await frame.InnerTextAsync("body", new() { Timeout = 3000 });
                if (JobSarthi.Api.Classification.ClosedJobDetector.Find(text) is { } phrase) return phrase;
            }
            catch (PlaywrightException) { /* frame detached or empty */ }
        }
        return null;
    }

    /// <summary>The form may be on the page itself or embedded (Greenhouse often uses an iframe). Waits a few seconds for it to appear.</summary>
    private static async Task<IFrame?> FindFormFrameAsync(IPage page)
    {
        for (var i = 0; i < 20; i++)
        {
            foreach (var frame in page.Frames)
            {
                try
                {
                    if (await frame.Locator("form input[type='file'], form#application_form, form[action*='apply']").CountAsync() > 0) return frame;
                }
                catch (PlaywrightException) { /* frame detached */ }
            }
            await Task.Delay(750);
        }
        return null;
    }
}
