[CmdletBinding()]
param(
    [string]$Version,
    [string]$ArtifactDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
    if ([string]::IsNullOrWhiteSpace($Version)) {
        [xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
        $versionNode = $props.SelectSingleNode('/Project/PropertyGroup/KeyinaVersion')
        if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
            throw 'Directory.Build.props does not define KeyinaVersion.'
        }
        $Version = $versionNode.InnerText.Trim()
    }
    $ArtifactDirectory = Join-Path $repoRoot "artifacts\release\$Version"
}
$artifactRoot = [System.IO.Path]::GetFullPath($ArtifactDirectory)
$manifestPath = Join-Path $artifactRoot 'release-manifest.json'
$checksumsPath = Join-Path $artifactRoot 'SHA256SUMS.txt'

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Release manifest not found: $manifestPath"
}
if (-not (Test-Path -LiteralPath $checksumsPath -PathType Leaf)) {
    throw "Checksum file not found: $checksumsPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($line in Get-Content -LiteralPath $checksumsPath) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }
    if ($line -notmatch '^([0-9a-fA-F]{64})\s{2}(.+)$') {
        throw "Invalid checksum line: $line"
    }
    $expected = $Matches[1].ToLowerInvariant()
    $fileName = $Matches[2]
    $candidate = Get-ChildItem -LiteralPath $artifactRoot -Recurse -File |
        Where-Object { $_.Name -eq $fileName } |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw "Checksummed artifact not found: $fileName"
    }
    $actual = (Get-FileHash -LiteralPath $candidate.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        throw "Checksum mismatch for $fileName."
    }
}

$publishDir = Join-Path $artifactRoot ([string]$manifest.runtime_identifier)
$host = Join-Path $publishDir 'Keyina.Host.exe'
if (-not (Test-Path -LiteralPath $host -PathType Leaf)) {
    throw "Published host not found: $host"
}

$reportedVersion = (& $host --version | Select-Object -First 1).Trim()
if ($LASTEXITCODE -ne 0 -or $reportedVersion -ne [string]$manifest.version) {
    throw "Published host reports '$reportedVersion'; manifest expects '$($manifest.version)'."
}
foreach ($selfTest in @('--self-test', '--speech-self-test', '--hotkey-self-test', '--resource-self-test')) {
    & $host $selfTest
    if ($LASTEXITCODE -ne 0) {
        throw "Published host self-test failed: $selfTest"
    }
}

if ([bool]$manifest.signed) {
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $signTool = Get-ChildItem -LiteralPath $sdkRoot -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($null -eq $signTool) {
        throw 'signtool.exe was not found for signature verification.'
    }
    foreach ($artifact in $manifest.artifacts) {
        if ([string]$artifact.file -like '*.exe') {
            $candidate = Get-ChildItem -LiteralPath $artifactRoot -Recurse -File |
                Where-Object { $_.Name -eq [string]$artifact.file } |
                Select-Object -First 1
            & $signTool.FullName verify /pa /all /v $candidate.FullName
            if ($LASTEXITCODE -ne 0) {
                throw "Authenticode verification failed: $($candidate.FullName)"
            }
        }
    }
}

Write-Host "Release verified: $artifactRoot" -ForegroundColor Green
Write-Host "Version: $($manifest.version)"
Write-Host "Signed:  $([bool]$manifest.signed)"
