[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("document-active", "auto-hide-rest", "auto-hide-open")]
    [string]$Scene
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$artifactsRoot = Join-Path $repoRoot "artifacts\parity"
$statePath = Join-Path $artifactsRoot "session.json"

if (!(Test-Path $statePath)) {
    throw "Parity session not found. Run tools/parity/launch-baselines.ps1 first."
}

$state = Get-Content $statePath | ConvertFrom-Json
$sceneDir = Join-Path $artifactsRoot $Scene
New-Item -ItemType Directory -Force -Path $sceneDir | Out-Null

function Invoke-AgentAction {
    param(
        [string]$BaseUrl,
        [string]$ActionName,
        [object[]]$Arguments = @()
    )

    $payload = @{ Args = @($Arguments) } | ConvertTo-Json -Depth 4 -Compress
    $response = Invoke-RestMethod -Uri "$BaseUrl/api/v1/invoke/actions/$ActionName" `
        -Method Post `
        -ContentType "application/json" `
        -Body $payload

    if ($null -ne $response.returnValue) {
        return $response.returnValue
    }

    return ($response | ConvertTo-Json -Depth 5 -Compress)
}

function Get-AgentStatus {
    param([string]$BaseUrl)

    Invoke-RestMethod -Uri "$BaseUrl/api/v1/agent/status"
}

function Save-AgentScreenshot {
    param(
        [string]$BaseUrl,
        [string]$OutputPath
    )

    $response = Invoke-WebRequest -Uri "$BaseUrl/api/v1/ui/screenshot" -UseBasicParsing
    $output = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
    try {
        $response.RawContentStream.CopyTo($output)
    } finally {
        $output.Dispose()
    }
}

function Prepare-Scene {
    param(
        [string]$BaseUrl,
        [string]$TargetName
    )

    $log = @()
    switch ($Scene) {
        "document-active" {
            $log += "${TargetName}: " + (Invoke-AgentAction -BaseUrl $BaseUrl -ActionName "dock-active-content")
        }
        "auto-hide-rest" {
            $toggleResult = Invoke-AgentAction -BaseUrl $BaseUrl -ActionName "dock-toggle-autohide" -Arguments @("solution-explorer")
            $log += "${TargetName}: $toggleResult"
            if ($toggleResult -match "now isAutoHidden=False") {
                $toggleResult = Invoke-AgentAction -BaseUrl $BaseUrl -ActionName "dock-toggle-autohide" -Arguments @("solution-explorer")
                $log += "${TargetName}: $toggleResult"
            }
        }
        "auto-hide-open" {
            $toggleResult = Invoke-AgentAction -BaseUrl $BaseUrl -ActionName "dock-toggle-autohide" -Arguments @("solution-explorer")
            $log += "${TargetName}: $toggleResult"
            if ($toggleResult -match "now isAutoHidden=False") {
                $toggleResult = Invoke-AgentAction -BaseUrl $BaseUrl -ActionName "dock-toggle-autohide" -Arguments @("solution-explorer")
                $log += "${TargetName}: $toggleResult"
            }
            Start-Sleep -Milliseconds 500
            $log += "${TargetName}: " + (Invoke-AgentAction -BaseUrl $BaseUrl -ActionName "dock-open-flyout" -Arguments @("solution-explorer"))
        }
    }

    return $log
}

$actions = @()
$actions += Prepare-Scene -BaseUrl $state.uno.baseUrl -TargetName "uno"
$actions += Prepare-Scene -BaseUrl $state.wpf.baseUrl -TargetName "wpf"
Start-Sleep -Milliseconds 800

$unoStatus = Get-AgentStatus -BaseUrl $state.uno.baseUrl
$wpfStatus = Get-AgentStatus -BaseUrl $state.wpf.baseUrl

Save-AgentScreenshot -BaseUrl $state.uno.baseUrl -OutputPath (Join-Path $sceneDir "uno.png")
Save-AgentScreenshot -BaseUrl $state.wpf.baseUrl -OutputPath (Join-Path $sceneDir "wpf.png")

$status = [ordered]@{
    scene = $Scene
    capturedAt = (Get-Date).ToString("o")
    uno = $unoStatus
    wpf = $wpfStatus
}

$status | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $sceneDir "status.json")
$actions | Set-Content -Path (Join-Path $sceneDir "actions.log")

$status | ConvertTo-Json -Depth 8
