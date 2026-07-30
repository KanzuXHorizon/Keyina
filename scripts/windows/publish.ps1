[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$OutputDirectory = 'artifacts/publish/win-x64'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$outputPath = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot $OutputDirectory))

$cmakePreset = if ($Configuration -eq 'Release') {
    'windows-msvc-release'
} else {
    'windows-msvc-debug'
}
$nativeBuildRoot = Join-Path $repositoryRoot "build/$cmakePreset"
$nativeInput = Join-Path $nativeBuildRoot "platform/windows/input/$Configuration/KeyinaInput.exe"
$nativeEngine = Join-Path $nativeBuildRoot "platform/windows/hook/$Configuration/KeyinaEngine.dll"
$managedProject = Join-Path $repositoryRoot 'apps/host/Keyina.Host/Keyina.Host.csproj'

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
Set-Location $repositoryRoot

$publishArguments = @(
    'publish',
    $managedProject,
    '-c', $Configuration,
    '-r', 'win-x64',
    '--self-contained', 'false',
    '-o', $outputPath
)
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

& cmake --preset $cmakePreset
if ($LASTEXITCODE -ne 0) {
    throw "CMake configure failed with exit code $LASTEXITCODE."
}
& cmake --build --preset $cmakePreset
if ($LASTEXITCODE -ne 0) {
    throw "CMake build failed with exit code $LASTEXITCODE."
}

Copy-Item -Force $nativeInput (Join-Path $outputPath 'KeyinaInput.exe')
Copy-Item -Force $nativeEngine (Join-Path $outputPath 'KeyinaEngine.dll')
Copy-Item -Force (Join-Path $repositoryRoot 'brand/generated/keyina-tray-active.ico') (Join-Path $outputPath 'keyina-tray-active.ico')
Copy-Item -Force (Join-Path $repositoryRoot 'brand/generated/keyina-tray-inactive.ico') (Join-Path $outputPath 'keyina-tray-inactive.ico')

$requiredFiles = @(
    'KeyinaInput.exe',
    'Keyina.Host.exe',
    'Keyina.Host.dll',
    'KeyinaEngine.dll',
    'keyina-tray-active.ico',
    'keyina-tray-inactive.ico'
)
foreach ($file in $requiredFiles) {
    $path = Join-Path $outputPath $file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Publish bundle is missing required file: $file"
    }
}

Write-Host "Keyina Windows bundle published to: $outputPath"
