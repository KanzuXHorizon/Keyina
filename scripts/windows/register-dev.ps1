[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$BuildPreset = 'windows-msvc-debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'TSF profile registration requires an elevated PowerShell session. Build and unit tests do not require elevation.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$buildDirectory = Join-Path $repoRoot "build\$BuildPreset\platform\windows\tsf\$Configuration"
$dllPath = Join-Path $buildDirectory 'KeyinaTsf.dll'
$clsid = '{D66D2599-6B75-4AFF-95B3-476C310CDE70}'
$registryPath = "HKCU:\Software\Classes\CLSID\$clsid\InprocServer32"
$tsfProfilePath = "HKLM:\SOFTWARE\Microsoft\CTF\TIP\$clsid"

if (-not (Test-Path -LiteralPath $dllPath)) {
    throw "KeyinaTsf.dll was not found at $dllPath. Build the matching preset first."
}

$process = Start-Process `
    -FilePath "$env:SystemRoot\System32\regsvr32.exe" `
    -ArgumentList @('/s', $dllPath) `
    -Wait `
    -PassThru
if ($process.ExitCode -ne 0) {
    throw "regsvr32 registration failed with exit code $($process.ExitCode)"
}

if (-not (Test-Path -LiteralPath $registryPath)) {
    throw "COM registration was not created at $registryPath"
}
if (-not (Test-Path -LiteralPath $tsfProfilePath)) {
    throw "TSF profile registration was not created at $tsfProfilePath"
}

$registeredPath = (Get-ItemProperty -LiteralPath $registryPath).'(default)'
if (-not [string]::Equals($registeredPath, $dllPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Registered DLL path does not match the requested developer build: $registeredPath"
}

Write-Host "Registered Keyina TSF developer build: $dllPath"
Write-Host 'The profile is registered but not automatically selected as the active keyboard.'
