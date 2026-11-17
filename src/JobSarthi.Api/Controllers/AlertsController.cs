using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Data;

namespace JobSarthi.Api.Controllers;

public record AlertDto(int Id, string Kind, string Message, DateTime CreatedAtUtc);

[ApiController]
[Route("api/[controller]")]
public class AlertsController(JobSarthiDbContext db) : ControllerBase
{
    /// <summary>Alerts you have not dismissed, newest first.</summary>
    [HttpGet]
    public async Task<IEnumerable<AlertDto>> Open() =>
        await db.Alerts.AsNoTracking().Where(a => !a.Acknowledged).OrderByDescending(a => a.CreatedAtUtc).Take(50)
            .Select(a => new AlertDto(a.Id, a.Kind, a.Message, a.CreatedAtUtc)).ToListAsync();

    [HttpPost("{id:int}/ack")]
    public async Task<IActionResult> Ack(int id)
    {
        var a = await db.Alerts.FindAsync(id);
        if (a is null) return NotFound();
        a.Acknowledged = true;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("ack-all")]
    public async Task<IActionResult> AckAll()
    {
        await db.Alerts.Where(a => !a.Acknowledged).ExecuteUpdateAsync(s => s.SetProperty(a => a.Acknowledged, true));
        return NoContent();
    }
}
