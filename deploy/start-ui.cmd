@echo off
rem Starts Job Sarthi so you can review and approve applications. Close this window to stop it.
rem The daily run does NOT need this. Task Scheduler runs it separately (install-scheduled-task.ps1).
cd /d "%~dp0"
if exist "JobSarthi.Api.exe" ( set EXE=JobSarthi.Api.exe ) else ( set EXE=..\src\JobSarthi.Api\bin\Release\net8.0\JobSarthi.Api.exe )
start "" http://localhost:5000
"%EXE%"
