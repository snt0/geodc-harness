<#
.SYNOPSIS
    Audits every Sample gallery group and every app page and prints a short summary of the findings.

.DESCRIPTION
    For each surface the script navigates there with the lib/ state scripts, runs `audit`, and saves the raw JSON lines
    to -OutFile. It then prints one line per surface with its counts by rule, followed by the id-naming, duplicate-id,
    reserved-id and raw-name findings in full. missing-id findings are counted by control type rather than listed.
    The Sample gallery and the app must already be running, built from the branch under test. Run it from the
    harness root; the shim drives the geoDC clone at geodc/.

.EXAMPLE
    pwsh -NoProfile -File dev/ui-scripts/audit-summary.ps1
    pwsh -NoProfile -File dev/ui-scripts/audit-summary.ps1 -Target app -OutFile artifacts/audit.jsonl
#>
param(
    [ValidateSet('sample', 'app', 'both')]
    [string]$Target = 'both',
    [string]$OutFile = 'artifacts/ui-driver/audit.jsonl'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
$driver = Join-Path $root 'dev/ui.ps1'
New-Item -ItemType Directory -Force (Split-Path $OutFile) | Out-Null
Set-Content -Path $OutFile -Value $null

$groups = 'Foundations', 'Actions', 'Inputs', 'Quantities', 'Conventions', 'Collections', 'Cards', 'Sections',
    'Progress', 'Navigation', 'Feedback', 'Overlays', 'Chrome'
$pages = 'LandingTab', 'SetupTab', 'SurveyTab', 'CalibrationTab', 'VerificationTab', 'ShoreControlTab', 'GnssTab',
    'ReportsTab'

$surfaces = @()
if ($Target -in 'sample', 'both') {
    $surfaces += $groups | ForEach-Object {
        [pscustomobject]@{ Label = "sample $_"; Args = @('do', '--target', 'sample', 'run', 'lib/sample-goto-page',
            "nav=$($_)NavItem", '++', 'audit', '--scope', "$($_)Page") }
    }
}
if ($Target -in 'app', 'both') {
    $surfaces += $pages | ForEach-Object {
        [pscustomobject]@{ Label = "app $_"; Args = @('do', 'run', 'lib/app-goto-page', "page=$_", '++', 'settle',
            '--ms', '500', '++', 'audit', '--main-window') }
    }
}

$results = foreach ($surface in $surfaces) {
    $lines = & pwsh -NoProfile -File $driver @($surface.Args) 2>&1 | ForEach-Object { "$_" }
    $lines | ForEach-Object { "$($surface.Label)`t$_" } | Add-Content -Path $OutFile
    $audit = $lines | Where-Object { $_ -match '"verb":"audit"' } | Select-Object -Last 1 | ConvertFrom-Json
    $failed = $lines | Where-Object { $_ -match '"ok":false' -and $_ -notmatch '"verb":"(audit|do)"' } |
        Select-Object -First 1
    $body = if ($audit) { if ($audit.ok) { $audit.data } else { $audit.error } }
    [pscustomobject]@{ Label = $surface.Label; Audit = $body; Failed = $failed }
}

Write-Host "`n== Counts (missing-id / id-naming / duplicate-id / reserved-id / raw-name)"
foreach ($result in $results) {
    if (-not $result.Audit) {
        Write-Host ("{0,-24} did not audit: {1}" -f $result.Label, $result.Failed)
        continue
    }

    $by = $result.Audit.byRule
    Write-Host ("{0,-24} {1,4} {2,4} {3,4} {4,4} {5,4}   walked {6}" -f $result.Label, $by.'missing-id',
        $by.'id-naming', $by.'duplicate-id', $by.'reserved-id', $by.'raw-name', $result.Audit.walked)
}

Write-Host "`n== missing-id by control type"
foreach ($result in $results | Where-Object { $_.Audit.findings }) {
    $missing = $result.Audit.findings | Where-Object rule -eq 'missing-id' | Group-Object role |
        Sort-Object Count -Descending | ForEach-Object { "$($_.Count) $($_.Name)" }
    if ($missing) { Write-Host ("{0,-24} {1}" -f $result.Label, ($missing -join ', ')) }
}

Write-Host "`n== Other findings"
foreach ($result in $results | Where-Object { $_.Audit.findings }) {
    foreach ($finding in $result.Audit.findings | Where-Object rule -ne 'missing-id') {
        $where = ($finding.path -join '/')
        Write-Host ("{0,-24} {1,-12} {2} (x{3}) [{4}] {5}" -f $result.Label, $finding.rule,
            ($finding.id ?? $finding.name), $finding.occurrences, $finding.role, $where)
    }
}

Write-Host "`nRaw output: $OutFile"
