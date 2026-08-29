param([switch]$SkipAppBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $SkipAppBuild) { & (Join-Path $root 'build.ps1') }

$app = Join-Path $root 'dist\Vanta Auto Clicker.exe'
if (-not (Test-Path -LiteralPath $app)) { throw 'Build the Vanta executable before building the installer.' }
$appVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductVersion
$parsedVersion = [Version]$appVersion
$fourPartVersion = '{0}.{1}.{2}.{3}' -f $parsedVersion.Major,$parsedVersion.Minor,$parsedVersion.Build,[Math]::Max(0,$parsedVersion.Revision)

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework 4.8 compiler is required.' }

$build = Join-Path $root 'build'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $build,$dist | Out-Null
$versionSource = Join-Path $build 'InstallerVersion.cs'
$versionText = @"
using System.Reflection;
[assembly: AssemblyTitle("Vanta Auto Clicker Setup")]
[assembly: AssemblyDescription("Installs Vanta Auto Clicker for the current Windows user")]
[assembly: AssemblyCompany("Vanta")]
[assembly: AssemblyProduct("Vanta Auto Clicker Setup")]
[assembly: AssemblyCopyright("Copyright © Vanta")]
[assembly: AssemblyVersion("$fourPartVersion")]
[assembly: AssemblyFileVersion("$fourPartVersion")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
"@
[System.IO.File]::WriteAllText($versionSource,$versionText,[System.Text.UTF8Encoding]::new($false))

$manifest = Join-Path $build 'Vanta.Setup.manifest'
$manifestText = (Get-Content -Raw -LiteralPath (Join-Path $root 'installer\installer.manifest.template')).Replace('{{VERSION}}',$fourPartVersion)
[System.IO.File]::WriteAllText($manifest,$manifestText,[System.Text.UTF8Encoding]::new($false))

$setup = Join-Path $dist 'Vanta.Auto.Clicker.Setup.exe'
$arguments = @(
    '/nologo','/optimize+','/warn:4','/platform:anycpu','/utf8output','/target:winexe',
    '/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll',
    "/out:$setup",
    "/win32icon:$(Join-Path $root 'assets\Vanta.ico')",
    "/win32manifest:$manifest",
    "/resource:$app,Vanta.Installer.App.exe",
    "/resource:$(Join-Path $root 'QUICKSTART.txt'),Vanta.Installer.QuickStart.txt",
    "/resource:$(Join-Path $root 'assets\fonts\OFL.txt'),Vanta.Installer.PaytoneLicense.txt",
    "/resource:$(Join-Path $root 'assets\Vanta_Logo.png'),Vanta.Installer.Logo.png",
    "/resource:$(Join-Path $root 'assets\fonts\PaytoneOne-Regular.ttf'),Vanta.Installer.Paytone.ttf",
    (Join-Path $root 'installer\Installer.cs'),$versionSource
)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
Write-Output "Built installer: $setup"
