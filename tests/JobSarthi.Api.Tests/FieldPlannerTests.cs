using JobSarthi.Api.Applying;
using JobSarthi.Api.Domain.Entities;
using Xunit;

namespace JobSarthi.Api.Tests;

public class FieldPlannerTests
{
    private static FormField F(int idx, string label, FieldType type = FieldType.Text, bool required = true, string name = "", string inputType = "text",
        string autocomplete = "", params string[] options) =>
        new(idx, type, label, name, "", autocomplete, required, inputType, options.Select(o => new FieldOption(idx, o)).ToList());

    private static PlanInput Input(params (string, string)[] answers) => new(
        new UserProfile { FullName = "Asha Kumari Verma", Email = "asha@example.com", Phone = "+91 98765 43210", Location = "Pune" },
        null, "Dear team, hello.", answers);

    // ---- classify ----

    [Theory]
    [InlineData("First Name", FieldRole.FirstName)]
    [InlineData("Last Name", FieldRole.LastName)]
    [InlineData("Email", FieldRole.Email)]
    [InlineData("Phone number", FieldRole.Phone)]
    [InlineData("LinkedIn Profile", FieldRole.LinkedIn)]
    [InlineData("Website or portfolio", FieldRole.Website)]
    [InlineData("Current location", FieldRole.Location)]
    [InlineData("Full name", FieldRole.FullName)]
    [InlineData("Gender", FieldRole.Eeo)]
    [InlineData("Veteran status", FieldRole.Eeo)]
    [InlineData("What is your notice period?", FieldRole.Other)]
    [InlineData("Are you open to relocating?", FieldRole.Other)]
    public void Classify_recognises_common_fields(string label, FieldRole expected) =>
        Assert.Equal(expected, FieldPlanner.Classify(F(0, label)));

    [Fact]
    public void Classify_uses_autocomplete_and_file_labels()
    {
        Assert.Equal(FieldRole.FirstName, FieldPlanner.Classify(F(0, "Name", autocomplete: "given-name")));
        Assert.Equal(FieldRole.Resume, FieldPlanner.Classify(F(0, "Resume/CV", FieldType.File)));
        Assert.Equal(FieldRole.CoverLetter, FieldPlanner.Classify(F(0, "Cover Letter", FieldType.File)));
    }

    // ---- plan ----

    [Fact]
    public void Plan_fills_identity_fields_from_the_profile()
    {
        var plan = FieldPlanner.Plan(new[] { F(0, "First Name"), F(1, "Last Name"), F(2, "Email", inputType: "email"), F(3, "Phone") }, Input());
        Assert.Equal("Asha", ((FillAction)plan[0]).Value);
        Assert.Equal("Verma", ((FillAction)plan[1]).Value);
        Assert.Equal("asha@example.com", ((FillAction)plan[2]).Value);
        Assert.Equal("+91 98765 43210", ((FillAction)plan[3]).Value);
    }

    [Fact]
    public void Plan_asks_when_a_required_question_has_no_saved_answer() =>
        Assert.IsType<AskAction>(FieldPlanner.Plan(new[] { F(0, "What is your notice period?") }, Input())[0]);

    [Fact]
    public void Plan_skips_an_optional_unknown_question() =>
        Assert.IsType<SkipAction>(FieldPlanner.Plan(new[] { F(0, "Anything else?", required: false) }, Input())[0]);

    [Fact]
    public void Plan_uses_a_saved_answer_for_a_reworded_question()
    {
        var plan = FieldPlanner.Plan(new[] { F(0, "What is your notice period?") }, Input(("Notice period", "30 days")));
        Assert.Equal("30 days", ((FillAction)plan[0]).Value);
    }

    [Fact]
    public void Plan_never_confuses_current_and_expected_ctc()
    {
        var plan = FieldPlanner.Plan(new[] { F(0, "Expected CTC") }, Input(("Current CTC", "28 LPA")));
        Assert.IsType<AskAction>(plan[0]);
    }

    [Fact]
    public void Plan_never_answers_demographic_questions()
    {
        var optional = FieldPlanner.Plan(new[] { F(0, "Gender", FieldType.Select, required: false, options: new[] { "Male", "Female" }) }, Input(("Gender", "Male")));
        var required = FieldPlanner.Plan(new[] { F(0, "Gender", FieldType.Select, required: true, options: new[] { "Male", "Female" }) }, Input(("Gender", "Male")));
        Assert.IsType<SkipAction>(optional[0]);
        Assert.IsType<AskAction>(required[0]);
    }

    [Fact]
    public void Plan_picks_dropdown_and_radio_options_from_a_yes_no_answer()
    {
        var select = F(0, "Are you authorized to work in India?", FieldType.Select, options: new[] { "Yes", "No" });
        var radio = new FormField(1, FieldType.Radio, "Do you need visa sponsorship?", "visa", "", "", true, "radio",
            new() { new FieldOption(5, "Yes"), new FieldOption(6, "No") });
        var plan = FieldPlanner.Plan(new[] { select, radio },
            Input(("Authorized to work in India", "Yes"), ("visa sponsorship", "No")));
        Assert.Equal("Yes", ((SelectAction)plan[0]).Option);
        Assert.Equal(6, ((ClickAction)plan[1]).Idx);
    }

    [Fact]
    public void Plan_asks_before_ticking_a_required_consent_box() =>
        Assert.IsType<AskAction>(FieldPlanner.Plan(new[] { F(0, "I agree to the privacy policy", FieldType.Checkbox) }, Input())[0]);

    [Fact]
    public void Plan_fills_the_cover_letter_textarea_and_skips_an_optional_file_upload()
    {
        var plan = FieldPlanner.Plan(new[] { F(0, "Cover Letter", FieldType.Textarea, required: false), F(1, "Cover Letter", FieldType.File, required: false) }, Input());
        Assert.Equal("Dear team, hello.", ((FillAction)plan[0]).Value);
        Assert.IsType<SkipAction>(plan[1]);
    }

    [Fact]
    public void Plan_asks_for_a_resume_when_none_is_uploaded() =>
        Assert.IsType<AskAction>(FieldPlanner.Plan(new[] { F(0, "Resume/CV", FieldType.File) }, Input())[0]);

    // ---- matcher ----

    [Fact]
    public void ChooseOption_prefers_exact_then_yes_no_then_single_contains()
    {
        Assert.Equal("30 days", AnswerMatcher.ChooseOption(new[] { "15 days", "30 days" }, "30 days"));
        Assert.Equal("Yes, I am", AnswerMatcher.ChooseOption(new[] { "Yes, I am", "No" }, "yes"));
        Assert.Null(AnswerMatcher.ChooseOption(new[] { "Pune office", "Pune remote" }, "Pune")); // ambiguous
    }
}
