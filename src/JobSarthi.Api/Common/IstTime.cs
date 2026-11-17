namespace JobSarthi.Api.Common;

/// <summary>DB/API always store UTC. IST is only for scheduling math and for display on the client.</summary>
public static class IstTime
{
    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");

    public static DateTime ToIst(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Ist);

    /// <summary>Next occurrence of a wall-clock IST time (e.g. 09:00), returned as UTC.</summary>
    public static DateTime NextRunUtc(TimeOnly istTime, DateTime utcNow)
    {
        var nowIst = ToIst(utcNow);
        var candidate = nowIst.Date + istTime.ToTimeSpan();
        if (candidate <= nowIst) candidate = candidate.AddDays(1);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), Ist);
    }

    /// <summary>Midnight at the start of the current IST day, as UTC. "Today" for the daily application limit.</summary>
    public static DateTime StartOfIstDayUtc(DateTime utcNow) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(ToIst(utcNow).Date, DateTimeKind.Unspecified), Ist);
}
