[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SignToolPath {
    if (-not [string]::IsNullOrWhiteSpace($env:KEYINA_SIGNTOOL_PATH)) {
        $configured = [System.IO.Path]::GetFullPath($env:KEYINA_SIGNTOOL_PATH)
        if (-not (Test-Path -LiteralPath $configured -PathType Leaf)) {
            throw "KEYINA_SIGNTOOL_PATH does not exist: $configured"
        }
        return $configured
    }

    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $candidate = Get-ChildItem -LiteralPath $sdkRoot -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw 'signtool.exe was not found. Install the Windows SDK or set KEYINA_SIGNTOOL_PATH.'
    }
    return $candidate.FullName
}

$target = [System.IO.Path]::GetFullPath($Path)
$signTool = Get-SignToolPath
$timestampUrl = if ([string]::IsNullOrWhiteSpace($env:KEYINA_SIGN_TIMESTAMP_URL)) {
    'http://timestamp.digicert.com'
} else {
    $env:KEYINA_SIGN_TIMESTAMP_URL
}

$arguments = @(
    'sign',
    '/fd', 'SHA256',
    '/td', 'SHA256',
    '/tr', $timestampUrl,
    '/d', 'Keyina'
)

if (-not [string]::IsNullOrWhiteSpace($env:KEYINA_SIGN_CERT_THUMBPRINT)) {
    $arguments += @('/sha1', $env:KEYINA_SIGN_CERT_THUMBPRINT.Replace(' ', ''))
    if ([string]::Equals($env:KEYINA_SIGN_CERT_STORE, 'LocalMachine', [System.StringComparison]::OrdinalIgnoreCase)) {
        $arguments += '/sm'
    }
} elseif (-not [string]::IsNullOrWhiteSpace($env:KEYINA_SIGN_PFX_PATH)) {
    $pfxPath = [System.IO.Path]::GetFullPath($env:KEYINA_SIGN_PFX_PATH)
    if (-not (Test-Path -LiteralPath $pfxPath -PathType Leaf)) {
        throw "KEYINA_SIGN_PFX_PATH does not exist: $pfxPath"
    }
    $arguments += @('/f', $pfxPath)
    if (-not [string]::IsNullOrWhiteSpace($env:KEYINA_SIGN_PFX_PASSWORD)) {
        $arguments += @('/p', $env:KEYINA_SIGN_PFX_PASSWORD)
    }
} else {
    throw 'No signing identity configured. Set KEYINA_SIGN_CERT_THUMBPRINT or KEYINA_SIGN_PFX_PATH.'
}

$arguments += $target
& $signTool @arguments
if ($LASTEXITCODE -ne 0) {
    throw "SignTool failed for $target with exit code $LASTEXITCODE."
}

& $signTool verify /pa /all /v $target
if ($LASTEXITCODE -ne 0) {
    throw "Signature verification failed for $target with exit code $LASTEXITCODE."
}
