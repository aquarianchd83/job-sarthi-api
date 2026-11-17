# job-sarthi-api

ASP.NET Core Web API (namespace `JobSarthi.Api`): fetch, filter, tailor, apply, schedule.

## Run

```bash
dotnet run --project src/JobSarthi.Api      # http://localhost:5000 , Swagger at /swagger
dotnet test
```

## Applying (Playwright)

- First time on a server: `dotnet run --project src/JobSarthi.Api -- --install-browsers` (downloads Chromium).
- On a PC that has Edge or Chrome you can skip that: set `"Apply": { "BrowserChannel": "msedge" }` in `appsettings.Local.json`.
- `"Headless": false` shows the browser window while it works.
- Browser tests run against local fixture pages only: `set JOBSARTHI_TEST_CHANNEL=msedge` then `dotnet test`.

## Secrets

Put IMAP password, Gemini key etc. in `src/JobSarthi.Api/appsettings.Local.json` (git-ignored).

Layout and stage plan: `docs/STRUCTURE.md`.
