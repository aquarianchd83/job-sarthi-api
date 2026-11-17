using Microsoft.Extensions.Options;

namespace JobSarthi.Api.Applying.Playwright;

public class GreenhouseApplier(PlaywrightHost host, ScreenshotStore shots, IOptions<ApplyOptions> options)
    : PlaywrightApplierBase(host, shots, options)
{
    public override string SourceKey => "greenhouse";
    protected override string ApplyUrl(string jobUrl) => jobUrl; // the posting page carries the form, possibly in an iframe
}
