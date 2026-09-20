[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',

    [switch]$SkipTests,

    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repoRoot 'artifacts'
}

$project = Join-Path $repoRoot 'src\FlowIME.App\FlowIME.App.csproj'
$solution = Join-Path $repoRoot 'FlowIME.sln'
$publishDir = Join-Path $OutputRoot "publish\$Runtime"
$installerDir = Join-Path $OutputRoot 'installer'
$issPath = Join-Path $PSScriptRoot 'FlowIME.iss'

$version = (& dotnet msbuild $project -nologo -getProperty:Version "-p:Configuration=$Configuration").Trim()
if ($LASTEXITCODE -ne 0 -or $version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
    throw "Could not read a valid project version. Actual value: '$version'"
}

$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
    (Join-Path ${env:ProgramFiles} 'Inno Setup 7\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe')
)
$iscc = $isccCandidates | Where-Object { $_ -and (Test-Path $_ -PathType Leaf) } | Select-Object -First 1
if (-not $iscc) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $iscc = $command.Source }
}
if (-not $iscc) {
    throw 'Inno Setup 7 was not found. Install Inno Setup 7.x and retry.'
}

New-Item -ItemType Directory -Force -Path $publishDir, $installerDir | Out-Null

if (-not $SkipTests) {
    & dotnet build $solution -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed; release stopped.' }

    $testProjects = Get-ChildItem -Path (Join-Path $repoRoot 'tests') -Recurse -Filter '*.Tests.csproj' -File
    foreach ($testProject in $testProjects) {
        $targetFramework = (& dotnet msbuild $testProject.FullName -nologo -getProperty:TargetFramework).Trim()
        $assemblyName = (& dotnet msbuild $testProject.FullName -nologo -getProperty:AssemblyName).Trim()
        $testExecutable = Join-Path $testProject.DirectoryName "bin\$Configuration\$targetFramework\$assemblyName.exe"
        if (-not (Test-Path $testExecutable -PathType Leaf)) {
            throw "Test executable is missing: $testExecutable"
        }

        & $testExecutable -noLogo
        if ($LASTEXITCODE -ne 0) {
            throw "Tests failed: $($testProject.Name)"
        }
    }
}

& dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishDir `
    --nologo
if ($LASTEXITCODE -ne 0) { throw 'FlowIME publish failed.' }

$publishedExe = Join-Path $publishDir 'FlowIME.App.exe'
if (-not (Test-Path $publishedExe -PathType Leaf)) {
    throw "Published executable is missing: $publishedExe"
}
if (-not (Test-Path (Join-Path $publishDir 'FlowIME.App.pri') -PathType Leaf)) {
    throw 'FlowIME.App.pri is missing; the WinUI publish is incomplete.'
}
if (-not (Get-ChildItem -Path $publishDir -Recurse -Filter *.xbf -File | Select-Object -First 1)) {
    throw 'XBF resources are missing; the WinUI publish is incomplete.'
}

& $iscc `
    "--define=AppVersion=$version" `
    "--define=PublishDir=$publishDir" `
    "--define=InstallerOutputDir=$installerDir" `
    $issPath
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

$installer = Join-Path $installerDir "FlowIME-Setup-$version-x64.exe"
if (-not (Test-Path $installer -PathType Leaf)) {
    throw "Expected installer was not produced: $installer"
}

$hash = (Get-FileHash -Algorithm SHA256 -Path $installer).Hash
Write-Host ""
Write-Host "FlowIME installer produced" -ForegroundColor Green
Write-Host "Path:    $installer"
Write-Host "Version: $version"
Write-Host "SHA256:  $hash"
