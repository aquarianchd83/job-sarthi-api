using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using JobSarthi.Api.Domain.Entities;

namespace JobSarthi.Api.Applying.Playwright;

/// <summary>
/// Opens the application page in an isolated browser context, finds the form (even inside an iframe) and runs <see cref="ApplyEngine"/>.
/// Tries each candidate URL in turn (an ATS's own hosted form first, then the company page). When none has a form it tells apart:
/// closed job, bot check, and a custom form we cannot fill. The last two move the job to Manual apply instead of failing.
/// </summary>
public abstract class PlaywrightApplierBase(PlaywrightHost host, ScreenshotStore shots, IOptions<ApplyOptions> options) : IJobApplier
{
    public abstract string SourceKey { get; }
    /// <summary>Where the form lives for a given posting URL.</summary>
    protected abstract string ApplyUrl(string jobUrl);
    protected virtual string[] SubmitSelectors => ApplyEngine.DefaultSubmit;

    /// <summary>Pages to try, best first. Constructed=true marks a URL we built ourselves (its 404 does not mean the job is closed).</summary>
    protected virtual IEnumerable<(string Url, bool Constructed)> CandidateUrls(Job job)
    {
        yield return (ApplyUrl(job.ApplyUrl ?? job.Url), false);
    }

    /// <summary>An authoritative "is it closed" check before opening any page (e.g. the ATS's public API). Null when open or unknown.</summary>
    protected virtual Task<string?> ClosedByApiAsync(IBrowserContext bctx, Job job, List<string> log) => Task.FromResult<string?>(null);

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
            if (await ClosedByApiAsync(bctx, ctx.Job, log) is { } apiClosed)
            {
                log.Add(apiClosed);
                return ApplyOutcome.ClosedBecause(apiClosed, null, log);
            }

            var candidates = CandidateUrls(ctx.Job).Where(c => !string.IsNullOrWhiteSpace(c.Url))
                .DistinctBy(c => c.Url.TrimEnd('/'), StringComparer.OrdinalIgnoreCase).ToList();
            string? botCheck = null, gone = null, lastShot = null;
            var tried = new List<string>();

            foreach (var (url, constructed) in candidates)
            {
                var pageHost = HostOf(url);
                IResponse? resp;
                try { resp = await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = timeout }); }
                catch (PlaywrightException ex) when (!ct.IsCancellationRequested)
                {
                    log.Add($"Could not open {pageHost}: {ex.Message.Split('\n')[0]}");
                    tried.Add(pageHost);
                    continue;
                }
                var status = resp?.Status ?? 0;
                log.Add($"Opened {pageHost}{(status is > 0 and not 200 ? $" (HTTP {status})" : "")}");
                tried.Add(pageHost);

                if (await ClosedPhraseAsync(page) is { } closedNow)
                {
                    log.Add($"Page says \"{closedNow}\"");
                    return ApplyOutcome.Closed(closedNow, await shots.SaveAsync(page, ctx.ApplicationId, "closed"), log);
                }
                if (!constructed && status is 404 or 410) gone ??= $"the apply page is gone (HTTP {status})";

                var frame = await FindFormFrameAsync(page);
                if (frame is not null)
                {
                    var outcome = await ApplyEngine.RunAsync(page, frame, ctx, SubmitSelectors, stage => shots.SaveAsync(page, ctx.ApplicationId, stage));
                    outcome.Log.InsertRange(0, log);
                    return outcome;
                }

                // The form may have shown up late, or the closed notice only after scripts ran.
                if (await ClosedPhraseAsync(page) is { } closed)
                {
                    log.Add($"Page says \"{closed}\"");
                    return ApplyOutcome.Closed(closed, await shots.SaveAsync(page, ctx.ApplicationId, "closed"), log);
                }
                if (await BotCheckAsync(page, status) is { } bot)
                {
                    botCheck ??= $"{pageHost} shows {bot}";
                    log.Add($"{pageHost}: {bot}");
                }
                else log.Add($"{pageHost}: no application form on the page");
                lastShot = await shots.SaveAsync(page, ctx.ApplicationId, "no-form");
            }

            if (gone is not null && botCheck is null)
                return ApplyOutcome.ClosedBecause(gone, lastShot, log);

            var site = HostOf(ctx.Job.ApplyUrl ?? ctx.Job.Url);
            var reason = botCheck is not null
                ? $"Auto-apply is not possible on {site}: {botCheck}, which only a person can pass."
                : $"Auto-apply is not possible on {site}: no application form we can fill was found (checked {string.Join(", ", tried.Distinct())}). "
                  + "The site likely uses its own custom form.";
            log.Add(reason);
            return ApplyOutcome.ManualOnly(reason, lastShot, log);
        }
        catch (PlaywrightException ex) when (!ct.IsCancellationRequested)
        {
            var shot = await shots.SaveAsync(page, ctx.ApplicationId, "error");
            log.Add(ex.Message.Split('\n')[0]);
            return ApplyOutcome.Failed("Browser error: " + ex.Message.Split('\n')[0], shot, log);
        }
    }

    protected static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? (u.Host.StartsWith("www.") ? u.Host[4..] : u.Host) : url;

    private static readonly Regex BotText = new(
        @"just a moment\.\.\.|checking (?:if the site connection is secure|your browser)|verify(?:ing)? (?:that )?you are (?:a )?human|are you a robot|"
        + @"unusual traffic|access denied|request blocked|attention required|enable javascript and cookies to continue|press (?:&|and) hold",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string ChallengeSelector =
        "iframe[src*='challenges.cloudflare.com'], .cf-turnstile, #challenge-form, .g-recaptcha:not([data-size='invisible']), .h-captcha, "
        + "iframe[src*='hcaptcha.com'], iframe[src*='recaptcha/api2/bframe'], #px-captcha, iframe[src*='captcha-delivery.com']";

    /// <summary>A bot wall or challenge instead of the page ("Just a moment...", Cloudflare/DataDome/PerimeterX, a visible CAPTCHA). Null when none.</summary>
    public static async Task<string?> BotCheckAsync(IPage page, int status)
    {
        try
        {
            foreach (var frame in page.Frames)
            {
                var challenge = frame.Locator(ChallengeSelector);
                for (var i = 0; i < await challenge.CountAsync(); i++)
                    if (await challenge.Nth(i).IsVisibleAsync()) return "a CAPTCHA / bot check";
            }
            var text = $"{await page.TitleAsync()}\n{await page.InnerTextAsync("body", new() { Timeout = 3000 })}";
            if (BotText.Match(text) is { Success: true } m) return $"a bot check (\"{m.Value.Trim()}\")";
        }
        catch (PlaywrightException) { /* page navigated or empty */ }
        return status is 403 or 429 ? $"a block page (HTTP {status})" : null;
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
    public static async Task<IFrame?> FindFormFrameAsync(IPage page, int attempts = 20)
    {
        for (var i = 0; i < attempts; i++)
        {
            foreach (var frame in page.Frames)
            {
                try
                {
                    if (await frame.Locator("form input[type='file'], form#application_form, form[action*='apply']").CountAsync() > 0) return frame;
                }
                catch (PlaywrightException) { /* frame detached */ }
            }
            if (i < attempts - 1) await Task.Delay(750);
        }
        return null;
    }
}
