#Requires -Version 7
# Prepares this harness to work on geoDC: clones snt0/geodc into geodc/ on dev (or fast-forwards an existing
# clone that is on dev and clean), then, inside it, pulls Git LFS files, restores the local dotnet tools and
# installs the Husky hooks. Run it from the harness root on a new machine, and again to update geodc/.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$url = if ($env:GEODC_REPOSITORY_URL) { $env:GEODC_REPOSITORY_URL } else { 'https://github.com/snt0/geodc' }

function Invoke-Native([string] $File, [string[]] $Arguments)
{
    & $File @Arguments
    if ($LASTEXITCODE) { throw "$File $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

if (-not (Test-Path geodc))
{
    Invoke-Native git @('clone', '--branch', 'dev', $url, 'geodc')
}
elseif ((git -C geodc symbolic-ref --quiet --short HEAD) -eq 'dev' -and -not (git -C geodc status --porcelain))
{
    Invoke-Native git @('-C', 'geodc', 'pull', '--ff-only', 'origin', 'dev')
}
else
{
    Write-Warning 'geodc/ is not a clean checkout of dev; leaving it as it is.'
}

Push-Location geodc
try
{
    if (-not (Get-Command git-lfs -ErrorAction SilentlyContinue))
    {
        throw 'git-lfs is missing: install it, then run this again. Without it the logos are blank and reports fail.'
    }
    Invoke-Native git @('lfs', 'install', '--local', '--skip-repo')
    Invoke-Native git @('lfs', 'pull')
    Invoke-Native dotnet @('tool', 'restore')
    Invoke-Native dotnet @('husky', 'install')
}
finally { Pop-Location }
Write-Output 'geodc/ is ready: dotnet build geodc/geoDC.slnx'
