param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $root 'build.ps1') -Test }
$dist = Join-Path $root 'dist'
$exe = Join-Path $dist 'Vanta Auto Clicker.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build the executable first.' }
& (Join-Path $root 'scripts\Build-Installer.ps1') -SkipAppBuild
& (Join-Path $root 'scripts\Check-Website-Images.ps1')
$installer = Join-Path $dist 'Vanta.Auto.Clicker.Setup.exe'
if (-not (Test-Path -LiteralPath $installer)) { throw 'Build the installer first.' }
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
$shortVersion = ($version -split '\.')[0..2] -join '.'
$guide = Join-Path $dist 'QUICKSTART.txt'
Copy-Item -LiteralPath (Join-Path $root 'QUICKSTART.txt') -Destination $guide -Force
$fontLicense = Join-Path $dist 'Paytone-One-OFL.txt'
Copy-Item -LiteralPath (Join-Path $root 'assets\fonts\OFL.txt') -Destination $fontLicense -Force
$zip = Join-Path $dist "Vanta-Auto-Clicker-$shortVersion-win.zip"
Compress-Archive -LiteralPath $exe,$guide,$fontLicense -DestinationPath $zip -CompressionLevel Optimal -Force
$checksums = @($installer,$exe,$zip | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(),[System.IO.Path]::GetFileName($_)
})
[System.IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'),$checksums,[System.Text.UTF8Encoding]::new($false))
Write-Output "Release ready: $zip"
Write-Output ($checksums -join [Environment]::NewLine)
