[CmdletBinding()]
param(
    [int]$UnoPort = 9223,
    [int]$WpfPort = 9224,
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$artifactsRoot = Join-Path $repoRoot "artifacts\parity"
$statePath = Join-Path $artifactsRoot "session.json"

function Wait-AgentReady {
    param(
        [string]$BaseUrl,
        [int]$TimeoutSeconds = 45
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            return Invoke-RestMethod -Uri "$BaseUrl/api/v1/agent/status" -TimeoutSec 5
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }

    throw "Timed out waiting for $BaseUrl/api/v1/agent/status"
}

function Invoke-Checked {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE"
    }
}

function Start-DevFlowProcess {
    param(
        [string]$FilePath,
        [string]$WorkingDirectory,
        [int]$Port
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.EnvironmentVariables["DEVFLOW_AGENT_PORT"] = "$Port"

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    $null = $process.Start()
    return $process
}

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null

Invoke-Checked -FilePath "dotnet" -Arguments @("build", "src\UnoDock.Sample\UnoDock.Sample.csproj", "-f", "net10.0-desktop", "-c", $Configuration)
Invoke-Checked -FilePath "dotnet" -Arguments @("build", "externals\AvalonDock\sample\AvalonDock.Sample.csproj", "-c", $Configuration)

$unoExe = Join-Path $repoRoot "src\UnoDock.Sample\bin\$Configuration\net10.0-desktop\UnoDock.Sample.exe"
$wpfExe = Join-Path $repoRoot "externals\AvalonDock\sample\bin\$Configuration\net8.0-windows\AvalonDock.Sample.exe"

if (!(Test-Path $unoExe)) {
    throw "Uno sample EXE not found: $unoExe"
}

if (!(Test-Path $wpfExe)) {
    throw "WPF sample EXE not found: $wpfExe"
}

$unoProcess = Start-DevFlowProcess -FilePath $unoExe -WorkingDirectory (Split-Path $unoExe) -Port $UnoPort
$wpfProcess = Start-DevFlowProcess -FilePath $wpfExe -WorkingDirectory (Split-Path $wpfExe) -Port $WpfPort

$unoStatus = Wait-AgentReady -BaseUrl "http://127.0.0.1:$UnoPort"
$wpfStatus = Wait-AgentReady -BaseUrl "http://127.0.0.1:$WpfPort"

$state = [ordered]@{
    startedAt = (Get-Date).ToString("o")
    uno = @{
        pid = $unoProcess.Id
        port = $UnoPort
        exe = $unoExe
        baseUrl = "http://127.0.0.1:$UnoPort"
        framework = $unoStatus.framework
    }
    wpf = @{
        pid = $wpfProcess.Id
        port = $WpfPort
        exe = $wpfExe
        baseUrl = "http://127.0.0.1:$WpfPort"
        framework = $wpfStatus.framework
    }
}

$state | ConvertTo-Json -Depth 6 | Set-Content -Path $statePath
$state | ConvertTo-Json -Depth 6
