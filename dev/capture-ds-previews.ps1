#Requires -Version 7
<#
.SYNOPSIS
    Writes the Sample gallery's screenshot corpus and manifest, dark and light.

.DESCRIPTION
    Run on Windows from the repository root: pwsh -NoProfile -File dev/capture-ds-previews.ps1

    1. tools/geoDC.GalleryCorpus renders every registered specimen offscreen and writes
           <OutDir>/dark/<Id>.png, <OutDir>/light/<Id>.png and <OutDir>/manifest.json
    2. Specimens the manifest marks requiresLiveWindow are captured through dev/ui.ps1 from the running
       gallery and added to the manifest with source "ui-driver".
    3. Everything is zipped to <OutDir>.zip.

    See "Screenshot corpus" in docs/ui/sample-gallery.md; dev/sync-design-system.ps1 turns the corpus into
    design-system cards ("Design-system sync").

.PARAMETER OutDir
    Defaults to dev/captures/ds-previews (dev/captures is gitignored).

.PARAMETER Dpi
    Offscreen capture resolution. Defaults to 96 (one pixel per device-independent pixel).

.PARAMETER OffscreenOnly
    Skip the UI driver: write only the offscreen captures and leave live-window specimens without images.

.PARAMETER PageShots
    Also take one whole-window UI driver shot per gallery group (Page_<Group>.png). They show only what fits in the
    window and are not listed in the manifest.

.PARAMETER Active
    Launch the gallery in active mode (comes to the front) instead of passive.
#>
param(
    [string] $OutDir = 'dev/captures/ds-previews',
    [double] $Dpi = 96,
    [switch] $OffscreenOnly,
    [switch] $PageShots,
    [switch] $Active
)

$ErrorActionPreference = 'Stop'
# geoDC is the nested clone at geodc/ (bootstrap.ps1). The UI driver runs from inside it, where its instance file
# resolves.
$geodc = Join-Path (Split-Path $PSScriptRoot -Parent) 'geodc'
$driver = Join-Path $geodc 'dev/ui.ps1'
$tool = Join-Path $geodc 'tools/geoDC.GalleryCorpus/geoDC.GalleryCorpus.csproj'
if (-not (Test-Path $driver) -or -not (Test-Path $tool)) { throw 'geodc/ has no dev/ui.ps1 or corpus tool; run bootstrap.ps1 first.' }

New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
$failures = [System.Collections.Generic.List[string]]::new()

# 1. Offscreen corpus. Exit code 1 means some figures failed (listed on stderr) but the rest were written.
& dotnet run --project $tool --configuration Release -- --out $OutDir --dpi $Dpi.ToString([cultureinfo]::InvariantCulture)
if ($LASTEXITCODE -eq 1) { $failures.Add('offscreen: some specimens failed to render (see FAIL lines above)') }
elseif ($LASTEXITCODE -ne 0) { throw "The corpus tool failed with exit code $LASTEXITCODE." }

$manifestPath = Join-Path $OutDir 'manifest.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$live = @($manifest.entries | Where-Object { $_.requiresLiveWindow })

function Invoke-UiDriver([string[]] $a) {
    $a = @($a[0], '--target', 'sample') + @($a | Select-Object -Skip 1)
    $lines = & pwsh -NoProfile -WorkingDirectory $geodc -File $driver @a 2>$null
    $json = @($lines | Where-Object { $_ -match '^\s*\{' } | ForEach-Object { $_ | ConvertFrom-Json })
    $bad = $json | Where-Object { -not $_.ok } | Select-Object -First 1
    if ($bad) { return $bad.error }
    return $null
}

# Width and height from a PNG's IHDR chunk (big-endian at bytes 16-23).
function Get-PngSize([string] $path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $w = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
    $h = ([int]$bytes[20] -shl 24) -bor ([int]$bytes[21] -shl 16) -bor ([int]$bytes[22] -shl 8) -bor [int]$bytes[23]
    return @($w, $h)
}

# 2. Live-window specimens (and optional page shots) through the UI driver.
if (-not $OffscreenOnly -and ($live.Count -gt 0 -or $PageShots)) {
    Invoke-UiDriver @('kill') | Out-Null
    $err = if ($Active) { Invoke-UiDriver @('attach', '--launch') } else { Invoke-UiDriver @('attach', '--launch', '--passive') }
    if ($err) { throw "Could not launch the Sample gallery: $err" }

    try {
        # The gallery starts dark; the theme button toggles.
        $current = 'dark'
        foreach ($theme in $manifest.themes) {
            if ($theme -ne $current) {
                $e = Invoke-UiDriver @('click', '--id', 'ThemeToggleButton'); if ($e) { throw "Theme toggle failed: $e" }
                Invoke-UiDriver @('settle', '--ms', '800') | Out-Null
                $current = $theme
            }
            $dir = Join-Path $OutDir $theme
            New-Item -ItemType Directory -Force $dir | Out-Null

            foreach ($entry in $live) {
                $id = $entry.id; $out = Join-Path $dir "$id.png"
                $e = Invoke-UiDriver @('do', 'click', '--id', $entry.navItemId, '++', 'settle', '--ms', '300')
                if (-not $e) {
                    # not-scrollable-item: nothing to scroll to, so screenshot as is.
                    $e = Invoke-UiDriver @('do', 'scrollto', '--id', $id, '++', 'settle', '--ms', '300')
                    if ($e -and $e.code -eq 'not-scrollable-item') { $e = $null }
                }
                if (-not $e) {
                    $e = Invoke-UiDriver @('screenshot', '--id', $id, '--main-window', '--out', $out)
                }
                if ($e) { $failures.Add("$theme/$id : $e"); [Console]::Error.WriteLine("FAIL $theme/$id"); continue }

                [Console]::Error.WriteLine("ok   $theme/$id (UI driver)")
                $size = Get-PngSize $out
                $capture = [ordered]@{
                    theme = $theme; file = "$theme/$id.png"; pixelWidth = $size[0]; pixelHeight = $size[1]
                    source = 'ui-driver'; pixelsSha256 = $null
                }
                $entry.captures = @(@($entry.captures) + [pscustomobject]$capture)
            }

            if ($PageShots) {
                foreach ($navItemId in @($manifest.entries.navItemId | Select-Object -Unique)) {
                    $name = 'Page_' + ($navItemId -replace 'NavItem$', ''); $out = Join-Path $dir "$name.png"
                    $e = Invoke-UiDriver @('do', 'click', '--id', $navItemId, '++', 'settle', '--ms', '500',
                                 '++', 'screenshot', '--out', $out)
                    if ($e) { $failures.Add("$theme/$name : $e"); [Console]::Error.WriteLine("FAIL $theme/$name") }
                    else { [Console]::Error.WriteLine("ok   $theme/$name") }
                }
            }
        }
    }
    finally {
        Invoke-UiDriver @('kill') | Out-Null
    }

    if ($live.Count -gt 0) {
        ($manifest | ConvertTo-Json -Depth 10) -replace "`r`n", "`n" | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
    }
}

# 3. Zip.
$zip = "$OutDir.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $OutDir '*') -DestinationPath $zip
Write-Host "Wrote $zip"
if ($failures.Count) { Write-Host "$($failures.Count) capture(s) failed:"; $failures | ForEach-Object { Write-Host "  $_" }; exit 1 }
