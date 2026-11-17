using JobSarthi.Api.Common;

namespace JobSarthi.Api.Scheduling;

public enum ScheduleDecision { RunNow, Wait, Missed }

/// <summary>Pure timing rules for the daily run, so they can be tested without waiting for a clock.</summary>
public static class SchedulePolicy
{
    /// <summary>If the server was down at run time, still run when it comes back within this long.</summary>
    public static readonly TimeSpan CatchUpWindow = TimeSpan.FromHours(6);

    /// <param name="lastScheduledRunUtc">Start of the latest automatic run. Manual runs must not count.</param>
    public static (ScheduleDecision Decision, DateTime NextUtc) Decide(DateTime nowUtc, TimeOnly runTimeIst, DateTime? lastScheduledRunUtc)
    {
        var dayStart = IstTime.StartOfIstDayUtc(nowUtc);
        var dueToday = dayStart + runTimeIst.ToTimeSpan(); // India has no daylight saving, so a fixed offset is exact
        var ranToday = lastScheduledRunUtc is { } last && last >= dayStart;

        if (ranToday) return (ScheduleDecision.Wait, dueToday.AddDays(1));
        if (nowUtc < dueToday) return (ScheduleDecision.Wait, dueToday);
        if (nowUtc < dueToday + CatchUpWindow) return (ScheduleDecision.RunNow, nowUtc);
        return (ScheduleDecision.Missed, dueToday.AddDays(1));
    }
}
