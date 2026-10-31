@echo off
rem Installs the Coding Agent Usage Meter: gets .NET 10 if needed, builds the widget, puts a shortcut on the desktop and starts it.
setlocal
cd /d "%~dp0"

rem dotnet may be machine-wide, a user install that is not on PATH, or only on PATH.
set "DOTNET="
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
if not defined DOTNET if exist "%USERPROFILE%\.dotnet\dotnet.exe" set "DOTNET=%USERPROFILE%\.dotnet\dotnet.exe"
if not defined DOTNET for /f "delims=" %%d in ('where dotnet 2^>nul') do if not defined DOTNET set "DOTNET=%%d"

if not defined DOTNET (
    echo .NET 10 is not installed. Installing it now...
    winget install --id Microsoft.DotNet.SDK.10 --accept-source-agreements --accept-package-agreements
    set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
)
if not exist "%DOTNET%" (
    echo Could not find .NET. Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0 and run this again.
    pause
    exit /b 1
)

rem A running widget locks its files, so the build could not replace them.
powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | Where-Object CommandLine -like '*UsageWidget.Prototype.dll*' | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }"

echo Building the widget...
"%DOTNET%" build prototype\UsageWidget.Prototype -nologo -v q
if errorlevel 1 (
    echo The build failed. If .NET 10 was just installed, close this window and run Install.cmd again.
    pause
    exit /b 1
)

echo Adding a shortcut to the desktop...
rem The shortcut had this name before the rename.
powershell -NoProfile -Command "Remove-Item ([IO.Path]::Combine([Environment]::GetFolderPath('Desktop'), 'Claude Usage Widget.lnk')) -ErrorAction SilentlyContinue"
powershell -NoProfile -Command "$s = (New-Object -ComObject WScript.Shell).CreateShortcut([IO.Path]::Combine([Environment]::GetFolderPath('Desktop'), 'Coding Agent Usage Meter.lnk')); $s.TargetPath = \"$env:WINDIR\System32\wscript.exe\"; $s.Arguments = '\"%~dp0Start Widget.vbs\"'; $s.WorkingDirectory = '%~dp0'; $s.IconLocation = '%~dp0widget.ico'; $s.Description = 'Shows your Claude Code and Codex usage limits'; $s.Save()"

echo Starting the widget...
wscript.exe "%~dp0Start Widget.vbs"
echo Done. The widget is above the clock, and "Coding Agent Usage Meter" is on your desktop.
timeout /t 5 >nul 2>&1
