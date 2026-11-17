using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Applying;

/// <summary>Puts an application in the queue. Shared by the Apply button, Approve, Retry and the daily run.</summary>
public class ApplyStarter(JobSarthiDbContext db, ApplyQueue queue)
{
    /// <param name="submit">Null follows the apply mode in Settings: approval stops before submitting, full auto submits.</param>
    public async Task<bool> StartAsync(JobApplication app, bool? submit, string note, CancellationToken ct)
    {
        var mode = (await db.Settings.AsNoTracking().FirstAsync(ct)).ApplyMode;
        if (!queue.Enqueue(app.Id, submit ?? mode == ApplyMode.FullAuto)) return false;
        app.Status = ApplicationStatus.Applying;
        app.UpdatedAtUtc = DateTime.UtcNow;
        db.ApplicationLogs.Add(new ApplicationLog { ApplicationId = app.Id, AtUtc = DateTime.UtcNow, Message = note });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
