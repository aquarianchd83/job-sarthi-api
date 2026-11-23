using JobSarthi.Api.Classification;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;
using Xunit;

namespace JobSarthi.Api.Tests;

public class GreenhouseJobRefTests
{
    [Fact]
    public void Greenhouse_source_job_on_company_site_gets_greenhouse_form()
    {
        // Stripe lists Greenhouse jobs on stripe.com with a custom form; the board id lives in ExternalId.
        var job = new Job { SourceKey = "greenhouse", ExternalId = "stripe:7123456", Url = "https://stripe.com/careers/apply/software-engineer-financial-business-insights" };
        var gh = GreenhouseJobRef.From(job);
        Assert.NotNull(gh);
        Assert.Equal("stripe", gh!.Board);
        Assert.Equal("7123456", gh.JobId);
        Assert.Equal("https://job-boards.greenhouse.io/embed/job_app?for=stripe&token=7123456", gh.EmbedFormUrl);
        Assert.Equal("https://boards-api.greenhouse.io/v1/boards/stripe/jobs/7123456", gh.ApiUrl);
    }

    [Theory]
    [InlineData("https://boards.greenhouse.io/acme/jobs/4455667", "acme", "4455667")]
    [InlineData("https://job-boards.greenhouse.io/acme/jobs/4455667?gh_src=x", "acme", "4455667")]
    [InlineData("https://boards.greenhouse.io/embed/job_app?for=acme&token=4455667", "acme", "4455667")]
    [InlineData("https://job-boards.eu.greenhouse.io/acme/jobs/4455667", "acme", "4455667")]
    public void Greenhouse_urls_are_parsed(string url, string board, string id)
    {
        var gh = GreenhouseJobRef.FromUrl(url);
        Assert.Equal(new GreenhouseJobRef(board, id), gh);
    }

    [Theory]
    [InlineData("https://stripe.com/careers/apply/software-engineer")]
    [InlineData("https://boards.greenhouse.io/acme")]
    [InlineData("https://jobs.lever.co/acme/123")]
    [InlineData("")]
    public void Non_postings_give_nothing(string url) => Assert.Null(GreenhouseJobRef.FromUrl(url));

    [Fact]
    public void Gh_jid_uses_board_from_greenhouse_source()
    {
        var job = new Job { SourceKey = "greenhouse", ExternalId = "stripe:", Url = "https://stripe.com/jobs/search?gh_jid=6042172" };
        Assert.Equal(new GreenhouseJobRef("stripe", "6042172"), GreenhouseJobRef.From(job));
    }

    [Fact]
    public void Gh_jid_without_board_is_unknown()
    {
        var job = new Job { SourceKey = "remoteok", ExternalId = "x1", Url = "https://acme.com/jobs?gh_jid=6042172" };
        Assert.Null(GreenhouseJobRef.From(job));
    }

    [Fact]
    public void Greenhouse_job_on_custom_site_is_classified_api()
    {
        var job = new Job { SourceKey = "greenhouse", ExternalId = "stripe:7123456", Title = "Software Engineer",
            Url = "https://stripe.com/careers/apply/software-engineer-financial-business-insights", Description = "Build payments." };
        JobClassificationService.ClassifyListing(job, DateTime.UtcNow);
        Assert.Equal(ApplyCategory.Api, job.ApplyCategory);
        Assert.Equal("greenhouse", job.AtsKey);
        Assert.True(job.CanAutoApply);
    }

    [Fact]
    public void Greenhouse_closed_text_is_detected() =>
        Assert.NotNull(ClosedJobDetector.Find("Sorry, the job you are looking for is no longer open."));
}
