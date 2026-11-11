---
name: geodc-ui-driver-handoff
description: Produce a PowerShell handoff script the developer runs on Windows from their harness checkout, whose geodc/ clone is already on an agent branch, to update it, build it, launch geoDC or the Sample gallery on a UI driver sample project, drive it to the changed surface, and pause for their manual check. Use when a geoDC change has visible UI or behaviour the developer should see or test, especially from a cloud session that cannot run WPF, or when the user asks how to view, try, review or test the changes or fix.
---

# Hand the developer a UI driver script

The goal is one paste: the developer runs a script in PowerShell on Windows and ends up looking at the change, with the app already on the right page and a UI driver window saying what to check. Cloud and Linux sessions cannot run the app or the UI driver, so this script is often the only way the change gets seen before review. Read the `wpf-ui-driver` skill for the UI driver grammar; this skill covers what to hand over and how.

The script runs from the root of the developer's harness checkout, with geoDC nested at `geodc/`, as this harness is laid out. The UI driver, its shim, its scripts and its sample projects are the harness's; the change being checked is on a geoDC branch in `geodc/`. Paths in the script and in the rules below are relative to the harness root, so geoDC's carry the `geodc/` prefix. While writing it, search `geodc/src` for AutomationIds and `dev/ui-scripts/lib/` for state scripts.

## Before writing it

1. Push the geoDC branch. The script pulls it from `origin` into `geodc/`; an unpushed branch cannot be handed over. If the check needs a script that is only on a harness branch, push that too and set `$harnessBranch` in the script.
2. Decide the target and the state:
   - `--target sample` for `geoDC.App.UI` controls, styles and themes; the `app` target for pages and workflows.
   - The sample project `dev/sample-projects/sample.gdcproj` by default, or `dev/sample-projects/sample-lifecycle.gdcproj` when the check needs attempts in every workflow state. Never another package unless the user names one.
   - Which `dev/ui-scripts/lib/` state script reaches the changed surface, and with which variables. Use only AutomationIds and names that exist on the geoDC branch (search the XAML and the test-data generator under `geodc/`); never guess. If no state script reaches it and it is worth reaching twice, add one to the harness's `lib/` on a harness branch, following the `wpf-ui-driver` conventions.
3. Write one `pause --message` per thing the developer should judge, phrased as what correct looks like ("The residual reads +0.12° with 'Observed − applied' beside it"), not "check the page". Add `assert` steps for anything the UI driver can prove itself, before the pause, so the developer only judges what needs eyes.

## Script shape

Prefer a checked-in check script when the check is worth keeping (the harness's `dev/ui-scripts/<target>-<check>.ui`, header comment, `@target`, built on `lib/` scripts, no `pause` in `lib/`), committed on a harness branch. Otherwise write the steps inline to a temp file under the harness's `.ui-driver/` from the handoff script, so nothing is added to either repository just for the handoff.

The handoff script must:

- Assume the developer runs it from the root of their harness checkout, with `geodc/` already on the geoDC branch (or `$geodc` pointed at the geoDC worktree that is); do not create, switch or remove worktrees or branches. Check that `git -C geodc branch --show-current` is the expected branch and that both trees are clean, and stop with a clear message otherwise. Check the harness branch the same way when the check needs one.
- `git pull --ff-only origin <branch>` in `geodc/` (and in the harness for a harness branch) to pick up the latest push; stop on divergence rather than resetting, since the developer may have local commits.
- Run the UI driver through the harness's shim with geoDC as the working directory: `pwsh -NoProfile -WorkingDirectory $geodc -File <harness>/dev/ui.ps1 ...`. The driver builds and launches geoDC from its working directory, and keeps its sample projects, scripts and `.ui-driver/` instance file in the harness root.
- `dotnet restore geodc/geoDC.slnx` once; if it fails, stop and say so (never change feeds or credentials).
- Create the sample project only if it is missing: `sample create` (`--lifecycle` for `sample-lifecycle.gdcproj`). It writes into the harness's `dev/sample-projects/`.
- Build and launch with one `rebuild --target <t> --project <sample-project> --active --then <script> --interactive --format text --report .ui-driver/reports/<branch>.md`. `rebuild` builds the same configuration it launches and kills the driver's instance for this harness checkout first; `--interactive` is correct here because a person is running it. Use `--passive` only when the check never needs focus.
- Leave the app running at the end and print the report path and how to stop it (`pwsh -NoProfile -WorkingDirectory geodc -File dev/ui.ps1 kill --target <t>`).
- Use `$ErrorActionPreference = 'Stop'`, check `$LASTEXITCODE` after each native command, and contain no secrets, absolute paths from the agent's machine, or wall-clock-dependent steps.

Template (fill the placeholders; drop the temp-file block when using a checked-in script):

```pwsh
#Requires -Version 7
# Review <branch>: <one line on what changed>. Run from the root of your geodc-harness checkout.
$ErrorActionPreference = 'Stop'
$branch = '<branch>'            # the geoDC branch
$harnessBranch = ''             # a harness branch the check needs, or '' for the one checked out
$target = '<app|sample>'
$sampleProject = 'dev/sample-projects/sample.gdcproj'   # or dev/sample-projects/sample-lifecycle.gdcproj
$root = git rev-parse --show-toplevel; if ($LASTEXITCODE) { throw 'Run this from inside your geodc-harness checkout.' }
$geodc = Join-Path $root 'geodc'  # or the path of the geoDC worktree on $branch
$shim = Join-Path $root 'dev/ui.ps1'
if (-not (Test-Path $shim)) { throw "$root is not a geodc-harness checkout." }
foreach ($repo in @(@{ Path = $geodc; Branch = $branch }) + @(if ($harnessBranch) { @{ Path = $root; Branch = $harnessBranch } })) {
    if ((git -C $repo.Path branch --show-current) -ne $repo.Branch) { throw "$($repo.Path) is not on $($repo.Branch)." }
    if (git -C $repo.Path status --porcelain) { throw "$($repo.Path) has local changes; commit or stash them first." }
    git -C $repo.Path pull --ff-only origin $repo.Branch; if ($LASTEXITCODE) { throw "Local $($repo.Branch) has diverged from origin." }
}

dotnet restore (Join-Path $geodc 'geoDC.slnx') --verbosity quiet; if ($LASTEXITCODE) { throw 'Restore failed; fix restore, then run this again.' }
if ($target -eq 'app' -and -not (Test-Path (Join-Path $root $sampleProject))) {
    $create = @('sample', 'create', '--format', 'text')
    if ($sampleProject -like '*sample-lifecycle*') { $create += '--lifecycle' }
    pwsh -NoProfile -WorkingDirectory $geodc -File $shim @create; if ($LASTEXITCODE) { throw 'Creating the UI driver sample project failed.' }
}

$check = Join-Path $root '.ui-driver/handoff.ui'
New-Item -ItemType Directory -Force (Split-Path $check) | Out-Null
@'
@target <app|sample>
run lib/<state-script> <var>=<value>
assert --id <AutomationId> --exists
pause --message '<what correct looks like>'
'@ | Set-Content $check

pwsh -NoProfile -WorkingDirectory $geodc -File $shim rebuild --target $target @(if ($target -eq 'app') { '--project', $sampleProject }) --active --then $check --interactive --format text --report '.ui-driver/reports/handoff.md'
Write-Host "Report: $(Join-Path $root '.ui-driver/reports/handoff.md')"
Write-Host "Stop the app: pwsh -NoProfile -WorkingDirectory geodc -File dev/ui.ps1 kill --target $target (from $root)"
```

## Delivering it

- Up to about 40 lines: put it in the reply in one fenced `pwsh` block, with a sentence above it on what it opens and what to check.
- Longer, or several checks: write it to the scratchpad (or the harness's `.ui-driver/` locally) as `review-<branch>.ps1` and send the file; do not commit handoff scripts. A check script under the harness's `dev/ui-scripts/` is the part worth committing.
- State the verification boundary: what the agent compiled or tested, and that the script's AutomationIds were checked against source but not run, when it could not run the UI driver itself.
- After the developer reports back, record their verdict where the work tracks it (`geodc-branch-wrap-up`, the issue's manual-check criteria).
