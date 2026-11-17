using JobSarthi.Api.Domain.Enums;
using JobSarthi.Api.Scheduling;
using Xunit;

namespace JobSarthi.Api.Tests;

public class SchedulerTests
{
    private static readonly TimeOnly Nine = new(9, 0);          // 09:00 IST = 03:30 UTC
    private static DateTime Utc(int d, int h, int m = 0) => new(2026, 11, d, h, m, 0, DateTimeKind.Utc);

    [Fact]
    public void Before_run_time_it_waits_until_todays_slot()
    {
        var (d, next) = SchedulePolicy.Decide(Utc(17, 2, 0), Nine, null);   // 07:30 IST
        Assert.Equal(ScheduleDecision.Wait, d);
        Assert.Equal(Utc(17, 3, 30), next);
    }

    [Fact]
    public void At_run_time_it_runs()
    {
        var (d, _) = SchedulePolicy.Decide(Utc(17, 3, 30), Nine, null);
        Assert.Equal(ScheduleDecision.RunNow, d);
    }

    [Fact]
    public void After_todays_run_it_waits_for_tomorrow()
    {
        var (d, next) = SchedulePolicy.Decide(Utc(17, 4, 0), Nine, Utc(17, 3, 30));
        Assert.Equal(ScheduleDecision.Wait, d);
        Assert.Equal(Utc(18, 3, 30), next);
    }

    [Fact]
    public void A_manual_run_earlier_does_not_suppress_the_scheduled_run()
    {
        // Callers pass only scheduled runs, so a null here means "no scheduled run yet today".
        var (d, _) = SchedulePolicy.Decide(Utc(17, 3, 31), Nine, Utc(16, 3, 30));  // last scheduled run was yesterday
        Assert.Equal(ScheduleDecision.RunNow, d);
    }

    [Fact]
    public void Server_back_up_within_six_hours_catches_up()
    {
        var (d, _) = SchedulePolicy.Decide(Utc(17, 8, 0), Nine, Utc(16, 3, 30)); // 13:30 IST, 4.5h late
        Assert.Equal(ScheduleDecision.RunNow, d);
    }

    [Fact]
    public void More_than_six_hours_late_is_missed_and_waits_for_tomorrow()
    {
        var (d, next) = SchedulePolicy.Decide(Utc(17, 10, 0), Nine, Utc(16, 3, 30)); // 15:30 IST, 6.5h late
        Assert.Equal(ScheduleDecision.Missed, d);
        Assert.Equal(Utc(18, 3, 30), next);
    }

    [Fact]
    public void The_ist_day_not_the_utc_day_decides_what_today_means()
    {
        // 22:00 IST on the 17th is 16:30 UTC on the 17th. A run at 03:30 UTC that day counts as today.
        var (d, next) = SchedulePolicy.Decide(Utc(17, 16, 30), Nine, Utc(17, 3, 30));
        Assert.Equal(ScheduleDecision.Wait, d);
        Assert.Equal(Utc(18, 3, 30), next);
        // 00:30 IST on the 18th is 19:00 UTC on the 17th. That is already the next IST day.
        var (d2, next2) = SchedulePolicy.Decide(Utc(17, 19, 0), Nine, Utc(17, 3, 30));
        Assert.Equal(ScheduleDecision.Wait, d2);
        Assert.Equal(Utc(18, 3, 30), next2);
    }

    // ---- which jobs the run applies to ----

    [Fact]
    public void Planner_takes_best_scores_first_within_budget_and_min_score()
    {
        var c = new[]
        {
            new ApplyCandidate(1, 90, null), new ApplyCandidate(2, 75, null), new ApplyCandidate(3, 55, null), new ApplyCandidate(4, 80, null),
        };
        Assert.Equal(new[] { 1, 4 }, AutoApplyPlanner.Select(c, budget: 2, minScore: 60));
        Assert.Equal(new[] { 1, 4, 2 }, AutoApplyPlanner.Select(c, budget: 10, minScore: 60)); // job 3 is below the minimum
    }

    [Fact]
    public void Planner_skips_jobs_already_attempted_or_decided()
    {
        var c = new[]
        {
            new ApplyCandidate(1, 90, ApplicationStatus.Submitted), new ApplyCandidate(2, 90, ApplicationStatus.Cancelled),
            new ApplyCandidate(3, 90, ApplicationStatus.Failed), new ApplyCandidate(4, 90, ApplicationStatus.AwaitingApproval),
            new ApplyCandidate(5, 90, ApplicationStatus.Shortlisted), new ApplyCandidate(6, 90, null),
        };
        Assert.Equal(new[] { 5, 6 }, AutoApplyPlanner.Select(c, 10, 0));
    }

    [Fact]
    public void Planner_with_no_budget_selects_nothing() =>
        Assert.Empty(AutoApplyPlanner.Select(new[] { new ApplyCandidate(1, 99, null) }, 0, 0));
}
