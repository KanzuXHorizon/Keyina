[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourceDirectory,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$installerScript = Join-Path $repoRoot 'installer\Keyina.iss'
$SourceDirectory = [System.IO.Path]::GetFullPath($SourceDirectory)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
    throw "Lifecycle installer source directory not found: $SourceDirectory"
}
foreach ($requiredFile in @('KeyinaInput.exe', 'Keyina.Host.exe', 'KeyinaEngine.dll')) {
    $candidate = Join-Path $SourceDirectory $requiredFile
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Lifecycle installer source is missing: $candidate"
    }
}

$candidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
)
$iscc = $candidates |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($iscc)) {
    throw 'Inno Setup compiler was not found.'
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$identifier = [Guid]::NewGuid().ToString('D').ToUpperInvariant()
$appId = "{{$identifier}"
$outputBaseName = "Keyina-Lifecycle-$([Guid]::NewGuid().ToString('N'))"
$arguments = @(
    "/DMyAppVersion=$Version",
    "/DSourceDir=$SourceDirectory",
    "/DOutputDir=$OutputDirectory",
    "/DMyAppId=$appId",
    "/DMyOutputBaseFilename=$outputBaseName",
    $installerScript
)

& $iscc @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup lifecycle compilation failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $OutputDirectory "$outputBaseName.exe"
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Lifecycle installer output not found: $installerPath"
}
Write-Output $installerPath
