[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64',

    [switch]$SkipVerification,
    [switch]$SkipInstaller,
    [switch]$Sign,
    [switch]$RequireSignature
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
$signScript = Join-Path $repoRoot 'scripts\windows\sign-file.ps1'
$installerScript = Join-Path $repoRoot 'installer\Keyina.iss'

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter()]
        [string[]]$ArgumentList = @(),
        [Parameter()]
        [string]$WorkingDirectory = $repoRoot
    )

    Push-Location $WorkingDirectory
    try {
        Write-Host "--> $FilePath $($ArgumentList -join ' ')" -ForegroundColor Cyan
        & $FilePath @ArgumentList
        if ($LASTEXITCODE -ne 0) {
            throw "$FilePath failed with exit code $LASTEXITCODE."
        }
    } finally {
        Pop-Location
    }
}

function Get-DefaultVersion {
    [xml]$props = Get-Content -LiteralPath $propsPath -Raw
    $candidate = @($props.Project.PropertyGroup.KeyinaVersion) |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw 'Directory.Build.props does not define KeyinaVersion.'
    }
    return [string]$candidate
}

function Get-FileVersion {
    param([Parameter(Mandatory)][string]$SemanticVersion)
    if ($SemanticVersion -notmatch '^(\d+)\.(\d+)\.(\d+)') {
        throw "Version is not a supported semantic version: $SemanticVersion"
    }
    return "$($Matches[1]).$($Matches[2]).$($Matches[3]).0"
}

function Get-InnoCompiler {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }
    throw 'Inno Setup compiler was not found. Install JRSoftware.InnoSetup with winget.'
}

function Get-SignTool {
    if (-not [string]::IsNullOrWhiteSpace($env:KEYINA_SIGNTOOL_PATH)) {
        return [System.IO.Path]::GetFullPath($env:KEYINA_SIGNTOOL_PATH)
    }
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $candidate = Get-ChildItem -LiteralPath $sdkRoot -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw 'signtool.exe was not found.'
    }
    return $candidate.FullName
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-DefaultVersion
}
$fileVersion = Get-FileVersion $Version
if ($RequireSignature) {
    $Sign = $true
}
if ($Sign -and
    [string]::IsNullOrWhiteSpace($env:KEYINA_SIGN_CERT_THUMBPRINT) -and
    [string]::IsNullOrWhiteSpace($env:KEYINA_SIGN_PFX_PATH)) {
    throw 'Signing was requested but no identity is configured. Set KEYINA_SIGN_CERT_THUMBPRINT or KEYINA_SIGN_PFX_PATH.'
}

$artifactRoot = Join-Path $repoRoot "artifacts\release\$Version"
$publishDir = Join-Path $artifactRoot $RuntimeIdentifier
$installerDir = Join-Path $artifactRoot 'installer'
$portableZip = Join-Path $artifactRoot "Keyina-$Version-$RuntimeIdentifier.zip"
$checksumsPath = Join-Path $artifactRoot 'SHA256SUMS.txt'
$manifestPath = Join-Path $artifactRoot 'release-manifest.json'

if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $installerDir -Force | Out-Null

$versionProperties = @(
    "-p:Version=$Version",
    "-p:InformationalVersion=$Version",
    "-p:AssemblyVersion=$fileVersion",
    "-p:FileVersion=$fileVersion"
)

if (-not $SkipVerification) {
    Invoke-Checked 'cmake.exe' @('--preset', 'windows-msvc-release')
    Invoke-Checked 'cmake.exe' @('--build', '--preset', 'windows-msvc-release')
    Invoke-Checked 'ctest.exe' @('--preset', 'windows-msvc-release', '--output-on-failure')
    Invoke-Checked 'python.exe' @('tools/check_vectors.py')
    Invoke-Checked 'python.exe' @('tools/test_compare_benchmark.py')

    Invoke-Checked 'dotnet.exe' (@('build', 'Keyina.slnx', '-c', 'Release') + $versionProperties)
    Invoke-Checked 'dotnet.exe' @(
        'run', '--project', 'apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj',
        '-c', 'Release', '--no-build'
    )
    foreach ($selfTest in @('--self-test', '--speech-self-test', '--hotkey-self-test', '--resource-self-test')) {
        Invoke-Checked 'dotnet.exe' @(
            'run', '--project', 'apps/host/Keyina.Host/Keyina.Host.csproj',
            '-c', 'Release', '--no-build', '--', $selfTest
        )
    }
    Invoke-Checked 'dotnet.exe' @(
        'run', '--project', 'apps/host/Keyina.Host.Benchmarks/Keyina.Host.Benchmarks.csproj',
        '-c', 'Release', '--no-build'
    )
}

Invoke-Checked 'dotnet.exe' (@(
    'publish', 'apps/host/Keyina.Host/Keyina.Host.csproj',
    '-c', 'Release',
    '-r', $RuntimeIdentifier,
    '--self-contained', 'true',
    '-p:PublishReadyToRun=false',
    '-p:SatelliteResourceLanguages=en',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-o', $publishDir
) + $versionProperties)

$requiredFiles = @(
    (Join-Path $publishDir 'Keyina.Host.exe'),
    (Join-Path $publishDir 'KeyinaEngine.dll'),
    (Join-Path $publishDir 'Assets\keyina-tray-active.ico'),
    (Join-Path $publishDir 'Assets\keyina-tray-inactive.ico'),
    (Join-Path $publishDir 'Assets\keyina-tray-listening.ico')
)
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Published release is missing required file: $requiredFile"
    }
}

$documentationDir = Join-Path $publishDir 'Documentation'
New-Item -ItemType Directory -Path $documentationDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $documentationDir
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $documentationDir
Copy-Item -LiteralPath (Join-Path $repoRoot 'SECURITY.md') -Destination $documentationDir
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\translation.md') -Destination $documentationDir

$publishedExe = Join-Path $publishDir 'Keyina.Host.exe'
$reportedVersion = (& $publishedExe --version | Select-Object -First 1).Trim()
if ($LASTEXITCODE -ne 0 -or $reportedVersion -ne $Version) {
    throw "Published host reports version '$reportedVersion'; expected '$Version'."
}
foreach ($selfTest in @('--self-test', '--speech-self-test', '--hotkey-self-test', '--resource-self-test')) {
    Invoke-Checked $publishedExe @($selfTest) $publishDir
}

if ($Sign) {
    $projectBinaries = Get-ChildItem -LiteralPath $publishDir -File |
        Where-Object {
            $_.Name -like 'Keyina*.exe' -or $_.Name -like 'Keyina*.dll'
        } |
        Sort-Object FullName
    foreach ($binary in $projectBinaries) {
        Invoke-Checked 'powershell.exe' @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass',
            '-File', $signScript,
            '-Path', $binary.FullName
        )
    }
}

Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $portableZip -CompressionLevel Optimal

$installerPath = $null
if (-not $SkipInstaller) {
    $iscc = Get-InnoCompiler
    $innoArguments = @(
        "/DMyAppVersion=$Version",
        "/DSourceDir=$publishDir",
        "/DOutputDir=$installerDir"
    )
    if ($Sign) {
        $signCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$signScript`" -Path `$f"
        $innoArguments += '/DEnableSigning=1'
        $innoArguments += "/SKeyinaSign=$signCommand"
    }
    $innoArguments += $installerScript
    Invoke-Checked $iscc $innoArguments

    $installerPath = Join-Path $installerDir "Keyina-Setup-$Version-x64.exe"
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
        throw "Installer output was not found: $installerPath"
    }
    if ($Sign) {
        $signTool = Get-SignTool
        Invoke-Checked $signTool @('verify', '/pa', '/all', '/v', $installerPath)
    }
}

$releaseFiles = @($portableZip)
if ($null -ne $installerPath) {
    $releaseFiles += $installerPath
}
$checksumLines = foreach ($file in $releaseFiles) {
    $hash = Get-FileHash -LiteralPath $file -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($file))"
}
Set-Content -LiteralPath $checksumsPath -Value $checksumLines -Encoding UTF8

$commit = (git -C $repoRoot rev-parse HEAD).Trim()
$publishedFiles = Get-ChildItem -LiteralPath $publishDir -Recurse -File
$manifest = [ordered]@{
    schema_version = 1
    product = 'Keyina'
    version = $Version
    runtime_identifier = $RuntimeIdentifier
    git_commit = $commit
    created_utc = [DateTimeOffset]::UtcNow.ToString('O')
    self_contained = $true
    ready_to_run = $false
    signed = [bool]$Sign
    published_file_count = $publishedFiles.Count
    published_bytes = [long](($publishedFiles | Measure-Object Length -Sum).Sum)
    artifacts = @(
        $releaseFiles | ForEach-Object {
            $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
            [ordered]@{
                file = [System.IO.Path]::GetFileName($_)
                bytes = (Get-Item -LiteralPath $_).Length
                sha256 = $hash.Hash.ToLowerInvariant()
            }
        }
    )
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host ''
Write-Host 'Release artifacts created:' -ForegroundColor Green
Write-Host "  Portable:  $portableZip"
if ($null -ne $installerPath) {
    Write-Host "  Installer: $installerPath"
}
Write-Host "  Checksums: $checksumsPath"
Write-Host "  Manifest:  $manifestPath"
Write-Host "  Signed:    $([bool]$Sign)"
