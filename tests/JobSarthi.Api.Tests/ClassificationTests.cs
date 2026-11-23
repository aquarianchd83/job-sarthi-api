using JobSarthi.Api.Classification;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;
using JobSarthi.Api.Filtering;
using Xunit;

namespace JobSarthi.Api.Tests;

public class ClosedJobDetectorTests
{
    [Theory]
    [InlineData("This job is no longer accepting applications.")]
    [InlineData("Sorry! We are no longer accepting applications for this role")]
    [InlineData("This position has been filled.")]
    [InlineData("The job posting has expired")]
    [InlineData("Applications are closed")]
    [InlineData("This job is no longer available")]
    [InlineData("The job you are looking for is no longer available.")]
    [InlineData("This requisition is closed")]
    [InlineData("Company X is not accepting applications at this time")]
    public void Closed_phrases_are_found(string text) => Assert.NotNull(ClosedJobDetector.Find(text));

    [Theory]
    [InlineData("We are accepting applications on a rolling basis.")]
    [InlineData("Apply now! Positions open in Pune and Remote.")]
    [InlineData("You will help close deals and fill the sales pipeline.")]
    [InlineData("")]
    public void Open_postings_are_not_flagged(string text) => Assert.Null(ClosedJobDetector.Find(text));
}

public class ApplyClassifierTests
{
    [Theory]
    [InlineData("https://boards.greenhouse.io/acme/jobs/123", "greenhouse")]
    [InlineData("https://job-boards.greenhouse.io/acme/jobs/123", "greenhouse")]
    [InlineData("https://jobs.lever.co/acme/abc-def", "lever")]
    [InlineData("https://jobs.eu.lever.co/acme/abc-def", "lever")]
    [InlineData("https://jobs.ashbyhq.com/acme/1234", "ashby")]
    [InlineData("https://apply.workable.com/acme/j/ABC123/", "workable")]
    [InlineData("https://acme.com/careers/job?gh_jid=4455", "greenhouse")]
    public void Ats_links_are_api_apply(string url, string ats)
    {
        var c = ApplyClassifier.Classify(url, "A great role.");
        Assert.Equal(ApplyCategory.Api, c.Category);
        Assert.Equal(ats, c.Ats);
    }

    [Fact]
    public void Lookalike_domain_is_not_an_ats() =>
        Assert.Equal(ApplyCategory.Manual, ApplyClassifier.Classify("https://notgreenhouse.io.evil.com/x", "").Category);

    [Theory]
    [InlineData("Interested candidates can send their resume to hr@acme.in", "hr@acme.in")]
    [InlineData("Please email your CV to Careers@Acme.com with the subject .NET Lead.", "careers@acme.com")]
    [InlineData("Share your updated resume at jobs@startup.io", "jobs@startup.io")]
    [InlineData("Apply by email: talent@foo.org", "talent@foo.org")]
    public void Resume_by_email_is_email_apply(string desc, string email)
    {
        var c = ApplyClassifier.Classify("https://remoteok.com/remote-jobs/123", desc);
        Assert.Equal(ApplyCategory.Email, c.Category);
        Assert.Equal(email, c.ApplyEmail);
    }

    [Fact]
    public void Contact_address_alone_is_not_email_apply() =>
        Assert.Equal(ApplyCategory.Manual,
            ApplyClassifier.Classify("https://weworkremotely.com/x", "Questions about privacy? Write to privacy@acme.com").Category);

    [Fact]
    public void Ats_link_in_description_beats_aggregator_page()
    {
        var c = ApplyClassifier.Classify("https://remoteok.com/remote-jobs/1", "To apply visit https://jobs.lever.co/acme/xyz.");
        Assert.Equal(ApplyCategory.Api, c.Category);
        Assert.Equal("https://jobs.lever.co/acme/xyz", c.ApplyUrl);
    }

    [Fact]
    public void Everything_else_is_manual() =>
        Assert.Equal(ApplyCategory.Manual, ApplyClassifier.Classify("https://careers.acme.com/jobs/9", "Apply on our site.").Category);
}

public class JobClassificationTests
{
    private static Job J(string url, string desc = "Senior .NET role") => new() { Title = "Senior .NET Developer", Url = url, Description = desc };

    [Fact]
    public void Closed_listing_gets_no_category_and_is_rejected()
    {
        var job = J("https://boards.greenhouse.io/acme/jobs/1", "We are no longer accepting applications for this role.");
        JobClassificationService.ClassifyListing(job, DateTime.UtcNow);
        Assert.True(job.IsClosed);
        Assert.Null(job.ApplyCategory);
        Assert.False(job.CanAutoApply);
        Assert.Equal("No longer accepting applications", RuleFilter.Check(job, new Rule()));
    }

    [Fact]
    public void Native_ats_can_auto_apply_workable_cannot()
    {
        var gh = J("https://boards.greenhouse.io/acme/jobs/1");
        var wk = J("https://apply.workable.com/acme/j/1/");
        JobClassificationService.ClassifyListing(gh, DateTime.UtcNow);
        JobClassificationService.ClassifyListing(wk, DateTime.UtcNow);
        Assert.True(gh.CanAutoApply);
        Assert.Equal(ApplyCategory.Api, wk.ApplyCategory);
        Assert.False(wk.CanAutoApply);   // no Playwright applier: drafted through MCP instead
    }

    [Fact]
    public void Closed_by_page_check_stays_closed_on_reclassify()
    {
        var job = J("https://careers.acme.com/1");
        JobClassificationService.MarkClosed(job, "Apply page says \"position filled\"");
        JobClassificationService.ClassifyListing(job, DateTime.UtcNow);
        Assert.True(job.IsClosed);
        Assert.Null(job.ApplyCategory);
    }
}

public class AtsLinkSafetyTests
{
    [Fact]
    public void Related_jobs_with_several_ats_links_stay_manual() =>
        Assert.Null(ApplyClassifier.FindAtsLink(
            "<a href='https://boards.greenhouse.io/acme/jobs/1'>Data Engineer</a> <a href='https://boards.greenhouse.io/acme/jobs/2'>Designer</a>"));

    [Fact]
    public void One_posting_linked_twice_counts_once()
    {
        var r = ApplyClassifier.FindAtsLink("<a href='https://jobs.lever.co/acme/abc'>Job</a> <a href='https://jobs.lever.co/acme/abc/apply?utm_source=x'>Apply</a>");
        Assert.Equal("lever", r?.Ats);
    }

    [Fact]
    public void Embedded_application_iframe_wins_over_related_links()
    {
        var html = "<iframe src=\"https://boards.greenhouse.io/embed/job_app?for=acme&amp;token=123\"></iframe>" +
                   "<a href='https://boards.greenhouse.io/acme/jobs/7'>Other</a><a href='https://boards.greenhouse.io/acme/jobs/8'>Other</a>";
        var r = ApplyClassifier.FindAtsLink(html);
        Assert.Equal("greenhouse", r?.Ats);
        Assert.Contains("token=123", r?.Url);
    }

    [Fact]
    public void Board_home_link_is_not_a_posting() =>
        Assert.Null(ApplyClassifier.FindAtsLink("See all openings at https://boards.greenhouse.io/acme"));
}
