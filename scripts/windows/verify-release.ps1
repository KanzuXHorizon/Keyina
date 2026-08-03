[CmdletBinding()]
param(
    [string]$Version,
    [string]$ArtifactDirectory,
    [switch]$RunDesktopInteractiveTests,
    [switch]$SkipDesktopInteractiveTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($RunDesktopInteractiveTests -and $SkipDesktopInteractiveTests) {
    throw 'RunDesktopInteractiveTests and SkipDesktopInteractiveTests cannot be combined.'
}
$desktopInteractiveTestsEnabled =
    [bool]$RunDesktopInteractiveTests -and -not [bool]$SkipDesktopInteractiveTests

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$installerLifecycleScript = Join-Path $repoRoot 'scripts\windows\test-installer.ps1'
$lifecycleInstallerBuilder = Join-Path $repoRoot 'scripts\windows\build-lifecycle-installer.ps1'

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
if ([int]$manifest.schema_version -ne 2) {
    throw "Unsupported release manifest schema: $($manifest.schema_version)"
}
if ([string]$manifest.product -ne 'Keyina') {
    throw "Unexpected release product: $($manifest.product)"
}
if (-not [string]::IsNullOrWhiteSpace($Version) -and
    [string]$manifest.version -ne $Version) {
    throw "Release manifest version '$($manifest.version)' does not match '$Version'."
}
if ([string]$manifest.runtime_identifier -ne 'win-x64') {
    throw "Unexpected runtime identifier: $($manifest.runtime_identifier)"
}

$checksumByName = @{}
foreach ($line in Get-Content -LiteralPath $checksumsPath) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }
    if ($line -notmatch '^([0-9a-fA-F]{64})\s{2}(.+)$') {
        throw "Invalid checksum line: $line"
    }
    $expected = $Matches[1].ToLowerInvariant()
    $fileName = $Matches[2]
    if ($checksumByName.ContainsKey($fileName)) {
        throw "Duplicate checksum entry: $fileName"
    }
    $checksumByName[$fileName] = $expected
}

$manifestArtifacts = @($manifest.artifacts)
if ($manifestArtifacts.Count -eq 0) {
    throw 'Release manifest contains no artifacts.'
}
if ($checksumByName.Count -ne $manifestArtifacts.Count) {
    throw 'Manifest and checksum artifact counts differ.'
}

$allReleaseFiles = @(Get-ChildItem -LiteralPath $artifactRoot -Recurse -File)
$artifactLocations = @{}
$manifestNames = @{}
foreach ($artifact in $manifestArtifacts) {
    $fileName = [string]$artifact.file
    if ([string]::IsNullOrWhiteSpace($fileName) -or
        [System.IO.Path]::GetFileName($fileName) -ne $fileName) {
        throw "Invalid manifest artifact name: $fileName"
    }
    if ($manifestNames.ContainsKey($fileName)) {
        throw "Duplicate manifest artifact: $fileName"
    }
    $manifestNames[$fileName] = $true

    $matches = @($allReleaseFiles | Where-Object { $_.Name -eq $fileName })
    if ($matches.Count -ne 1) {
        throw "Manifest artifact '$fileName' resolved to $($matches.Count) files."
    }
    $candidate = $matches[0]
    $artifactLocations[$fileName] = $candidate.FullName

    if ([long]$artifact.bytes -ne [long]$candidate.Length) {
        throw "Artifact length mismatch for $fileName."
    }
    $actual = (Get-FileHash -LiteralPath $candidate.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ([string]$artifact.sha256 -ne $actual) {
        throw "Manifest hash mismatch for $fileName."
    }
    if (-not $checksumByName.ContainsKey($fileName)) {
        throw "Checksum entry missing for $fileName."
    }
    if ([string]$checksumByName[$fileName] -ne $actual) {
        throw "Checksum mismatch for $fileName."
    }
}
foreach ($fileName in $checksumByName.Keys) {
    if (-not $manifestNames.ContainsKey($fileName)) {
        throw "Checksum references an unknown artifact: $fileName"
    }
}

$publishDir = Join-Path $artifactRoot ([string]$manifest.runtime_identifier)
$publishedHost = Join-Path $publishDir 'Keyina.Host.exe'
$publishedResident = Join-Path $publishDir 'KeyinaInput.exe'
if (-not (Test-Path -LiteralPath $publishedHost -PathType Leaf)) {
    throw "Published host not found: $publishedHost"
}
if (-not (Test-Path -LiteralPath $publishedResident -PathType Leaf)) {
    throw "Published native resident not found: $publishedResident"
}

$versionResult = Invoke-CheckedCapturedProcess $publishedHost '--version' $publishDir
$reportedVersion = (($versionResult.StandardOutput -split "`r?`n") | Select-Object -First 1).Trim()
if ($reportedVersion -ne [string]$manifest.version) {
    throw "Published host reports '$reportedVersion'; manifest expects '$($manifest.version)'."
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
if ($desktopInteractiveTestsEnabled) {
    $null = Invoke-CheckedCapturedProcess $publishedResident '--typing-self-test' $publishDir
}

$installerArtifacts = @(
    $manifest.artifacts |
        Where-Object { [string]$_.file -like 'Keyina-Setup-*.exe' }
)
if ([string]$manifest.installer_type -eq 'inno_setup') {
    if ($installerArtifacts.Count -ne 1) {
        throw 'Release manifest must contain exactly one Inno Setup installer.'
    }
    if ([string]$manifest.install_scope -ne 'current_user') {
        throw "Unexpected installer scope: $($manifest.install_scope)"
    }
    if ([string]$manifest.preserved_user_data_directory -ne '%LOCALAPPDATA%\Keyina') {
        throw 'Release manifest does not preserve the Keyina user-data directory.'
    }
    $installerFileName = [string]$installerArtifacts[0].file
    $installerPath = [string]$artifactLocations[$installerFileName]
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
        throw "Installer not found: $installerPath"
    }
    $installerInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($installerPath)
    $installerProductName = ([string]$installerInfo.ProductName).Trim()
    $installerProductVersion = ([string]$installerInfo.ProductVersion).Trim()
    if ($installerProductName -ne 'Keyina') {
        throw "Unexpected installer product name: $installerProductName"
    }
    if ([string]::IsNullOrWhiteSpace($installerProductVersion) -or
        -not $installerProductVersion.StartsWith(
            [string]$manifest.version,
            [StringComparison]::Ordinal)) {
        throw "Installer product version '$installerProductVersion' does not match '$($manifest.version)'."
    }
    $lifecycleDirectory = Join-Path `
        $env:TEMP `
        "KeyinaLifecycleInstallers\$([Guid]::NewGuid().ToString('N'))"
    try {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass `
            -File $lifecycleInstallerBuilder `
            -SourceDirectory $publishDir `
            -Version ([string]$manifest.version) `
            -OutputDirectory $lifecycleDirectory
        if ($LASTEXITCODE -ne 0) {
            throw "Lifecycle installer compilation failed with exit code $LASTEXITCODE."
        }
        $lifecycleInstallers = @(
            Get-ChildItem -LiteralPath $lifecycleDirectory -Filter '*.exe' -File
        )
        if ($lifecycleInstallers.Count -ne 1) {
            throw "Expected one lifecycle installer; found $($lifecycleInstallers.Count)."
        }
        & powershell.exe -NoProfile -ExecutionPolicy Bypass `
            -File $installerLifecycleScript `
            -InstallerPath $lifecycleInstallers[0].FullName `
            -Version ([string]$manifest.version)
        if ($LASTEXITCODE -ne 0) {
            throw "Installer lifecycle verification failed with exit code $LASTEXITCODE."
        }
    } finally {
        Remove-Item `
            -LiteralPath $lifecycleDirectory `
            -Recurse `
            -Force `
            -ErrorAction SilentlyContinue
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
    foreach ($artifact in $manifestArtifacts) {
        if ([string]$artifact.file -like '*.exe') {
            $candidatePath = [string]$artifactLocations[[string]$artifact.file]
            & $signTool.FullName verify /pa /all /v $candidatePath
            if ($LASTEXITCODE -ne 0) {
                throw "Authenticode verification failed: $candidatePath"
            }
        }
    }
}

Write-Host "Release verified: $artifactRoot" -ForegroundColor Green
Write-Host "Version: $($manifest.version)"
Write-Host "Signed:  $([bool]$manifest.signed)"
