using System.Text.RegularExpressions;

namespace JobSarthi.Api.Classification;

/// <summary>
/// Spots postings that no longer take applications ("no longer accepting applications", position filled, expired...).
/// Pure text check, used on the listing text and on the apply page text. Such jobs are ignored and never categorized.
/// </summary>
public static class ClosedJobDetector
{
    private static readonly Regex Closed = new(
        @"no longer (?:accepting|taking|receiving|open for|available for) (?:any )?(?:new )?(?:applications|applicants|candidates|submissions)"
        + @"|(?:not|isn['’]t|is not) accepting (?:any )?(?:new )?applications"
        + @"|applications? (?:are|is|has been|have been) (?:now )?closed"
        + @"|(?:this |the )?(?:job|position|role|vacancy|opening|posting|job posting|job listing|requisition) (?:has been |is |was )?(?:now )?(?:closed|filled|expired|archived|removed|unpublished|no longer (?:available|open|active))"
        + @"|(?:job|posting|listing|position|vacancy|advert) (?:has )?expired"
        + @"|(?:position|role|job) (?:has been|is) (?:already )?filled"
        + @"|(?:hiring|recruitment|recruiting) (?:for this (?:role|position|job) )?(?:is |has )?(?:now )?closed"
        + @"|the job you are looking for (?:is no longer (?:available|open)|no longer exists|has (?:been )?(?:closed|expired|removed))"
        + @"|this job (?:is )?no longer (?:exists|available|active|open)"
        + @"|(?:sorry, )?this (?:job|position|opening) (?:is )?(?:no longer|not) (?:available|active|open)"
        + @"|deadline (?:for applications )?has passed"
        + @"|closed for applications",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The matched phrase, so the reason shown to you quotes the page. Null when the job looks open.</summary>
    public static string? Find(params string?[] texts)
    {
        foreach (var t in texts)
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            var m = Closed.Match(t);
            if (m.Success) return m.Value.Trim();
        }
        return null;
    }

    public static bool IsClosed(params string?[] texts) => Find(texts) is not null;
}
