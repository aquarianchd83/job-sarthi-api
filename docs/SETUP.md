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

1. Build the web app: `cd job-sarthi-web && npm run build`.
2. In an elevated PowerShell from the `job-sarthi-api` folder: `deploy\install-windows-service.ps1`.
   It publishes the API, copies the Angular build into `wwwroot`, installs Chromium and creates the `JobSarthiApi`
   Windows Service (starts with the server, restarts after a crash).
3. The service account needs access to SQL Server. Easiest: run the service as a Windows user that has a SQL login
   (`sc.exe config JobSarthiApi obj= "DOMAIN\user" password= "..."`), or use a SQL login in the connection string.
4. To reach it from other devices, set `"Urls": "http://0.0.0.0:5000"` and open the port in Windows Firewall.
   The app has no login. Keep it on your private network or put it behind a VPN or a reverse proxy with authentication.
5. IIS is optional. If you prefer it, publish with `dotnet publish` and host the folder with the ASP.NET Core Hosting Bundle.

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
