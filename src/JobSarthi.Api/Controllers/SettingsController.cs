using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Common;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Controllers;

public record SettingsDto(string DailyRunTimeIst, bool SchedulerEnabled, int DailyApplyLimit, int GapBetweenAppliesSeconds,
    int AutoApplyMinScore, string ApplyMode, bool UseGemini, bool UseOllama, bool NotifySummary, bool NotifyAlerts);

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly JobSarthiDbContext _db;
    public SettingsController(JobSarthiDbContext db) => _db = db;

    [HttpGet]
    public async Task<SettingsDto> Get()
    {
        var s = await _db.Settings.AsNoTracking().FirstAsync();
        return new SettingsDto(s.DailyRunTimeIst.ToString("HH:mm"), s.SchedulerEnabled, s.DailyApplyLimit,
            s.GapBetweenAppliesSeconds, s.AutoApplyMinScore, s.ApplyMode.ToString(), s.UseGemini, s.UseOllama, s.NotifySummary, s.NotifyAlerts);
    }

    [HttpPut]
    public async Task<IActionResult> Put(SettingsDto dto)
    {
        if (!TimeOnly.TryParse(dto.DailyRunTimeIst, out var time)) return BadRequest("Run time must be HH:mm.");
        if (dto.DailyApplyLimit is < 0 or > 100) return BadRequest("Daily limit must be 0 to 100.");
        if (dto.AutoApplyMinScore is < 0 or > 100) return BadRequest("Minimum score must be 0 to 100.");
        if (!Enum.TryParse<ApplyMode>(dto.ApplyMode, out var mode)) return BadRequest("Unknown apply mode.");
        var s = await _db.Settings.FirstAsync();
        s.DailyRunTimeIst = new TimeOnly(time.Hour, time.Minute);
        s.SchedulerEnabled = dto.SchedulerEnabled;
        s.DailyApplyLimit = dto.DailyApplyLimit;
        s.GapBetweenAppliesSeconds = Math.Max(0, dto.GapBetweenAppliesSeconds);
        s.AutoApplyMinScore = dto.AutoApplyMinScore;
        s.ApplyMode = mode;
        s.UseGemini = dto.UseGemini;
        s.UseOllama = dto.UseOllama;
        s.NotifySummary = dto.NotifySummary;
        s.NotifyAlerts = dto.NotifyAlerts;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>When the daily run will next start, in UTC. Clients show it in IST.</summary>
    [HttpGet("next-run")]
    public async Task<object> NextRun()
    {
        var s = await _db.Settings.AsNoTracking().FirstAsync();
        var last = await _db.DailyRuns.AsNoTracking().Where(r => r.Trigger == "schedule").MaxAsync(r => (DateTime?)r.StartedAtUtc);
        var (_, next) = Scheduling.SchedulePolicy.Decide(DateTime.UtcNow, s.DailyRunTimeIst, last);
        return new { nextRunUtc = DateTime.SpecifyKind(next, DateTimeKind.Utc), enabled = s.SchedulerEnabled };
    }
}
