[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [switch]$KeepSandbox
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-CheckedProcess {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter()]
        [string[]]$ArgumentList = @(),
        [Parameter()]
        [string]$WorkingDirectory = (Get-Location).Path
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = [string]::Join(' ', $ArgumentList)
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Could not start $FilePath."
    }
    try {
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "$FilePath failed with exit code $($process.ExitCode)."
        }
    } finally {
        $process.Dispose()
    }
}

function Invoke-CheckedCapturedProcess {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter()]
        [string]$Arguments = '',
        [Parameter()]
        [string]$WorkingDirectory = (Get-Location).Path
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
        if ($process.ExitCode -ne 0) {
            throw "$FilePath $Arguments failed with exit code $($process.ExitCode): $standardError"
        }
        return [pscustomobject]@{
            StandardOutput = $standardOutput
            StandardError = $standardError
        }
    } finally {
        $process.Dispose()
    }
}

function Get-InstalledResidentProcesses {
    param([Parameter(Mandatory)][string]$InstallDirectory)

    $prefix = [System.IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\') + '\'
    return @(
        Get-CimInstance Win32_Process -Filter "Name = 'KeyinaInput.exe'" -ErrorAction SilentlyContinue |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
                $_.ExecutablePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
            }
    )
}

function Wait-PathAbsent {
    param(
        [Parameter(Mandatory)][string]$Path,
        [int]$TimeoutMilliseconds = 10000
    )

    $deadline = [DateTimeOffset]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    while (Test-Path -LiteralPath $Path) {
        if ([DateTimeOffset]::UtcNow -ge $deadline) {
            throw "Path remained after uninstall: $Path"
        }
        Start-Sleep -Milliseconds 100
    }
}

$InstallerPath = [System.IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    throw "Installer not found: $InstallerPath"
}
if ([System.IO.Path]::GetExtension($InstallerPath) -ne '.exe') {
    throw 'InstallerPath must reference an executable file.'
}

$installerInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($InstallerPath)
if ([string]::IsNullOrWhiteSpace($installerInfo.ProductVersion) -or
    -not $installerInfo.ProductVersion.StartsWith($Version, [StringComparison]::Ordinal)) {
    throw "Installer product version '$($installerInfo.ProductVersion)' does not match '$Version'."
}

$identifier = [Guid]::NewGuid().ToString('N')
$sandboxRoot = Join-Path $env:TEMP "KeyinaInstallerTests\$identifier"
$installDirectory = Join-Path $sandboxRoot 'installed'
$firstInstallLog = Join-Path $sandboxRoot 'install-first.log'
$upgradeLog = Join-Path $sandboxRoot 'install-upgrade.log'
$uninstallLog = Join-Path $sandboxRoot 'uninstall.log'
$userDataRoot = Join-Path $env:LOCALAPPDATA 'Keyina'
$sentinelDirectory = Join-Path $userDataRoot "installer-lifecycle-$identifier"
$sentinelPath = Join-Path $sentinelDirectory 'settings.json'
$runRegistryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startupDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
$legacyStartupShortcut = Join-Path $startupDirectory 'Keyina.lnk'
$legacyStartupBackup = Join-Path $sandboxRoot 'Keyina.lnk.backup'
$originalRunValuePresent = $false
$originalRunValue = $null
$originalShortcutPresent = Test-Path -LiteralPath $legacyStartupShortcut -PathType Leaf
$uninstallerPath = Join-Path $installDirectory 'unins000.exe'
$installed = $false

try {
    New-Item -ItemType Directory -Path $sandboxRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $sentinelDirectory -Force | Out-Null
    Set-Content -LiteralPath $sentinelPath -Value '{"preserve":true}' -Encoding UTF8

    try {
        $originalRunValue = Get-ItemPropertyValue `
            -LiteralPath $runRegistryPath `
            -Name 'Keyina' `
            -ErrorAction Stop
        $originalRunValuePresent = $true
    } catch [System.Management.Automation.ItemNotFoundException] {
        $originalRunValuePresent = $false
    } catch [System.Management.Automation.PSArgumentException] {
        $originalRunValuePresent = $false
    }

    if ($originalShortcutPresent) {
        Copy-Item -LiteralPath $legacyStartupShortcut -Destination $legacyStartupBackup -Force
    }

    $foreignResidents = @(
        Get-CimInstance Win32_Process -Filter "Name = 'KeyinaInput.exe'" -ErrorAction SilentlyContinue
    )
    if ($foreignResidents.Count -ne 0) {
        throw 'Installer lifecycle verification requires KeyinaInput.exe to be stopped first.'
    }

    $commonInstallArguments = @(
        '/CURRENTUSER',
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        '/NOICONS',
        "/DIR=`"$installDirectory`""
    )

    Invoke-CheckedProcess `
        -FilePath $InstallerPath `
        -ArgumentList ($commonInstallArguments + "/LOG=`"$firstInstallLog`"") `
        -WorkingDirectory $sandboxRoot
    $installed = $true

    $requiredFiles = @(
        'KeyinaInput.exe',
        'Keyina.Host.exe',
        'KeyinaEngine.dll',
        'Assets\keyina-tray-active.ico',
        'Assets\keyina-tray-inactive.ico',
        'Assets\keyina-tray-listening.ico',
        'unins000.exe'
    )
    foreach ($relativePath in $requiredFiles) {
        $candidate = Join-Path $installDirectory $relativePath
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "Installed file was missing: $candidate"
        }
    }

    if (@(Get-InstalledResidentProcesses -InstallDirectory $installDirectory).Count -ne 0) {
        throw 'Silent install left a Keyina resident running.'
    }

    $hostPath = Join-Path $installDirectory 'Keyina.Host.exe'
    $residentPath = Join-Path $installDirectory 'KeyinaInput.exe'
    $versionResult = Invoke-CheckedCapturedProcess $hostPath '--version' $installDirectory
    $reportedVersion = (($versionResult.StandardOutput -split "`r?`n") | Select-Object -First 1).Trim()
    if ($reportedVersion -ne $Version) {
        throw "Installed host reports '$reportedVersion'; expected '$Version'."
    }

    foreach ($selfTest in @('--self-test', '--speech-self-test', '--hotkey-self-test')) {
        $null = Invoke-CheckedCapturedProcess $hostPath $selfTest $installDirectory
    }
    foreach ($selfTest in @('--self-test', '--resource-self-test', '--tray-resource-self-test', '--profile-reload-self-test')) {
        $null = Invoke-CheckedCapturedProcess $residentPath $selfTest $installDirectory
    }

    if (-not (Test-Path -LiteralPath $sentinelPath -PathType Leaf)) {
        throw 'Silent install removed user settings data.'
    }

    Invoke-CheckedProcess `
        -FilePath $InstallerPath `
        -ArgumentList ($commonInstallArguments + "/LOG=`"$upgradeLog`"") `
        -WorkingDirectory $sandboxRoot

    if (@(Get-InstalledResidentProcesses -InstallDirectory $installDirectory).Count -ne 0) {
        throw 'Silent upgrade left a Keyina resident running.'
    }
    if (-not (Test-Path -LiteralPath $sentinelPath -PathType Leaf)) {
        throw 'Silent upgrade removed user settings data.'
    }
    $null = Invoke-CheckedCapturedProcess $residentPath '--self-test' $installDirectory

    New-Item -ItemType Directory -Path $runRegistryPath -Force | Out-Null
    Set-ItemProperty `
        -LiteralPath $runRegistryPath `
        -Name 'Keyina' `
        -Value ('"' + $residentPath + '"') `
        -Type String
    Set-Content -LiteralPath $legacyStartupShortcut -Value 'legacy startup shortcut' -Encoding ASCII

    Invoke-CheckedProcess `
        -FilePath $uninstallerPath `
        -ArgumentList @(
            '/VERYSILENT',
            '/SUPPRESSMSGBOXES',
            '/NORESTART',
            "/LOG=`"$uninstallLog`""
        ) `
        -WorkingDirectory $installDirectory
    $installed = $false

    Wait-PathAbsent -Path $installDirectory
    if (@(Get-InstalledResidentProcesses -InstallDirectory $installDirectory).Count -ne 0) {
        throw 'Uninstall left a Keyina resident running.'
    }
    if ($null -ne (Get-ItemProperty -LiteralPath $runRegistryPath -Name 'Keyina' -ErrorAction SilentlyContinue)) {
        throw 'Uninstall left the Keyina startup registry value behind.'
    }
    if (Test-Path -LiteralPath $legacyStartupShortcut) {
        throw 'Uninstall left the legacy Startup shortcut behind.'
    }
    if (-not (Test-Path -LiteralPath $sentinelPath -PathType Leaf)) {
        throw 'Uninstall removed user settings data.'
    }

    Write-Host "Installer lifecycle verified: $InstallerPath" -ForegroundColor Green
    Write-Host "Version: $Version"
    Write-Host "Sandbox: $sandboxRoot"
} finally {
    if ($installed -and (Test-Path -LiteralPath $uninstallerPath -PathType Leaf)) {
        try {
            Invoke-CheckedProcess `
                -FilePath $uninstallerPath `
                -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') `
                -WorkingDirectory $installDirectory
        } catch {
            Write-Warning "Best-effort uninstall failed: $($_.Exception.Message)"
        }
    }

    if ($originalRunValuePresent) {
        New-Item -ItemType Directory -Path $runRegistryPath -Force | Out-Null
        Set-ItemProperty `
            -LiteralPath $runRegistryPath `
            -Name 'Keyina' `
            -Value $originalRunValue `
            -Type String
    } else {
        Remove-ItemProperty `
            -LiteralPath $runRegistryPath `
            -Name 'Keyina' `
            -ErrorAction SilentlyContinue
    }

    if ($originalShortcutPresent -and (Test-Path -LiteralPath $legacyStartupBackup -PathType Leaf)) {
        Copy-Item -LiteralPath $legacyStartupBackup -Destination $legacyStartupShortcut -Force
    } elseif (-not $originalShortcutPresent) {
        Remove-Item -LiteralPath $legacyStartupShortcut -Force -ErrorAction SilentlyContinue
    }

    Remove-Item -LiteralPath $sentinelDirectory -Recurse -Force -ErrorAction SilentlyContinue
    if (-not $KeepSandbox) {
        Remove-Item -LiteralPath $sandboxRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
