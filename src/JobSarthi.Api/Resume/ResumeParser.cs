using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace JobSarthi.Api.Resume;

public record ParsedResume(string FullName, string Email, string Phone, string Headline, int ExperienceYears, List<string> Skills);

/// <summary>Rule-based resume reader (PDF or DOCX). Only reports what is literally in the text.</summary>
public static class ResumeParser
{
    private static readonly Regex EmailRx = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly Regex PhoneRx = new(@"(?<![\d])\+?(?:\d[ \-()]?){9,13}\d(?!\d)", RegexOptions.Compiled);
    private static readonly Regex YearsRx = new(@"(\d{1,2})\s*\+?\s*(?:years?|yrs?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeadlineRx = new(@"\b(developer|engineer|architect|lead|manager|consultant|analyst|programmer)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string ExtractText(Stream stream, string extension)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".pdf":
            {
                using var pdf = PdfDocument.Open(stream);
                var sb = new StringBuilder();
                foreach (var page in pdf.GetPages()) sb.AppendLine(page.Text);
                return sb.ToString();
            }
            case ".docx":
            {
                using var doc = WordprocessingDocument.Open(stream, false);
                var body = doc.MainDocumentPart?.Document.Body;
                if (body is null) return "";
                return string.Join('\n', body.Descendants<Paragraph>().Select(p => p.InnerText));
            }
            default:
                throw new NotSupportedException("Only PDF and DOCX resumes are supported.");
        }
    }

    public static ParsedResume Parse(string text)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        var email = EmailRx.Match(text).Value;
        var phone = PhoneRx.Matches(text).Select(m => m.Value.Trim()).FirstOrDefault(p => p.Count(char.IsDigit) is >= 10 and <= 13) ?? "";

        var name = lines.Take(5).FirstOrDefault(l =>
            l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length is >= 2 and <= 4
            && !l.Any(char.IsDigit) && !l.Contains('@') && !HeadlineRx.IsMatch(l)) ?? "";
        var headline = lines.Take(10).FirstOrDefault(l => l.Length <= 80 && HeadlineRx.IsMatch(l) && l != name) ?? "";

        var years = YearsRx.Matches(text).Select(m => int.Parse(m.Groups[1].Value)).Where(y => y is > 0 and <= 40).DefaultIfEmpty(0).Max();

        var skills = SkillCatalog.Find(text);
        return new ParsedResume(name, email, phone, headline, years, skills);
    }
}
