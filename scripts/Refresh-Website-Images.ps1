param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Website-ImageState.ps1')

if (-not $SkipBuild) { & (Join-Path $root 'build.ps1') }
& (Join-Path $PSScriptRoot 'Inspect-UI.ps1') -ExecutablePath (Join-Path $root 'dist\Vanta Auto Clicker.exe')

$images = Join-Path $root 'website\public\images'
New-Item -ItemType Directory -Force -Path $images | Out-Null
foreach ($name in @('vanta-default.png','vanta-advanced.png','vanta-settings.png','vanta-test-pad.png')) {
    Copy-Item -LiteralPath (Join-Path $root "artifacts\$name") -Destination (Join-Path $images $name) -Force
}
$logo = Join-Path $root 'assets\Vanta_Logo.png'
Copy-Item -LiteralPath $logo -Destination (Join-Path $images 'vanta-logo.png') -Force
Copy-Item -LiteralPath $logo -Destination (Join-Path $root 'website\public\og.png') -Force
Copy-Item -LiteralPath (Join-Path $root 'assets\Vanta.ico') -Destination (Join-Path $root 'website\public\favicon.ico') -Force

$stamp = Get-VantaWebsiteImageStamp -ProjectRoot $root
[System.IO.File]::WriteAllText((Join-Path $images 'app-preview-source.sha256'),$stamp + [Environment]::NewLine,[System.Text.UTF8Encoding]::new($false))
Write-Output "Website app images refreshed for Vanta $(Get-VantaAppShortVersion -ProjectRoot $root)."
