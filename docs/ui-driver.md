# UI Driver

The UI driver (`tools/geoDC.UiDriver` in this harness, executable `geodc-ui`) drives a running geoDC or Sample
gallery through Windows UI Automation. It reads the automation tree, clicks, types, selects, scrolls, waits on
conditions, checks state and takes screenshots. Use it to check a UI change, replay a check, or reach a screen in one command instead of
clicking through the app.

It runs only on Windows, with PowerShell 7 (`pwsh`) and the .NET 10 SDK. It builds on Linux with the rest of
`geodc-harness.slnx`, and its tests run there, but it can't drive anything there.

## Running it

Run every command from the harness root through the shim, with geoDC (`geodc/`) as the working directory. The shim
builds the driver first when it is missing or older than its sources:

```pwsh
pwsh -NoProfile -WorkingDirectory geodc -File dev/ui.ps1 <verb> [options]
```

`-File` resolves against the folder you run `pwsh` from, so `dev/ui.ps1` is the harness's shim, while
`-WorkingDirectory geodc` makes geoDC the driver's working directory. The driver uses two folders:

- **geoDC**, its working directory: it builds and launches the app and the Sample gallery from there, reads the
  strings files for `source` and runs the test-data generator for `sample create`. To drive another geoDC worktree,
  pass that worktree as `-WorkingDirectory`. Run from a folder that is not a geoDC checkout, the shim uses `geodc/`.
- **The driver home**, the harness root, which the shim passes in `GEODC_UI_HOME`: the `.ui-driver/` state, the
  sample projects in `dev/sample-projects/` and the scripts in `dev/ui-scripts/` live there, so geoDC's tree stays
  clean. Relative `--project`, `--file`, `--out`, `--report` and `--append` paths resolve against it. Without
  `GEODC_UI_HOME`, the working directory is the home.

At a terminal, each result prints as one readable line. When the output is piped or captured, each result is one
JSON line. `--format text` or `--format json` chooses explicitly.

A first session looks like this:

```pwsh
pwsh -NoProfile -WorkingDirectory geodc -File dev/ui.ps1 sample create   # once, to create the sample project
pwsh -NoProfile -WorkingDirectory geodc -File dev/ui.ps1 attach --launch --project dev/sample-projects/sample.gdcproj
pwsh -NoProfile -WorkingDirectory geodc -File dev/ui.ps1 do --file dev/ui-scripts/app-smoke.ui
pwsh -NoProfile -WorkingDirectory geodc -File dev/ui.ps1 kill
```

[Sample scripts](#sample-scripts) lists more checks to start from.

## Attach and the driver's instance

The driver acts on one instance of geoDC (`--target app`, the default) or the Sample gallery (`--target sample`).
This is the driver's instance: the process it launched and recorded for this checkout of the harness.

- `attach` finds the instance the driver launched earlier from this checkout.
- `attach --launch` starts one. It records the process in `.ui-driver/<target>-instance.json` (gitignored), and
  later commands act only on that process, not on a process of the same name that tests or another checkout started.
- `--passive` keeps the launched app from taking the foreground, so it doesn't steal focus while you work. Scripts
  that send keys need an active launch.
- `status` reports the driver's instance, `kill` stops it, and `rebuild` stops it, builds, and relaunches it the
  same way. Run them from the same checkout as the `attach --launch`.

`.ui-driver/` also holds tree snapshots for `tree --diff`, screenshots, reports and the register copies described
below.

## Sample projects

The app is launched only on the gitignored sample projects, not on a real project:

```pwsh
pwsh -NoProfile -WorkingDirectory geodc -File dev/ui.ps1 sample create               # dev/sample-projects/sample.gdcproj
pwsh -NoProfile -WorkingDirectory geodc -File dev/ui.ps1 sample create --lifecycle   # dev/sample-projects/sample-lifecycle.gdcproj
pwsh -NoProfile -WorkingDirectory geodc -File dev/ui.ps1 sample status               # which exist, and their fingerprints
```

`sample.gdcproj` has one job with a pitch calibration cycle, a survey to align and a Shore Control set.
`sample-lifecycle.gdcproj` has work items in every workflow state. Both come from the test-data generator's presets
([test-data-generator.md](../geodc/docs/test-data-generator.md#presets)). Sample projects are for the app only: the
Sample gallery (`--target sample`) opens no project.

`sample create` runs geoDC's test-data generator CLI and writes three files into the harness's `dev/sample-projects/`:
the package, its Shore Control Register (`<name>-register.sqlite`), and a provenance file (`<name>.fingerprint.json`)
that records the seed and fingerprints. If the sample project is missing, `attach --launch` tells you to run `sample create`. Each run writes
a log to `.ui-driver/sample-projects/<name>-create.log`.

`attach --launch` copies the sample project's Shore Control Register into `.ui-driver/registers/`, so the driver
never touches your own register.

## Verbs

| Verb | What it does |
| --- | --- |
| `attach`, `status`, `kill`, `rebuild` | Find, launch, inspect, stop or rebuild the driver's instance. |
| `sample create`, `sample status` | Create the sample projects, or report which exist and their fingerprints (see [Sample projects](#sample-projects)). |
| `tree`, `find`, `audit` | Read the automation tree, search it, or check it against the automation conventions. |
| `source` | Trace the text an element shows back to its entry in the strings files (see [Finding a string](../geodc/docs/string-resources.md#finding-a-string)). A Debug build's text inspector (Ctrl+Shift+I) does the same for the text under the pointer. |
| `click`, `settext`, `select`, `scroll`, `scrollto`, `key` | Act on an element by its AutomationId, or send a key chord. |
| `wait`, `assert`, `settle` | Wait for a condition, check one, or, as a last resort, wait a fixed time. |
| `screenshot` | Capture an element, a region or a window. |
| `logs` | Show the app's recent log lines. |
| `do` | Run several steps in one session, inline (`step ++ step`) or from a script (`--file`). |
| `pause` | In `do --interactive`, stop for a check only a person can make. |

### Where a verb lives

Every verb is one entry in `tools/geoDC.UiDriver/Verbs/VerbTable.cs`, with its usage forms and the method that runs
it. The command line, `do` and the usage message all come from that table, so to add a verb, write its method in the
matching class under `Verbs/` (`SessionVerbs`, `ReadVerbs`, `ActionVerbs`, `CheckVerbs` and so on) and add its entry.
Logic that needs no UI Automation belongs in `tools/geoDC.UiDriver.Core`, where `tests/geoDC.UiDriver.Tests` can
cover it on Linux too. All three are in this harness, in `geodc-harness.slnx`, and build with geoDC's settings
([Building the harness tools](#building-the-harness-tools)).

## Scripts

Checked-in scripts live in the harness's `dev/ui-scripts/` with the `.ui` extension. Each line is one step in the
same grammar as the command line, and `#` lines are comments.

- `@target app|sample` names the script's target.
- `@var name=value` gives a variable a default. `${name}` uses it, and `--var name=value` overrides it.
- `run <script> [name=value ...]` runs another script's steps in place. A bare name resolves under
  `dev/ui-scripts/`, so `run lib/app-goto-page page=SetupTab` reads `dev/ui-scripts/lib/app-goto-page.ui`.
- `dev/ui-scripts/lib/` holds state scripts: each brings the app or gallery into one state from wherever it is and
  ends with an `assert` proving it. Build new checks on them.
- A script should end with an `assert`, so a run proves its outcome. A test parses every checked-in script and
  checks this, and that every `run` reference resolves.

`do --report <path.md>` writes a Markdown walkthrough of a run, with every screenshot it took. `--append <path>`,
or `GEODC_UI_APPEND`, also appends all output to a log file.

## Sample scripts

Start with these. Each needs an `attach --launch` first, as in [Running it](#running-it), and runs with
`pwsh -NoProfile -WorkingDirectory geodc -File dev/ui.ps1 do --file dev/ui-scripts/<script>`:

| Script | What it checks |
| --- | --- |
| `app-smoke.ui` | The shell, its page tabs and the Help menu are there. Run it first after a rebuild. |
| `app-page-tour.ui` | Visits every page and screenshots each one under `.ui-driver/screenshots/`. Add `--report .ui-driver/reports/tour.md` for a walkthrough. |
| `app-job-grid.ui` | Selects a job, opens its Shore Control set and lists the point table's rows. |
| `app-calibration-results.ui` | Opens a calibration attempt's Results tab, computes it and screenshots it. Run it on a freshly launched sample project. |
| `app-about-roundtrip.ui` | Opens Help > About, checks its lines and closes it. |
| `sample-popup-roundtrip.ui` | Opens and closes a blocking popup in the Sample gallery. Needs `attach --launch --target sample`, without `--passive`. |

To check a change after editing code, `rebuild --then <script>` rebuilds, relaunches and runs a script in one
command, for example `pwsh -NoProfile -WorkingDirectory geodc -File dev/ui.ps1 rebuild --then app-smoke`.

## Auditing automation conventions

`dev/ui-scripts/audit-summary.ps1` audits every Sample gallery group and app page against the automation conventions
and prints a summary. The app and the gallery must both be running. Run it from the harness root:

```pwsh
pwsh -NoProfile -File dev/ui-scripts/audit-summary.ps1 [-Target sample|app|both] [-OutFile <path>]
```

## Building the harness tools

The driver, its tests and the gallery corpus tool are in `geodc-harness.slnx`. The harness's `Directory.Build.props`,
`Directory.Build.targets` and `Directory.Packages.props` import geoDC's, so these projects get geoDC's SDK settings,
analyzers, strict code style option and package versions, and references such as `$(GeoDCSourceRoot)` point into
`geodc/`. `.editorconfig` is a copy of geoDC's, which CI checks. Nerdbank.GitVersioning is left out, so the harness
needs no version history. Build and test from the harness root:

```pwsh
dotnet build geodc-harness.slnx -c Release -p:GeoDCStrictCodeStyle=true
dotnet test tests/geoDC.UiDriver.Tests -c Release --no-build      # any OS
dotnet test tests/geoDC.GalleryCorpus.Tests -c Release --no-build # Windows
```

`Directory.Build.rsp` keeps referenced geoDC projects in the solution's configuration, because they are not in the
harness solution. The harness finds geoDC at `geodc/`, or wherever the `GeoDCCloneRoot` environment variable or
property points. When `geodc/` is a symbolic link, as in a cloud session, MSBuild reaches some of a geoDC project's
files through the link and others through its target, which drops files from the output and lets one build delete
what another wrote. The cloud session-start hook sets `GeoDCCloneRoot` to the clone's real path for that reason; set
it yourself wherever else `geodc/` is a link.
