$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Invoke-WeChatMutation {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('chinese', 'english')]
        [string]$TargetMode,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Chinese', 'English')]
        [string]$StartMode
    )

    Write-Host ''
    Write-Host "=== WeChat mutation candidate: $StartMode -> $TargetMode ===" -ForegroundColor Cyan
    Write-Host "1. Open Notepad and focus the SAME normal text field used for read-only discovery."
    Write-Host "2. Select WeChat Input Method."
    Write-Host "3. Manually put WeChat in $StartMode mode."
    Write-Host "4. Return here and press Enter."
    [void](Read-Host)

    Write-Host "After pressing Enter, you have 4 seconds to Alt+Tab back to Notepad." -ForegroundColor Yellow
    Write-Host "Do not click elsewhere while the probe verifies foreground/focus/profile stability." -ForegroundColor Yellow

    & dotnet run --project .\src\FlowIME.Probe\FlowIME.Probe.csproj -- `
        set-open --foreground --mode $TargetMode --delay-ms 4000

    if ($LASTEXITCODE -ne 0) {
        throw "WeChat $TargetMode mutation candidate failed with exit code $LASTEXITCODE. Stop P5B and send the full output to ChatGPT."
    }

    Write-Host ''
    Write-Host "The native read-back passed. Now manually TYPE in Notepad without toggling the IME." -ForegroundColor Green
    Write-Host "Confirm that WeChat actually behaves as $TargetMode (candidate window/text behavior), then return here."
    $answer = Read-Host "Visible/input behavior matches $TargetMode? [y/n]"
    if ($answer -notmatch '^(?i:y|yes)$') {
        throw "Manual visual/input confirmation failed for target mode '$TargetMode'. Do not integrate a provider."
    }
}

Write-Host 'FlowIME P5B-2 - WeChat Input Method open-status mutation probe' -ForegroundColor Cyan
Write-Host 'This stage DOES mutate IME open status, but only after exact WeChat TSF-profile verification.' -ForegroundColor Yellow
Write-Host 'It never sends text, hotkeys, or simulated keyboard input.' -ForegroundColor Yellow

Invoke-WeChatMutation -StartMode 'English' -TargetMode 'chinese'
Invoke-WeChatMutation -StartMode 'Chinese' -TargetMode 'english'

Write-Host ''
Write-Host 'P5B-2 candidate PASS: both Open and Closed transitions passed native read-back and manual confirmation.' -ForegroundColor Green
Write-Host 'Send the full two mutation outputs (or screenshots) to ChatGPT before provider integration.' -ForegroundColor Green
