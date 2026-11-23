using System.Text.RegularExpressions;

namespace JobSarthi.Api.Applying;

/// <summary>
/// Pure decision logic: given the form's fields and what we know about you, decide what to do with each field.
/// No browser here, so it is fully unit-tested. Anything required and unknown becomes an AskAction. Nothing is guessed.
/// </summary>
public static class FieldPlanner
{
    private static readonly string[] EeoWords =
        { "gender", "race", "ethnic", "veteran", "disability", "pronoun", "hispanic", "sexual orientation", "transgender", "demographic" };
    private static readonly string[] ConsentWords = { "privacy", "consent", "agree", "acknowledge", "terms", "gdpr", "i have read" };

    public static FieldRole Classify(FormField f)
    {
        var text = $"{f.Label} {f.Name} {f.Id}".ToLowerInvariant();
        var ac = f.Autocomplete.ToLowerInvariant();

        if (f.Type == FieldType.File)
        {
            if (Has(text, "cover")) return FieldRole.CoverLetter;
            if (Has(text, "resume", "cv", "curriculum")) return FieldRole.Resume;
            return FieldRole.Other;
        }
        if (EeoWords.Any(w => text.Contains(w))) return FieldRole.Eeo;
        if (f.Type == FieldType.Checkbox && ConsentWords.Any(w => text.Contains(w))) return FieldRole.Consent;

        if (ac == "given-name" || Has(text, "first name", "first_name", "firstname", "given name")) return FieldRole.FirstName;
        if (ac == "family-name" || Has(text, "last name", "last_name", "lastname", "surname", "family name")) return FieldRole.LastName;
        if (ac == "email" || f.InputType == "email" || Has(text, "email", "e-mail")) return FieldRole.Email;
        if (ac == "tel" || f.InputType == "tel" || Has(text, "phone", "mobile", "telephone")) return FieldRole.Phone;
        if (Has(text, "linkedin")) return FieldRole.LinkedIn;
        if (Has(text, "github")) return FieldRole.GitHub;
        if (Has(text, "website", "portfolio", "personal site", "personal url")) return FieldRole.Website;
        if (f.Type == FieldType.Textarea && Has(text, "cover letter", "cover_letter")) return FieldRole.CoverLetter;
        if (Regex.IsMatch(text, @"\b(full name|your name|legal name)\b") || f.Label.Trim().Equals("name", StringComparison.OrdinalIgnoreCase)
            || f.Name.Equals("name", StringComparison.OrdinalIgnoreCase)) return FieldRole.FullName;
        // "Location (City)" is the profile location; a long question that mentions a location ("authorized to work in the location(s) ...") is not.
        if (Regex.IsMatch(text, @"\b(current )?(location|city)\b") && !text.Contains("relocat")
            && f.Label.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 5) return FieldRole.Location;
        return FieldRole.Other;
    }

    public static List<PlannedAction> Plan(IReadOnlyList<FormField> fields, PlanInput input)
    {
        var p = input.Profile;
        var (first, last) = SplitName(p.FullName);
        var actions = new List<PlannedAction>();

        foreach (var raw in fields)
        {
            // A question with no visible label still has to be answerable: name it after the field.
            var f = string.IsNullOrWhiteSpace(raw.Label)
                ? raw with { Label = $"Unlabelled field ({(raw.Name.Length > 0 ? raw.Name : raw.Id.Length > 0 ? raw.Id : "#" + raw.Idx)})" }
                : raw;
            var role = Classify(f);
            string? known = role switch
            {
                FieldRole.FirstName => first,
                FieldRole.LastName => last,
                FieldRole.FullName => p.FullName,
                FieldRole.Email => p.Email,
                FieldRole.Phone => p.Phone,
                FieldRole.Location => p.Location,
                FieldRole.CoverLetter when f.Type != FieldType.File => input.CoverLetter,
                FieldRole.LinkedIn or FieldRole.GitHub or FieldRole.Website or FieldRole.Other or FieldRole.Consent
                    => AnswerMatcher.Find(f.Label, input.Answers),
                _ => null,
            };

            switch (role)
            {
                case FieldRole.Resume:
                    actions.Add(!string.IsNullOrEmpty(input.ResumePath) && File.Exists(input.ResumePath)
                        ? new UploadAction(f.Idx, f.Label, input.ResumePath)
                        : f.Required ? new AskAction(f.Idx, f.Label + " (upload a resume in Resume & Profile first)", FieldType.File) : new SkipAction(f.Idx, f.Label, "no resume file"));
                    continue;
                case FieldRole.CoverLetter when f.Type == FieldType.File:
                    actions.Add(f.Required ? Ask(f) : new SkipAction(f.Idx, f.Label, "optional cover letter upload"));
                    continue;
                case FieldRole.Eeo:
                    // Never answered automatically. Left blank when optional.
                    actions.Add(f.Required ? Ask(f) : new SkipAction(f.Idx, f.Label, "optional demographic question left blank"));
                    continue;
            }

            if (string.IsNullOrWhiteSpace(known))
            {
                actions.Add(f.Required ? Ask(f) : new SkipAction(f.Idx, f.Label, "optional, no saved answer"));
                continue;
            }

            switch (f.Type)
            {
                case FieldType.Text or FieldType.Textarea:
                    actions.Add(new FillAction(f.Idx, f.Label, known));
                    break;
                case FieldType.Select:
                {
                    var opt = AnswerMatcher.ChooseOption(f.Options.Select(o => o.Label).ToList(), known);
                    actions.Add(opt is null ? (f.Required ? Ask(f) : new SkipAction(f.Idx, f.Label, "no matching option"))
                                            : new SelectAction(f.Idx, f.Label, opt));
                    break;
                }
                case FieldType.Radio:
                {
                    var opt = AnswerMatcher.ChooseOption(f.Options.Select(o => o.Label).ToList(), known);
                    var picked = f.Options.FirstOrDefault(o => o.Label == opt);
                    actions.Add(picked is null ? (f.Required ? Ask(f) : new SkipAction(f.Idx, f.Label, "no matching option"))
                                               : new ClickAction(picked.Idx, f.Label + ": " + picked.Label));
                    break;
                }
                case FieldType.CheckboxGroup:
                {
                    // Every saved choice must match a box, else ask again rather than tick a wrong set.
                    var labels = f.Options.Select(o => o.Label).ToList();
                    var wanted = Question.SplitMulti(known).Select(w => AnswerMatcher.ChooseOption(labels, w)).ToList();
                    if (wanted.Count == 0 || wanted.Any(w => w is null))
                    {
                        actions.Add(f.Required ? Ask(f) : new SkipAction(f.Idx, f.Label, "no matching option"));
                        break;
                    }
                    foreach (var pick in f.Options.Where(o => wanted.Contains(o.Label)))
                        actions.Add(new ClickAction(pick.Idx, f.Label + ": " + pick.Label));
                    break;
                }
                case FieldType.Checkbox:
                    actions.Add(AnswerMatcher.IsYes(known) ? new ClickAction(f.Idx, f.Label)
                              : f.Required ? Ask(f) : new SkipAction(f.Idx, f.Label, "answer is not yes"));
                    break;
                default:
                    actions.Add(new SkipAction(f.Idx, f.Label, "unsupported field"));
                    break;
            }
        }
        return actions;
    }

    /// <summary>Ask for the field, offering its own choices when it has them.</summary>
    private static AskAction Ask(FormField f)
    {
        if (f.Type is not (FieldType.Select or FieldType.Radio or FieldType.CheckboxGroup)) return new(f.Idx, f.Label, f.Type);
        var options = f.Options.Select(o => o.Label).Where(l => l.Length > 0).Distinct().ToList();
        // No readable choices (a custom widget): a typed answer is the best we can do.
        return options.Count == 0 ? new(f.Idx, f.Label, FieldType.Text) : new(f.Idx, f.Label, f.Type, options);
    }

    public static (string First, string Last) SplitName(string full)
    {
        var parts = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch { 0 => ("", ""), 1 => (parts[0], ""), _ => (parts[0], parts[^1]) };
    }

    private static bool Has(string text, params string[] words) => words.Any(w => text.Contains(w));
}

/// <summary>Finds your saved answer for a form question, and picks the matching option in a dropdown or radio group.</summary>
public static class AnswerMatcher
{
    private static readonly HashSet<string> Stop = new()
        { "the","a","an","of","to","in","for","do","you","your","are","is","what","please","how","will","be","and","or","on","at","have","with","this","that","us","we","our","can","could","would","if","any" };

    public static string? Find(string question, IReadOnlyList<(string Question, string Answer)> answers)
    {
        var q = Tokens(question);
        if (q.Count == 0) return null;
        string? best = null;
        double bestScore = 0;
        foreach (var (sq, answer) in answers)
        {
            var s = Tokens(sq);
            if (s.Count == 0 || string.IsNullOrWhiteSpace(answer)) continue;
            var overlap = s.Count(q.Contains);
            // Every word of the saved question must appear, so "Notice period" matches "What is your notice period?"
            // but "Current CTC" never matches "Expected CTC".
            if (overlap < s.Count) continue;
            var score = overlap / (double)Math.Max(q.Count, s.Count);
            if (score > bestScore) { bestScore = score; best = answer; }
        }
        return best;
    }

    public static string? ChooseOption(IReadOnlyList<string> options, string answer)
    {
        var a = answer.Trim();
        var exact = options.FirstOrDefault(o => o.Equals(a, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        if (IsYes(a) || IsNo(a))
        {
            var want = IsYes(a);
            var yn = options.FirstOrDefault(o => want ? IsYes(o) : IsNo(o));
            if (yn is not null) return yn;
        }
        var contains = options.Where(o => o.Contains(a, StringComparison.OrdinalIgnoreCase) || a.Contains(o, StringComparison.OrdinalIgnoreCase)).ToList();
        return contains.Count == 1 ? contains[0] : null;
    }

    public static bool IsYes(string s) => Regex.IsMatch(s.Trim(), @"^(yes|y|true|i agree|agree)\b", RegexOptions.IgnoreCase);
    public static bool IsNo(string s) => Regex.IsMatch(s.Trim(), @"^(no|n|false)\b", RegexOptions.IgnoreCase);

    private static HashSet<string> Tokens(string s) =>
        Regex.Matches(s.ToLowerInvariant(), @"[a-z0-9#+]+").Select(m => m.Value).Where(w => w.Length > 1 && !Stop.Contains(w)).ToHashSet();
}
