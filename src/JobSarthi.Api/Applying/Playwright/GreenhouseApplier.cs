using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using JobSarthi.Api.Classification;
using JobSarthi.Api.Domain.Entities;

namespace JobSarthi.Api.Applying.Playwright;

/// <summary>
/// Greenhouse. Many companies (Stripe, Airbnb, ...) show Greenhouse jobs on their own careers site with a custom form that cannot be
/// filled reliably. When the board and job id are known, Greenhouse's own embeddable form is opened first: same application, plain form.
/// </summary>
public class GreenhouseApplier(PlaywrightHost host, ScreenshotStore shots, IOptions<ApplyOptions> options)
    : PlaywrightApplierBase(host, shots, options)
{
    public override string SourceKey => "greenhouse";
    protected override string ApplyUrl(string jobUrl) => jobUrl; // the posting page carries the form, possibly in an iframe

    protected override IEnumerable<(string Url, bool Constructed)> CandidateUrls(Job job)
    {
        if (GreenhouseJobRef.From(job) is { } gh)
        {
            yield return (gh.EmbedFormUrl, true);
            yield return (gh.HostedUrl, true);
        }
        if (!string.IsNullOrWhiteSpace(job.ApplyUrl)) yield return (job.ApplyUrl, false);
        if (!string.IsNullOrWhiteSpace(job.Url)) yield return (job.Url, false);
    }

    /// <summary>The public board API answers 404 for a job that was taken down. Network trouble means "unknown", never "closed".</summary>
    protected override async Task<string?> ClosedByApiAsync(IBrowserContext bctx, Job job, List<string> log)
    {
        if (GreenhouseJobRef.From(job) is not { } gh) return null;
        try
        {
            var resp = await bctx.APIRequest.GetAsync(gh.ApiUrl, new() { Timeout = 15000 });
            log.Add($"Greenhouse job {gh.Board}/{gh.JobId}: HTTP {resp.Status}");
            return resp.Status is 404 or 410 ? "Greenhouse says this job is no longer open" : null;
        }
        catch (PlaywrightException ex)
        {
            log.Add($"Greenhouse API check skipped: {ex.Message.Split('\n')[0]}");
            return null;
        }
    }
}
