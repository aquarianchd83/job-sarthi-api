# job-sarthi-api - proposed structure

```
job-sarthi-api/
  JobSarthi.Api.sln
  src/JobSarthi.Api/
    Program.cs                      host, DI, Windows Service
    appsettings.json
    Controllers/                    Profile, Sources, Rules, SavedAnswers, Shortlist, Applications, Settings, Runs
    Domain/Entities, Domain/Enums   EF entities + SourceCapability/ApplicationStatus/ApplyMode/WorkMode
    Data/                           JobSarthiDbContext, Migrations/, Seed/
    Sources/                        IJobSource (plugin contract), JobSourceRegistry
      Providers/                    Greenhouse, Lever, Ashby, RemoteOk, WeWorkRemotely, NaukriAlert, LinkedInAlert
      Alerts/                       ImapAlertReader + Naukri/LinkedIn email parsers (no scraping)
    Resume/                         ResumeParser
    Filtering/                      RuleFilter, JobScorer (no LLM)
    Tailoring/                      ITailorService; RuleBased -> Gemini -> Ollama, TailorOrchestrator (fallback), TailorSafetyGuard
    Applying/                       IJobApplier, ApplyOrchestrator, ScreenshotStore
      Playwright/                   Greenhouse/Lever/Ashby appliers, FormFieldMapper, browser factory
    Scheduling/                     DailyRunHostedService (IST), DailyRunPipeline
    Notifications/                  INotifier: Telegram, Email, DailySummaryBuilder
    Common/                         IstTime (UTC <-> IST)
  tests/JobSarthi.Api.Tests/
  deploy/                           Windows Service / IIS scripts
  docs/
```

| Stage | Folders |
|---|---|
| 1 Setup | Program, Data, Domain, Controllers |
| 2 Sources + fetch | Sources/* |
| 3 Filter + shortlist | Filtering, Resume, ShortlistController |
| 4 Tailor | Tailoring |
| 5 Apply | Applying |
| 6 Scheduler | Scheduling, Common |
| 7 Notify + track | Notifications, ApplicationsController |