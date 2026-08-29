param([switch]$Test, [switch]$UiTest, [switch]$CompileTests, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework 4.8 compiler is required. Install .NET Framework 4.8 on Windows.' }
$build = Join-Path $root 'build'
$dist = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'dist' }
New-Item -ItemType Directory -Force -Path $build,$dist | Out-Null
# WPF fonts must be in the assembly's .g.resources collection, not an ordinary
# embedded-resource entry. Store the original font as a stream without changes.
$fontResources = Join-Path $build 'Vanta.Fonts.g.resources'
$fontStream = [System.IO.File]::OpenRead((Join-Path $root 'assets\fonts\PaytoneOne-Regular.ttf'))
$resourceWriter = [System.Resources.ResourceWriter]::new($fontResources)
try {
    $resourceWriter.AddResource('fonts/paytoneone-regular.ttf',$fontStream,$true)
    $resourceWriter.Generate()
} finally { $resourceWriter.Dispose(); $fontStream.Dispose() }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$references = @('System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.Runtime.Serialization.dll')
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll' | ForEach-Object { Join-Path $framework "WPF\$_" })
$common = @('/nologo','/optimize+','/warn:4','/platform:anycpu','/utf8output')
$common += @($references | ForEach-Object { "/reference:$_" })
$common += "/resource:$(Join-Path $root 'src\MainWindow.xaml'),Vanta.MainWindow.xaml"
$common += "/resource:$(Join-Path $root 'src\Theme.xaml'),Vanta.Theme.xaml"
$common += "/resource:$(Join-Path $root 'assets\Vanta_Logo.png'),Vanta.Logo.png"
$common += "/resource:$(Join-Path $root 'assets\fonts\OFL.txt'),Vanta.PaytoneOne.License.txt"
$exe = Join-Path $dist 'Vanta Auto Clicker.exe'
& $compiler @common '/target:winexe' '/main:Vanta.Program' "/out:$exe" "/resource:$fontResources,Vanta Auto Clicker.g.resources" "/win32icon:$(Join-Path $root 'assets\Vanta.ico')" "/win32manifest:$(Join-Path $root 'src\app.manifest')" @sources
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
Write-Output "Built: $exe"
if ($Test -or $UiTest -or $CompileTests) {
    $testExe = Join-Path $build 'Vanta.Tests.exe'
    & $compiler @common '/target:exe' '/main:Vanta.Tests' "/out:$testExe" "/resource:$fontResources,Vanta.Tests.g.resources" "/win32manifest:$(Join-Path $root 'src\app.manifest')" @sources (Join-Path $root 'tests\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    if ($Test) { & $testExe; if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' } }
    if ($UiTest) { & $testExe '--ui'; if ($LASTEXITCODE -ne 0) { throw 'UI tests failed.' } }
}
