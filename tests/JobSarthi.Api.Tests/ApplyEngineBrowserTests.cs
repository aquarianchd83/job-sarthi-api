using Microsoft.Playwright;
using JobSarthi.Api.Applying;
using JobSarthi.Api.Applying.Playwright;
using JobSarthi.Api.Domain.Entities;
using Xunit;

namespace JobSarthi.Api.Tests;

/// <summary>
/// Runs the real apply routine in a real browser against LOCAL fixture pages. Nothing here touches a live site.
/// Needs a browser: run "--install-browsers" once, or set JOBSARTHI_TEST_CHANNEL=msedge to use Edge. Without one these tests pass quietly.
/// </summary>
public class ApplyEngineBrowserTests : IAsyncLifetime
{
    private IPlaywright? _pw;
    private IBrowser? _browser;
    private string _resume = "";

    public async Task InitializeAsync()
    {
        _resume = Path.Combine(Path.GetTempPath(), "js-test-resume.pdf");
        await File.WriteAllTextAsync(_resume, "%PDF-1.4 test");
        try
        {
            _pw = await Microsoft.Playwright.Playwright.CreateAsync();
            var channel = Environment.GetEnvironmentVariable("JOBSARTHI_TEST_CHANNEL");
            _browser = await _pw.Chromium.LaunchAsync(new() { Headless = true, Channel = string.IsNullOrEmpty(channel) ? null : channel });
        }
        catch (Exception) { _browser = null; }
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null) await _browser.CloseAsync();
        _pw?.Dispose();
    }

    private const string Form = """
    <form id="application_form" onsubmit="event.preventDefault(); if (!this.checkValidity()) return; document.body.innerHTML = '<h1>Thank you for applying</h1>';">
      <div class="field"><label for="first_name">First Name *</label><input id="first_name" name="first_name" required></div>
      <div class="field"><label for="last_name">Last Name *</label><input id="last_name" name="last_name" required></div>
      <div class="field"><label for="email">Email *</label><input id="email" name="email" type="email" required></div>
      <div class="field"><label for="phone">Phone</label><input id="phone" name="phone" type="tel"></div>
      <div class="field"><label for="resume">Resume/CV *</label><input id="resume" name="resume" type="file" required></div>
      <div class="field"><label for="cover">Cover Letter</label><textarea id="cover" name="cover"></textarea></div>
      <div class="field"><label for="notice">What is your notice period? *</label><input id="notice" name="notice" required></div>
      <div class="field"><label for="auth">Are you legally authorized to work in India? *</label>
        <select id="auth" required><option value="">Select...</option><option>Yes</option><option>No</option></select></div>
      <fieldset class="field"><legend>Do you require visa sponsorship? *</legend>
        <label><input type="radio" name="visa" value="y" required> Yes</label><label><input type="radio" name="visa" value="n"> No</label></fieldset>
      <div class="field"><label for="gender">Gender</label><select id="gender"><option value="">Select...</option><option>Male</option><option>Female</option></select></div>
      EXTRA
      <button type="submit">Submit Application</button>
    </form>
    """;

    private ApplyContext Ctx(bool submit, params (string, string)[] answers) => new(1,
        new Job { Title = "Engineer", Company = "Acme" },
        new PlanInput(new UserProfile { FullName = "Asha Verma", Email = "asha@example.com", Phone = "+91 98765 43210" }, _resume, "Dear Acme team, hello.", answers),
        submit);

    private static readonly (string, string)[] Known =
        { ("Notice period", "30 days"), ("authorized to work in India", "Yes"), ("visa sponsorship", "No") };

    private async Task<(ApplyOutcome Outcome, IPage Page, List<string> Shots)> Run(string extra, ApplyContext ctx)
    {
        var page = await _browser!.NewPageAsync();
        await page.SetContentAsync(Form.Replace("EXTRA", extra));
        var shots = new List<string>();
        var outcome = await ApplyEngine.RunAsync(page, page.MainFrame, ctx, ApplyEngine.DefaultSubmit, stage => { shots.Add(stage); return Task.FromResult<string?>(stage + ".png"); });
        return (outcome, page, shots);
    }

    [Fact]
    public async Task Approval_mode_fills_the_form_and_stops_before_submitting()
    {
        if (_browser is null) return;
        var (o, page, shots) = await Run("", Ctx(submit: false, Known));
        Assert.Equal(OutcomeKind.Filled, o.Kind);
        Assert.Equal("Asha", await page.InputValueAsync("#first_name"));
        Assert.Equal("Verma", await page.InputValueAsync("#last_name"));
        Assert.Equal("30 days", await page.InputValueAsync("#notice"));
        Assert.Equal("Yes", await page.InputValueAsync("#auth"));
        Assert.Equal("Dear Acme team, hello.", await page.InputValueAsync("#cover"));
        Assert.True(await page.Locator("input[value='n']").IsCheckedAsync());
        Assert.Equal("", await page.InputValueAsync("#gender"));          // demographic question left blank
        Assert.Contains("filled", shots);
        Assert.DoesNotContain("Thank you", await page.InnerTextAsync("body")); // not submitted
        Assert.DoesNotContain(o.Log, l => l.Contains("asha@example.com"));    // values are never logged
    }

    [Fact]
    public async Task Full_auto_submits_and_confirms_from_the_page()
    {
        if (_browser is null) return;
        var (o, _, shots) = await Run("", Ctx(submit: true, Known));
        Assert.Equal(OutcomeKind.Submitted, o.Kind);
        Assert.Contains("submitted", shots);
    }

    [Fact]
    public async Task Unknown_required_question_pauses_and_asks_without_filling_anything()
    {
        if (_browser is null) return;
        var (o, page, _) = await Run("", Ctx(submit: true, ("authorized to work in India", "Yes"), ("visa sponsorship", "No"))); // no notice period
        Assert.Equal(OutcomeKind.NeedsInput, o.Kind);
        Assert.Contains(o.Questions, q => q.Kind == "question" && q.Prompt.Contains("notice period"));
        Assert.Equal("", await page.InputValueAsync("#first_name"));
        Assert.DoesNotContain("Thank you", await page.InnerTextAsync("body"));
    }

    [Fact]
    public async Task A_visible_captcha_stops_the_run()
    {
        if (_browser is null) return;
        var (o, page, _) = await Run("<div class='g-recaptcha' data-sitekey='x' style='width:300px;height:80px'>captcha</div>", Ctx(submit: true, Known));
        Assert.Equal(OutcomeKind.Blocked, o.Kind);
        Assert.Contains(o.Questions, q => q.Kind == "captcha");
        Assert.Equal("", await page.InputValueAsync("#first_name"));
    }

    [Fact]
    public async Task An_invisible_recaptcha_badge_does_not_block()
    {
        if (_browser is null) return;
        var (o, _, _) = await Run("<div class='g-recaptcha' data-size='invisible' style='width:70px;height:60px'></div>", Ctx(submit: false, Known));
        Assert.Equal(OutcomeKind.Filled, o.Kind);
    }

    [Fact]
    public async Task A_verification_code_field_stops_the_run()
    {
        if (_browser is null) return;
        var (o, _, _) = await Run("<div class='field'><label for='otp'>Enter the verification code we emailed you</label><input id='otp'></div>", Ctx(submit: true, Known));
        Assert.Equal(OutcomeKind.Blocked, o.Kind);
        Assert.Contains(o.Questions, q => q.Kind == "otp");
    }

    [Fact]
    public async Task A_form_that_rejects_the_submission_is_reported_as_failed_not_submitted()
    {
        if (_browser is null) return;
        var page = await _browser.NewPageAsync();
        await page.SetContentAsync(Form.Replace("EXTRA", "").Replace("document.body.innerHTML = '<h1>Thank you for applying</h1>';", "document.body.insertAdjacentHTML('beforeend','<p class=error>Email is already registered</p>');"));
        var o = await ApplyEngine.RunAsync(page, page.MainFrame, Ctx(true, Known), ApplyEngine.DefaultSubmit, s => Task.FromResult<string?>(null));
        Assert.Equal(OutcomeKind.Failed, o.Kind);
        Assert.Contains("already registered", o.Message);
    }
}
