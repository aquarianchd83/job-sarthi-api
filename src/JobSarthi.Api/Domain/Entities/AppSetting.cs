using JobSarthi.Api.Domain.Enums;

namespace JobSarthi.Api.Domain.Entities;

public class AppSetting
{
    public int Id { get; set; }
    /// <summary>When the daily run starts, as IST wall-clock time.</summary>
    public TimeOnly DailyRunTimeIst { get; set; } = new(9, 0);
    public bool SchedulerEnabled { get; set; } = true;
    public int DailyApplyLimit { get; set; } = 10;
    public int GapBetweenAppliesSeconds { get; set; } = 90;
    /// <summary>The daily run only applies to shortlisted jobs scoring at least this.</summary>
    public int AutoApplyMinScore { get; set; } = 60;
    public ApplyMode ApplyMode { get; set; } = ApplyMode.Approval;
    public bool UseGemini { get; set; }
    public bool UseOllama { get; set; }
    /// <summary>Send the daily shortlist and summary to Telegram and email (the channels you set up).</summary>
    public bool NotifySummary { get; set; } = true;
    /// <summary>Send failure alerts to those channels.</summary>
    public bool NotifyAlerts { get; set; } = true;
}
