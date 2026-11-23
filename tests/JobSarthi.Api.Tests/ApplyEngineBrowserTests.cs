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

    [Fact]
    public async Task Bot_wall_is_recognised_and_has_no_form()
    {
        if (_browser is null) return;
        var page = await _browser.NewPageAsync();
        await page.SetContentAsync("<title>Just a moment...</title><body><h1>Checking your browser before accessing the site</h1></body>");
        Assert.Null(await PlaywrightApplierBase.FindFormFrameAsync(page, attempts: 1));
        Assert.NotNull(await PlaywrightApplierBase.BotCheckAsync(page, 403));
    }

    [Fact]
    public async Task Custom_careers_form_is_not_a_bot_check()
    {
        if (_browser is null) return;
        var page = await _browser.NewPageAsync();
        // A company-built form (React, no file input, no apply action) is what stripe.com shows: not fillable, but not a bot wall either.
        await page.SetContentAsync("<main><h1>Software Engineer</h1><div role='form'><input name='first'><button>Next</button></div></main>");
        Assert.Null(await PlaywrightApplierBase.FindFormFrameAsync(page, attempts: 1));
        Assert.Null(await PlaywrightApplierBase.BotCheckAsync(page, 200));
    }

    // Stripe-style: a required question answered by ticking countries. Boxes have their own names and no fieldset.
    private const string CountryForm = """
    <form id="application_form" onsubmit="event.preventDefault()">
      <div class="q"><label for="first_name">First Name *</label><input id="first_name" required></div>
      <div class="q">
        <div class="lbl">Please select the country or countries you anticipate working in. *</div>
        <div class="opts">
          <div><input type="checkbox" id="c1" name="c_au"><label for="c1">Australia</label></div>
          <div><input type="checkbox" id="c2" name="c_in"><label for="c2">India</label></div>
          <div><input type="checkbox" id="c3" name="c_sg"><label for="c3">Singapore</label></div>
        </div>
      </div>
      <div class="q"><input type="checkbox" id="wa" name="wa"><label for="wa">Yes, send me WhatsApp messages</label></div>
      <input type="file" name="resume">
      <button type="submit">Submit</button>
    </form>
    """;

    [Fact]
    public async Task Checkbox_list_is_read_as_one_multi_select_question()
    {
        if (_browser is null) return;
        var page = await _browser.NewPageAsync();
        await page.SetContentAsync(CountryForm);
        var fields = await FormFieldMapper.CollectAsync(page.MainFrame);
        var group = Assert.Single(fields, f => f.Type == FieldType.CheckboxGroup);
        Assert.StartsWith("Please select the country", group.Label);
        Assert.Equal(new[] { "Australia", "India", "Singapore" }, group.Options.Select(o => o.Label));
        Assert.Single(fields, f => f.Type == FieldType.Checkbox && f.Label.Contains("WhatsApp")); // a lone box stays yes/no

        var o = await ApplyEngine.RunAsync(page, page.MainFrame, Ctx(false), ApplyEngine.DefaultSubmit, s => Task.FromResult<string?>(null));
        Assert.Equal(OutcomeKind.NeedsInput, o.Kind);
        var q = Assert.Single(o.Questions);
        Assert.Equal("multi", q.Input);
        Assert.Equal(3, q.Options!.Count);
        Assert.DoesNotContain(o.Log, l => l.Contains("Australia"));   // choices are not dumped into the log
    }

    [Fact]
    public async Task Chosen_countries_are_ticked()
    {
        if (_browser is null) return;
        var page = await _browser.NewPageAsync();
        await page.SetContentAsync(CountryForm);
        var o = await ApplyEngine.RunAsync(page, page.MainFrame, Ctx(false, ("countries you anticipate working in", "India; Singapore")),
            ApplyEngine.DefaultSubmit, s => Task.FromResult<string?>(null));
        Assert.Equal(OutcomeKind.Filled, o.Kind);
        Assert.False(await page.IsCheckedAsync("#c1"));
        Assert.True(await page.IsCheckedAsync("#c2"));
        Assert.True(await page.IsCheckedAsync("#c3"));
    }

    // Greenhouse's new job board (used by Stripe): react-select style dropdowns. Options exist only while the menu is open,
    // and each dropdown carries an invisible "required" helper input with no label.
    private const string ComboForm = """
    <style>.menu{border:1px solid #ccc}.requiredInput{opacity:0;position:absolute;left:0;bottom:0;width:100%;height:4px}.sel{position:relative;width:300px}</style>
    <form id="application_form" onsubmit="event.preventDefault()">
      <div><label for="first_name">First Name *</label><input id="first_name" aria-required="true"></div>
      <div class="sel" data-opts="Yes|No"><label id="q1-label" for="q1">Are you authorized to work in the location(s) you selected? *</label>
        <div class="val"></div><input id="q1" role="combobox" aria-labelledby="q1-label" aria-required="true" autocomplete="off">
        <input class="requiredInput" tabindex="-1" aria-hidden="true" required value=""></div>
      <div class="sel" data-opts="Bachelor's Degree|Master's Degree|Doctorate" ><label for="degree">Degree *</label>
        <div class="val"></div><input id="degree" role="combobox" aria-required="true" autocomplete="off">
        <input class="requiredInput" tabindex="-1" aria-hidden="true" required value=""></div>
      <div class="sel" data-search="Indian Institute of Technology Delhi|Delhi University|Panjab University"><label for="school">School *</label>
        <div class="val"></div><input id="school" role="combobox" aria-required="true" autocomplete="off">
        <input class="requiredInput" tabindex="-1" aria-hidden="true" required value=""></div>
      <input type="file" name="resume">
      <button type="submit">Submit application</button>
    </form>
    <script>
      document.querySelectorAll('.sel').forEach(sel => {
        const input = sel.querySelector('[role=combobox]'), hidden = sel.querySelector('.requiredInput');
        const all = (sel.dataset.opts || sel.dataset.search).split('|'), search = !!sel.dataset.search;
        let menu = null;
        const close = () => { if (menu) { menu.remove(); menu = null; } };
        const open = () => {
          close();
          const q = input.value.toLowerCase();
          if (search && !q) return;
          menu = document.createElement('div'); menu.className = 'menu'; menu.setAttribute('role', 'listbox');
          all.filter(o => o.toLowerCase().includes(q)).forEach(o => {
            const d = document.createElement('div'); d.setAttribute('role', 'option'); d.textContent = o;
            d.addEventListener('mousedown', e => { e.preventDefault(); sel.querySelector('.val').textContent = o; hidden.value = o; input.value = ''; close(); });
            menu.appendChild(d);
          });
          sel.appendChild(menu);
        };
        input.addEventListener('click', open);
        input.addEventListener('input', () => search ? setTimeout(open, 200) : open());
        input.addEventListener('keydown', e => { if (e.key === 'Escape') close(); });
      });
    </script>
    """;

    [Fact]
    public async Task Custom_dropdowns_are_read_with_their_options_and_helper_inputs_are_ignored()
    {
        if (_browser is null) return;
        var page = await _browser.NewPageAsync();
        await page.SetContentAsync(ComboForm);
        var fields = await FormFieldMapper.CollectAsync(page.MainFrame);
        Assert.DoesNotContain(fields, f => f.Label.Length == 0 || f.Label.StartsWith("Unlabelled"));
        var auth = Assert.Single(fields, f => f.Label.StartsWith("Are you authorized"));
        Assert.Equal(FieldType.Select, auth.Type);
        Assert.Equal(new[] { "Yes", "No" }, auth.Options.Select(o => o.Label));
        Assert.Equal(3, Assert.Single(fields, f => f.Label == "Degree").Options.Count);
        var school = Assert.Single(fields, f => f.Label == "School");
        Assert.Equal(FieldType.Text, school.Type);   // type-ahead: no options until you type
        Assert.Equal(0, await page.Locator("[role=listbox]").CountAsync()); // menus closed again
    }

    [Fact]
    public async Task Custom_dropdowns_get_the_chosen_option()
    {
        if (_browser is null) return;
        var page = await _browser.NewPageAsync();
        await page.SetContentAsync(ComboForm);
        var o = await ApplyEngine.RunAsync(page, page.MainFrame,
            Ctx(false, ("authorized to work in the location", "Yes"), ("Degree", "Master's Degree"), ("School", "Panjab University")),
            ApplyEngine.DefaultSubmit, s => Task.FromResult<string?>(null));
        Assert.True(o.Kind == OutcomeKind.Filled, string.Join(" | ", o.Log));
        var vals = await page.Locator(".val").AllInnerTextsAsync();
        Assert.Equal(new[] { "Yes", "Master's Degree", "Panjab University" }, vals);
    }

    [Fact]
    public async Task A_dropdown_answer_that_is_not_a_choice_is_asked_again()
    {
        if (_browser is null) return;
        var page = await _browser.NewPageAsync();
        await page.SetContentAsync(ComboForm);
        var o = await ApplyEngine.RunAsync(page, page.MainFrame,
            Ctx(false, ("authorized to work in the location", "Yes"), ("Degree", "Master's Degree"), ("School", "Hogwarts")),
            ApplyEngine.DefaultSubmit, s => Task.FromResult<string?>(null));
        Assert.Equal(OutcomeKind.NeedsInput, o.Kind);
        Assert.Equal("School", Assert.Single(o.Questions).Prompt);
    }
}
