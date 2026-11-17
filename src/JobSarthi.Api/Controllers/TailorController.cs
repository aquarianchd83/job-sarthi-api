using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Tailoring;

namespace JobSarthi.Api.Controllers;

public record TailorDto(int JobId, string Engine, List<string> OrderedSkills, List<string> MatchedKeywords,
    List<string> MissingKeywords, string CoverLetter, string? Note, DateTime TailoredAtUtc);
public record SaveLetterRequest(string CoverLetter);
public record EngineStatus(string Name, bool Enabled, bool Configured, string Detail);

[ApiController]
[Route("api/[controller]")]
public class TailorController(JobSarthiDbContext db, TailoringService tailoring,
    IOptions<GeminiOptions> gemini, IOptions<OllamaOptions> ollama) : ControllerBase
{
    /// <summary>Tailor for one job. Safe to call again to regenerate.</summary>
    [HttpPost("{jobId:int}")]
    public async Task<ActionResult<TailorDto>> Run(int jobId, CancellationToken ct)
    {
        var (app, error) = await tailoring.RunAsync(jobId, ct);
        if (app is null) return error == "Job not found." ? NotFound(error) : BadRequest(error);
        return ToDto(app);
    }

    [HttpGet("{jobId:int}")]
    public async Task<ActionResult<TailorDto>> Get(int jobId, CancellationToken ct)
    {
        var app = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.JobId == jobId && a.TailoredAtUtc != null, ct);
        return app is null ? NotFound() : ToDto(app);
    }

    /// <summary>Save your own edits to the cover letter. The same safety check applies.</summary>
    [HttpPut("{jobId:int}")]
    public async Task<ActionResult<TailorDto>> SaveLetter(int jobId, SaveLetterRequest req, CancellationToken ct)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => a.JobId == jobId && a.TailoredAtUtc != null, ct);
        if (app is null) return NotFound("Tailor this job first.");
        var input = await tailoring.LoadInputAsync(jobId, ct);
        var problems = TailorSafetyGuard.ValidateLetter(req.CoverLetter, input!);
        if (problems.Count > 0) return BadRequest(string.Join(". ", problems));
        app.CoverLetter = req.CoverLetter.Trim();
        await db.SaveChangesAsync(ct);
        return ToDto(app);
    }

    /// <summary>Which optional engines are switched on and set up. Rule-based is always available.</summary>
    [HttpGet("engines")]
    public async Task<IEnumerable<EngineStatus>> Engines(CancellationToken ct)
    {
        var s = await db.Settings.AsNoTracking().FirstAsync(ct);
        var hasKey = !string.IsNullOrWhiteSpace(gemini.Value.ApiKey);
        return new[]
        {
            new EngineStatus("rule", true, true, "Always on"),
            new EngineStatus("gemini", s.UseGemini, hasKey, hasKey ? $"Model {gemini.Value.Model}" : "No API key in appsettings.Local.json"),
            new EngineStatus("ollama", s.UseOllama, true, $"{ollama.Value.BaseUrl}, model {ollama.Value.Model}"),
        };
    }

    private static TailorDto ToDto(JobApplication a) => new(a.JobId, a.TailorEngine ?? "rule", a.TailoredSkills,
        a.MatchedKeywords, a.MissingKeywords, a.CoverLetter ?? "", a.TailorNote, a.TailoredAtUtc ?? DateTime.UtcNow);
}
