[CmdletBinding()]
param(
    [switch]$ForceInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$requiredComponent = 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64'

function Get-BuildToolsPath {
    if (-not (Test-Path -LiteralPath $vsWhere)) {
        return $null
    }

    $result = & $vsWhere `
        -latest `
        -products '*' `
        -requires $requiredComponent `
        -property installationPath
    if ($LASTEXITCODE -ne 0) {
        throw "vswhere failed with exit code $LASTEXITCODE"
    }
    return ($result | Select-Object -First 1)
}

$buildToolsPath = Get-BuildToolsPath
if ($ForceInstall -or [string]::IsNullOrWhiteSpace($buildToolsPath)) {
    $winget = Get-Command winget.exe -ErrorAction Stop
    & $winget.Source install `
        --id Microsoft.VisualStudio.2022.BuildTools `
        --exact `
        --silent `
        --accept-package-agreements `
        --accept-source-agreements `
        --disable-interactivity `
        --override '--wait --passive --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended'
    if ($LASTEXITCODE -ne 0) {
        throw "Visual Studio Build Tools installation failed with exit code $LASTEXITCODE"
    }
    $buildToolsPath = Get-BuildToolsPath
}

if ([string]::IsNullOrWhiteSpace($buildToolsPath)) {
    throw 'MSVC x64 build tools were not found after installation.'
}

$cmake = Get-Command cmake.exe -ErrorAction SilentlyContinue
if ($null -eq $cmake) {
    $knownCMake = 'F:\Cmake\bin\cmake.exe'
    if (Test-Path -LiteralPath $knownCMake) {
        $cmake = Get-Item -LiteralPath $knownCMake
    } else {
        throw 'CMake 3.25 or newer is required. Install CMake and place it on PATH.'
    }
}

Write-Host "MSVC Build Tools: $buildToolsPath"
Write-Host "CMake: $($cmake.FullName)"
Write-Host 'Configure: cmake --preset windows-msvc-debug'
Write-Host 'Build:     cmake --build --preset windows-msvc-debug'
Write-Host 'Test:      ctest --preset windows-msvc-debug'
