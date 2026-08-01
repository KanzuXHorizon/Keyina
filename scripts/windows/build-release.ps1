[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64',

    [switch]$SkipVerification,
    [switch]$SkipBuildTests,
    [switch]$SkipDesktopInteractiveTests,
    [switch]$SkipInstaller,
    [switch]$Sign,
    [switch]$RequireSignature
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
$signScript = Join-Path $repoRoot 'scripts\windows\sign-file.ps1'
$installerScript = Join-Path $repoRoot 'installer\Keyina.iss'
$installerLifecycleScript = Join-Path $repoRoot 'scripts\windows\test-installer.ps1'
$lifecycleInstallerBuilder = Join-Path $repoRoot 'scripts\windows\build-lifecycle-installer.ps1'

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

function Invoke-CheckedCapturedProcess {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter()]
        [string]$Arguments = '',
        [Parameter()]
        [string]$WorkingDirectory = $repoRoot
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = $Arguments
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    Write-Host "--> $FilePath $Arguments" -ForegroundColor Cyan
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Could not start $FilePath."
    }
    try {
        $standardOutput = $process.StandardOutput.ReadToEnd()
        $standardError = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if (-not [string]::IsNullOrWhiteSpace($standardOutput)) {
            Write-Host $standardOutput.TrimEnd()
        }
        if (-not [string]::IsNullOrWhiteSpace($standardError)) {
            Write-Host $standardError.TrimEnd() -ForegroundColor DarkYellow
        }
        if ($process.ExitCode -ne 0) {
            throw "$FilePath failed with exit code $($process.ExitCode)."
        }
        return [pscustomobject]@{
            StandardOutput = $standardOutput
            StandardError = $standardError
        }
    } finally {
        $process.Dispose()
    }
}

function Remove-DirectoryWithRetry {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        } catch {
            if ($attempt -ge 20) {
                throw
            }
            [GC]::Collect()
            [GC]::WaitForPendingFinalizers()
            Start-Sleep -Milliseconds ([Math]::Min(1000, 150 * $attempt))
        }
    }
    Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
}

function Get-DefaultVersion {
    [xml]$props = Get-Content -LiteralPath $propsPath -Raw
    $candidate = $props.SelectSingleNode('/Project/PropertyGroup/KeyinaVersion')
    if ($null -eq $candidate -or [string]::IsNullOrWhiteSpace($candidate.InnerText)) {
        throw 'Directory.Build.props does not define KeyinaVersion.'
    }
    return $candidate.InnerText.Trim()
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

$releaseRoot = Join-Path $repoRoot 'artifacts\release'
$finalArtifactRoot = Join-Path $releaseRoot $Version
$artifactRoot = Join-Path `
    $releaseRoot `
    ".staging-$Version-$([Guid]::NewGuid().ToString('N'))"
$publishDir = Join-Path $artifactRoot $RuntimeIdentifier
$installerDir = Join-Path $artifactRoot 'installer'
$portableZip = Join-Path $artifactRoot "Keyina-$Version-$RuntimeIdentifier.zip"
$checksumsPath = Join-Path $artifactRoot 'SHA256SUMS.txt'
$manifestPath = Join-Path $artifactRoot 'release-manifest.json'

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
Remove-DirectoryWithRetry $artifactRoot
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $installerDir -Force | Out-Null

$versionProperties = @(
    "-p:Version=$Version",
    "-p:InformationalVersion=$Version",
    "-p:AssemblyVersion=$fileVersion",
    "-p:FileVersion=$fileVersion"
)

Invoke-Checked 'cmake.exe' @(
    '--preset', 'windows-msvc-release',
    "-DKEYINA_VERSION=$Version"
)
Invoke-Checked 'cmake.exe' @('--build', '--preset', 'windows-msvc-release')

if (-not $SkipVerification -and -not $SkipBuildTests) {
    $ctestArguments = @('--preset', 'windows-msvc-release', '--output-on-failure')
    if ($SkipDesktopInteractiveTests) {
        $ctestArguments += @(
            '-E',
            'keyina\.windows\.input_(typing|clipboard_typing|callback_latency|transform_callback_latency)'
        )
    }
    Invoke-Checked 'ctest.exe' $ctestArguments
    Invoke-Checked 'python.exe' @('tools/check_vectors.py')
    Invoke-Checked 'python.exe' @('tools/test_compare_benchmark.py')

    Invoke-Checked 'dotnet.exe' (@('build', 'Keyina.slnx', '-c', 'Release') + $versionProperties)
    Invoke-Checked 'dotnet.exe' @(
        'run', '--project', 'apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj',
        '-c', 'Release', '--no-build'
    )
    foreach ($selfTest in @('--self-test', '--speech-self-test', '--hotkey-self-test')) {
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
    '-p:NuGetLockFilePath=obj\publish-packages.lock.json',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-o', $publishDir
) + $versionProperties)

$nativeInputPath = Join-Path $repoRoot 'build\windows-msvc-release\platform\windows\input\Release\KeyinaInput.exe'
if (-not (Test-Path -LiteralPath $nativeInputPath -PathType Leaf)) {
    throw "Native resident output was not found: $nativeInputPath"
}
Copy-Item -LiteralPath $nativeInputPath -Destination (Join-Path $publishDir 'KeyinaInput.exe') -Force

$requiredFiles = @(
    (Join-Path $publishDir 'KeyinaInput.exe'),
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

$publishedHost = Join-Path $publishDir 'Keyina.Host.exe'
$publishedResident = Join-Path $publishDir 'KeyinaInput.exe'
$versionResult = Invoke-CheckedCapturedProcess $publishedHost '--version' $publishDir
$reportedVersion = (($versionResult.StandardOutput -split "`r?`n") | Select-Object -First 1).Trim()
if ($reportedVersion -ne $Version) {
    throw "Published host reports version '$reportedVersion'; expected '$Version'."
}
foreach ($selfTest in @('--self-test', '--speech-self-test', '--hotkey-self-test')) {
    $null = Invoke-CheckedCapturedProcess $publishedHost $selfTest $publishDir
}
foreach ($selfTest in @(
    '--self-test',
    '--resource-self-test',
    '--tray-resource-self-test',
    '--profile-reload-self-test'
)) {
    $null = Invoke-CheckedCapturedProcess $publishedResident $selfTest $publishDir
}
if (-not $SkipDesktopInteractiveTests) {
    $null = Invoke-CheckedCapturedProcess $publishedResident '--typing-self-test' $publishDir
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
$installerLifecycleVerified = $false
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
    if (-not $SkipVerification) {
        $lifecycleDirectory = Join-Path `
            $env:TEMP `
            "KeyinaLifecycleInstallers\$([Guid]::NewGuid().ToString('N'))"
        try {
            Invoke-Checked 'powershell.exe' @(
                '-NoProfile', '-ExecutionPolicy', 'Bypass',
                '-File', $lifecycleInstallerBuilder,
                '-SourceDirectory', $publishDir,
                '-Version', $Version,
                '-OutputDirectory', $lifecycleDirectory
            )
            $lifecycleInstallers = @(
                Get-ChildItem -LiteralPath $lifecycleDirectory -Filter '*.exe' -File
            )
            if ($lifecycleInstallers.Count -ne 1) {
                throw "Expected one lifecycle installer; found $($lifecycleInstallers.Count)."
            }
            Invoke-Checked 'powershell.exe' @(
                '-NoProfile', '-ExecutionPolicy', 'Bypass',
                '-File', $installerLifecycleScript,
                '-InstallerPath', $lifecycleInstallers[0].FullName,
                '-Version', $Version
            )
            $installerLifecycleVerified = $true
        } finally {
            Remove-Item `
                -LiteralPath $lifecycleDirectory `
                -Recurse `
                -Force `
                -ErrorAction SilentlyContinue
        }
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
    schema_version = 2
    product = 'Keyina'
    version = $Version
    runtime_identifier = $RuntimeIdentifier
    git_commit = $commit
    created_utc = [DateTimeOffset]::UtcNow.ToString('O')
    self_contained = $true
    ready_to_run = $false
    signed = [bool]$Sign
    installer_type = if ($null -ne $installerPath) { 'inno_setup' } else { 'none' }
    install_scope = if ($null -ne $installerPath) { 'current_user' } else { 'portable' }
    installer_lifecycle_verified = $installerLifecycleVerified
    build_test_suites_skipped = [bool]$SkipBuildTests
    desktop_interactive_tests_skipped = [bool]$SkipDesktopInteractiveTests
    preserved_user_data_directory = '%LOCALAPPDATA%\Keyina'
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

Remove-DirectoryWithRetry $finalArtifactRoot
Move-Item -LiteralPath $artifactRoot -Destination $finalArtifactRoot

$finalPortableZip = Join-Path `
    $finalArtifactRoot `
    ([System.IO.Path]::GetFileName($portableZip))
$finalChecksumsPath = Join-Path $finalArtifactRoot 'SHA256SUMS.txt'
$finalManifestPath = Join-Path $finalArtifactRoot 'release-manifest.json'
$finalInstallerPath = if ($null -ne $installerPath) {
    Join-Path `
        (Join-Path $finalArtifactRoot 'installer') `
        ([System.IO.Path]::GetFileName($installerPath))
} else {
    $null
}

Write-Host ''
Write-Host 'Release artifacts created:' -ForegroundColor Green
Write-Host "  Portable:  $finalPortableZip"
if ($null -ne $finalInstallerPath) {
    Write-Host "  Installer: $finalInstallerPath"
}
Write-Host "  Checksums: $finalChecksumsPath"
Write-Host "  Manifest:  $finalManifestPath"
Write-Host "  Signed:    $([bool]$Sign)"
