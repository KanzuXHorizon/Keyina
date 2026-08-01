[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Baseline,

    [Parameter(Mandatory = $true)]
    [string]$Current,

    [Parameter(Mandatory = $true)]
    [string]$Thresholds
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-JsonDocument {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label file does not exist: $Path"
    }

    try {
        return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "$Label file is not valid JSON: $Path. $($_.Exception.Message)"
    }
}

function Get-RequiredNumber {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if ($Value -is [bool] -or $Value -isnot [ValueType]) {
        throw "$Label must be a non-negative number."
    }

    $number = [double]$Value
    if ([double]::IsNaN($number) -or [double]::IsInfinity($number) -or $number -lt 0) {
        throw "$Label must be a finite non-negative number."
    }
    return $number
}

function New-CaseMap {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Document,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if ($null -eq $Document.SchemaVersion -or [int]$Document.SchemaVersion -ne 1) {
        throw "$Label SchemaVersion must be 1."
    }
    if ($null -eq $Document.Cases) {
        throw "$Label Cases must be an array."
    }

    $map = [System.Collections.Generic.Dictionary[string, object]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($case in @($Document.Cases)) {
        if ($null -eq $case -or [string]::IsNullOrWhiteSpace([string]$case.Name)) {
            throw "$Label contains a benchmark with an invalid Name."
        }
        $name = [string]$case.Name
        if ($map.ContainsKey($name)) {
            throw "$Label contains duplicate benchmark case '$name'."
        }
        [void](Get-RequiredNumber $case.MedianNanoseconds "$Label case '$name' MedianNanoseconds")
        [void](Get-RequiredNumber $case.P95Nanoseconds "$Label case '$name' P95Nanoseconds")
        $map.Add($name, $case)
    }
    return $map
}

function Get-ObjectProperty {
    param(
        [AllowNull()]
        [object]$Object,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

function Get-MetricPolicy {
    param(
        [Parameter(Mandatory = $true)]
        [object]$ThresholdDocument,

        [Parameter(Mandatory = $true)]
        [string]$CaseName,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Median', 'P95')]
        [string]$Metric
    )

    $defaultPolicy = Get-ObjectProperty $ThresholdDocument.Defaults $Metric
    if ($null -eq $defaultPolicy) {
        throw "Threshold Defaults.$Metric is required."
    }

    $casePolicy = Get-ObjectProperty $ThresholdDocument.Cases $CaseName
    $metricOverride = Get-ObjectProperty $casePolicy $Metric
    $relativeValue = Get-ObjectProperty $metricOverride 'RelativeTolerance'
    if ($null -eq $relativeValue) {
        $relativeValue = Get-ObjectProperty $defaultPolicy 'RelativeTolerance'
    }
    $absoluteValue = Get-ObjectProperty $metricOverride 'AbsoluteToleranceNanoseconds'
    if ($null -eq $absoluteValue) {
        $absoluteValue = Get-ObjectProperty $defaultPolicy 'AbsoluteToleranceNanoseconds'
    }

    return [pscustomobject]@{
        RelativeTolerance = Get-RequiredNumber `
            $relativeValue `
            "Threshold $CaseName.$Metric.RelativeTolerance"
        AbsoluteToleranceNanoseconds = Get-RequiredNumber `
            $absoluteValue `
            "Threshold $CaseName.$Metric.AbsoluteToleranceNanoseconds"
    }
}

$baselineDocument = Read-JsonDocument $Baseline 'Baseline benchmark'
$currentDocument = Read-JsonDocument $Current 'Current benchmark'
$thresholdDocument = Read-JsonDocument $Thresholds 'Threshold configuration'
if ($null -eq $thresholdDocument.SchemaVersion -or
    [int]$thresholdDocument.SchemaVersion -ne 1) {
    throw 'Threshold SchemaVersion must be 1.'
}
if ($null -eq $thresholdDocument.Defaults) {
    throw 'Threshold Defaults is required.'
}
if ($null -eq $thresholdDocument.Cases) {
    throw 'Threshold Cases is required, even when empty.'
}

$baselineCases = New-CaseMap $baselineDocument 'Baseline benchmark'
$currentCases = New-CaseMap $currentDocument 'Current benchmark'
$regressions = [System.Collections.Generic.List[string]]::new()

foreach ($entry in $baselineCases.GetEnumerator()) {
    $name = $entry.Key
    $baselineCase = $entry.Value
    if (-not $currentCases.ContainsKey($name)) {
        $regressions.Add("${name}: missing from current benchmark results.")
        continue
    }

    $currentCase = $currentCases[$name]
    foreach ($metric in @(
        @{ Policy = 'Median'; Property = 'MedianNanoseconds' },
        @{ Policy = 'P95'; Property = 'P95Nanoseconds' }
    )) {
        $property = [string]$metric.Property
        $baselineValue = Get-RequiredNumber `
            $baselineCase.$property `
            "Baseline case '$name' $property"
        $currentValue = Get-RequiredNumber `
            $currentCase.$property `
            "Current case '$name' $property"
        $policy = Get-MetricPolicy $thresholdDocument $name ([string]$metric.Policy)
        $allowedDelta = [Math]::Max(
            $policy.AbsoluteToleranceNanoseconds,
            $baselineValue * $policy.RelativeTolerance)
        $limit = $baselineValue + $allowedDelta
        if ($currentValue -gt $limit) {
            $relativePercent = if ($baselineValue -eq 0) {
                [double]::PositiveInfinity
            } else {
                (($currentValue / $baselineValue) - 1.0) * 100.0
            }
            $regressions.Add(
                "${name} ${property}: $baselineValue ns -> $currentValue ns " +
                "($relativePercent percent); limit $limit ns " +
                "(relative $($policy.RelativeTolerance), absolute " +
                "$($policy.AbsoluteToleranceNanoseconds) ns).")
        }
    }
}

if ($regressions.Count -gt 0) {
    foreach ($regression in $regressions) {
        [Console]::Error.WriteLine($regression)
    }
    exit 1
}

Write-Host (
    "Benchmark comparison passed: {0} baseline cases, median and p95 within configured tolerances." -f
        $baselineCases.Count)
exit 0
