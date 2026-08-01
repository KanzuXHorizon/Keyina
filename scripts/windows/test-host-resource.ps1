[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$HostPath,

    [string]$WorkingDirectory,

    [ValidateRange(1, 5)]
    [int]$RequiredSamples = 3,

    [ValidateRange(1, 8)]
    [int]$MaximumAttempts = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$HostPath = [System.IO.Path]::GetFullPath($HostPath)
if (-not (Test-Path -LiteralPath $HostPath -PathType Leaf)) {
    throw "Host executable not found: $HostPath"
}
if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
    $WorkingDirectory = Split-Path -Parent $HostPath
}
$WorkingDirectory = [System.IO.Path]::GetFullPath($WorkingDirectory)
if ($MaximumAttempts -lt $RequiredSamples) {
    throw 'MaximumAttempts must be greater than or equal to RequiredSamples.'
}

$cleanSnapshots = @()
for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $HostPath
    $startInfo.Arguments = '--resource-self-test'
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Could not start $HostPath."
    }
    try {
        $standardOutput = $process.StandardOutput.ReadToEnd()
        $standardError = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        $exitCode = $process.ExitCode
    } finally {
        $process.Dispose()
    }

    $lines = @(
        $standardOutput -split "`r?`n" |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($lines.Count -eq 0) {
        throw "Resource self-test produced no JSON output. stderr: $standardError"
    }
    try {
        $snapshot = $lines[-1] | ConvertFrom-Json
    } catch {
        throw "Resource self-test returned invalid JSON: $($lines[-1])"
    }

    Write-Host $lines[-1]
    if (-not [bool]$snapshot.typing_hook_running) {
        throw "Resource self-test hook was not running. stderr: $standardError"
    }
    if ([int]$snapshot.thread_count_delta -gt 1) {
        throw "Resource self-test exceeded the one-thread budget: $($snapshot.thread_count_delta)."
    }
    if ([bool]$snapshot.measurement_contaminated_by_input) {
        Write-Warning "Resource probe was contaminated by physical input; discarding attempt $attempt."
        continue
    }

    $cleanSnapshots += $snapshot
    if ($cleanSnapshots.Count -ge $RequiredSamples) {
        break
    }

    if ($exitCode -ne 0 -and
        [long]$snapshot.private_memory_bytes -le
            [long]$snapshot.private_memory_budget_bytes) {
        throw "Resource self-test failed for an unknown reason. stderr: $standardError"
    }
}

if ($cleanSnapshots.Count -lt $RequiredSamples) {
    throw "Only $($cleanSnapshots.Count) clean resource sample(s) were collected; $RequiredSamples required."
}

$orderedPrivateMemory = @(
    $cleanSnapshots |
        ForEach-Object { [long]$_.private_memory_bytes } |
        Sort-Object
)
$medianIndex = [int][Math]::Floor($orderedPrivateMemory.Count / 2)
$medianPrivateMemory = [long]$orderedPrivateMemory[$medianIndex]
$budget = [long]$cleanSnapshots[0].private_memory_budget_bytes
if ($medianPrivateMemory -gt $budget) {
    throw "Median private memory $medianPrivateMemory exceeded budget $budget."
}

Write-Host (
    "Host resource verification passed: median_private_memory_bytes={0}; " +
    "budget_bytes={1}; clean_samples={2}; attempts={3}" -f
    $medianPrivateMemory,
    $budget,
    $cleanSnapshots.Count,
    [Math]::Min($MaximumAttempts, $cleanSnapshots.Count)) -ForegroundColor Green
