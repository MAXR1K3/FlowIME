[CmdletBinding()]
param(
    [switch]$SkipRestore,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($env:OS -ne 'Windows_NT') {
    throw 'FlowIME P0 validation must run on Windows 11.'
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    Write-Host '== FlowIME P0 environment ==' -ForegroundColor Cyan
    $os = Get-CimInstance Win32_OperatingSystem
    Write-Host ("OS: {0} ({1})" -f $os.Caption, $os.Version)
    Write-Host ("Architecture: {0}" -f $env:PROCESSOR_ARCHITECTURE)

    $dotnetVersion = (& dotnet --version).Trim()
    Write-Host ("dotnet: {0}" -f $dotnetVersion)

    if (-not $SkipRestore) {
        Write-Host "`n== Restore ==" -ForegroundColor Cyan
        & dotnet restore FlowIME.sln
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }
    }

    Write-Host "`n== Build ==" -ForegroundColor Cyan
    & dotnet build FlowIME.sln -c Debug --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

    if (-not $SkipTests) {
        Write-Host "`n== Tests ==" -ForegroundColor Cyan
        & dotnet test FlowIME.sln -c Debug --no-build --no-restore
        if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE." }
    }

    Write-Host "`n== Probe ready ==" -ForegroundColor Green
    Write-Host '1. Start the foreground watcher:'
    Write-Host '   dotnet run --project src/FlowIME.Probe -- watch'
    Write-Host ''
    Write-Host '2. Copy a target HWND from the watcher, then inspect it:'
    Write-Host '   dotnet run --project src/FlowIME.Probe -- inspect --hwnd 0x123456'
    Write-Host ''
    Write-Host '3. With Microsoft Pinyin active, test both requests:'
    Write-Host '   dotnet run --project src/FlowIME.Probe -- set --hwnd 0x123456 --mode chinese'
    Write-Host '   dotnet run --project src/FlowIME.Probe -- set --hwnd 0x123456 --mode english'
    Write-Host ''
    Write-Host 'Record the results in docs/p0/P0-INPUT-BACKEND-REPORT.md.'
    Write-Host ''
    Write-Host 'P5B WeChat read-only discovery:'
    Write-Host '   .\scripts\p5b-wechat-probe.ps1'
}
finally {
    Pop-Location
}
