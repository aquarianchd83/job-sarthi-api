using JobSarthi.Api.Domain.Entities;

namespace JobSarthi.Api.Applying;

public enum FieldType { Text, Textarea, Select, Radio, Checkbox, File }

/// <param name="Idx">Index stamped on the element in the page (data-js-idx), used to find it again.</param>
public record FieldOption(int Idx, string Label);

/// <param name="Options">Select options (all share the field's Idx) or radio buttons (each with its own Idx).</param>
public record FormField(int Idx, FieldType Type, string Label, string Name, string Id, string Autocomplete,
    bool Required, string InputType, List<FieldOption> Options);

public enum FieldRole { FirstName, LastName, FullName, Email, Phone, Location, LinkedIn, GitHub, Website, Resume, CoverLetter, Eeo, Consent, Other }

public abstract record PlannedAction(int Idx, string Label);
public sealed record FillAction(int Idx, string Label, string Value) : PlannedAction(Idx, Label);
public sealed record SelectAction(int Idx, string Label, string Option) : PlannedAction(Idx, Label);
public sealed record ClickAction(int Idx, string Label) : PlannedAction(Idx, Label);
public sealed record UploadAction(int Idx, string Label, string Path) : PlannedAction(Idx, Label);
public sealed record SkipAction(int Idx, string Label, string Reason) : PlannedAction(Idx, Label);
/// <summary>A required field with no known answer. The application pauses and asks you.</summary>
public sealed record AskAction(int Idx, string Label) : PlannedAction(Idx, Label);

public record PlanInput(UserProfile Profile, string? ResumePath, string CoverLetter, IReadOnlyList<(string Question, string Answer)> Answers);

public record ApplyContext(int ApplicationId, Job Job, PlanInput Plan, bool Submit);

/// <summary>Closed: the page says the job no longer takes applications.</summary>
public enum OutcomeKind { Filled, Submitted, NeedsInput, Blocked, Failed, Closed }

/// <param name="Questions">(kind, prompt) pairs. kind is question, captcha or otp.</param>
public record ApplyOutcome(OutcomeKind Kind, string Message, string? ScreenshotFile, List<(string Kind, string Prompt)> Questions, List<string> Log)
{
    public static ApplyOutcome Failed(string message, string? shot, List<string> log) => new(OutcomeKind.Failed, message, shot, new(), log);
    public static ApplyOutcome Closed(string phrase, string? shot, List<string> log) =>
        new(OutcomeKind.Closed, $"The page says \"{phrase}\". Job ignored.", shot, new(), log);
}
