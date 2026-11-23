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

## Apply categories and closed jobs

After every search each job is sorted by its apply link and description (`Classification/`):

| Category | When | What the app does |
|---|---|---|
| **Api** | Link (or a link in the description) is Greenhouse, Lever, Ashby or Workable | Greenhouse/Lever/Ashby: Playwright fills the form (approval or full auto, as before). Workable: drafted through MCP |
| **Email** | The posting asks for the resume by email ("send your CV to hr@...") | Cover letter + subject ready, you send it from your mail app. Never sent automatically |
| **Manual** | Everything else | Link + cover letter ready. With Playwright MCP on, the page opens pre-filled in your browser. You press Submit |

Jobs that say "no longer accepting applications" (position filled, expired, closed...) are ignored: no category, never
shortlisted, never applied to. The listing text is checked on every scoring pass, the live apply page is checked for the
shortlist in the daily run (`POST /api/jobs/classify`, `POST /api/jobs/{id}/check-page`), and again by the browser before filling.

## Company discovery

Greenhouse, Lever and Ashby have no "search all companies" API, so they only read the company boards in your list.
After each daily run (or **Scan jobs now** on Job Sources) the app reads the apply links of jobs from RemoteOK,
We Work Remotely and email alerts, and suggests every Greenhouse/Lever/Ashby board it finds (`CompanyDiscoveryService`).
Add them one by one, all at once, or switch on auto-add. A company you remove or mark Not interested is never suggested again.

## MCP client (Playwright MCP)

The app is an MCP **client**: it starts external MCP servers and uses their tools. It is not an MCP server.
Off by default. In `appsettings.Local.json`:

```json
"Mcp": {
  "Enabled": true,
  "BrowserServer": "playwright",
  "Servers": {
    "playwright": { "Command": "npx", "Args": [ "@playwright/mcp@latest", "--browser", "msedge" ] }
  }
}
```

- Needs Node.js. Leave out `--headless` so the window stays open for you to review and press Submit.
- **Prepare draft** on a Manual job opens the apply page, checks it is still open, fills what is known, attaches your resume
  (the resume folder is shared with the server as an MCP root) and stops. Nothing is ever submitted by this flow.
- Other servers work too (`Url` for Streamable HTTP servers). Try them at `/api/mcp/servers`, `/api/mcp/servers/{name}/tools`.
  These endpoints answer only calls from this computer.
- Tokens never go into settings as plain text: write `"Headers": { "Authorization": "Bearer ${MY_MCP_TOKEN}" }` or
  `"Env": { "API_KEY": "${MY_KEY}" }` and set the environment variable on the PC.

## Secrets

Put IMAP password, Gemini key etc. in `src/JobSarthi.Api/appsettings.Local.json` (git-ignored).

Layout and stage plan: `docs/STRUCTURE.md`.
