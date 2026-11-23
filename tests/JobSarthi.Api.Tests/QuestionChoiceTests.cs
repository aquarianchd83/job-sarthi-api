using JobSarthi.Api.Applying;
using JobSarthi.Api.Domain.Entities;
using Xunit;

namespace JobSarthi.Api.Tests;

/// <summary>Questions keep the form's own choices, and a chosen answer fills exactly those choices.</summary>
public class QuestionChoiceTests
{
    private static readonly string[] Countries = { "Australia", "Canada", "India", "Singapore", "US" };

    private static FormField Group(bool required = true) => new(10, FieldType.CheckboxGroup,
        "Please select the country or countries you anticipate working in", "", "", "", required, "checkbox",
        Countries.Select((c, i) => new FieldOption(100 + i, c)).ToList());

    private static PlanInput Input(params (string, string)[] answers) =>
        new(new UserProfile { FullName = "Asha Verma", Email = "a@example.com" }, null, "", answers);

    [Fact]
    public void Unknown_checkbox_group_is_asked_as_multi_select_with_its_options()
    {
        var ask = Assert.IsType<AskAction>(Assert.Single(FieldPlanner.Plan(new[] { Group() }, Input())));
        Assert.Equal("multi", ask.Input);
        Assert.Equal(Countries, ask.Options);
    }

    [Fact]
    public void Multi_answer_ticks_every_chosen_box_and_nothing_else()
    {
        var plan = FieldPlanner.Plan(new[] { Group() }, Input(("countries you anticipate working in", "India; Singapore")));
        var clicks = plan.OfType<ClickAction>().Select(c => c.Idx).ToList();
        Assert.Equal(new[] { 102, 103 }, clicks);
    }

    [Fact]
    public void Multi_answer_with_an_unknown_option_asks_again()
    {
        var plan = FieldPlanner.Plan(new[] { Group() }, Input(("countries you anticipate working in", "India; Narnia")));
        Assert.IsType<AskAction>(Assert.Single(plan));
    }

    [Fact]
    public void Select_and_radio_are_asked_with_their_options()
    {
        var select = new FormField(1, FieldType.Select, "Country", "country", "", "", true, "select",
            new[] { "India", "Japan" }.Select(o => new FieldOption(1, o)).ToList());
        var radio = new FormField(2, FieldType.Radio, "Do you opt in to WhatsApp messages?", "wa", "", "", true, "radio",
            new() { new(3, "Yes"), new(4, "No") });
        var asks = FieldPlanner.Plan(new[] { select, radio }, Input()).Cast<AskAction>().ToList();
        Assert.Equal("select", asks[0].Input);
        Assert.Equal(new[] { "India", "Japan" }, asks[0].Options);
        Assert.Equal("radio", asks[1].Input);
        Assert.Equal(new[] { "Yes", "No" }, asks[1].Options);
    }

    [Fact]
    public void A_select_with_no_readable_options_falls_back_to_text()
    {
        var custom = new FormField(1, FieldType.Select, "Country", "country", "", "", true, "select", new());
        var ask = Assert.IsType<AskAction>(Assert.Single(FieldPlanner.Plan(new[] { custom }, Input())));
        Assert.Equal("text", ask.Input);
        Assert.Null(ask.Options);
    }

    [Fact]
    public void Unlabelled_required_field_gets_a_name_instead_of_a_blank_question()
    {
        var blank = new FormField(5, FieldType.Text, "", "school_name", "", "", true, "text", new());
        var ask = Assert.IsType<AskAction>(Assert.Single(FieldPlanner.Plan(new[] { blank }, Input())));
        Assert.Equal("Unlabelled field (school_name)", ask.Label);
    }

    [Fact]
    public void Multi_answers_split_on_semicolons() =>
        Assert.Equal(new[] { "India", "US" }, Question.SplitMulti(" India ;US; "));
}

public class LocationRoleTests
{
    private static FormField T(string label) => new(0, FieldType.Text, label, "", "", "", true, "text", new());

    [Fact]
    public void Short_location_label_is_the_profile_location() =>
        Assert.Equal(FieldRole.Location, FieldPlanner.Classify(T("Location (City)")));

    [Fact]
    public void A_question_that_mentions_a_location_is_not() =>
        Assert.Equal(FieldRole.Other, FieldPlanner.Classify(T("Are you authorized to work in the location(s) you selected in your previous response?")));
}
