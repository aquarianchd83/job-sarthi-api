using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Domain.Entities;

namespace JobSarthi.Api.Data;

public class JobSarthiDbContext : DbContext
{
    public JobSarthiDbContext(DbContextOptions<JobSarthiDbContext> options) : base(options) { }

    public DbSet<UserProfile> Profiles => Set<UserProfile>();
    public DbSet<ResumeDocument> Resumes => Set<ResumeDocument>();
    public DbSet<JobSource> JobSources => Set<JobSource>();
    public DbSet<CompanySlug> CompanySlugs => Set<CompanySlug>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<SavedAnswer> SavedAnswers => Set<SavedAnswer>();
    public DbSet<JobApplication> Applications => Set<JobApplication>();
    public DbSet<ApplicationLog> ApplicationLogs => Set<ApplicationLog>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<DailyRun> DailyRuns => Set<DailyRun>();
    public DbSet<PendingQuestion> PendingQuestions => Set<PendingQuestion>();
    public DbSet<Alert> Alerts => Set<Alert>();

    /// <summary>Every DateTime is UTC. Mark it so the API serializes a trailing "Z" and clients never misread it as local.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder cb)
    {
        cb.Properties<DateTime>().HaveConversion<UtcConverter>();
        cb.Properties<DateTime?>().HaveConversion<NullableUtcConverter>();
    }

    private class UtcConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>
    {
        public UtcConverter() : base(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)) { }
    }

    private class NullableUtcConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>
    {
        public NullableUtcConverter() : base(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v) { }
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        // All timestamps are stored as UTC. Enums are stored as readable strings.
        b.Entity<JobSource>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(50);
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.Property(x => x.Capability).HasConversion<string>().HasMaxLength(20);
        });
        b.Entity<CompanySlug>(e =>
        {
            e.HasIndex(x => new { x.SourceKey, x.Slug }).IsUnique();
            e.Property(x => x.SourceKey).HasMaxLength(50);
            e.Property(x => x.Slug).HasMaxLength(100);
        });
        b.Entity<Job>(e =>
        {
            e.HasIndex(x => new { x.SourceKey, x.ExternalId }).IsUnique();
            e.Property(x => x.SourceKey).HasMaxLength(50);
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Company).HasMaxLength(200);
            e.Property(x => x.Url).HasMaxLength(1000);
            e.Property(x => x.Location).HasMaxLength(300);
            e.Property(x => x.ScoreNotes).HasMaxLength(300);
            e.Property(x => x.RejectReason).HasMaxLength(300);
            e.HasIndex(x => new { x.Shortlisted, x.FitScore });
            e.Property(x => x.WorkMode).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ApplyCategory).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.AtsKey).HasMaxLength(30);
            e.Property(x => x.ApplyUrl).HasMaxLength(1000);
            e.Property(x => x.ApplyEmail).HasMaxLength(254);
            e.Property(x => x.CategoryReason).HasMaxLength(300);
            e.Property(x => x.ClosedReason).HasMaxLength(300);
            e.HasIndex(x => new { x.IsClosed, x.ApplyCategory });
        });
        b.Entity<Rule>(e =>
        {
            // Per-place preferences are stored as one JSON column.
            e.Property(x => x.LocationPrefs).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                s => System.Text.Json.JsonSerializer.Deserialize<List<LocationPref>>(s, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<LocationPref>(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<LocationPref>>(
                    (a, c) => System.Text.Json.JsonSerializer.Serialize(a, (System.Text.Json.JsonSerializerOptions?)null) == System.Text.Json.JsonSerializer.Serialize(c, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null).GetHashCode(),
                    v => System.Text.Json.JsonSerializer.Deserialize<List<LocationPref>>(System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null), (System.Text.Json.JsonSerializerOptions?)null)!));
            e.Property(x => x.WorkMode).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.MinSalaryLpa).HasPrecision(8, 2);
        });
        b.Entity<JobApplication>(e =>
        {
            e.HasIndex(x => x.JobId).IsUnique();
            e.Property(x => x.TailorEngine).HasMaxLength(20);
            e.Property(x => x.TailorNote).HasMaxLength(1000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        });
        b.Entity<ApplicationLog>().HasIndex(x => x.ApplicationId);
        b.Entity<DailyRun>(e =>
        {
            e.Property(x => x.Trigger).HasMaxLength(20);
            e.Property(x => x.Error).HasMaxLength(1000);
            e.Property(x => x.Notes).HasMaxLength(4000);
            e.HasIndex(x => x.StartedAtUtc);
        });
        b.Entity<Alert>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(30);
            e.Property(x => x.Message).HasMaxLength(1000);
            e.HasIndex(x => new { x.Acknowledged, x.CreatedAtUtc });
        });
        b.Entity<PendingQuestion>(e =>
        {
            e.HasIndex(x => x.ApplicationId);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Prompt).HasMaxLength(500);
        });
        b.Entity<SavedAnswer>(e => { e.Property(x => x.Question).HasMaxLength(300); e.HasIndex(x => x.Question).IsUnique(); });
        b.Entity<AppSetting>().Property(x => x.ApplyMode).HasConversion<string>().HasMaxLength(20);
    }
}
