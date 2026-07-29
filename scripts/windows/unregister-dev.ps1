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
    throw 'TSF profile unregistration requires an elevated PowerShell session.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$buildDirectory = Join-Path $repoRoot "build\$BuildPreset\platform\windows\tsf\$Configuration"
$dllPath = Join-Path $buildDirectory 'KeyinaTsf.dll'
$clsid = '{D66D2599-6B75-4AFF-95B3-476C310CDE70}'
$registryPath = "HKCU:\Software\Classes\CLSID\$clsid"
$tsfProfilePath = "HKLM:\SOFTWARE\Microsoft\CTF\TIP\$clsid"

if (-not (Test-Path -LiteralPath $dllPath)) {
    throw "KeyinaTsf.dll was not found at $dllPath. The exact registered build is required for clean unregistration."
}

$process = Start-Process `
    -FilePath "$env:SystemRoot\System32\regsvr32.exe" `
    -ArgumentList @('/u', '/s', $dllPath) `
    -Wait `
    -PassThru
if ($process.ExitCode -ne 0) {
    throw "regsvr32 unregistration failed with exit code $($process.ExitCode)"
}

if (Test-Path -LiteralPath $registryPath) {
    throw "COM registration still exists at $registryPath"
}
if (Test-Path -LiteralPath $tsfProfilePath) {
    throw "TSF profile registration still exists at $tsfProfilePath"
}

Write-Host "Unregistered Keyina TSF developer build: $dllPath"
