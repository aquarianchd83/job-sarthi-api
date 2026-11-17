# Run in an elevated PowerShell on the server, from the job-sarthi-api folder.
# Not yet tested on a real server. Read docs/SETUP.md first.
param(
  [string]$AppDir = 'C:\JobSarthi\app',
  [string]$WebDist = '..\job-sarthi-web\dist\job-sarthi-web\browser'
)
$ErrorActionPreference = 'Stop'

# 1. Publish the API and put the built Angular app next to it (served from the same address).
dotnet publish src/JobSarthi.Api -c Release -o $AppDir
if (Test-Path $WebDist) { New-Item -ItemType Directory -Force "$AppDir\wwwroot" | Out-Null; Copy-Item "$WebDist\*" "$AppDir\wwwroot" -Recurse -Force }
else { Write-Warning "Angular build not found at $WebDist. Run 'npm run build' in job-sarthi-web first." }

# 2. Chromium for Playwright, in a folder every account (including the service) can read.
[Environment]::SetEnvironmentVariable('PLAYWRIGHT_BROWSERS_PATH', 'C:\JobSarthi\browsers', 'Machine')
$env:PLAYWRIGHT_BROWSERS_PATH = 'C:\JobSarthi\browsers'
& "$AppDir\JobSarthi.Api.exe" --install-browsers

# 3. Windows Service that starts with the server and restarts itself after a crash.
sc.exe create JobSarthiApi binPath= "$AppDir\JobSarthi.Api.exe" start= auto DisplayName= "Job Sarthi API"
sc.exe failure JobSarthiApi reset= 86400 actions= restart/5000/restart/5000/restart/5000
sc.exe start JobSarthiApi
Write-Host "Done. Open http://localhost:5000 on the server. Put secrets in $AppDir\appsettings.Local.json (see docs/SETUP.md)."
