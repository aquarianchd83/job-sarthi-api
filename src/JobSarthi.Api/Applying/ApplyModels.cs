using JobSarthi.Api.Domain.Entities;

namespace JobSarthi.Api.Applying;

/// <summary>CheckboxGroup: several checkboxes that answer one question ("select the countries ..."), each option has its own Idx.</summary>
public enum FieldType { Text, Textarea, Select, Radio, Checkbox, File, CheckboxGroup }

/// <param name="Idx">Index stamped on the element in the page (data-js-idx), used to find it again.</param>
public record FieldOption(int Idx, string Label);

/// <param name="Options">Select options (all share the field's Idx), or radio buttons / checkbox-group boxes (each with its own Idx).</param>
public record FormField(int Idx, FieldType Type, string Label, string Name, string Id, string Autocomplete,
    bool Required, string InputType, List<FieldOption> Options);

public enum FieldRole { FirstName, LastName, FullName, Email, Phone, Location, LinkedIn, GitHub, Website, Resume, CoverLetter, Eeo, Consent, Other }

public abstract record PlannedAction(int Idx, string Label);
public sealed record FillAction(int Idx, string Label, string Value) : PlannedAction(Idx, Label);
public sealed record SelectAction(int Idx, string Label, string Option) : PlannedAction(Idx, Label);
public sealed record ClickAction(int Idx, string Label) : PlannedAction(Idx, Label);
public sealed record UploadAction(int Idx, string Label, string Path) : PlannedAction(Idx, Label);
public sealed record SkipAction(int Idx, string Label, string Reason) : PlannedAction(Idx, Label);
/// <summary>A required field with no known answer. The application pauses and asks you, with the field's own choices when it has them.</summary>
public sealed record AskAction(int Idx, string Label, FieldType Type = FieldType.Text, IReadOnlyList<string>? Options = null) : PlannedAction(Idx, Label)
{
    /// <summary>How the answer is asked for: text, textarea, select, radio, multi (pick any) or yesno (one checkbox).</summary>
    public string Input => Type switch
    {
        FieldType.Select => "select",
        FieldType.Radio => "radio",
        FieldType.CheckboxGroup => "multi",
        FieldType.Checkbox => "yesno",
        FieldType.Textarea => "textarea",
        _ => "text",
    };
}

/// <summary>
/// Something the run needs from you. Kind: question, captcha or otp. Input and Options describe the answer control for questions;
/// a multi answer is stored as the chosen options joined with "; ".
/// </summary>
public record Question(string Kind, string Prompt, string Input = "text", IReadOnlyList<string>? Options = null)
{
    public const string MultiSeparator = "; ";
    public static implicit operator Question((string Kind, string Prompt) t) => new(t.Kind, t.Prompt);
    public static List<string> SplitMulti(string answer) =>
        answer.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}

public record PlanInput(UserProfile Profile, string? ResumePath, string CoverLetter, IReadOnlyList<(string Question, string Answer)> Answers);

public record ApplyContext(int ApplicationId, Job Job, PlanInput Plan, bool Submit);

/// <summary>
/// Closed: the page says the job no longer takes applications.
/// ManualOnly: the job is open but auto-apply cannot work there (custom form, bot check). It moves to Manual apply with a draft.
/// </summary>
public enum OutcomeKind { Filled, Submitted, NeedsInput, Blocked, Failed, Closed, ManualOnly }

/// <param name="Questions">What the run needs from you (questions with their choices, or a captcha / otp to handle on the site).</param>
public record ApplyOutcome(OutcomeKind Kind, string Message, string? ScreenshotFile, List<Question> Questions, List<string> Log)
{
    public static ApplyOutcome Failed(string message, string? shot, List<string> log) => new(OutcomeKind.Failed, message, shot, new(), log);
    public static ApplyOutcome Closed(string phrase, string? shot, List<string> log) =>
        new(OutcomeKind.Closed, $"The page says \"{phrase}\". Job ignored.", shot, new(), log);
    /// <summary>Closed for a reason other than a phrase on the page (ATS API says 404, apply page gone).</summary>
    public static ApplyOutcome ClosedBecause(string reason, string? shot, List<string> log) =>
        new(OutcomeKind.Closed, $"Closed: {reason}. Job ignored.", shot, new(), log);
    public static ApplyOutcome ManualOnly(string reason, string? shot, List<string> log) => new(OutcomeKind.ManualOnly, reason, shot, new(), log);
}
