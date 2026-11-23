# Job Sarthi: setup and daily use

## 1. Run it on your PC

```bash
cd job-sarthi-api && dotnet run --project src/JobSarthi.Api     # API on http://localhost:5000
cd job-sarthi-web && npm install && npm start                    # app on http://localhost:4200
```

The database `JobSarthi` is created on the local SQL Server on first start (Windows login). Change the
connection string in `appsettings.Local.json` if you use a named instance or a SQL login.

## 2. Settings that stay out of git: `src/JobSarthi.Api/appsettings.Local.json`

```json
{
  "ConnectionStrings": { "Default": "Server=localhost;Database=JobSarthi;Trusted_Connection=True;TrustServerCertificate=True" },
  "App":      { "PublicUrl": "http://your-server:5000" },
  "Apply":    { "BrowserChannel": "msedge", "Headless": false },
  "Imap":     { "Host": "imap.gmail.com", "User": "you@gmail.com", "AppPassword": "xxxx xxxx xxxx xxxx" },
  "Telegram": { "BotToken": "123456:ABC...", "ChatId": "987654321" },
  "Email":    { "Host": "smtp.gmail.com", "Port": 465, "User": "you@gmail.com", "Password": "app password", "From": "you@gmail.com", "To": "you@gmail.com" },
  "Gemini":   { "ApiKey": "optional free key" }
}
```

| Setting | What it does | How to get it (all free) |
|---|---|---|
| `Imap` | Reads Naukri and LinkedIn job-alert emails | Gmail: turn on 2-step verification, create an App Password |
| `Telegram` | Daily summary and alerts on your phone | Message `@BotFather`, `/newbot`, copy the token. Message your new bot once, then use "Find my chat id" in Settings |
| `Email` | Same messages by email | Gmail App Password. Port 465 uses `SslOnConnect`, port 587 needs `"Security": "StartTls"` |
| `Gemini` | Optional smarter cover letters | Google AI Studio free key. Switch it on in Settings |
| `Apply.BrowserChannel` | Use Edge or Chrome already on the PC | `msedge` or `chrome`. Leave empty on the server and install Chromium (below) |

Restart the API after changing this file. Use **Settings > Send test message** to check the channels.

## 3. Install on the dedicated server

Pick **one** way to run the daily routine.

### Option A: cron-style with Task Scheduler (recommended)

The app is not left running. Task Scheduler starts `JobSarthi.Api.exe --run-daily` once a day. It fetches, filters,
tailors, queues applications, waits for them to finish filling, sends the Telegram/email summary and exits
(exit code 0 ok, 1 run failed, 2 already ran today).

1. Build the web app: `cd job-sarthi-web && npm run build`.
2. Publish: `dotnet publish src/JobSarthi.Api -c Release -o C:\JobSarthi\app`, then copy
   `job-sarthi-web\dist\job-sarthi-web\browser\*` into `C:\JobSarthi\app\wwwroot`.
3. Install Chromium once: `C:\JobSarthi\app\JobSarthi.Api.exe --install-browsers` (or set `Apply.BrowserChannel`).
4. Create the daily task. Check first with `-DryRun`, which changes nothing:
   ```powershell
   .\deploy\install-scheduled-task.ps1 -DryRun
   .\deploy\install-scheduled-task.ps1 -TimeIst 09:00          # add -RunWhenLoggedOff to run with nobody logged in
   Start-ScheduledTask -TaskName JobSarthiDaily               # test it now, then read C:\JobSarthi\app\logs\daily.log
   ```
   The time is given in IST and converted to the PC's clock. If the PC was off at that time, the task runs when it is
   back (`StartWhenAvailable`). The app still allows only one scheduled run per IST day, so a second start does nothing.
   To remove it: `.\deploy\install-scheduled-task.ps1 -Remove`.
5. To review and approve applications, double-click `deploy\start-ui.cmd` (opens http://localhost:5000). Close it when done.
   In **Settings**, you can switch off "Run automatically every day". The task replaces that timer, so this avoids a
   second run if you keep the app open at run time.

Keep the PC's time zone on India Standard Time or UTC. A zone with daylight saving moves the task by an hour twice a year.

### Option B: always-on Windows Service

`deploy\install-windows-service.ps1` publishes the app, installs Chromium and creates a `JobSarthiApi` service that starts
with the server and restarts after a crash. The built-in timer then starts the daily run, and the app and its pages are always available.

### Both options

- The account that runs the task or service needs access to SQL Server. Easiest: a Windows user that has a SQL login,
  or a SQL login in the connection string.
- To reach the app from other devices, set `"Urls": "http://0.0.0.0:5000"` and open the port in Windows Firewall.
  The app has no login. Keep it on your private network or behind a VPN or a reverse proxy with authentication.
- IIS is optional. Host the published folder with the ASP.NET Core Hosting Bundle if you prefer it.

## 4. Daily routine

1. **Resume & Profile:** upload your resume, fix any wrong field, save.
2. **Rules:** roles, locations, keywords, experience, blacklist.
3. **Job Sources:** switch on what you want, add company slugs for Greenhouse, Lever and Ashby.
4. **Saved Answers:** notice period, expected CTC, work authorization.
5. **Settings:** run time (IST), daily limit, minimum score, keep **Approval mode** at first.
6. Every day at the run time you get a summary. Open **Applications**, check each screenshot, press **Approve and submit**.

## 5. When something goes wrong

| You see | Meaning |
|---|---|
| Red strip "Job source problem" | A company slug is wrong, or the site changed or is down |
| "Application form not found" | The job closed or the form layout changed. Apply by hand with Open job link |
| "Needs you" with a CAPTCHA or code | The app never solves these. Open the job link and apply yourself |
| "Run missed" alert | The server was off more than 6 hours past the run time. Use **Run now** |

## 6. Tests

```bash
cd job-sarthi-api && set JOBSARTHI_TEST_CHANNEL=msedge && dotnet test
```
Browser tests run against local fixture forms only. They never touch a live site.
