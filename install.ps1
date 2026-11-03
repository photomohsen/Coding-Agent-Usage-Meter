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
    $version = $null
    if (-not $Zip) {
        # The commit this copy comes from, so the widget can tell when GitHub has a newer one. Read before the download:
        # a push in between only makes the next check offer one extra update.
        try { $version = (Invoke-RestMethod 'https://api.github.com/repos/photomohsen/Coding-Agent-Usage-Meter/commits/main' -Headers @{ Accept = 'application/vnd.github.sha' }).ToString().Trim() } catch { }
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
    if ($version) { Set-Content (Join-Path $folder 'version.txt') $version -NoNewline }
    & cmd.exe /c "`"$folder\Install.cmd`""
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
