---
name: geodc-ui-driver-handoff
description: Produce a PowerShell handoff script the developer runs on Windows from a worktree already on an agent branch to update it, build it, launch geoDC or the Sample gallery on a UI driver fixture, drive it to the changed surface, and pause for their manual check. Use when a geoDC change has visible UI or behaviour the developer should see or test, especially from a cloud session that cannot run WPF, or when the user asks how to view, try, review or test the changes or fix.
---

# Hand the developer a UI driver script

The goal is one paste: the developer runs a script in PowerShell on Windows and ends up looking at the change, with the app already on the right page and a UI driver window saying what to check. Cloud and Linux sessions cannot run the app or the UI driver, so this script is often the only way the change gets seen before review. Read the `wpf-ui-driver` skill for the UI driver grammar; this skill covers what to hand over and how.

The script runs in the developer's geoDC worktree, not in this harness, so every path in it and in the rules below is relative to geoDC's root, without the harness's `geodc/` prefix. While writing it, find those files under `geodc/` (search `geodc/src` for AutomationIds, and `geodc/dev/ui-scripts/lib/` for state scripts).

## Before writing it

1. Push the branch. The script pulls it from `origin`; an unpushed branch cannot be handed over.
2. Decide the target and the state:
   - `--target sample` for `geoDC.App.UI` controls, styles and themes; the `app` target for pages and workflows.
   - `dev/fixtures/probe.gdcproj` by default; `dev/fixtures/probe-lifecycle.gdcproj` when the check needs attempts in every workflow state. Never another package unless the user names one.
   - Which `dev/ui-scripts/lib/` state script reaches the changed surface, and with which variables. Use only AutomationIds and names that exist on the branch (search the XAML and the fixture generator); never guess. If no state script reaches it and it is worth reaching twice, add one to `lib/` on the branch, following the `wpf-ui-driver` conventions.
3. Write one `pause --message` per thing the developer should judge, phrased as what correct looks like ("The residual reads +0.12° with 'Observed − applied' beside it"), not "check the page". Add `assert` steps for anything the UI driver can prove itself, before the pause, so the developer only judges what needs eyes.

## Script shape

Prefer a checked-in check script when the check is worth keeping (`dev/ui-scripts/<target>-<check>.ui`, header comment, `@target`, built on `lib/` scripts, no `pause` in `lib/`). Otherwise write the steps inline to a temp `.ui-driver` file from the handoff script, so nothing is added to the branch just for the handoff.

The handoff script must:

- Assume the developer runs it from the root of a worktree already on the branch; do not create, switch or remove worktrees or branches. Check that `git branch --show-current` is the expected branch and that the tree is clean, and stop with a clear message otherwise.
- `git pull --ff-only origin <branch>` to pick up the latest push; stop on divergence rather than resetting, since the developer may have local commits.
- Run every command from the worktree root, because the UI driver resolves build output, fixtures and its `.ui-driver/` lease from its working directory.
- `dotnet restore` once; if it fails, stop and say so (never change feeds or credentials).
- Seed the fixture only if missing: `dev/ui.ps1 fixture seed` (`--lifecycle` for the lifecycle fixture).
- Build and launch with one `rebuild --target <t> --project <fixture> --active --then <script> --interactive --format text --report .ui-driver/reports/<branch>.md`. `rebuild` builds the same configuration it launches and kills any instance this worktree's probe leased; `--interactive` is correct here because a person is running it. Use `--passive` only when the check never needs focus.
- Leave the app running at the end and print the report path and how to stop it (`dev/ui.ps1 kill --target <t>`).
- Use `$ErrorActionPreference = 'Stop'`, check `$LASTEXITCODE` after each native command, and contain no secrets, absolute paths from the agent's machine, or wall-clock-dependent steps.

Template (fill the placeholders; drop the temp-file block when using a checked-in script):

```pwsh
#Requires -Version 7
# Review <branch>: <one line on what changed>.
$ErrorActionPreference = 'Stop'
$branch = '<branch>'
$target = '<app|sample>'
$fixture = 'dev/fixtures/probe.gdcproj'   # or dev/fixtures/probe-lifecycle.gdcproj
$root = git rev-parse --show-toplevel; if ($LASTEXITCODE) { throw 'Run this from inside the geoDC worktree for this branch.' }
if ((git -C $root branch --show-current) -ne $branch) { throw "This worktree is not on $branch." }
if (git -C $root status --porcelain) { throw 'The worktree has local changes; commit or stash them first.' }
git -C $root pull --ff-only origin $branch; if ($LASTEXITCODE) { throw "Local $branch has diverged from origin/$branch." }

Push-Location $root
try {
    dotnet restore geoDC.slnx --verbosity quiet; if ($LASTEXITCODE) { throw 'Restore failed; fix restore, then run this again.' }
    if ($target -eq 'app' -and -not (Test-Path $fixture)) {
        $seed = @('-NoProfile', '-File', 'dev/ui.ps1', 'fixture', 'seed', '--format', 'text')
        if ($fixture -like '*probe-lifecycle*') { $seed += '--lifecycle' }
        pwsh @seed; if ($LASTEXITCODE) { throw 'Seeding the UI driver fixture failed.' }
    }

    $check = Join-Path $root '.ui-driver/handoff.ui'
    New-Item -ItemType Directory -Force (Split-Path $check) | Out-Null
    @'
@target <app|sample>
run lib/<state-script> <var>=<value>
assert --id <AutomationId> --exists
pause --message '<what correct looks like>'
'@ | Set-Content $check

    pwsh -NoProfile -File dev/ui.ps1 rebuild --target $target @(if ($target -eq 'app') { '--project', $fixture }) --active --then $check --interactive --format text --report ".ui-driver/reports/handoff.md"
    Write-Host "Report: $root\.ui-driver\reports\handoff.md"
    Write-Host "Stop the app: pwsh -NoProfile -File dev/ui.ps1 kill --target $target"
}
finally { Pop-Location }
```

## Delivering it

- Up to about 40 lines: put it in the reply in one fenced `pwsh` block, with a sentence above it on what it opens and what to check.
- Longer, or several checks: write it to the scratchpad (or `.ui-driver/` locally) as `review-<branch>.ps1` and send the file; do not commit handoff scripts. A checked-in `.ui-driver` check script is the part worth committing.
- State the verification boundary: what the agent compiled or tested, and that the script's AutomationIds were checked against source but not run, when it could not run the UI driver itself.
- After the developer reports back, record their verdict where the work tracks it (`geodc-branch-wrap-up`, the issue's manual-check criteria).
