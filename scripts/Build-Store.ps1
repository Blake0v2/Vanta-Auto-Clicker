param(
    [string]$IdentityPath,
    [switch]$Preview,
    [string]$SdkBinDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Preview identities are explicitly local placeholders, never Store submission identities.
if ($Preview -and $IdentityPath) { throw 'Use either -Preview or -IdentityPath, not both.' }
if ($Preview) {
    $identity = [PSCustomObject]@{
        Name = 'Vanta.LocalPackagingPreview'
        Publisher = 'CN=Vanta Local Packaging Preview'
        PublisherDisplayName = 'Local packaging preview'
        DisplayName = 'Vanta Auto Clicker'
    }
} else {
    if (-not $IdentityPath) { throw 'Supply -IdentityPath with Partner Center product identity, or use -Preview for local packaging validation only.' }
    $identity = Get-Content -LiteralPath $IdentityPath -Raw | ConvertFrom-Json
}
foreach ($field in @('Name','Publisher','PublisherDisplayName','DisplayName')) {
    $value = $identity.$field
    if ($value -isnot [string] -or [String]::IsNullOrWhiteSpace($value) -or $value -match 'REPLACE_WITH|\{\{|\}\}') {
        throw "Identity field '$field' is missing or still a placeholder. Copy the actual value from Partner Center."
    }
}
if ($identity.Name -notmatch '^[A-Za-z0-9.-]{3,50}$') { throw 'Package identity Name must be 3-50 letters, digits, periods, or hyphens.' }
if ($identity.Publisher -notmatch '^CN=') { throw 'Publisher must be the complete certificate subject from Partner Center, starting with CN=.' }
if (-not $Preview -and ($identity.Name -eq 'Vanta.LocalPackagingPreview' -or $identity.Publisher -eq 'CN=Vanta Local Packaging Preview')) {
    throw 'The local preview identity cannot be used for a Store submission.'
}

if (-not $SdkBinDirectory) {
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $sdk = Get-ChildItem -LiteralPath $sdkRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^10\.0\.\d+\.0$' } |
        Sort-Object { [Version]$_.Name } -Descending |
        Where-Object { (Test-Path -LiteralPath (Join-Path $_.FullName 'x64\makeappx.exe')) -and (Test-Path -LiteralPath (Join-Path $_.FullName 'x64\makepri.exe')) } |
        Select-Object -First 1
    if (-not $sdk) { throw 'Install the Windows SDK packaging tools (MakeAppx and MakePri), or supply -SdkBinDirectory.' }
    $SdkBinDirectory = Join-Path $sdk.FullName 'x64'
}
$makeAppx = Join-Path $SdkBinDirectory 'makeappx.exe'
$makePri = Join-Path $SdkBinDirectory 'makepri.exe'
foreach ($tool in @($makeAppx,$makePri)) {
    if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw "Missing Windows SDK tool: $tool" }
}

# Fresh, project-owned staging prevents old installers or unrelated files entering the package.
$work = Join-Path $root ('build\store\' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $work 'package'
$assets = Join-Path $stage 'Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
& (Join-Path $root 'build.ps1') -Store -Test -OutputDirectory $stage
$app = Join-Path $stage 'Vanta Auto Clicker.exe'
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductVersion
$parsedVersion = [Version]$version
if ($parsedVersion.Revision -ne 0) { throw 'The fourth version component must be zero for Microsoft Store submissions.' }

$template = [System.IO.File]::ReadAllText((Join-Path $root 'store\AppxManifest.template.xml'))
$replacements = @{
    NAME = $identity.Name
    PUBLISHER = $identity.Publisher
    VERSION = $version
    DISPLAY_NAME = $identity.DisplayName
    PUBLISHER_DISPLAY_NAME = $identity.PublisherDisplayName
}
foreach ($key in $replacements.Keys) {
    $template = $template.Replace(('{{' + $key + '}}'), [System.Security.SecurityElement]::Escape($replacements[$key]))
}
$utf8 = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'),$template,$utf8)
Copy-Item -LiteralPath (Join-Path $root 'assets\fonts\OFL.txt') -Destination (Join-Path $stage 'Paytone-One-OFL.txt')
Copy-Item -LiteralPath (Join-Path $root 'store\USER-GUIDE.txt') -Destination (Join-Path $stage 'USER-GUIDE.txt')
[System.IO.File]::WriteAllText((Join-Path $stage 'Vanta Auto Clicker.exe.config'),
    '<?xml version="1.0" encoding="utf-8"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" /></startup></configuration>',$utf8)

# Resize the existing brand asset; preserve its aspect ratio and transparency.
Add-Type -AssemblyName System.Drawing
$logo = [System.Drawing.Image]::FromFile((Join-Path $root 'assets\Vanta_Logo.png'))
try {
    foreach ($asset in @(@{Name='StoreLogo';Size=50},@{Name='Square44x44Logo';Size=44},@{Name='Square150x150Logo';Size=150})) {
        foreach ($scale in @(100,200,400)) {
            $size = [int]($asset.Size * $scale / 100)
            $bitmap = [System.Drawing.Bitmap]::new($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $ratio = [Math]::Min($size / $logo.Width,$size / $logo.Height)
                $width = [int][Math]::Round($logo.Width * $ratio)
                $height = [int][Math]::Round($logo.Height * $ratio)
                $graphics.DrawImage($logo,[int](($size-$width)/2),[int](($size-$height)/2),$width,$height)
                $bitmap.Save((Join-Path $assets ($asset.Name + '.scale-' + $scale + '.png')),[System.Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
    }
} finally { $logo.Dispose() }

$priConfig = Join-Path $work 'priconfig.xml'
& $makePri createconfig /cf $priConfig /dq en-US /o
if ($LASTEXITCODE -ne 0) { throw 'MakePri configuration failed.' }
# Keep all DPI variants in one package/resource index, not separate resource packages.
[xml]$priSettings = Get-Content -LiteralPath $priConfig -Raw
$packaging = $priSettings.SelectSingleNode('/resources/packaging')
if ($packaging) { $packaging.ParentNode.RemoveChild($packaging) | Out-Null }
$priSettings.Save($priConfig)
& $makePri new /pr $stage /cf $priConfig /of (Join-Path $stage 'resources.pri') /o
if ($LASTEXITCODE -ne 0) { throw 'Package resource indexing failed.' }

$outputDirectory = Join-Path $root $(if ($Preview) { 'artifacts\store-preview' } else { 'dist\store' })
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$fileName = if ($Preview) { "Vanta-$version-x64-PREVIEW-NOT-FOR-DISTRIBUTION.msix" } else { "Vanta-$version-x64.msix" }
$package = Join-Path $outputDirectory $fileName
& $makeAppx pack /d $stage /p $package /o
if ($LASTEXITCODE -ne 0) { throw 'MSIX packaging or manifest validation failed.' }
$hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText(($package + '.sha256'), "$hash  $fileName" + [Environment]::NewLine,$utf8)
[System.IO.File]::WriteAllText((Join-Path $outputDirectory 'last-build.json'),
    (@{ Package=$package; Manifest=(Join-Path $stage 'AppxManifest.xml'); Preview=[bool]$Preview; Version=$version; Sha256=$hash } | ConvertTo-Json),$utf8)
if ($Preview) {
    Write-Warning 'LOCAL PREVIEW ONLY: placeholder identity; unsigned; not installable by public users and not suitable for Store submission.'
} else {
    Write-Output 'Unsigned Store submission package built. Microsoft signs it after certification; do not distribute this MSIX directly.'
}
Write-Output "Package: $package"
Write-Output "Manifest: $(Join-Path $stage 'AppxManifest.xml')"
