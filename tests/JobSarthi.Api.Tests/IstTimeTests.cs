using JobSarthi.Api.Common;
using Xunit;

namespace JobSarthi.Api.Tests;

public class IstTimeTests
{
    [Fact]
    public void ToIst_adds_five_thirty()
    {
        var utc = new DateTime(2026, 11, 17, 3, 30, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 11, 17, 9, 0, 0), IstTime.ToIst(utc));
    }

    [Fact]
    public void NextRunUtc_is_today_when_time_not_passed()
    {
        var now = new DateTime(2026, 11, 17, 2, 0, 0, DateTimeKind.Utc); // 7:30 AM IST
        Assert.Equal(new DateTime(2026, 11, 17, 3, 30, 0), IstTime.NextRunUtc(new TimeOnly(9, 0), now));
    }

    [Fact]
    public void NextRunUtc_rolls_to_tomorrow_when_time_passed()
    {
        var now = new DateTime(2026, 11, 17, 4, 0, 0, DateTimeKind.Utc); // 9:30 AM IST
        Assert.Equal(new DateTime(2026, 11, 18, 3, 30, 0), IstTime.NextRunUtc(new TimeOnly(9, 0), now));
    }
}
