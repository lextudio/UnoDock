[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$statePath = Join-Path $repoRoot "artifacts\parity\session.json"

if (Test-Path $statePath) {
    $state = Get-Content $statePath | ConvertFrom-Json

    foreach ($entry in @($state.uno, $state.wpf)) {
        if ($null -eq $entry) {
            continue
        }

        try {
            $process = Get-Process -Id $entry.pid -ErrorAction Stop
            if (!$process.HasExited) {
                Stop-Process -Id $entry.pid -Force
            }
        } catch {
        }
    }
} else {
    Write-Output "No parity session found."
}

foreach ($processName in @("UnoDock.Sample", "AvalonDock.Sample")) {
    Get-Process -Name $processName -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            Stop-Process -Id $_.Id -Force
        } catch {
        }
    }
}

Remove-Item $statePath -Force -ErrorAction SilentlyContinue
Write-Output "Stopped parity baseline apps."
