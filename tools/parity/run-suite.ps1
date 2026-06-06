[CmdletBinding()]
param(
    [string[]]$Scenes = @("document-active", "auto-hide-rest", "auto-hide-open"),
    [int]$Threshold = 12
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$artifactsRoot = Join-Path $repoRoot "artifacts\parity"
$reportPath = Join-Path $artifactsRoot "report.md"
$htmlReportPath = Join-Path $artifactsRoot "report.html"

& (Join-Path $PSScriptRoot "stop-baselines.ps1") | Out-Host
& (Join-Path $PSScriptRoot "launch-baselines.ps1") | Out-Host

try {
    $results = @()
    foreach ($scene in $Scenes) {
        & (Join-Path $PSScriptRoot "capture-scene.ps1") -Scene $scene | Out-Host
        $metricsJson = & (Join-Path $PSScriptRoot "diff-scene.ps1") -Scene $scene -Threshold $Threshold
        $metrics = $metricsJson | ConvertFrom-Json
        $results += $metrics
    }

    $lines = @()
    $lines += "# UnoDock VS2013 Parity Report"
    $lines += ""
    $lines += "- Generated: $(Get-Date -Format o)"
    $lines += "- Threshold: $Threshold per RGB channel"
    $lines += "- WPF crop: content rectangle offset `(0,5`)"
    $lines += ""
    $lines += "| Scene | Uno Size | WPF Size | Compared | Different | Avg Delta | Max Delta | Artifacts |"
    $lines += "| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |"

    foreach ($result in $results) {
        $scene = $result.scene
        $unoSize = "$($result.unoWidth)x$($result.unoHeight)"
        $wpfSize = "$($result.wpfWidth)x$($result.wpfHeight)"
        $compared = "$($result.comparedWidth)x$($result.comparedHeight)"
        $different = "$($result.percentDifferent)%"
        $artifacts = "[folder](./$scene/)"
        $lines += "| $scene | $unoSize | $wpfSize | $compared | $different | $($result.averageDeltaChanged) | $($result.maxDelta) | $artifacts |"
    }

    $lines += ""
    $lines += 'Each scene folder contains `uno.png`, `wpf.png`, `diff.png`, `diff.json`, `status.json`, and `actions.log`.'
    $lines | Set-Content -Path $reportPath

    $html = @()
    $html += "<!doctype html>"
    $html += "<html><head><meta charset=`"utf-8`"><title>UnoDock Parity Report</title>"
    $html += "<style>"
    $html += "body{font-family:Segoe UI,Arial,sans-serif;margin:24px;background:#f5f5f5;color:#1f1f1f}"
    $html += "h1{font-size:22px;margin:0 0 8px}"
    $html += "section{margin:24px 0;padding:16px;background:#fff;border:1px solid #ddd}"
    $html += "h2{font-size:16px;margin:0 0 12px}"
    $html += ".metrics{font-size:13px;margin-bottom:12px;color:#444}"
    $html += ".grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}"
    $html += "figure{margin:0}"
    $html += "figcaption{font-size:12px;margin-bottom:6px;color:#555}"
    $html += "img{max-width:100%;height:auto;border:1px solid #bbb;background:#222}"
    $html += "</style></head><body>"
    $html += "<h1>UnoDock VS2013 Parity Report</h1>"
    $html += "<p>Generated $(Get-Date -Format o). Threshold $Threshold per RGB channel.</p>"

    foreach ($result in $results) {
        $scene = $result.scene
        $html += "<section>"
        $html += "<h2>$scene</h2>"
        $html += "<div class=`"metrics`">Uno $($result.unoWidth)x$($result.unoHeight), WPF $($result.wpfWidth)x$($result.wpfHeight), different $($result.percentDifferent)%, avg delta $($result.averageDeltaChanged), max delta $($result.maxDelta)</div>"
        $html += "<div class=`"grid`">"
        $html += "<figure><figcaption>Uno</figcaption><img src=`"$scene/uno.png`"></figure>"
        $html += "<figure><figcaption>WPF</figcaption><img src=`"$scene/wpf.png`"></figure>"
        $html += "<figure><figcaption>Diff</figcaption><img src=`"$scene/diff.png`"></figure>"
        $html += "</div>"
        $html += "</section>"
    }

    $html += "</body></html>"
    $html | Set-Content -Path $htmlReportPath

    Get-Content $reportPath
} finally {
    & (Join-Path $PSScriptRoot "stop-baselines.ps1") | Out-Host
}
