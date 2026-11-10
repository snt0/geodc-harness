#Requires -Version 7
<#
.SYNOPSIS
    Brings a local copy of the geoDC design system up to date with a screenshot corpus and its manifest.

.DESCRIPTION
    Runs anywhere pwsh runs. Reads <Corpus>/manifest.json (written by dev/capture-ds-previews.ps1) and a copy of the
    design system's files (<DesignSystem>/project/...), and writes the updated system to <Out>/project:

    - every card is filed under its GalleryGroup, read from the manifest or, for a card with no specimen page,
      from $CardGroups below;
    - each card's figures (preview.html) and Specimens list (README.md) are regenerated from its specimens and their details;
    - a registered control with no card gets one, documented from its source in src/geoDC.App.UI;
    - a card whose control no longer exists moves to archived/;
    - screenshots are replaced by name under assets/_preview/{dark,light}/, and screenshots no card shows are
      deleted;
    - the Component index in README.md is regenerated, and design-system.json's lastChange records the commit.

    <Out>/changes.json lists the paths to write and delete, and <Out>/changes.md summarises them for review.
    Publishing is a separate step; see "Design-system sync" in docs/ui/sample-gallery.md.

.PARAMETER DesignSystem
    Folder holding a copy of the design system's published files (its project/ folder, including the PNGs under
    project/assets/_preview).

.PARAMETER Corpus
    Folder holding manifest.json and the dark/ and light/ captures. Defaults to dev/captures/ds-previews.

.PARAMETER Out
    Folder to write the updated system to. Replaced on each run. Defaults to dev/captures/ds-sync.

.PARAMETER By
    Who the change is made for, recorded in lastChange.by.
#>
param(
    [Parameter(Mandatory)] [string] $DesignSystem,
    [string] $Corpus = 'dev/captures/ds-previews',
    [string] $Out = 'dev/captures/ds-sync',
    [string] $By = 'Claude'
)

$ErrorActionPreference = 'Stop'
# geoDC is the nested clone at geodc/ (bootstrap.ps1); the toolkit sources the cards are documented from are there.
$repositoryRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'geodc'
$toolkitRoot = Join-Path $repositoryRoot 'src/geoDC.App.UI'

# Card names that differ from the registered control's type name.
$CardNames = @{ 'Control' = 'BuiltInControls'; 'Feedback' = 'FeedbackBehaviour' }

# Groups for cards whose control has no specimen page of its own; their figures are kept as they are.
$CardGroups = @{
    'UiDivider' = 'Foundations'; 'UiWindow' = 'Chrome'; 'UiToolbar' = 'Chrome'; 'UiRibbonGroup' = 'Chrome'
    'UiSectionCard' = 'Cards'
}

$boilerplate = @'
> WPF control from `geoDC.App.UI`. This design system documents a desktop WPF
> application: these are reference specimens, not React components.
'@
$stylingSection = @'
## Styling

Colours, spacing, radius and type come from the tokens in `tokens.css`
(generated from `tokens.json`, whose values come from the WPF theme dictionaries). Use those custom properties rather than
literal values — they carry both the dark and light themes.
'@
$previewStyle = @'
<style>
  body { margin: 0; padding: 24px; background: var(--color-bg-primary); color: var(--color-text-primary);
         font-family: var(--font-ui); font-size: var(--font-size-body); }
  h1 { font-size: var(--font-size-title); margin: 0 0 4px; }
  .sub { color: var(--color-text-secondary); margin: 0 0 20px; }
  .related { font-family: var(--font-mono); font-size: var(--font-size-caption);
              color: var(--color-text-tertiary); margin-bottom: 20px; }
  figure { margin: 0 0 24px; }
  figcaption { font-family: var(--font-mono); font-size: var(--font-size-caption);
                color: var(--color-text-tertiary); margin-bottom: 8px; }
  .pair { display: grid; gap: 12px; }
  img { max-width: 100%; height: auto; display: block;
         border: 1px solid var(--color-border-subtle); border-radius: var(--radius-default); }
  .pending { border: 1px dashed var(--color-border-default); border-radius: var(--radius-default); padding: var(--spacing-large);
             color: var(--color-text-secondary); font-size: var(--font-size-caption); }
  .pending code { font-family: var(--font-mono); }
</style>
'@
# The here-strings take the checkout's line endings; the files written are LF throughout.
$boilerplate, $stylingSection, $previewStyle = ($boilerplate, $stylingSection, $previewStyle) -replace "`r`n", "`n"

function Read-Text([string] $path) { [System.IO.File]::ReadAllText($path) -replace "`r`n", "`n" }
function Write-Text([string] $path, [string] $text) {
    New-Item -ItemType Directory -Force (Split-Path $path -Parent) | Out-Null
    [System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($false))
}
function Html([string] $text) { [System.Net.WebUtility]::HtmlEncode($text) }

# Plain text from an XML doc comment: cref names in backticks, tags dropped, paragraphs kept.
function ConvertFrom-XmlDoc([string] $xml) {
    $text = $xml -replace '<see\s+(?:cref|langword)="(?:[A-Z]:)?([^"]+)"\s*/>', '`$1`'
    $text = $text -replace '<c>(.*?)</c>', '`$1`' -replace '<para>|</para>', "`n`n" -replace '<[^>]+>', ''
    $text = [System.Net.WebUtility]::HtmlDecode($text)
    $paragraphs = $text -split "\n\s*\n" | ForEach-Object { ($_ -replace '\s+', ' ').Trim() } | Where-Object { $_ }
    return $paragraphs -join "`n`n"
}

# Summary and dependency properties of a toolkit type, read from its source file.
function Get-SourceDoc([string] $typeName) {
    $pattern = "^\s*(?:public|internal)[\w\s]*\s(?:class|record|struct|interface)\s+$typeName\b"
    $file = Get-ChildItem -LiteralPath $toolkitRoot -Recurse -Filter '*.cs' |
        Where-Object { (Read-Text $_.FullName) -match "(?m)$pattern" } | Select-Object -First 1
    if (-not $file) { return $null }

    $lines = (Read-Text $file.FullName) -split "`n"
    $declaration = ($lines | Select-String -Pattern $pattern | Select-Object -First 1).LineNumber - 1
    $doc = [System.Collections.Generic.List[string]]::new()
    for ($index = $declaration - 1; $index -ge 0; $index--) {
        $line = $lines[$index].Trim()
        if ($line.StartsWith('///')) { $doc.Insert(0, $line.Substring(3)) }
        elseif ($line.StartsWith('[') -or $line -eq '') { if ($doc.Count) { break } }
        else { break }
    }
    $summary = if (($doc -join "`n") -match '(?s)<summary>(.*?)</summary>') { ConvertFrom-XmlDoc $Matches[1] } else { '' }

    $source = Read-Text $file.FullName
    $registration = 'DependencyProperty\.(Register(?:Attached)?(?:ReadOnly)?)\(\s*(?:nameof\((\w+)\)|"(\w+)")\s*,\s*' +
        'typeof\(([^()]+)\)\s*,\s*typeof\(\w+\)(?:\s*,\s*new\s+(?:Framework)?PropertyMetadata\(\s*((?:[^,()]|\([^()]*\))*))?'
    $properties = foreach ($match in [regex]::Matches($source, $registration)) {
        $name = if ($match.Groups[2].Success) { $match.Groups[2].Value } else { $match.Groups[3].Value }
        $default = $match.Groups[5].Value.Trim()
        [pscustomobject]@{
            Name = $name; Type = $match.Groups[4].Value.Trim()
            Default = if ($default -and $default -notmatch '^On\w+Changed$') { $default } else { '' }
            Attached = $match.Groups[1].Value -like 'RegisterAttached*'
        }
    }
    return [pscustomobject]@{
        File = [System.IO.Path]::GetRelativePath($repositoryRoot, $file.FullName) -replace '\\', '/'
        Summary = $summary; Properties = @($properties)
    }
}

function Get-Figures($card) {
    foreach ($entry in $card.Entries) {
        $detail = $entry.kind -eq 'detail'
        $caption = if ($detail) { "Detail: $($entry.id)" } elseif ($entry.caption) { $entry.caption } else { $entry.pageTitle }
        $themes = @($entry.captures | ForEach-Object theme)
        if ($themes.Count -and $themes.Count -lt $manifest.themes.Count) {
            # A theme that failed to capture keeps its previous image rather than dropping out of the card.
            $report.warnings += "$($entry.id): captured only in $($themes -join ', ')"
            $themes = @($manifest.themes)
        }
        [pscustomobject]@{ Id = $entry.id; Caption = $caption; Live = $entry.requiresLiveWindow; Detail = $detail; Themes = $themes }
    }
}

function New-PreviewHtml([string] $name, [string] $group, [string] $sub, [string] $related, $figures) {
    $html = [System.Text.StringBuilder]::new()
    [void] $html.Append("<!-- @dsCard group=`"$group`" -->`n$previewStyle`n<h1>$(Html $name)</h1>`n")
    if ($sub) { [void] $html.Append("<p class=`"sub`">$sub</p>`n") }
    if ($related) { [void] $html.Append("$related`n") }
    foreach ($figure in $figures) {
        [void] $html.Append("      <figure>`n        <figcaption>$(Html $figure.Caption)</figcaption>`n")
        if ($figure.Themes.Count -eq 0) {
            [void] $html.Append("        <div class=`"pending`">Captured from the live gallery window; run " +
                "<code>dev/capture-ds-previews.ps1</code> without <code>-OffscreenOnly</code> for <code>$($figure.Id)</code>.</div>`n")
        }
        else {
            [void] $html.Append("        <div class=`"pair`">`n")
            foreach ($theme in $figure.Themes) {
                [void] $html.Append("          <img src=`"../../assets/_preview/$theme/$($figure.Id).png`" " +
                    "alt=`"$(Html $name) $theme`" loading=`"lazy`">`n")
            }
            [void] $html.Append("        </div>`n")
        }
        [void] $html.Append("      </figure>`n")
    }
    if (-not $figures) { [void] $html.Append("<p class=`"pending`">No gallery specimen yet.</p>`n") }
    return $html.ToString()
}

function New-SpecimenList($figures) {
    if (-not $figures) { return "## Specimens`n`n- No gallery specimen yet.`n" }
    $items = foreach ($figure in $figures) {
        $suffix = if ($figure.Live) { ' (live window)' } else { '' }
        if ($figure.Detail) { "  - detail ``$($figure.Id)``" }
        else { "- **$($figure.Caption)** — ``$($figure.Id)``$suffix" }
    }
    return "## Specimens`n`n" + ($items -join "`n") + "`n"
}

function New-Readme([string] $name, $doc, $figures) {
    $parts = [System.Collections.Generic.List[string]]::new()
    $parts.Add("# $name")
    if ($doc.Summary) { $parts.Add($doc.Summary) }
    $parts.Add($boilerplate.TrimEnd())
    $parts.Add("Source: ``$($doc.File)``")
    $parts.Add((New-SpecimenList $figures).TrimEnd())
    foreach ($attached in @($false, $true)) {
        $rows = @($doc.Properties | Where-Object Attached -eq $attached)
        if (-not $rows) { continue }
        $table = @("## $(if ($attached) { 'Attached properties' } else { 'Properties' })", '',
                   '| Property | Type | Default |', '| --- | --- | --- |')
        $table += $rows | ForEach-Object {
            $default = if ($_.Default) { "``$($_.Default)``" } else { '' }
            "| ``$($_.Name)`` | ``$($_.Type)`` | $default |"
        }
        $parts.Add($table -join "`n")
    }
    $parts.Add($stylingSection.TrimEnd())
    return ($parts -join "`n`n") + "`n"
}

# --- Inputs ---------------------------------------------------------------------------------------------------

$sourceProject = Join-Path $DesignSystem 'project'
$manifestPath = Join-Path $Corpus 'manifest.json'
if (-not (Test-Path (Join-Path $sourceProject 'design-system.json'))) { throw "No design system at $sourceProject." }
if (-not (Test-Path $manifestPath)) { throw "No manifest at $manifestPath; run dev/capture-ds-previews.ps1 first." }
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) { throw "Manifest schema $($manifest.schemaVersion) is not supported (expected 1)." }

$groupSource = Read-Text (Join-Path $repositoryRoot 'src/geoDC.App.UI.Sample/Gallery/GalleryGroup.cs')
$groups = [ordered]@{}
$enumBody = $groupSource.Substring($groupSource.IndexOf('public enum GalleryGroup'))
foreach ($match in [regex]::Matches($enumBody, '(?s)<summary>(.*?)</summary>\s*(\w+)\s*[,}]')) {
    $groups[$match.Groups[2].Value] = ConvertFrom-XmlDoc ($match.Groups[1].Value -replace '///', '')
}

# Cards, in gallery order, from the manifest's specimens.
$cards = [ordered]@{}
foreach ($entry in $manifest.entries) {
    $name = if ($CardNames.ContainsKey($entry.control)) { $CardNames[$entry.control] } else { $entry.control }
    if (-not $cards.Contains($name)) {
        $cards[$name] = [pscustomobject]@{ Name = $name; Control = $entry.control; Group = $entry.group
                                           PageTitle = $entry.pageTitle; Entries = [System.Collections.Generic.List[object]]::new() }
    }
    $cards[$name].Entries.Add($entry)
}

$existing = @(Get-ChildItem -LiteralPath (Join-Path $sourceProject 'components') -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName 'preview.html') } | ForEach-Object Name |
    Where-Object { $_ -ne 'Cover' })

# --- Output ---------------------------------------------------------------------------------------------------

if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
$outProject = Join-Path $Out 'project'
Copy-Item -Recurse -LiteralPath $sourceProject -Destination $outProject
$report = [ordered]@{ added = @(); archived = @(); regrouped = @(); unpaged = @(); warnings = @() }
$referenced = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$index = [ordered]@{}
foreach ($group in $groups.Keys) { $index[$group] = [System.Collections.Generic.List[string]]::new() }

function Add-ToIndex([string] $name, [string] $group) {
    if (-not $index.Contains($group)) { $script:report.warnings += "$name is filed under unknown group '$group'"; $index[$group] = [System.Collections.Generic.List[string]]::new() }
    $index[$group].Add($name)
}

foreach ($card in $cards.Values) {
    $dir = Join-Path $outProject "components/$($card.Name)"
    $previewPath = Join-Path $dir 'preview.html'
    $readmePath = Join-Path $dir 'README.md'
    $figures = @(Get-Figures $card)
    foreach ($figure in $figures) { foreach ($theme in $figure.Themes) { [void] $referenced.Add("$theme/$($figure.Id).png") } }
    Add-ToIndex $card.Name $card.Group

    if (Test-Path $previewPath) {
        $old = Read-Text $previewPath
        $sub = if ($old -match '<p class="sub">(.*?)</p>') { $Matches[1] } else { '' }
        $related = if ($old -match '(<p class="related">.*?</p>)') { $Matches[1] } else { '' }
        if ($old -match '@dsCard group="([^"]*)"' -and $Matches[1] -ne $card.Group) {
            $report.regrouped += "$($card.Name): $($Matches[1]) → $($card.Group)"
        }
        Write-Text $previewPath (New-PreviewHtml $card.Name $card.Group $sub $related $figures)

        $readme = if (Test-Path $readmePath) { Read-Text $readmePath } else { "# $($card.Name)`n" }
        $list = New-SpecimenList $figures
        if ($readme -match '(?ms)^## Specimens\n.*?(?=^## |\z)') {
            $readme = $readme.Replace($Matches[0], "$list`n")
        }
        else {
            $readme = $readme -replace '(?ms)(^## Properties)', "$list`n`$1"
            if ($readme -notmatch '(?m)^## Specimens') { $readme = $readme.TrimEnd() + "`n`n$list" }
        }
        Write-Text $readmePath $readme
    }
    else {
        $doc = Get-SourceDoc $card.Control
        if (-not $doc) {
            $report.warnings += "$($card.Name): no source for $($card.Control) under src/geoDC.App.UI"
            $doc = [pscustomobject]@{ File = ''; Summary = ''; Properties = @() }
        }
        $sub = Html (($doc.Summary -split "`n`n")[0] -replace '`', '')
        Write-Text $previewPath (New-PreviewHtml $card.Name $card.Group $sub '' $figures)
        Write-Text $readmePath (New-Readme $card.Name $doc $figures)
        $report.added += "$($card.Name) ($($card.Group))"
    }
}

# Cards with no specimen page: regroup and keep their figures, or archive them when the control is gone.
foreach ($name in $existing | Where-Object { -not $cards.Contains($_) }) {
    $dir = Join-Path $outProject "components/$name"
    $previewPath = Join-Path $dir 'preview.html'
    $old = Read-Text $previewPath
    $control = @($CardNames.Keys | Where-Object { $CardNames[$_] -eq $name })[0] ?? $name
    if (-not (Get-SourceDoc $control)) {
        $archive = Join-Path $outProject "archived/$name"
        New-Item -ItemType Directory -Force $archive | Out-Null
        Write-Text (Join-Path $archive "$name.html") ($old -replace '^<!-- @dsCard[^\n]*\n', '')
        $readmePath = Join-Path $dir 'README.md'
        if (Test-Path $readmePath) { Copy-Item -LiteralPath $readmePath -Destination (Join-Path $archive "$name.prompt.md") }
        Remove-Item -Recurse -Force $dir
        $report.archived += $name
        continue
    }

    foreach ($match in [regex]::Matches($old, 'assets/_preview/(\w+/[\w.-]+\.png)')) { [void] $referenced.Add($match.Groups[1].Value) }
    $oldGroup = if ($old -match '@dsCard group="([^"]*)"') { $Matches[1] } else { '' }
    $group = if ($CardGroups.ContainsKey($name)) { $CardGroups[$name] } else { $oldGroup }
    if (-not $groups.Contains($group)) { $report.warnings += "$name has no specimen page and no group in `$CardGroups" }
    if ($group -ne $oldGroup) {
        Write-Text $previewPath ($old -replace '@dsCard group="[^"]*"', "@dsCard group=`"$group`"")
        $report.regrouped += "${name}: $oldGroup → $group"
    }
    $report.unpaged += $name
    Add-ToIndex $name $group
}

# Screenshots: copy what the cards show, keep what archived cards show, delete the rest.
$archivedRoot = Join-Path $outProject 'archived'
if (Test-Path $archivedRoot) {
    foreach ($file in Get-ChildItem -LiteralPath $archivedRoot -Recurse -File -Filter '*.html') {
        foreach ($match in [regex]::Matches((Read-Text $file.FullName), 'assets/_preview/(\w+/[\w.-]+\.png)')) { [void] $referenced.Add($match.Groups[1].Value) }
    }
}
foreach ($relative in $referenced) {
    $source = Join-Path $Corpus $relative
    $target = Join-Path $outProject "assets/_preview/$relative"
    if (-not (Test-Path $source)) {
        if (-not (Test-Path $target)) { $report.warnings += "no image for assets/_preview/$relative" }
        continue
    }
    New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
}
$previewRoot = Join-Path $outProject 'assets/_preview'
if (Test-Path $previewRoot) {
    foreach ($file in Get-ChildItem -LiteralPath $previewRoot -Recurse -File -Filter '*.png') {
        $relative = [System.IO.Path]::GetRelativePath($previewRoot, $file.FullName) -replace '\\', '/'
        if (-not $referenced.Contains($relative)) { Remove-Item -LiteralPath $file.FullName }
    }
}

# README: the Component index, rebuilt from the cards.
$readmePath = Join-Path $outProject 'README.md'
$readme = Read-Text $readmePath
$retired = if ($readme -match '(?ms)^\*\*Retired controls\.\*\*.*?(?=\n\n|\z)') { $Matches[0].TrimEnd() } else { '' }
$cardCount = ($index.Values | Measure-Object -Property Count -Sum).Sum
$usedGroups = @($index.Keys | Where-Object { $index[$_].Count -gt 0 })
$section = [System.Collections.Generic.List[string]]::new()
$section.Add('## Component index')
$section.Add('Cards are filed by what a page author reaches for them to do, in the same groups and order as the Sample gallery.')
foreach ($group in $usedGroups) {
    $description = if ($groups.Contains($group)) { " $($groups[$group])" } else { '' }
    $names = ($index[$group] | ForEach-Object { '`' + $_ + '`' }) -join ', '
    $section.Add("**$group** —$description`n`n$names")
}
$section.Add("$cardCount components, $($usedGroups.Count) groups. Item and column types (``UiNavBarItem``, ``UiTabItem``, " +
    "``UiPropertyRow``, the typed ``UiDataGrid*Column``s and so on) are documented on their parent's card.")
if ($retired) { $section.Add($retired) }
$section.Add('**Screenshots.** Every card is generated from the Sample gallery in the geodc repo: ' +
    '`dev/capture-ds-previews.ps1` renders each registered specimen dark and light into a corpus with `manifest.json`, ' +
    'and `dev/sync-design-system.ps1` rebuilds the cards, their Specimens lists and `assets/_preview/{dark,light}/` from it. ' +
    'A figure is one gallery specimen, or a detail inside one, named by its id. Edit the gallery, not the figures here; ' +
    'prose outside a card''s ' +
    'Specimens list is kept.')
$sectionText = ($section -join "`n`n") + "`n"
if ($readme -match '(?ms)^## Component index\n.*?(?=^## |\z)') { $readme = $readme.Replace($Matches[0], $sectionText) }
else { $readme = $readme.TrimEnd() + "`n`n`n$sectionText" }
Write-Text $readmePath $readme

# Index: lastChange records the commit the corpus was captured from.
$indexPath = Join-Path $outProject 'design-system.json'
$system = Get-Content -Raw -LiteralPath $indexPath | ConvertFrom-Json -AsHashtable
$commit = if ($manifest.commit) { $manifest.commit } else { 'unknown commit' }
$system['lastChange'] = [ordered]@{
    by = $By; at = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'); via = 'geodc dev/sync-design-system.ps1'
    note = "Synced from the Sample gallery at geodc $commit"; commit = $manifest.commit
}
Write-Text $indexPath (($system | ConvertTo-Json -Depth 20) + "`n")

# --- Changes --------------------------------------------------------------------------------------------------

$writes = [System.Collections.Generic.List[string]]::new()
$deletes = [System.Collections.Generic.List[string]]::new()
$before = @{}
foreach ($file in Get-ChildItem -LiteralPath $sourceProject -Recurse -File) {
    $before[[System.IO.Path]::GetRelativePath($sourceProject, $file.FullName) -replace '\\', '/'] = $file.FullName
}
foreach ($file in Get-ChildItem -LiteralPath $outProject -Recurse -File) {
    $relative = [System.IO.Path]::GetRelativePath($outProject, $file.FullName) -replace '\\', '/'
    if (-not $before.ContainsKey($relative) -or
        (Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $before[$relative]).Hash) {
        $writes.Add("project/$relative")
    }
    $before.Remove($relative)
}
foreach ($relative in $before.Keys) { $deletes.Add("project/$relative") }
if ($deletes.Count -eq 0 -and $writes.Count -eq 1 -and $writes[0] -eq 'project/design-system.json') {
    Copy-Item -LiteralPath (Join-Path $sourceProject 'design-system.json') -Destination $indexPath -Force
    $writes.Clear()
}
$sortedWrites = @($writes | Where-Object { $_ -ne 'project/design-system.json' } | Sort-Object) +
    @($writes | Where-Object { $_ -eq 'project/design-system.json' })

$changes = [ordered]@{
    commit = $manifest.commit; writes = $sortedWrites; deletes = @($deletes | Sort-Object)
    added = $report.added; archived = $report.archived; regrouped = $report.regrouped
    unpaged = $report.unpaged; warnings = $report.warnings
}
Write-Text (Join-Path $Out 'changes.json') (($changes | ConvertTo-Json -Depth 5) + "`n")

$summary = [System.Collections.Generic.List[string]]::new()
$summary.Add("# Design-system sync from geodc $commit")
$summary.Add("$($sortedWrites.Count) files to write, $($deletes.Count) to delete ($cardCount cards).")
foreach ($key in 'added', 'archived', 'regrouped', 'unpaged', 'warnings') {
    if ($report[$key].Count) { $summary.Add("## $key`n`n" + (($report[$key] | ForEach-Object { "- $_" }) -join "`n")) }
}
if ($deletes.Count) { $summary.Add("## deletes`n`n" + (($deletes | Sort-Object | ForEach-Object { "- $_" }) -join "`n")) }
Write-Text (Join-Path $Out 'changes.md') (($summary -join "`n`n") + "`n")

Write-Host "Wrote $Out/project: $($sortedWrites.Count) writes, $($deletes.Count) deletes, $($report.added.Count) new cards, $($report.archived.Count) archived."
foreach ($warning in $report.warnings) { Write-Warning $warning }
