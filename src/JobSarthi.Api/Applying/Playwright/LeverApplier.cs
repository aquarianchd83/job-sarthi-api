using Microsoft.Extensions.Options;

namespace JobSarthi.Api.Applying.Playwright;

public class LeverApplier(PlaywrightHost host, ScreenshotStore shots, IOptions<ApplyOptions> options)
    : PlaywrightApplierBase(host, shots, options)
{
    public override string SourceKey => "lever";
    protected override string ApplyUrl(string jobUrl) =>
        jobUrl.TrimEnd('/').EndsWith("/apply", StringComparison.OrdinalIgnoreCase) ? jobUrl : jobUrl.TrimEnd('/') + "/apply";
    protected override string[] SubmitSelectors =>
        new[] { "button#btn-submit", "button:has-text('Submit application')" }.Concat(ApplyEngine.DefaultSubmit).ToArray();
}
