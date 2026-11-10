#Requires -Version 7

[CmdletBinding()]
param(
    [Alias('Write')]
    [switch] $UpdateMirror
)

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$sourceRoot = Join-Path $repositoryRoot '.agents\skills'
$mirrorRoot = Join-Path $repositoryRoot '.claude\skills'

if (-not (Test-Path -LiteralPath $sourceRoot))
{
    throw "Canonical skill directory not found: $sourceRoot"
}

$sourceFiles = @(
    Get-ChildItem -LiteralPath $sourceRoot -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/]agents[\\/]openai\.yaml$' }
)

$expectedRelativePaths = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
$differences = [System.Collections.Generic.List[string]]::new()

foreach ($sourceFile in $sourceFiles)
{
    $relativePath = [System.IO.Path]::GetRelativePath($sourceRoot, $sourceFile.FullName)
    [void] $expectedRelativePaths.Add($relativePath)
    $mirrorPath = Join-Path $mirrorRoot $relativePath

    $matches = Test-Path -LiteralPath $mirrorPath
    if ($matches)
    {
        $sourceHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash
        $mirrorHash = (Get-FileHash -LiteralPath $mirrorPath -Algorithm SHA256).Hash
        $matches = $sourceHash -eq $mirrorHash
    }

    if ($matches)
    {
        continue
    }

    $differences.Add($relativePath)
    if ($UpdateMirror)
    {
        $mirrorDirectory = Split-Path $mirrorPath -Parent
        [void] (New-Item -ItemType Directory -Path $mirrorDirectory -Force)
        Copy-Item -LiteralPath $sourceFile.FullName -Destination $mirrorPath -Force
    }
}

$extraFiles = [System.Collections.Generic.List[string]]::new()
if (Test-Path -LiteralPath $mirrorRoot)
{
    foreach ($mirrorFile in Get-ChildItem -LiteralPath $mirrorRoot -Recurse -File)
    {
        $relativePath = [System.IO.Path]::GetRelativePath($mirrorRoot, $mirrorFile.FullName)
        if (-not $expectedRelativePaths.Contains($relativePath))
        {
            $extraFiles.Add($relativePath)
        }
    }
}

$result = [ordered]@{
    ok = $extraFiles.Count -eq 0 -and ($UpdateMirror -or $differences.Count -eq 0)
    updated = [bool] $UpdateMirror
    copied = if ($UpdateMirror) { @($differences) } else { @() }
    differences = if ($UpdateMirror) { @() } else { @($differences) }
    extra = @($extraFiles)
}

$result | ConvertTo-Json -Depth 3 -Compress

if ($extraFiles.Count -gt 0 -or (-not $UpdateMirror -and $differences.Count -gt 0))
{
    exit 1
}
