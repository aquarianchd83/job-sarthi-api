using JobSarthi.Api.Domain.Enums;
using JobSarthi.Api.Sources.Alerts;
using JobSarthi.Api.Sources.Providers;
using Xunit;

namespace JobSarthi.Api.Tests;

public class SourceParserTests
{
    [Fact]
    public void Greenhouse_parses_jobs_and_decodes_html()
    {
        const string json = """
        {"jobs":[{"id":123,"title":"Senior .NET Engineer","absolute_url":"https://boards.greenhouse.io/acme/jobs/123",
        "location":{"name":"Bengaluru, India"},"content":"&lt;p&gt;Build &amp;amp; ship APIs&lt;/p&gt;"}]}
        """;
        var jobs = GreenhouseSource.Parse(json, "acme-corp");
        var j = Assert.Single(jobs);
        Assert.Equal("acme-corp:123", j.ExternalId);
        Assert.Equal("Acme Corp", j.Company);
        Assert.Equal("Build & ship APIs", j.Description);
        Assert.Equal(WorkMode.Onsite, j.WorkMode);
    }

    [Fact]
    public void Lever_parses_and_detects_remote()
    {
        const string json = """
        [{"id":"abc-1","text":"Backend Developer","hostedUrl":"https://jobs.lever.co/netflix/abc-1",
          "categories":{"location":"India"},"workplaceType":"remote","descriptionPlain":"Write C#"}]
        """;
        var j = Assert.Single(LeverSource.Parse(json, "netflix"));
        Assert.Equal(WorkMode.Remote, j.WorkMode);
        Assert.Equal("Write C#", j.Description);
    }

    [Fact]
    public void Ashby_uses_isRemote_flag()
    {
        const string json = """
        {"jobs":[{"id":"x1","title":"Engineer","location":"Anywhere","isRemote":true,
                  "jobUrl":"https://jobs.ashbyhq.com/co/x1","descriptionPlain":"Hi"}]}
        """;
        var j = Assert.Single(AshbySource.Parse(json, "co"));
        Assert.Equal(WorkMode.Remote, j.WorkMode);
    }

    [Fact]
    public void RemoteOk_skips_legal_notice_entry()
    {
        const string json = """
        [{"legal":"terms"},{"id":"99","position":"Full Stack Dev","company":"Acme","location":"","url":"https://remoteok.com/remote-jobs/99","description":"<b>Hi</b>"}]
        """;
        var j = Assert.Single(RemoteOkSource.Parse(json));
        Assert.Equal("Acme", j.Company);
        Assert.Equal("Worldwide", j.Location);
        Assert.Equal("Hi", j.Description);
    }

    [Fact]
    public void WeWorkRemotely_splits_company_and_title()
    {
        const string xml = """
        <rss><channel><item><title>Doist: Software Engineer, API</title>
        <link>https://weworkremotely.com/remote-jobs/doist-api</link><guid>https://weworkremotely.com/remote-jobs/doist-api</guid>
        <region>Anywhere in the World</region><description>&lt;p&gt;Join us&lt;/p&gt;</description></item></channel></rss>
        """;
        var j = Assert.Single(WeWorkRemotelySource.Parse(xml));
        Assert.Equal("Doist", j.Company);
        Assert.Equal("Software Engineer, API", j.Title);
    }

    [Fact]
    public void Naukri_alert_extracts_job_links_and_dedupes()
    {
        const string html = """
        <a href="https://www.naukri.com/job-listings-senior-net-developer-acme-pune-5-to-9-years-241120123456?src=jobalert&x=1">Senior .NET Developer</a>
        <a href="https://www.naukri.com/job-listings-senior-net-developer-acme-pune-5-to-9-years-241120123456?src=other">Senior .NET Developer</a>
        <a href="https://www.naukri.com/mnjuser/profile">Edit profile</a>
        <a href="https://www.naukri.com/job-listings-x-1">View all</a>
        """;
        var j = Assert.Single(NaukriEmailParser.Parse(html));
        Assert.Equal("241120123456", j.ExternalId);
        Assert.DoesNotContain("?", j.Url);
        Assert.Equal("Senior .NET Developer", j.Title);
    }

    [Fact]
    public void LinkedIn_alert_extracts_job_id_and_cleans_url()
    {
        const string html = """
        <a href="https://www.linkedin.com/comm/jobs/view/3912345678/?trackingId=abc&amp;refId=1">Lead .NET Engineer</a>
        <a href="https://www.linkedin.com/jobs/view/senior-dotnet-at-acme-3999999999?trk=x">Senior .NET at Acme</a>
        <a href="https://www.linkedin.com/feed/">See all jobs</a>
        """;
        var jobs = LinkedInEmailParser.Parse(html);
        Assert.Equal(2, jobs.Count);
        Assert.Equal("https://www.linkedin.com/jobs/view/3912345678", jobs[0].Url);
        Assert.Equal("3999999999", jobs[1].ExternalId);
    }
}
