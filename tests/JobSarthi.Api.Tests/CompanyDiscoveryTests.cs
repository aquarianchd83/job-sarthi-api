using JobSarthi.Api.Sources;
using Xunit;

namespace JobSarthi.Api.Tests;

public class CompanyDiscoveryTests
{
    [Theory]
    [InlineData("https://boards.greenhouse.io/stripe/jobs/123", "greenhouse", "stripe")]
    [InlineData("https://job-boards.greenhouse.io/Figma/jobs/9?gh_src=x", "greenhouse", "figma")]
    [InlineData("https://boards.eu.greenhouse.io/acme/jobs/1", "greenhouse", "acme")]
    [InlineData("https://boards.greenhouse.io/embed/job_app?for=postman&token=55", "greenhouse", "postman")]
    [InlineData("https://jobs.lever.co/razorpay/abc-123/apply", "lever", "razorpay")]
    [InlineData("https://jobs.eu.lever.co/acme/abc", "lever", "acme")]
    [InlineData("https://jobs.ashbyhq.com/notion/1234/application", "ashby", "notion")]
    public void Slug_is_read_from_ats_links(string url, string source, string slug)
    {
        var r = CompanyDiscoveryService.Extract(url);
        Assert.Equal((source, slug), r);
    }

    [Theory]
    [InlineData("https://acme.com/careers?gh_jid=123")]       // company site: no slug in the link
    [InlineData("https://apply.workable.com/acme/j/1/")]       // no Workable source
    [InlineData("https://remoteok.com/remote-jobs/1")]
    [InlineData("https://boards.greenhouse.io/embed/job_app")] // embed without ?for=
    [InlineData("")]
    public void Other_links_give_nothing(string url) => Assert.Null(CompanyDiscoveryService.Extract(url));
}
