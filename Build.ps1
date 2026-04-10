<#
.SYNOPSIS
    Build splashshell. Stops any running splash.exe first.
.DESCRIPTION
    The csproj publishes as NativeAOT (PublishAot=true). -Publish runs dotnet publish
    which generates a self-contained native exe in ./dist and also mirrors it to
    ./npm/dist for the npm package.
.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Debug.
.PARAMETER Publish
    Run dotnet publish instead of build. Produces a NativeAOT single native exe.
.EXAMPLE
    .\Build.ps1
.EXAMPLE
    .\Build.ps1 -Publish -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ProjectRoot = $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot 'splashshell.csproj'
$DistDir = Join-Path $ProjectRoot 'dist'
$NpmDistDir = Join-Path $ProjectRoot 'npm\dist'

Write-Host '=== splashshell Build ===' -ForegroundColor Cyan

# Step 1: Stop running splashshell processes
Write-Host "`n[1/2] Stopping running splash.exe processes..." -ForegroundColor Yellow
$processes = @(Get-Process -Name 'splash' -ErrorAction Ignore)
if ($processes.Count -gt 0) {
    $processes | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    Write-Host "      Stopped $($processes.Count) process(es)." -ForegroundColor Green
} else {
    Write-Host '      No running processes found.' -ForegroundColor DarkGray
}

# Step 2: Build or publish
if ($Publish) {
    Write-Host "`n[2/2] Publishing ($Configuration, NativeAOT)..." -ForegroundColor Yellow
    dotnet publish $ProjectFile -c $Configuration -r win-x64 -o $DistDir
    if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

    # Mirror to npm/dist so the npm package ships the fresh binary
    $src = Join-Path $DistDir 'splash.exe'
    $dst = Join-Path $NpmDistDir 'splash.exe'
    if (Test-Path $src) {
        New-Item -ItemType Directory -Force -Path $NpmDistDir | Out-Null
        Copy-Item $src $dst -Force
        Write-Host "      Copied to $dst" -ForegroundColor DarkGray
    }

    Write-Host "`n=== Published to $DistDir ===" -ForegroundColor Green
} else {
    Write-Host "`n[2/2] Building ($Configuration)..." -ForegroundColor Yellow
    dotnet build $ProjectFile -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
    Write-Host "`n=== Build succeeded ===" -ForegroundColor Green
}
