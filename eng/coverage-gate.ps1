[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Report,
    [string] $RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string] $BaseRef,
    [double] $GlobalLineThreshold = 75,
    [double] $GlobalBranchThreshold = 65,
    [double] $CoreLineThreshold = 90,
    [double] $CoreBranchThreshold = 85,
    [double] $CriticalLineThreshold = 98,
    [double] $CriticalBranchThreshold = 95,
    [double] $NewLineThreshold = 90,
    [double] $NewBranchThreshold = 85
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Report)) {
    throw "Cobertura report not found: $Report"
}

[xml] $coverage = Get-Content -LiteralPath $Report -Raw
$classes = @($coverage.coverage.packages.package.classes.class)
if ($classes.Count -eq 0) {
    throw "The Cobertura report contains no instrumented classes."
}

function Normalize-Path([string] $Path) {
    return ($Path -replace '\\', '/').TrimStart('./')
}

function Get-LineRecords([object[]] $SelectedClasses) {
    foreach ($class in $SelectedClasses) {
        $file = Normalize-Path ([string] $class.filename)
        foreach ($line in @($class.lines.line)) {
            $conditionTotal = 0
            $conditionCovered = 0
            if ([string] $line.'condition-coverage' -match '\((\d+)\s*/\s*(\d+)\)') {
                $conditionCovered = [int] $Matches[1]
                $conditionTotal = [int] $Matches[2]
            }

            [pscustomobject]@{
                File = $file
                Number = [int] $line.number
                Covered = ([int] $line.hits -gt 0)
                BranchCovered = $conditionCovered
                BranchTotal = $conditionTotal
            }
        }
    }
}

function Assert-Coverage(
    [string] $Name,
    [object[]] $Records,
    [double] $LineThreshold,
    [double] $BranchThreshold,
    [switch] $AllowEmpty) {
    if ($Records.Count -eq 0) {
        if ($AllowEmpty) {
            Write-Host "${Name}: no coverable lines changed; gate is not applicable."
            return $true
        }

        Write-Error "${Name}: no instrumented lines were found."
        return $false
    }

    $coveredLines = @($Records | Where-Object Covered).Count
    $lineRate = 100 * $coveredLines / $Records.Count
    $branchTotal = ($Records | Measure-Object BranchTotal -Sum).Sum
    $branchCovered = ($Records | Measure-Object BranchCovered -Sum).Sum
    $branchRate = if ($branchTotal -eq 0) { 100 } else { 100 * $branchCovered / $branchTotal }
    $passed = $lineRate -ge $LineThreshold -and $branchRate -ge $BranchThreshold
    $status = if ($passed) { 'PASS' } else { 'FAIL' }
    Write-Host ("{0}: {1} - lines {2:N2}% ({3}/{4}, minimum {5}%), branches {6:N2}% ({7}/{8}, minimum {9}%)" -f
        $Name, $status, $lineRate, $coveredLines, $Records.Count, $LineThreshold,
        $branchRate, $branchCovered, $branchTotal, $BranchThreshold)
    return $passed
}

$allRecords = @(Get-LineRecords $classes)
$coreClasses = @($classes | Where-Object {
    ([string] $_.name).StartsWith('VisualNotes.Core.') -or
    (Normalize-Path ([string] $_.filename)) -match '(^|/)VisualNotes\.Core/'
})

$criticalPatterns = @(
    'BoundingBoxNormalizer.cs',              # box normalization
    'CaptureDuplicateDetection.cs',          # duplicate-result cache rules
    'EffectiveSettingsResolver.cs',          # effective configuration
    'HierarchicalSettings.cs',               # configuration model and provenance
    'DurableAnalysisJobProcessor.cs',         # durable queue transitions
    'StructuredAnalysisResponses.cs',         # response-retention privacy
    'PortableSettingsService.cs',             # secret-removal privacy
    'DiagnosticPackageService.cs'             # diagnostic allow-list privacy
)
$criticalClasses = @($classes | Where-Object {
    $file = Normalize-Path ([string] $_.filename)
    @($criticalPatterns | Where-Object { $file.EndsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
})

$passed = $true
$passed = (Assert-Coverage 'Global coverage' $allRecords $GlobalLineThreshold $GlobalBranchThreshold) -and $passed
$passed = (Assert-Coverage 'Core coverage' @(Get-LineRecords $coreClasses) $CoreLineThreshold $CoreBranchThreshold) -and $passed
$passed = (Assert-Coverage 'Critical-code coverage' @(Get-LineRecords $criticalClasses) $CriticalLineThreshold $CriticalBranchThreshold) -and $passed

Push-Location $RepositoryRoot
try {
    if ([string]::IsNullOrWhiteSpace($BaseRef)) {
        git rev-parse --verify 'HEAD^' *> $null
        if ($LASTEXITCODE -eq 0) { $BaseRef = 'HEAD^' }
    }

    $changedLines = @{}
    if (-not [string]::IsNullOrWhiteSpace($BaseRef)) {
        $mergeBase = (git merge-base $BaseRef HEAD).Trim()
        if ($LASTEXITCODE -ne 0) { throw "Unable to find merge base for '$BaseRef'." }
        $currentFile = $null
        foreach ($diffLine in (git diff --unified=0 --no-renames $mergeBase HEAD -- '*.cs')) {
            if ($diffLine -match '^\+\+\+ b/(.+)$') {
                $currentFile = Normalize-Path $Matches[1]
            }
            elseif ($currentFile -and $diffLine -match '^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@') {
                $start = [int] $Matches[1]
                $count = if ($Matches[2]) { [int] $Matches[2] } else { 1 }
                for ($number = $start; $number -lt ($start + $count); $number++) {
                    $changedLines["$currentFile`:$number"] = $true
                }
            }
        }
    }

    $newRecords = @($allRecords | Where-Object {
        $record = $_
        $suffix = "$($record.File):$($record.Number)"
        @($changedLines.Keys | Where-Object { $_ -eq $suffix -or $_.EndsWith("/$suffix") }).Count -gt 0
    })
    $passed = (Assert-Coverage 'New-code coverage' $newRecords $NewLineThreshold $NewBranchThreshold -AllowEmpty) -and $passed
}
finally {
    Pop-Location
}

if (-not $passed) {
    Write-Error 'One or more coverage quality gates failed.'
    exit 1
}
