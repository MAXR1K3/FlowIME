[CmdletBinding()]
param(
    [ValidateRange(1000, 10000)]
    [int]$DelayMs = 4000,

    [ValidateRange(3, 20)]
    [int]$Samples = 5,

    [ValidateRange(25, 1000)]
    [int]$IntervalMs = 100
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($env:OS -ne 'Windows_NT') {
    throw 'The WeChat IME probe must run on Windows.'
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$probeRoot = Join-Path $env:LOCALAPPDATA 'FlowIME\probe'
$chinesePath = Join-Path $probeRoot 'wechat-chinese.json'
$englishPath = Join-Path $probeRoot 'wechat-english.json'

New-Item -ItemType Directory -Force -Path $probeRoot | Out-Null

function Invoke-Capture {
    param(
        [Parameter(Mandatory)]
        [string]$Label,

        [Parameter(Mandatory)]
        [string]$OutputPath
    )

    & dotnet run --project 'src/FlowIME.Probe' -- `
        capture --foreground `
        --label $Label `
        --delay-ms $DelayMs `
        --samples $Samples `
        --interval-ms $IntervalMs `
        --out $OutputPath

    if ($LASTEXITCODE -ne 0) {
        throw "Probe capture '$Label' failed with exit code $LASTEXITCODE."
    }
}

Push-Location $root
try {
    Write-Host '== FlowIME P5B WeChat IME discovery ==' -ForegroundColor Cyan
    Write-Host 'This stage is READ-ONLY. It will not send IME mutation commands.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'Use a simple text host such as Notepad for the first matrix.'
    Write-Host 'Before each capture, make sure WeChat Input Method itself is the active IME.'
    Write-Host ''

    Read-Host '1/2 Put WeChat Input Method in CHINESE mode. Press Enter here, then switch to the target text field when the probe gives you the countdown'
    Invoke-Capture -Label 'wechat-chinese' -OutputPath $chinesePath

    Write-Host ''
    Read-Host '2/2 Put WeChat Input Method in ENGLISH mode. Press Enter here, then switch to the SAME target text field when the probe gives you the countdown'
    Invoke-Capture -Label 'wechat-english' -OutputPath $englishPath

    Write-Host ''
    Write-Host '== Comparison ==' -ForegroundColor Cyan
    & dotnet run --project 'src/FlowIME.Probe' -- `
        compare-captures `
        --left $chinesePath `
        --right $englishPath

    $comparisonExit = $LASTEXITCODE
    Write-Host ''
    Write-Host "Chinese capture: $chinesePath"
    Write-Host "English capture: $englishPath"
    Write-Host 'Send the comparison output to ChatGPT. If requested, also upload the two JSON files.' -ForegroundColor Green

    if ($comparisonExit -ne 0) {
        Write-Warning 'The comparison says the capture was invalid, unstable, or changed TSF profiles. Repeat the matrix before any write experiment.'
        exit $comparisonExit
    }
}
finally {
    Pop-Location
}
