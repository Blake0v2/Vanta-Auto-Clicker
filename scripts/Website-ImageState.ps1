function Get-VantaWebsiteImageStamp {
    param([Parameter(Mandatory=$true)][string]$ProjectRoot)

    $resolvedRoot = [System.IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\','/')
    $sourceFiles = @(
        Get-ChildItem -LiteralPath (Join-Path $resolvedRoot 'src') -File |
            Sort-Object Name
        Get-Item -LiteralPath (Join-Path $resolvedRoot 'assets\Vanta_Logo.png')
        Get-Item -LiteralPath (Join-Path $resolvedRoot 'assets\fonts\PaytoneOne-Regular.ttf')
        Get-Item -LiteralPath (Join-Path $resolvedRoot 'scripts\Inspect-UI.ps1')
    )
    $records = foreach ($file in $sourceFiles) {
        $relative = $file.FullName.Substring($resolvedRoot.Length).TrimStart('\','/').Replace('\','/')
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        '{0}:{1}' -f $relative,$hash
    }
    $payload = [System.Text.UTF8Encoding]::new($false).GetBytes(($records -join "`n"))
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha.ComputeHash($payload))).Replace('-','').ToLowerInvariant()
    } finally { $sha.Dispose() }
}

function Get-VantaAppShortVersion {
    param([Parameter(Mandatory=$true)][string]$ProjectRoot)

    $source = [System.IO.File]::ReadAllText((Join-Path $ProjectRoot 'src\App.cs'))
    $match = [System.Text.RegularExpressions.Regex]::Match($source, 'AssemblyVersion\("(\d+\.\d+\.\d+)\.\d+"\)')
    if (-not $match.Success) { throw 'Could not read the app version from src\App.cs.' }
    return $match.Groups[1].Value
}
