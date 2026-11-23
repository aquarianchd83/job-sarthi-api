# Cron-style scheduling for Windows: Task Scheduler runs "JobSarthi.Api.exe --run-daily" once a day.
# The app is NOT left running. It starts, does the whole daily routine, waits for queued applications to finish
# filling, sends the Telegram/email summary and exits. Start the app by hand (deploy\start-ui.cmd) when you want to
# review and approve applications.
#
#   .\install-scheduled-task.ps1 -DryRun                 shows what would be created, changes nothing
#   .\install-scheduled-task.ps1                         creates the task (daily 09:00 IST)
#   .\install-scheduled-task.ps1 -TimeIst 08:30
#   .\install-scheduled-task.ps1 -RunWhenLoggedOff       also runs when nobody is logged in
#   .\install-scheduled-task.ps1 -Remove                 deletes the task
#
# Not yet tested on a real server. Run in an elevated PowerShell if you use -RunWhenLoggedOff.
param(
  [string]$AppDir = 'C:\JobSarthi\app',
  [string]$TimeIst = '09:00',
  [string]$TaskName = 'JobSarthiDaily',
  [switch]$RunWhenLoggedOff,
  [switch]$DryRun,
  [switch]$Remove
)
$ErrorActionPreference = 'Stop'

if ($Remove) {
  Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
  Write-Host "Removed task $TaskName."
  return
}

# Task Scheduler uses this PC's clock. Job Sarthi thinks in IST, so convert the IST time to local time.
$ist = [TimeZoneInfo]::FindSystemTimeZoneById('India Standard Time')
$istTime = [DateTime]::SpecifyKind([DateTime]::Today.Add([TimeSpan]::Parse($TimeIst)), [DateTimeKind]::Unspecified)
$utc = [TimeZoneInfo]::ConvertTimeToUtc($istTime, $ist)
$local = [TimeZoneInfo]::ConvertTimeFromUtc($utc, [TimeZoneInfo]::Local)

$exe = Join-Path $AppDir 'JobSarthi.Api.exe'
if (-not $DryRun -and -not (Test-Path $exe)) { throw "Not found: $exe. Publish first: dotnet publish src/JobSarthi.Api -c Release -o $AppDir" }

# cmd wrapper so the output of every run is kept in logs\daily.log
$cmd = "cd /d `"$AppDir`" && JobSarthi.Api.exe --run-daily >> logs\daily.log 2>&1"
$action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument "/c `"$cmd`"" -WorkingDirectory $AppDir
$trigger = New-ScheduledTaskTrigger -Daily -At $local
# StartWhenAvailable = if the PC was off at run time, run as soon as it is back (the app still allows only one run per IST day).
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 3)
$logon = if ($RunWhenLoggedOff) { 'S4U' } else { 'Interactive' }
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType $logon -RunLevel Limited

Write-Host ("Run time: {0} IST = {1} on this PC ({2})" -f $TimeIst, $local.ToString('HH:mm'), [TimeZoneInfo]::Local.DisplayName)
if ([TimeZoneInfo]::Local.SupportsDaylightSavingTime) {
  Write-Warning 'This PC changes its clock for daylight saving, so the run will drift by an hour against IST twice a year. Set the PC time zone to India Standard Time or UTC to avoid it.'
}
Write-Host "Command : $cmd"
Write-Host "Runs as : $env:USERDOMAIN\$env:USERNAME ($logon logon)"

if ($DryRun) { Write-Host 'Dry run. Nothing was created.'; return }

New-Item -ItemType Directory -Force (Join-Path $AppDir 'logs') | Out-Null

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal `
  -Description 'Job Sarthi daily routine: fetch, filter, tailor, queue applications, send summary.' -Force | Out-Null
Write-Host "Created task $TaskName. Test it now with:  Start-ScheduledTask -TaskName $TaskName   (then read $AppDir\logs\daily.log)"
