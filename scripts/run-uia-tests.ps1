# Runs the FlaUI UI-automation suite without ever blocking the calling shell.
#
# Anti-hang contract:
#   - test process runs in the BACKGROUND with output redirected to files
#   - the caller polls liveness every 500 ms
#   - a hard deadline (default 300 s) kills the whole process tree
#   - last stdout/stderr lines are printed for diagnosis either way
#
# Usage:
#   pwsh scripts/run-uia-tests.ps1 [-TimeoutSeconds 300] [-Configuration Debug]
#
# NOTE: this opens the real application window on THIS machine - by design,
# and only because whoever invoked it asked for UI tests explicitly.

param(
    [int]$TimeoutSeconds = 300,
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "..\tests\TaskManager.UiAutomationTests\TaskManager.UiAutomationTests.csproj"

Write-Host "Building $Configuration ..."
dotnet build $project -c $Configuration --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "build failed ($LASTEXITCODE)"; exit $LASTEXITCODE }

$exe = Join-Path $PSScriptRoot "..\tests\TaskManager.UiAutomationTests\bin\$Configuration\net10.0-windows\TaskManager.UiAutomationTests.exe"
if (-not (Test-Path $exe)) { Write-Error "test exe not found at $exe"; exit 1 }

$out = Join-Path $env:TEMP "uia-out.log"
$err = Join-Path $env:TEMP "uia-err.log"
Remove-Item $out, $err -ErrorAction SilentlyContinue

# MTP rejects --nologo/--timeout on this version; the poll loop IS the timeout.
$proc = Start-Process -FilePath $exe -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -NoNewWindow
$sw = [Diagnostics.Stopwatch]::StartNew()
$maxAppInstances = 0

while (-not $proc.HasExited -and $sw.Elapsed.TotalSeconds -lt $TimeoutSeconds)
{
    $maxAppInstances = [Math]::Max($maxAppInstances, @(Get-Process -Name TaskManager -ErrorAction SilentlyContinue).Count)
    Start-Sleep -Milliseconds 500
}

if (-not $proc.HasExited)
{
    Write-Warning "HARD-KILLED after $($sw.Elapsed.TotalSeconds)s (deadline $TimeoutSeconds s); max concurrent app instances: $maxAppInstances"
    $proc.Kill($true)
    Start-Sleep -Milliseconds 800
    Get-Content $out -Tail 40 -ErrorAction SilentlyContinue
    Get-Content $err -Tail 20 -ErrorAction SilentlyContinue
    exit 124
}

Write-Host "exit=$($proc.ExitCode) elapsed=$([int]$sw.Elapsed.TotalSeconds)s maxConcurrentAppInstances=$maxAppInstances"
Get-Content $out -Tail 25 -ErrorAction SilentlyContinue

$errText = Get-Content $err -Raw -ErrorAction SilentlyContinue
if ($errText) { Write-Host "--- stderr ---"; Write-Host $errText }

exit $proc.ExitCode
