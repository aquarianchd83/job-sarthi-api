using Microsoft.Extensions.Options;

namespace JobSarthi.Api.Applying.Playwright;

public class AshbyApplier(PlaywrightHost host, ScreenshotStore shots, IOptions<ApplyOptions> options)
    : PlaywrightApplierBase(host, shots, options)
{
    public override string SourceKey => "ashby";
    protected override string ApplyUrl(string jobUrl) =>
        jobUrl.TrimEnd('/').EndsWith("/application", StringComparison.OrdinalIgnoreCase) ? jobUrl : jobUrl.TrimEnd('/') + "/application";
}
