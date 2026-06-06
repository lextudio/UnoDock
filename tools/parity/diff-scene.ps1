[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Scene,
    [int]$Threshold = 12,
    [int]$WpfOffsetX = 0,
    [int]$WpfOffsetY = 5
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sceneDir = Join-Path $repoRoot "artifacts\parity\$Scene"
$unoPath = Join-Path $sceneDir "uno.png"
$wpfPath = Join-Path $sceneDir "wpf.png"
$diffPath = Join-Path $sceneDir "diff.png"
$metricsPath = Join-Path $sceneDir "diff.json"

if (!(Test-Path $unoPath)) {
    throw "Uno screenshot not found: $unoPath"
}

if (!(Test-Path $wpfPath)) {
    throw "WPF screenshot not found: $wpfPath"
}

Add-Type -AssemblyName System.Drawing

$uno = [System.Drawing.Bitmap]::new($unoPath)
$wpf = [System.Drawing.Bitmap]::new($wpfPath)

try {
    $width = [Math]::Min($uno.Width, $wpf.Width - $WpfOffsetX)
    $height = [Math]::Min($uno.Height, $wpf.Height - $WpfOffsetY)
    $diff = [System.Drawing.Bitmap]::new($width, $height)

    $differentPixels = 0L
    $sumDelta = 0L
    $maxDelta = 0
    $maxDeltaX = 0
    $maxDeltaY = 0

    for ($y = 0; $y -lt $height; $y++) {
        for ($x = 0; $x -lt $width; $x++) {
            $a = $uno.GetPixel($x, $y)
            $b = $wpf.GetPixel($x + $WpfOffsetX, $y + $WpfOffsetY)

            $dr = [Math]::Abs([int]$a.R - [int]$b.R)
            $dg = [Math]::Abs([int]$a.G - [int]$b.G)
            $db = [Math]::Abs([int]$a.B - [int]$b.B)
            $delta = $dr + $dg + $db

            if ($delta -gt $maxDelta) {
                $maxDelta = $delta
                $maxDeltaX = $x
                $maxDeltaY = $y
            }

            if ($dr -gt $Threshold -or $dg -gt $Threshold -or $db -gt $Threshold) {
                $differentPixels++
                $sumDelta += $delta

                $heat = [Math]::Min(255, [int]($delta / 3))
                $diff.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, 255, (255 - $heat), (255 - $heat)))
            } else {
                $gray = [int](($a.R + $a.G + $a.B) / 12) + 24
                $diff.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $gray, $gray, $gray))
            }
        }
    }

    $comparedPixels = [long]$width * [long]$height
    $percentDifferent = if ($comparedPixels -eq 0) { 0 } else { [Math]::Round(($differentPixels * 100.0) / $comparedPixels, 4) }
    $averageDeltaChanged = if ($differentPixels -eq 0) { 0 } else { [Math]::Round($sumDelta / [double]$differentPixels, 2) }

    $diff.Save($diffPath, [System.Drawing.Imaging.ImageFormat]::Png)

    $metrics = [ordered]@{
        scene = $Scene
        threshold = $Threshold
        comparedWidth = $width
        comparedHeight = $height
        wpfOffsetX = $WpfOffsetX
        wpfOffsetY = $WpfOffsetY
        unoWidth = $uno.Width
        unoHeight = $uno.Height
        wpfWidth = $wpf.Width
        wpfHeight = $wpf.Height
        sizeMismatch = ($uno.Width -ne $wpf.Width) -or ($uno.Height -ne $wpf.Height)
        comparedPixels = $comparedPixels
        differentPixels = $differentPixels
        percentDifferent = $percentDifferent
        averageDeltaChanged = $averageDeltaChanged
        maxDelta = $maxDelta
        maxDeltaX = $maxDeltaX
        maxDeltaY = $maxDeltaY
        diffPath = $diffPath
    }

    $metrics | ConvertTo-Json -Depth 5 | Set-Content -Path $metricsPath
    $metrics | ConvertTo-Json -Depth 5
} finally {
    if ($null -ne $diff) {
        $diff.Dispose()
    }

    $uno.Dispose()
    $wpf.Dispose()
}
