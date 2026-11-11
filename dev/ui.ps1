#Requires -Version 7
# Shim for the geoDC UI driver. Forwards all arguments to geodc-ui.exe, building it first if the binary is missing or
# older than any of its sources: tools/geoDC.UiDriver, the UIA-free tools/geoDC.UiDriver.Core it references, and
# geoDC's tools/geoDC.FindText, whose strings catalog and matcher the source verb uses.
#
# The driver works on two folders. geoDC is its working directory: it builds and launches the app from there, reads
# the strings files and runs the test-data generator. Run the shim with geoDC as the working directory
# (pwsh -NoProfile -WorkingDirectory geodc -File dev/ui.ps1 ...); from anywhere else it uses the harness's nested
# geodc/. The harness root is the driver home (GEODC_UI_HOME): .ui-driver/ state, dev/sample-projects/ and
# dev/ui-scripts/ live there, so geoDC's tree stays clean.
#
# stdout is reserved for the UI driver's own output (one JSON line per result when piped or captured, text at a
# terminal). Anything the shim itself says, build output included, goes to stderr, so a caller can parse stdout
# unconditionally.

$ErrorActionPreference = 'Stop'
$harnessRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $harnessRoot 'tools/geoDC.UiDriver'
$sourceDirectories = @($project, (Join-Path $harnessRoot 'tools/geoDC.UiDriver.Core'),
    (Join-Path $harnessRoot 'geodc/tools/geoDC.FindText'))
$exe = Join-Path $project 'bin/Debug/net10.0-windows/geodc-ui.exe'

function Write-Failure([string] $code, [string] $message)
{
    $verb = if ($args.Count -gt 0) { $args[0] } else { '' }
    $payload = @{ ok = $false; verb = $verb; error = @{ code = $code; message = $message } }
    [Console]::Out.WriteLine(($payload | ConvertTo-Json -Compress -Depth 4))
}

$geodc = if (Test-Path (Join-Path (Get-Location) 'geoDC.slnx')) { (Get-Location).Path } else { Join-Path $harnessRoot 'geodc' }
if (-not (Test-Path (Join-Path $geodc 'geoDC.slnx')))
{
    Write-Failure 'no-repository' "No geoDC repository at $geodc. Run bootstrap.ps1, or run the shim with a geoDC checkout as the working directory." @args
    exit 1
}

# A pull that changes the UI driver must not leave the old binary running: compare the exe against every source file
# outside bin/ and obj/.
$stale = $false
if (Test-Path $exe)
{
    $built = (Get-Item $exe).LastWriteTimeUtc
    $stale = $null -ne (Get-ChildItem $sourceDirectories -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.LastWriteTimeUtc -gt $built } |
        Select-Object -First 1)
}

if ($stale -or -not (Test-Path $exe))
{
    $reason = if ($stale) { 'geodc-ui.exe is older than its sources' } else { 'geodc-ui.exe not found' }
    [Console]::Error.WriteLine("$reason; building tools/geoDC.UiDriver...")
    dotnet build $project --nologo --verbosity minimal 2>&1 |
        ForEach-Object { [Console]::Error.WriteLine($_) }
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe))
    {
        Write-Failure 'ui-driver-build-failed' 'dotnet build tools/geoDC.UiDriver failed; see stderr for compiler output.' @args
        exit 1
    }
}

$env:GEODC_UI_HOME = $harnessRoot
Push-Location -LiteralPath $geodc
try
{
    & $exe @args
    exit $LASTEXITCODE
}
finally
{
    Pop-Location
}
