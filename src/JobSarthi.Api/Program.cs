using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Applying;
using JobSarthi.Api.Data;
using JobSarthi.Api.Notifications;
using JobSarthi.Api.Scheduling;
using JobSarthi.Api.Sources;
using JobSarthi.Api.Sources.Alerts;
using JobSarthi.Api.Tailoring;

// One-time setup on a new server: downloads the Chromium that Playwright drives.
//   dotnet JobSarthi.Api.dll --install-browsers
if (args.Contains("--install-browsers"))
    return Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });

// Cron-style mode: run the whole daily routine once, wait for queued applications to finish, send the summary, exit.
// Meant for Windows Task Scheduler (see deploy/install-scheduled-task.ps1). Exit code 0 ok, 1 run failed, 2 already ran today.
var oneShot = args.Contains("--run-daily");

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();           // runs as a Windows Service, auto-restarts
// Put secrets (IMAP app password, tokens) in appsettings.Local.json. It is git-ignored.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
// One-shot mode must not fight a running copy of the app for port 5000, so it listens on a random local port.
if (oneShot) builder.Configuration["Urls"] = "http://127.0.0.1:0";

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<JobSarthiDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.Configure<ImapOptions>(builder.Configuration.GetSection("Imap"));
builder.Services.AddSingleton<ImapAlertReader>();
builder.Services.AddJobSources();          // plugin registration lives in JobSourceExtensions
builder.Services.AddTailoring(builder.Configuration);
builder.Services.AddApplying(builder.Configuration);
// Later stages register INotifier the same way.
builder.Services.AddNotifications(builder.Configuration);
builder.Services.AddScheduling(backgroundScheduler: !oneShot);   // the cron job replaces the in-app timer

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:4200" };
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await DbSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<JobSarthiDbContext>(), app.Environment.ContentRootPath);

if (oneShot) return await OneShotRunner.RunAsync(app);

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
// Production: the built Angular app is copied into wwwroot and served from the same address as the API.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
    app.MapFallbackToFile("index.html");
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", utcNow = DateTime.UtcNow }));
app.Run();
return 0;

public partial class Program { }
