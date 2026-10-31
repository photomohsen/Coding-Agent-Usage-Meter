# Installs the Coding Agent Usage Meter from GitHub. Paste this in PowerShell:
#   irm https://raw.githubusercontent.com/photomohsen/Coding-Agent-Usage-Meter/main/install.ps1 | iex
# It downloads the code to %LOCALAPPDATA%\Coding Agent Usage Meter and runs Install.cmd there.
# Running it again updates the widget; settings are kept.
param([string]$Zip) # a local zip instead of GitHub's, for testing

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # the progress bar makes the download many times slower
$folder = Join-Path $env:LOCALAPPDATA 'Coding Agent Usage Meter'
$temp = Join-Path $env:TEMP "coding-agent-usage-meter-$(Get-Random)"
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    if (-not $Zip) {
        Write-Host 'Downloading the Coding Agent Usage Meter...'
        $Zip = Join-Path $temp 'source.zip'
        Invoke-WebRequest 'https://github.com/photomohsen/Coding-Agent-Usage-Meter/archive/refs/heads/main.zip' -OutFile $Zip -UseBasicParsing
    }
    Expand-Archive $Zip -DestinationPath $temp
    # GitHub's zip holds one folder, Coding-Agent-Usage-Meter-main.
    $source = Get-ChildItem $temp -Directory | Select-Object -First 1

    # A running widget locks its files.
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
        Where-Object CommandLine -like '*UsageWidget.Prototype.dll*' |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    Copy-Item (Join-Path $source.FullName '*') $folder -Recurse -Force
    & cmd.exe /c "`"$folder\Install.cmd`""
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
