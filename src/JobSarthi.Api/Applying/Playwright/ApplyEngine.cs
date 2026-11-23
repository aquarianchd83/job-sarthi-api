using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace JobSarthi.Api.Applying.Playwright;

/// <summary>
/// The fill-and-submit routine, shared by all ATS appliers. It works on an already-loaded page, so it can be tested on a local fixture.
/// It never solves CAPTCHAs or one-time codes. It stops and reports them.
/// </summary>
public static class ApplyEngine
{
    private static readonly Regex Success = new(
        @"thank you for (applying|your application|submitting)|thanks for applying|application (has been |was )?(received|submitted|sent)|we(?:'|’)ve received your application|successfully submitted",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OtpLabel = new(@"verification code|security code|one[- ]time|\botp\b|confirmation code", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A visible challenge the user must solve. The invisible reCAPTCHA badge that many forms carry is not one.
    private const string CaptchaSelector =
        ".g-recaptcha:not([data-size='invisible']), .h-captcha, .cf-turnstile, iframe[src*='recaptcha/api2/bframe'], iframe[src*='hcaptcha.com/captcha']";

    public static readonly string[] DefaultSubmit =
    {
        "button[type='submit']", "input[type='submit']",
        "button:has-text('Submit application')", "button:has-text('Submit')", "button:has-text('Apply')",
    };

    public static async Task<ApplyOutcome> RunAsync(IPage page, IFrame frame, ApplyContext ctx,
        string[] submitSelectors, Func<string, Task<string?>> screenshot)
    {
        var log = new List<string>();

        // 1. Blockers before touching anything.
        var blocker = await DetectBlockerAsync(frame);
        if (blocker is not null)
        {
            var shot0 = await screenshot("blocked");
            log.Add(blocker.Value.Prompt);
            return new ApplyOutcome(OutcomeKind.Blocked, blocker.Value.Prompt, shot0, new() { blocker.Value }, log);
        }

        // 2. Read the form and decide what to do with every field.
        var fields = await FormFieldMapper.CollectAsync(frame);
        log.Add($"Found {fields.Count} form fields");
        var plan = FieldPlanner.Plan(fields, ctx.Plan);

        // 3. Required fields with no known answer: stop and ask. Nothing is guessed.
        var asks = plan.OfType<AskAction>().ToList();
        if (asks.Count > 0)
        {
            var shot1 = await screenshot("needs-input");
            var questions = asks.GroupBy(a => a.Label).Select(g => g.First())
                .Select(a => new Question("question", a.Label, a.Input, a.Options)).ToList();
            // One short line per question. Choices are shown in the answer box, not dumped into the log.
            log.Add($"{questions.Count} question(s) need your answer");
            foreach (var q in questions) log.Add($"Needs your answer: {ShortLabel(q.Prompt)}{(q.Options is { Count: > 0 } o ? $" (choose from {o.Count} options)" : "")}");
            return new ApplyOutcome(OutcomeKind.NeedsInput, $"{questions.Count} question(s) need your answer", shot1, questions, log);
        }

        // 4. Fill. Values are never written to the log, only which fields were filled.
        var failures = new List<string>();
        var reask = new List<Question>();
        foreach (var action in plan)
        {
            if (action is SkipAction skip) { log.Add($"Skipped \"{skip.Label}\" ({skip.Reason})"); continue; }
            var err = await FormFieldMapper.ExecuteAsync(frame, action);
            if (err is null) log.Add($"Filled \"{action.Label}\"");
            else if (err == FormFieldMapper.NoMatch)
            {
                // The answer we had is not one of this dropdown's choices: ask again rather than fail.
                log.Add($"Your answer for \"{ShortLabel(action.Label)}\" is not a choice on this form. Answer it again");
                var f = fields.FirstOrDefault(x => x.Idx == action.Idx);
                reask.Add(f is { Type: FieldType.Select, Options.Count: > 0 }
                    ? new Question("question", f.Label, "select", f.Options.Select(o => o.Label).Distinct().ToList())
                    : new Question("question", f?.Label ?? action.Label));
            }
            else { failures.Add(action.Label); log.Add($"Could not fill \"{action.Label}\": {err}"); }
        }
        var filledShot = await screenshot("filled");
        if (reask.Count > 0 && failures.Count == 0)
            return new ApplyOutcome(OutcomeKind.NeedsInput, $"{reask.Count} question(s) need a different answer", filledShot, reask, log);
        if (failures.Count > 0)
            return ApplyOutcome.Failed($"Could not fill: {string.Join(", ", failures)}. The site layout may have changed.", filledShot, log);

        if (!ctx.Submit)
        {
            log.Add("Form filled. Waiting for your approval before submitting");
            return new ApplyOutcome(OutcomeKind.Filled, "Filled, waiting for approval", filledShot, new(), log);
        }

        // 5. Submit.
        var submit = await FindSubmitAsync(frame, submitSelectors);
        if (submit is null) return ApplyOutcome.Failed("Submit button not found. The site layout may have changed.", filledShot, log);
        await submit.ClickAsync(new() { Timeout = 8000 });
        log.Add("Clicked submit");

        // 6. Judge the result from the page, not from hope.
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(500);
            var text = await SafeBodyText(frame);
            if (Success.IsMatch(text) || page.Url.Contains("confirmation", StringComparison.OrdinalIgnoreCase))
            {
                var done = await screenshot("submitted");
                log.Add("Confirmation page detected");
                return new ApplyOutcome(OutcomeKind.Submitted, "Submitted", done, new(), log);
            }
            var post = await DetectBlockerAsync(frame);
            if (post is not null)
            {
                var s = await screenshot("blocked");
                log.Add(post.Value.Prompt);
                return new ApplyOutcome(OutcomeKind.Blocked, post.Value.Prompt, s, new() { post.Value }, log);
            }
        }

        var after = await screenshot("after-submit");
        var errors = await ValidationErrorsAsync(frame);
        var why = errors.Count > 0 ? $"The site rejected the form: {string.Join("; ", errors.Take(3))}"
                                   : "No confirmation was detected. Check your email before retrying, it may have gone through";
        log.Add(why);
        return ApplyOutcome.Failed(why, after, log);
    }

    private static string ShortLabel(string label) => label.Length <= 120 ? label : label[..117].TrimEnd() + "...";

    public static async Task<(string Kind, string Prompt)?> DetectBlockerAsync(IFrame frame)
    {
        try
        {
            var captcha = frame.Locator(CaptchaSelector);
            for (var i = 0; i < await captcha.CountAsync(); i++)
                if (await captcha.Nth(i).IsVisibleAsync())
                    return ("captcha", "A CAPTCHA is on the form. Open the job link and apply yourself");

            var labels = await frame.EvaluateAsync<string[]>(
                "() => [...document.querySelectorAll('label, input[placeholder], input[aria-label]')].filter(e => { const r = e.getBoundingClientRect(); return r.width > 0 || r.height > 0; })" +
                ".map(e => (e.innerText || e.placeholder || e.getAttribute('aria-label') || ''))");
            if (labels.Any(l => OtpLabel.IsMatch(l)))
                return ("otp", "The site wants a one-time or verification code. Open the job link and finish yourself");
        }
        catch (PlaywrightException) { /* page navigated mid-check */ }
        return null;
    }

    private static async Task<ILocator?> FindSubmitAsync(IFrame frame, string[] selectors)
    {
        foreach (var sel in selectors)
        {
            var loc = frame.Locator(sel);
            var n = await loc.CountAsync();
            for (var i = 0; i < n; i++)
                if (await loc.Nth(i).IsVisibleAsync() && await loc.Nth(i).IsEnabledAsync()) return loc.Nth(i);
        }
        return null;
    }

    private static async Task<string> SafeBodyText(IFrame frame)
    {
        try { return await frame.InnerTextAsync("body", new() { Timeout = 2000 }); }
        catch (PlaywrightException) { return ""; }
    }

    private static async Task<List<string>> ValidationErrorsAsync(IFrame frame)
    {
        try
        {
            return (await frame.EvaluateAsync<string[]>(
                "() => [...document.querySelectorAll('.error, .field_with_errors, [role=alert], .invalid-feedback, [aria-invalid=true]')]" +
                ".map(e => (e.innerText || e.getAttribute('name') || '').trim()).filter(t => t)")).Distinct().ToList();
        }
        catch (PlaywrightException) { return new(); }
    }
}
