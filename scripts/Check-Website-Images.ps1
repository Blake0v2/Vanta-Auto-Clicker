$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Website-ImageState.ps1')
$images = Join-Path $root 'website\public\images'
$stampPath = Join-Path $images 'app-preview-source.sha256'

if (-not (Test-Path -LiteralPath $stampPath)) {
    throw 'Website previews have no source stamp. Run scripts\Refresh-Website-Images.ps1.'
}
$expectedStamp = Get-VantaWebsiteImageStamp -ProjectRoot $root
$actualStamp = [System.IO.File]::ReadAllText($stampPath).Trim()
if ($actualStamp -ne $expectedStamp) {
    throw 'Website previews are stale. Run scripts\Refresh-Website-Images.ps1 after changing the app.'
}

$pngSignature = @(137,80,78,71,13,10,26,10)
foreach ($name in @('vanta-default.png','vanta-advanced.png','vanta-settings.png','vanta-test-pad.png','vanta-logo.png')) {
    $path = Join-Path $images $name
    if (-not (Test-Path -LiteralPath $path)) { throw "Website image is missing: $name" }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 1024) { throw "Website image is unexpectedly small: $name" }
    for ($index = 0; $index -lt $pngSignature.Count; $index++) {
        if ($bytes[$index] -ne $pngSignature[$index]) { throw "Website image is not a valid PNG: $name" }
    }
}

$sourceLogoHash = (Get-FileHash -LiteralPath (Join-Path $root 'assets\Vanta_Logo.png') -Algorithm SHA256).Hash
$websiteLogoHash = (Get-FileHash -LiteralPath (Join-Path $images 'vanta-logo.png') -Algorithm SHA256).Hash
$openGraphHash = (Get-FileHash -LiteralPath (Join-Path $root 'website\public\og.png') -Algorithm SHA256).Hash
if ($sourceLogoHash -ne $websiteLogoHash -or $sourceLogoHash -ne $openGraphHash) {
    throw 'The website logo does not match assets\Vanta_Logo.png. Run scripts\Refresh-Website-Images.ps1.'
}
$sourceIconHash = (Get-FileHash -LiteralPath (Join-Path $root 'assets\Vanta.ico') -Algorithm SHA256).Hash
$websiteIconHash = (Get-FileHash -LiteralPath (Join-Path $root 'website\public\favicon.ico') -Algorithm SHA256).Hash
if ($sourceIconHash -ne $websiteIconHash) {
    throw 'The website favicon does not match assets\Vanta.ico. Run scripts\Refresh-Website-Images.ps1.'
}

$version = Get-VantaAppShortVersion -ProjectRoot $root
$page = [System.IO.File]::ReadAllText((Join-Path $root 'website\app\page.tsx'))
if ($page -notmatch ("const APP_PREVIEW_VERSION = '" + [System.Text.RegularExpressions.Regex]::Escape($version) + "';")) {
    throw "The website preview label does not match app version $version."
}
Write-Output "Website previews and logo match Vanta $version."
