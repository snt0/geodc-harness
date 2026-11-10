---
name: wpf-ui-driver
description: Inspect and drive running geoDC WPF surfaces through the repository UI Automation driver. Use when an agent needs to verify WPF layout, theming, control state, navigation, AutomationIds, screenshots, or interactions in geoDC.App or geoDC.App.UI.Sample, including requests to check the UI or confirm that it looks right. Do not use for WebView2 chart internals; use the chart harness or HTML preview for those.
---

# Use the geoDC UI driver

Ask the user before invoking the UI driver. To give the developer a script that opens the change for them to check themselves, use `geodc-ui-driver-handoff`. If the shim/build hits a restore failure, stop and ask the user to run restore; do not remediate feeds, credentials, or network access autonomously.

The developer guide is [`geodc/docs/ui-driver.md`](../../../geodc/docs/ui-driver.md). Run commands from the harness root through geoDC's shim, with geoDC (`geodc/`) as the working directory; the shim builds the UI driver when needed. Every path the driver takes or reports (`dev/ui-scripts/...`, `dev/fixtures/...`, `.ui-driver/`, `--project` paths) is relative to `geodc/`, as the driver sees it. Stdout is one JSON line per result whenever it is piped or captured, as agent tools do; a terminal gets `--format text` unless `--format json` is given:

```pwsh
pwsh -NoProfile -WorkingDirectory geodc -File geodc/dev/ui.ps1 <verb> [options]
```

Use the raw `geodc/tools/geoDC.UiDriver/bin/Debug/net10.0-windows/geodc-ui.exe` only when the shim cannot run.

### Driving a different worktree

The UI driver resolves everything from its working directory, not from the shim's location: the target's build output
(`src/.../bin/...`), relative `--project` paths, and the `.ui-driver/` lease and tree snapshots. When the shell sits in another checkout (for
example an agent session whose working directory is a different worktree), set the working directory explicitly:

```pwsh
pwsh -NoProfile -WorkingDirectory <worktree> -File <worktree>/dev/ui.ps1 <verb> [options]
```

- Run `kill`, `status`, and `rebuild` from the same working directory as the `attach --launch` that created the lease.
  From anywhere else they find no lease: `kill` leaves the app running, which keeps the build outputs locked.
- `GEODC_APP_EXE` overrides the executable for the `app` target only; it has no effect on `--target sample`.

## Choose the target

- Prefer `--target sample` for App.UI controls, templates, styles, themes, and automation peers. It is fast, state-free, and has a shallow UIA tree.
- Use the default `app` target for application pages and workflows.
- Allow `attach --launch` to open only `dev/fixtures/probe.gdcproj`, or `dev/fixtures/probe-lifecycle.gdcproj` when a check needs calibrations, verifications and GNSS checks in every workflow state. Regenerate them with `pwsh -NoProfile -WorkingDirectory geodc -File geodc/dev/ui.ps1 fixture seed` (add `--lifecycle` for the second) if missing; `fixture status` says which exist.
- `attach --launch` never gives geoDC the developer's own Shore Control Register. It copies the package's `<name>-register.sqlite` (which `fixture seed` writes beside each fixture) to `.ui-driver/registers/<target>.sqlite` and points geoDC at the copy, so register edits last only until the next launch; a package without one gets an empty register. A copy still open by an instance the UI driver did not launch fails the launch with `register-busy`.
- `attach --launch` clears a project lock left by a geoDC process that is no longer running (a killed or crashed instance), so geoDC does not open behind its lock prompt. Any other lock (a live session, another computer, a lock that does not name its holder, or one another session changed while it was being cleared) fails the launch with `project-locked`: close the geoDC session holding the fixture, or `kill` the UI driver instance, then launch again.
- Never pass another `.gdcproj` to `--project` unless the user explicitly names it.
- Add reusable App.UI controls, variants, or styles to the Sample gallery when their relevant states are not already reachable there.

## Follow the interaction workflow

1. Invoke one UI driver command per tool call. Do not chain commands with `&&`, `;`, pipelines, or exit-code echoes.
2. Run `attach`; if no instance is running, run `attach --launch` (add `--passive` to keep the app from ever taking the foreground; see below).
   `attach --launch` records a worktree-local PID lease under `.ui-driver/`; later commands and `kill`
   use only that leased process and deliberately ignore same-named processes launched by tests or other tools.
   After changing code the app runs, use `rebuild` (one call) to stop it, build, and relaunch it the same way; see *Rebuild and stale builds*. A line carrying `"warning":"stale-build"` came from an app older than its build output.
3. Discover with `tree --interactable` before acting, scoped with `--scope <AutomationId>` or `--window` once you know where to look; use `find` to locate a specific element anywhere. Fall back to `tree --depth <n>` for layout or for non-interactive nodes such as badges and text. Never guess AutomationIds or click coordinates. See *Discover compactly*.
4. After an action, ask what changed with `tree --diff` and the same `--scope`/`--window`/`--depth`/`--interactable` arguments as the `tree` that preceded it, instead of dumping the tree again and comparing. In `do`: `tree --interactable --scope X ++ click --id Y ++ tree --diff --interactable --scope X`. See *See what changed*.
5. Prefer `do` for a known multi-step sequence so actions share one attached session. End it with `assert` steps so the run proves its outcome instead of leaving you to read every JSON line; for a check worth repeating, run or add a script under `dev/ui-scripts/` (see *Run checked-in scripts*).
6. Prefer `tree` for state and behavior. Nodes report values, toggled/selected/expanded state, enabled state, and offscreen state.
7. Use `screenshot --id <element>` for visual review. Crop to the relevant element instead of capturing a high-DPI full window; add `--pad` for surrounding context and `--highlight` to mark what is being checked. See *Shape screenshots* below.
8. Wait on a condition, never a guessed delay. `wait --id <element>` waits for visible non-zero layout bounds (not streamed-data quiescence); its condition flags and the actions' `--await` / `--await-gone` cover popups closing, commands enabling, and values loading. See *Wait on conditions* below.
9. Use `scroll --id <container>` for deterministic ScrollPattern movement and `scrollto --id <element>` for ScrollItem/focus-based reveal; inspect the tree first to confirm the relevant patterns.
10. On failure, read the JSON error before retrying, and follow `error.hint` before re-dumping the tree. Element-resolution errors (`element-not-found`, `scope-not-found`, `ambiguous-match`, `element-not-ready`, and a `wait-timeout` or `assert-failed` whose element stayed absent) keep their codes and `candidates` and may add:
   - `closest`: up to 5 AutomationIds in the searched root that resemble the requested one, closest first (`click --id SaveButon` lists `SaveButton`).
   - `elsewhere`: when `--scope` or `--main-window` narrowed the search and the id exists outside it, the `--window` selector of each window holding it (`main`, `title:<title>`, or `handle:0x…`). Element verbs search every window by default, so drop the narrowing option; use the selector for `tree` or `screenshot --window`.
   - `matches`: for an ambiguous id, each match as `{element, scope}`, where `scope` is the nearest ancestor AutomationId that `--scope` resolves to that match alone (null when none does).
   - `hint`: one suggested next step, such as `use --id SaveButton`, `drop --main-window: … in --window "title:Approve attempt"`, `use --scope PopupFooterPanel`, `try scroll --id AttemptList …; … may be an unrealized item in a virtualized list`, or `run scrollto --id X` for an offscreen element.

   Use `logs` after silent failures or crashes.
11. Report interactable nodes with no AutomationId as an annotation worklist instead of working around them. `audit` produces that worklist, with the other convention checks; see *Apply automation conventions*.

## Commands

```text
attach      [--target app|sample] [--launch [--project <path>] [--passive]] [--timeout <s>]
tree        [--id <AutomationId>] [--scope <AutomationId>] [--interactable] [--depth <n=4, unlimited with --interactable>] [--main-window] [--window <selector>] [--diff [--bounds]] [--target app|sample]
find        [--name <substring>] [--role <ControlType>] [--pattern <Pattern>] [--id-prefix <prefix>] [--scope <AutomationId>] [--main-window] [--window <selector>] [--target app|sample]
source      [--id <AutomationId>] [--scope <AutomationId>] [--name <substring>] [--main-window] [--window <selector>] [--target app|sample]
audit       [--scope <AutomationId>] [--main-window] [--window <selector>] [--target app|sample]
screenshot  [--id <AutomationId> [--scope <AutomationId>] [--pad <dip>] | --region x,y,w,h] [--max-width <px=1600>] [--highlight <id>[,<id>...]] [--out <path>] [--main-window] [--window <selector>] [--target app|sample]
click       --id <AutomationId> [--scope <AutomationId>] [<await>] [--target app|sample]
settext     --id <AutomationId> --value <text> [--scope <AutomationId>] [<await>] [--target app|sample]
select      --id <container> --item <name> [--scope <AutomationId>] [<await>] [--target app|sample]
scroll      --id <container> --direction up|down|left|right|top|bottom [--amount small|large] [--scope <AutomationId>] [--target app|sample]
scrollto    --id <AutomationId> [--scope <AutomationId>] [--target app|sample]
wait        --id <AutomationId> [--scope <AutomationId>] [--timeout <s=5>] [<condition>] [--target app|sample]
assert      --id <AutomationId> [--scope <AutomationId>] [--timeout <s=0>] [<condition>] [--target app|sample]
key         --keys <chord> [--window <selector>] [<await>] [--target app|sample]
settle      --ms <n=300>
pause       --message <text> [--timeout <s>]      (do steps only; waits only with do --interactive)
logs        [--tail <n=60>] [--target app|sample]
kill        [--target app|sample]
status      [--target app|sample]
rebuild     [--target app|sample] [--project <path>] [--passive | --active] [--timeout <s=30>] [--then <script> [--var name=value ...] [--report <path.md>]]
do          [--target app|sample] [--report <path.md>] [--interactive] <step> ++ <step> ++ ...
do          [--target app|sample] [--report <path.md>] [--interactive] --file <script> [--var name=value ...]

<condition> = --gone | [--exists] [--enabled | --disabled] [--value <exact> | --value-matches <regex>] [--name <exact>]
              [--toggled on|off|indeterminate] [--selected] [--expanded]
<await>     = [--await <AutomationId>] [--await-gone <AutomationId>] [--await-timeout <s=5>]

Every verb also takes --format json|text (default: text at a terminal, json when piped or captured) and --append <path>; for do they go before the first step.
```

`do` supports `tree` (including `tree --diff`), `find`, `source`, `audit`, `screenshot`, `click`, `settext`, `select`, `scroll`, `scrollto`, `wait`, `assert`, `key`, `settle`, and `logs`, plus `run <script> [name=value ...]`, which runs another script's steps in its place (see *Run checked-in scripts*). It emits one JSON line per step, stops at the first failure, and ends with a summary line: `{"ok":…,"verb":"do","summary":{"steps":n,"run":n,"passed":n,"script":…,"firstFailure":{"step":k,"verb":…,"code":…,"from":[…]}|null}}`. A step that came from a script carries `from`, its `path:line` chain from the outermost script to the line itself; `firstFailure` repeats it.

## Rebuild and stale builds

A driver-launched app that is still running locks its build output, so a later build can fail to replace its assemblies, or succeed into another configuration, while the app keeps running the old code. The UI driver checks for this on every command.

- `status` reports the target's lease without attaching. It never fails for want of an instance:

  ```text
  {"ok":true,"verb":"status","data":{"target":"app","leased":true,"pid":4242,"alive":true,"launchedUtc":"2026-10-16T09:00:00Z","workingDirectory":"C:\\src\\geodc","mode":"active","executable":"C:\\src\\geodc\\src\\geoDC.App\\bin\\Debug\\…\\geoDC.App.exe","projectPath":"C:\\src\\geodc\\dev\\fixtures\\probe.gdcproj","stale":true,"newerBuild":{"path":"C:\\src\\geodc\\src\\geoDC.App\\obj\\Debug\\…\\geoDC.App.dll","writtenUtc":"2026-10-16T09:12:31Z"},"lease":"C:\\src\\geodc\\.ui-driver\\app.json"},"warning":"stale-build"}
  ```

  With no lease, `leased` is false and the lease fields are null. `alive: false` with a lease means the app exited or crashed; the lease stays so `rebuild` can launch it again.
- The running app is **stale** when a build output was written after it started. The files compared are every `*.exe` and `*.dll` beside the launched executable, the compiled copy under `obj/<configuration>/<framework>/` of each of those assemblies built from a project under `geodc/src/` (a build whose copy into the locked output failed still compiled it), and the other configuration's executable (a relaunch takes the newest build). `newerBuild` names the newest such file.
- While the leased app is running and stale, every command's JSON line ends with `"warning":"stale-build"`, and stderr names the newer file. In `do`, every step line and the summary carry it. Treat any result from such a run as describing the old code: `rebuild`, then repeat the check.
- `rebuild` kills the leased app and waits for it to exit, runs `dotnet build` on the target's project, then launches the build it just made, as `attach --launch` would. Launch arguments not given on the command line come from the lease, alive or not: `--project`, passive or active mode, and the configuration (Debug or Release) of the leased executable; the build and the launch always use the same configuration. Without a lease it builds the newest existing configuration (else Debug) and opens `dev/fixtures/probe.gdcproj`, actively unless `--passive` is given; without a lease it needs `--target`, and fails `no-lease` otherwise. `--passive` / `--active` change the mode. It refuses to rebuild the app while `GEODC_APP_EXE` is set.
- `rebuild --then <script> [--var name=value ...]` runs a script against the relaunched app, as `do --file` would: the rebuild line comes first, then the script's step lines and summary, and the command exits with the script's exit code. Use it with a `lib/` state script to get back to where you were after changing code, for example `rebuild --then lib/app-open-attempt --var page=VerificationTab --var "attempt=Pitch verification (INITIAL)"` (a bare name resolves under `dev/ui-scripts/`, as for `run`). The script is parsed before the app is stopped, so a script that cannot run fails with the app untouched; a failed build skips it; a failing step leaves the app running in whatever state it reached.
- A successful `rebuild` reports the build and the relaunch, whose `launch` object has the `attach` fields:

  ```text
  {"ok":true,"verb":"rebuild","data":{"target":"app","killedPid":4242,"reusedLease":true,"build":{"project":"src/geoDC.App/geoDC.App.csproj","configuration":"Debug","exitCode":0,"durationMs":21873,"log":"C:\\src\\geodc\\.ui-driver\\builds\\app.log"},"launch":{"pid":5120,"mainWindowHandle":"0x000A1234","mainWindowTitle":"geoDC","launched":true,"mode":"active","executable":"…","executableBuiltUtc":"…","projectPath":"…","lease":"…"}}}
  ```

- A failed build fails `build-failed` and does not relaunch the app. `error.errors` holds the first 10 distinct errors as `{file, line, column, code, message, project}` (`file` is the tool, such as `MSBUILD`, when the error has no source location), `error.errorCount` counts them all, and `error.build.log` is the full MSBuild log. The lease stays, so fix the errors and run `rebuild` again:

  ```text
  {"ok":false,"verb":"rebuild","error":{"code":"build-failed","message":"dotnet build src/geoDC.App/geoDC.App.csproj -c Debug failed with exit code 1 and 1 error(s); …","errors":[{"file":"C:\\src\\geodc\\src\\geoDC.App\\Views\\MainWindow.xaml.cs","line":12,"column":5,"code":"CS1002","message":"; expected","project":"C:\\src\\geodc\\src\\geoDC.App\\geoDC.App.csproj"}],"errorCount":1,"killedPid":4242,"build":{…}}}
  ```

- A restore failure surfaces the same way, as `NU…` errors; stop and ask the user to run restore.
- `kill` stops the app and removes the lease, so a `rebuild` after `kill` needs `--target` again.

## Discover compactly

- `tree --interactable` prints only nodes with an Invoke, Toggle, Value, SelectionItem, ExpandCollapse, or Scroll pattern, plus each one's ancestors up to the nearest node with an AutomationId (the `--scope` that disambiguates it). Other nodes are dropped and their kept descendants move up a level, so indentation shows the kept outline, not the full depth. It walks the whole subtree unless `--depth` limits the walk.
- It hides non-interactive nodes even when they have an AutomationId (badges, text, pages with nothing actionable); use `find --id-prefix` or a plain `tree` to see those.
- `tree --scope <AutomationId>` without `--id` dumps that element's subtree. `--scope` and `--window` cannot be combined.
- `find` searches the whole subtree under `--scope` (or the selected windows) with no depth limit. Criteria combine with AND and ignore case: `--name` is a Name substring, `--role` a UIA control type (`Button`, `Edit`, `CheckBox`, `ListItem`, `TabItem`, ...), `--pattern` one of `Invoke`, `Value`, `Toggle`, `Selection`, `SelectionItem`, `ExpandCollapse`, `Scroll`, `ScrollItem`, and `--id-prefix` an AutomationId prefix. An unknown role or pattern fails `bad-arguments` with the valid names as `error.candidates`.
- `find` prints one line per match in the `tree` node format followed by `path:`, the AutomationIds of its ancestors (`—` when it has none). No match is a success with `data.matches: 0`; use `assert` to require an element.
- `tree` reports `data.nodes` (lines printed) and `data.walked` (elements read), so you can see what the filter removed.

The output shape (action ids are illustrative; each popup names its own):

```text
find --role Button --scope PopupFooterPanel
{"ok":true,"verb":"find","data":{"criteria":{"name":null,"role":"Button","pattern":null,"idPrefix":null},"scope":"PopupFooterPanel","matches":2,"walked":7}}
[Button] id: CancelButton  name: "Cancel"  bounds=(812,604 88x32)  enabled  patterns: Invoke  path: PopupFooterPanel
[Button] id: ApplyButton  name: "Apply"  bounds=(908,604 88x32)  enabled  patterns: Invoke  path: PopupFooterPanel
```

## Trace text to its strings entry

Use `source` to answer "where does this text come from?" instead of grepping the YAML:

- It reads every distinct text in the subtree of `--id`, of `--scope` when there is no `--id`, or of the selected windows: each element's Name, and its ValuePattern value when that differs. `--name <substring>` keeps only the texts containing it, ignoring case. Texts with no letter (numbers, glyphs) are skipped.
- Each text prints a line naming it and the first element that shows it (`[Role] id: …`, `×n` when several do, and `path:`), then each strings-file entry that reads as it, as `file:line › key.path (case c)  Class.Member` (`filled in` when its placeholders were filled), with the `read at file:line` lines under `src` that read the entry. A text no entry reads as says it is data, or text built in code.
- The entry must account for the whole text, so `Save` is not traced to every sentence containing it. Several entries mean the same words live in several strings files: the same-case match comes first, and the `read at` lines tell them apart.
- It reads the strings files of the worktree it runs in. Outside one it fails `no-repository`, and a strings file the catalog cannot read fails `strings-unreadable`; both exit 1. Finding no text is a success with `data.texts: 0`.

```text
source --name "Choose a job"
{"ok":true,"verb":"source","data":{"elementId":null,"scope":null,"name":"Choose a job","texts":1,"sourced":1,"ambiguous":0,"unsourced":0,"walked":412}}
"Choose a job from the title bar, or create one in Setup, before calibrating a sensor."  [Text]  path: …
  src/geoDC.App/Strings/calibration.yaml:97 › pageGate.message (case false)  CalibrationText.PageGateMessage
    read at src/geoDC.App/Features/Calibration/CalibrationShellPage.xaml:180
```

## See what changed

Every `tree` stores what it read under `.ui-driver/snapshots/`, keyed by the target and the arguments that decide what is read: `--id`, `--scope`, `--window`, `--main-window`, the effective depth, and `--interactable`. `tree --diff` with the same arguments reads again, replaces the snapshot, and reports only the differences.

- `data.added` and `data.removed` list the roots of subtrees that appeared or disappeared, each as `{role, id, name, path, descendants}`; descendants are counted, not listed. `data.changed` lists nodes present in both readings as `{role, id, name, path, changes}`, where `changes` holds only the fields that differ, each as `{from, to}`: `role` (an element that kept its AutomationId but changed control type), `name`, `value`, `enabled`, `toggled`, `selected`, `expanded`, `offscreen`. `path` is the AutomationIds of the node's ancestors.
- Bounds changes are left out, since layout shifts would bury state changes; add `--bounds` to include them as `bounds: {from: "x,y wxh", to: …}`. Scroll position and patterns are not compared; `scroll` reports its own before/after percentages.
- The text after the JSON line prints each added subtree in full in the `tree` line format prefixed `+ `, each removed root prefixed `- ` with its descendant count, and each changed node prefixed `~ ` with `field: from -> to`. Nothing is printed when nothing changed; the three arrays are then empty.
- Nodes match level by level under a matched parent: by AutomationId, else by control type and Name, in order of occurrence for repeats. Leftover nodes without an id pair up by control type when both readings have the same number left, so a text block whose Name changed reads as a `name` change. A node under a parent that did not match, or that moved to another parent, is part of an added or removed subtree.
- With no stored snapshot for those arguments (or an unusable one, reported on stderr), `tree --diff` prints the full tree like `tree` and sets `data.baseline: true`. Otherwise `data.baseline` is false.
- Diff a scope that survives the action. A `--scope` the action removes (a closing popup's `PopupPanel`) fails to resolve; diff its window or the whole tree instead, and the popup appears as a removed subtree.

The output shape (ids other than the popup anchors are illustrative):

```text
do --target sample tree --interactable ++ click --id PopupBlockingButton --await PopupPanel ++ tree --diff --interactable
{"ok":true,"verb":"tree","step":3,"data":{…,"diff":true,"baseline":false,"bounds":false,"added":[{"role":"Pane","id":"PopupPanel","name":"","path":["SampleWindow"],"descendants":9}],"removed":[],"changed":[]}}
+ [Pane] id: PopupPanel  bounds=(…)  enabled  patterns: —  path: SampleWindow
+   [Pane] id: PopupFooterPanel  …
```

A switch toggle reads as one changed node:

```text
{"ok":true,"verb":"tree","data":{…,"added":[],"removed":[],"changed":[{"role":"CheckBox","id":"ShowGridToggle","name":"Show grid","path":["GalleryWindow","InputsPage"],"changes":{"toggled":{"from":"off","to":"on"}}}]}}
~ [CheckBox] id: ShowGridToggle  name: "Show grid"  path: GalleryWindow > InputsPage  toggled: off -> on
```

## Assert outcomes

`assert` checks the same conditions as `wait` (below), but once: it fails `assert-failed` straight away, with the expected conditions and the observed state in the message and `error.state`. Give it `--timeout <s>` only when the state may legitimately still be settling. With no condition it asserts the element is laid out.

- `--exists` needs the element in the tree, laid out or not; `--gone` needs it absent.
- `--value <exact>` compares the ValuePattern value (or Name without one) exactly; `--value-matches` is the regex form. `--name <exact>` compares the UIA Name.
- An `assert` failure inside `do` stops the run like any other failed step.

## Shape screenshots

Every capture is a PrintWindow of one top-level window, then cropped, downscaled, and annotated on the bitmap only; nothing is drawn into the app.

- `--max-width <px>` (default 1600) downscales a wider capture proportionally with area averaging; it never upscales. `--max-width 0` keeps every physical pixel, for checking 1px details at high DPI.
- `--pad <dip>` grows an `--id` crop by that many device-independent pixels on each side (converted with the monitor's scaling), clamped to the window. It needs `--id`.
- `--highlight <id>[,<id>...]` draws a 2px magenta outline just outside each element (on its edge pixels where the image ends). It combines with an `--id` crop, `--region`, or a full-window capture, and resolves ids with the same `--scope` and `--main-window`. A missing id fails as an unresolved element.
- `--region x,y,w,h` is in window-relative physical pixels, before any downscale.

The JSON reports the written size, the captured size before downscaling, and where each highlight landed in the written image. `scale` is output pixels per captured pixel (1 when not downscaled); `padPixels` is the pad in physical pixels before clamping. A highlight that cannot be drawn is reported, not failed: `visible: false` with `reason` `outside-capture` (outside the crop), `empty-bounds` (offscreen or collapsed), or `other-window` (it lives in another top-level window, such as a detached popup; capture that window with `--window` instead). `clipped: true` means only part of the element is in the image.

```text
{"ok":true,"verb":"screenshot","data":{"path":".ui-driver/screenshots/screenshot-….png","width":1600,"height":900,"originalWidth":2560,"originalHeight":1440,"scale":0.625,"maxWidth":1600,"pad":0,"padPixels":0,"elementId":null,"window":"…","highlights":[{"elementId":"SaveButton","visible":true,"clipped":false,"bounds":"250,125 100x30","reason":null}]}}
```

## Run checked-in scripts

`dev/ui-scripts/*.ui` holds reviewed, repeatable checks. Run one with `do --file <script>`; an `attach` (or `attach --launch`) must come first, as for any `do`.

- One step per line in the inline step grammar; blank lines and `#` lines are skipped. Single or double quotes group a token with spaces, with no escapes inside (so regex backslashes survive); `''` inside single quotes is a literal quote. Steps cannot contain `++`.
- `${name}` is replaced by `--var name=value`, or by a script default `@var name=value` (the rest of the line). An undefined variable, or a `--var` the script never uses, fails `bad-script` before any step runs.
- `@target app|sample` names the script's target; a conflicting `--target` is rejected. Every checked-in script declares one.
- Current checks: the developer samples in `geodc/docs/ui-driver.md` (`app-smoke.ui`, `app-page-tour.ui`, `app-job-grid.ui`, `app-calibration-results.ui`, `app-about-roundtrip.ui`), `sample-popup-roundtrip.ui` (Sample gallery, active launch: it presses Escape), `app-open-calibration.ui` (app fixture; `--var "attempt=<attempt list name>"` picks another attempt) and `sample-popup-manual-check.ui` (Sample gallery, a `pause` between opening and closing the popup). All are built on the `lib/` state scripts below.

### Compose scripts with `run`

`run <script> [name=value ...]` is a step, in a script or inline, that runs another script's steps in its place, in the same session. Use it to reach a known state and then check something, instead of copying navigation steps:

```text
do --file dev/ui-scripts/app-open-calibration.ui
do run lib/app-open-attempt page=VerificationTab "attempt=Pitch verification (INITIAL)" ++ assert --id ResultsTab --exists
```

- A bare name resolves under `dev/ui-scripts/` (`lib/app-goto-page` reads `dev/ui-scripts/lib/app-goto-page.ui`; `.ui` is added when there is no extension). A name that already starts with `dev/ui-scripts/` is kept as it is. A name starting with `./` or `../` is relative to the calling script's folder, or to the working directory for an inline step or `rebuild --then`.
- The called script sees only the variables its `run` passes and its own `@var` defaults, not the caller's. The caller's `${…}` substitute into the arguments first, so `run lib/x attempt=${attempt}` passes a variable on. Quote an argument with spaces: `run lib/x 'attempt=Pitch calibration (INITIAL)'`. Passing a variable the called script never uses fails `bad-script` and names the ones it takes.
- Called scripts are expanded when `do` parses, before any step runs. A missing script fails `script-not-found`; an `@target` other than the run's, a script that reaches itself, or an error inside a called script fails `bad-script`, and the message ends with the chain of callers `(run from lib/a.ui:2 < top.ui:5)`.
- `run` prints no line of its own. Each step it contributed is a normal step line with `from`, and the summary's `steps` counts the expanded steps.

### State scripts in `lib/`

`dev/ui-scripts/lib/` holds short scripts that each bring the app or the gallery into one state from wherever it is, and end with an `assert` that proves it. Build checks on them, run them after `rebuild --then`, and add one when a state is worth reaching twice. Name them `app-<state>.ui` or `sample-<state>.ui`, give each a header comment (the state, its variables, whether it needs an active launch, a `run` example), declare `@target`, and give variables defaults that exist in `dev/fixtures/probe.gdcproj`. A test parses every checked-in script, expands its `run` steps and checks that it ends with an `assert`, so a broken reference fails CI; whether the AutomationIds still exist is checked only by running it on Windows.

Current state scripts (defaults are `dev/fixtures/probe.gdcproj` content; all are passive-safe):

| Script | State it reaches | Variables (default) |
|---|---|---|
| `lib/app-goto-page` | A top-level page is showing. Navigation needs no job; a page without one shows its gate. | `page` (`LandingTab`): `LandingTab`, `SetupTab`, `SurveyTab`, `CalibrationTab`, `VerificationTab`, `ShoreControlTab`, `GnssTab`, `ReportsTab` |
| `lib/app-select-job` | A job is the active job, selected on Setup. | `job` (`Initial survey`): a substring of the list name `Name (Code)`; the job name input must then equal it |
| `lib/app-open-attempt` | An attempt is selected on Calibration, Verification or GNSS check, with a tab showing. It asserts the attempt name. | `page` (`CalibrationTab`), `attempt` (`Pitch calibration (INITIAL)`, the exact list name), `tab` (`DataInputTab` or `ResultsTab`) |
| `lib/app-open-verification` | `app-open-attempt` on Verification. | `attempt` (`Pitch verification (INITIAL)`), `tab` (`DataInputTab`) |
| `lib/app-open-gnss-check` | `app-open-attempt` on GNSS check. | `attempt` (`GNSS spot check (quayside)`), `tab` (`DataInputTab`) |
| `lib/app-open-survey-section` | A Survey section is showing. | `section` (`FeaturesTab`): `FrameTab`, `FeaturesTab`, `SensorsTab`, `GroupsTab` |
| `lib/app-open-shore-control` | Shore Control shows the active job's set and its points. | `set` (`Quayside control 2026`): the title bar's Shore Control name |
| `lib/sample-goto-page` | A gallery group is showing. | `nav` (`OverlaysNavItem`): any `<GalleryGroup>NavItem` |
| `lib/sample-open-popup` | The blocking popup is open. | none |
| `lib/sample-close-popup` | The open popup was cancelled through its footer `CancelButton`. This is the passive-safe alternative to Escape. | none |

The attempt scripts work on the active job; the app activates the first job on load, so run `lib/app-select-job` first when another job may be active.

## Output for people: `--format text` and `do --report`

Agents read the default JSON. Leave `--format` alone when you parse the output yourself. Use these when the output is for a person: a developer running a script, or evidence in a pull request.

- `--format text` prints each JSON line as one readable line. A step line reads `✓  2 click CalibrationTab → AttemptList (212 ms)  · lib/app-goto-page.ui:12`: the step number, the verb and what it acted on, what it awaited, how long the step took, and the innermost script line it came from. A failure reads `✗  3 click: element-not-found`, followed by indented lines with the message, `hint`, `closest`, `elsewhere`, `candidates`, ambiguous `matches`, every `audit` finding, and a failed build's first errors and log. A `stale-build` warning is printed once, as a `!` line above the first line that carries it. The `do` summary reads `── 6 steps, all passed`, or `── failed at step 5 (select, item-not-found) at lib/app-open-attempt.ui:17; 4 of 10 passed, 5 not run`. Output that is already text passes through unchanged: tree, `find`, `source` and diff lines, and `logs` lines.
- The text is rendered from the same JSON objects, and the default JSON output is unchanged. Step times exist only in the text and the report.
- `do --report <path.md>` (and `rebuild --then <script> --report <path.md>`) writes a Markdown walkthrough of the run. It has the script, its variables as the steps saw them, a table with one row per step that ran (command, outcome, time and the script line it came from), the first failure in full (message, caller chain, hint, closest ids, observed state), a `stale-build` warning in its header when the run had one, and every screenshot the run took, embedded by a path relative to the report. The report is written when the run ends, whether it passed, failed at a step, or failed before its first step (for example an `attach` that found nothing). With `--report`, the JSON summary adds `report`, the path as given. Keep reports under `.ui-driver/` unless the user wants one elsewhere.

```text
do --format text --report .ui-driver/reports/calibration.md --file dev/ui-scripts/app-open-calibration.ui
```

- `--append <path>`, or the `GEODC_UI_APPEND` environment variable when the option is not given, also appends everything the command prints to a file: stdout and stderr, in the chosen format, after a header line `=== <UTC time> geodc-ui <arguments>`. The file is never truncated, so one file can collect a whole session. A developer can set it once per shell (`$env:GEODC_UI_APPEND = '.ui-driver/session.log'`) and share the file instead of copying output from the terminal. A file that cannot be opened is reported on stderr, and the command runs anyway.

## Pause for a manual check

`pause --message <text> [--timeout <s>]` is a `do` step for a check only a person can make: a chart's shape, a layout, whether wording reads well. **Never pass `--interactive` yourself**: it is for a developer running a script, and without it every `pause` passes at once with `data.skipped: true`, so the scripts that contain pauses still run unattended.

- With `do --interactive` (or `rebuild --then <script> --interactive`), the step brings geoDC's main window forward and opens a small topmost UI driver window: "Paused at step 7 · lib/x.ui:12", the message, an optional note, and Continue and Fail. geoDC stays usable while it waits, and the passive-mode foreground guard does not push the app back after the step.
- Continue passes with `data.verdict: "continued"` and the note. Fail, or closing the window, fails `manual-check-failed` with the note in the message and `error.note`. `--timeout` fails `pause-timeout` when nobody answers. Both failures exit 2 and stop the run like any failed step.
- The verdict and note appear in `--format text` (`✓  7 pause "Check the sign" continued: reads correctly`) and in `do --report`, so a report records the manual checks next to the automated ones.
- Keep `pause` out of `lib/` state scripts; put it in the check scripts built on them. `dev/ui-scripts/sample-popup-manual-check.ui` is the example.

## Wait on conditions

`settle` is the last resort: use it only when no condition below can express what you are waiting for, such as a window resize where every anchor is already laid out at the old size.

- `wait --id X` with no flag waits until X is present, on screen, and has non-zero bounds.
- `wait --id X --gone` waits until X is absent, or offscreen with zero bounds. Use it for popups, dropdowns, and overlays closing. A missing `--scope` counts as gone, since a closed popup takes its scope anchor with it.
- `--enabled` / `--disabled` wait on IsEnabled, for example a Save button enabling once a form validates.
- `--value-matches <regex>` tests the ValuePattern value, or the Name when there is no ValuePattern (text blocks, badges). It is an unanchored .NET regex; add `^`/`$` for an exact match.
- `--toggled on|off|indeterminate`, `--selected`, and `--expanded` test the Toggle, SelectionItem, and ExpandCollapse patterns.
- State flags combine with AND, need the element present but not on screen, and cannot combine with `--gone`. A state condition on an AutomationId that matches several present elements fails `ambiguous-match`; add `--scope`.
- On timeout `wait` fails `wait-timeout`; the message and `error.state` carry the element's last observed state (presence, enabled, offscreen, size, value/name, toggle, selection, expansion). Read it before retrying: it usually says which precondition is missing.
- `click`, `settext`, `select`, and `key` take `--await <id>` (wait until laid out) and `--await-gone <id>` (wait until gone) so an action and its proof run as one step; both may be given (`--await-gone` runs first), each with `--await-timeout` (default 5s). Await targets ignore the action's `--scope` and search every window. Results appear as `data.awaited`. If an await fails, the action has still happened; the error message says so.
- `--await <id>` returns at once if `<id>` was already laid out before the action, so await something the action creates or reveals, not something that was already there.

A Sample-gallery popup round trip needs no `settle` (`key` needs an active launch):

```text
do --target sample click --id OverlaysNavItem ++ click --id PopupBlockingButton --await PopupPanel ++ screenshot --id PopupPanel ++ key --keys Escape --await-gone PopupPanel
```

## Choose active or passive launch

Every `attach --launch` starts the app behind the user's windows (`ShowActivated = false`, then sent to the bottom of the z-order). The flag decides what happens next:

- **Active** (`attach --launch`, the default): windows then behave normally. UI Automation Invoke and Select may bring the app forward, and the UI driver never pushes it back, so the user can click it to watch the run. Every verb works, including `key`.
- **Passive** (`attach --launch --passive`): the app never takes the foreground, so the user can keep working while it is driven.
  - Windows carry `WS_EX_NOACTIVATE`, so UI Automation Invoke and Select (which otherwise activate the window from outside the process) leave it in the background. Clicking it with the mouse does not activate it either.
  - Actions that need the foreground fail with `passive-mode` instead of pulling the app forward: `key`, `select` on a collapsed drop-down, and `settext` on an element without a ValuePattern. `scrollto` skips its focus fallback and scrolls the viewport instead.
  - If a step still pulls the app forward, the UI driver hands the foreground back and writes `geodc-ui: '<verb>' brought geoDC to the foreground` to stderr.
  - Focus theft after a step returns (for example a chart grabbing focus as it loads) is only caught by the next step.
- To find which call brings the app forward, set `$env:GEODC_UI_TRACE = '1'`: each step then writes timestamped `ui-driver-trace` lines to stderr (step start, Invoke call and return, step end, restore) naming the foreground owner. Leave it off otherwise; it adds several lines per step.
- `screenshot` works in both: PrintWindow captures occluded windows. Do not minimize the app; WPF stops rendering minimized windows.
- The mode is recorded in the lease, so later commands and `rebuild` follow it without repeating the flag. `rebuild --active` or `rebuild --passive` changes it, as does `kill` and a relaunch.
- Prefer passive for unattended captures and checks that need no typing; use active when a run needs `key` or the user wants to watch.

## Handle WPF/UIA behavior

- `click` falls back through Invoke, Toggle, SelectionItem, and ExpandCollapse.
- Use `select` for data-bound items without AutomationIds. It matches UIA Name exact, case-insensitive, then unique substring; missing or ambiguous errors list available names.
- Do not inspect dropdown contents with click followed by tree: WPF collapses the dropdown when foreground changes. Use `select`; to enumerate items, deliberately request an absent item and read the returned names.
- Use `key` only for chords such as `Escape`, `Enter`, `Ctrl+S`, or `F5`; use `settext` for text.
- Use `--window main|title:<substring>|handle:0x...` for top-level `tree`, `screenshot`, or `key` against detached/immersive popups. It cannot combine with `--id` or `--main-window`; use `--scope` for elements.
- `key` follows a modal popup by default, but modeless popups must be named with `--window`. It fails `window-not-foreground` on locked or disconnected sessions rather than sending input elsewhere.
- Use `scroll --amount small` for line-sized movement, the default `large` for page-sized movement, and `top` / `bottom` for deterministic vertical endpoints. The JSON result reports before/after percentages.
- `scrollto` prefers UIA ScrollItemPattern and falls back to focusing keyboard-focusable WPF controls. It cannot realize a virtualized element that is absent from the UIA tree.
- Prefer `wait` (or an action's `--await`) after asynchronous navigation. Use `settle` only when no condition can be expressed, such as a window resize.
- Use `rebuild` for the edit-and-verify loop rather than `kill`, a separate build, and `attach --launch`. A plain `attach --launch` takes the newest of the Debug and Release builds; check `executable` and `executableBuiltUtc` in its output, and watch for `"warning":"stale-build"` on later commands.
- Virtualized items that have never been realized are absent from UIA; scroll or filter first.
- When UI driver calls slow to many seconds, or the app stalls, take a `dotnet-trace` CPU sample of the UI thread before guessing. If `ContextLayoutManager.UpdateLayout` dominates, the window is in a layout loop. A debug build also logs `UiLayoutStallDetected` when layout runs on with no input; the trace shows which element. See `geodc/docs/logging.md` > Diagnosing UI Stalls.

## Apply automation conventions

Check a surface against these conventions with `audit` rather than by reading a tree dump; see *Audit automation conventions* below. The role vocabulary is the closed list in `geodc/docs/code-vocabulary.md`.

- Name IDs as PascalCase `{Noun}{Role}` using only `Button`, `Input`, `Combo`, `Picker`, `Choice`, `Toggle`, `Tab`, `List`, `Item`, `Text`, `Badge`, `Page`, `Panel`, or `Popup` as roles. Pick the role the element plays, not its WPF type (a check box is a `Toggle`, a metric is `Text`, an empty state is a `Panel`); `geodc/docs/code-vocabulary.md` > Automation ID roles says what each names.
- Keep IDs unique within a page; do not prefix them with the page name.
- Reserve `LandingTab`, `SetupTab`, `SurveyTab`, `CalibrationTab`, `VerificationTab`, `ShoreControlTab`, `GnssTab`, and `ReportsTab` application-wide. Name page-level tabs for their content.
- Give scope anchors an automation peer. A named bare `Border` or `StackPanel` does not appear in the UIA tree.
- Permit fixed template-part IDs to repeat per control instance; disambiguate them with `--scope <host>`.
- Add `OnCreateAutomationPeer` to custom App.UI controls only when a tree dump proves the default peer hides the control.
- Treat raw record-style UIA names as a prompt to add an appropriate view-model `ToString` override.
- Use `PopupPanel` (`DialogPanel` for dialogs) and `PopupFooterPanel` as popup scope anchors for repeated footer action IDs.

### Audit automation conventions

`audit` walks the whole subtree under `--scope` (or the selected windows, like `find`) and reports each breach as a finding with a rule id:

| Rule | Finding |
|---|---|
| `missing-id` | An interactable node (an Invoke, Toggle, Value, SelectionItem, ExpandCollapse, or Scroll pattern) with no AutomationId. Data-bound items (`ListItem`, `DataItem`, `TreeItem`, `HeaderItem`) and everything inside them are exempt; `select` reaches them by Name. |
| `id-naming` | An AutomationId that is not PascalCase `{Noun}{Role}` with a role from the closed vocabulary. One finding per distinct id. |
| `duplicate-id` | An AutomationId on more than one node in the same page scope, other than an allowlisted template part. One finding per id and scope. |
| `reserved-id` | A shell destination id (`LandingTab` … `ReportsTab`) anywhere but directly in a top-level window outside every page scope, or a second such occurrence. |
| `raw-name` | A UIA Name that is raw `ToString` output: record-style (`JobRow { Name = A }`) or a namespace-qualified type name. One finding per type. |

- A **page scope** is the nearest ancestor whose id ends in `Page`, a popup frame (`PopupPanel`, `DialogPanel`, `LoadingPanel`), a `Window` (an owned popup window sits under its owner), or a data-bound item; else the audit root. A node's own id belongs to the scope it sits in. A popup with its own `PopupPresentation.AutomationId` that is not a window shares the scope of the page beneath it.
- The allowlists live in one place, `geodc/tools/geoDC.UiDriver.Core/AutomationConventions.cs`: the roles, the reserved shell ids, the template-part ids that may repeat within a page (help badge, coordinate, date and CRS pickers, calendar, list view, overlay drawer, wizard frame and stepper, popup footer; `*` is a glob), and the popup frame ids. WPF's own `PART_*` ids are exempt from naming and duplicates, and so is every `x:Name` declared in `geodc/src/geoDC.App.UI` XAML: WPF reports an element's `x:Name` as its AutomationId when none is set, so template internals (`Chrome`, `HeaderToggle`, `HelperText`) surface from every toolkit control. The UI driver embeds the toolkit XAML at build time, so rebuild it after changing a template. Because the match is by id alone, an app element that reuses one of those names is exempt too. Scroll bars and the window title bar are not walked. Add a toolkit template part there when you document it; a UI driver test fails when an entry matches nothing declared in `geodc/src/geoDC.App.UI`.
- Each finding is `{rule, role, id, name, path, scope, occurrences, message}`: `path` is the ancestor AutomationIds from the audit root down (as `find` prints them), `scope` the page scope's id (null for an id-less window or item), and `occurrences` how many nodes a grouped finding stands for. A grouped finding points at the first occurrence. Findings are ordered by rule, in the table's order, then by document order, so the same surface gives the same list.
- No findings is a success: `{"ok":true,"verb":"audit","data":{"scope":null,"walked":812,"count":0,"byRule":{…},"findings":[]}}`. Findings fail `audit-findings` with exit code 4 (distinct from the UI driver's error codes) and the same fields on `error`; in `do` this stops the run like a failed `assert`:

  ```text
  {"ok":false,"verb":"audit","error":{"code":"audit-findings","message":"2 automation convention finding(s): 1 missing-id, 1 duplicate-id.","scope":"SetupPage","walked":214,"count":2,"byRule":{"missing-id":1,"id-naming":0,"duplicate-id":1,"reserved-id":0,"raw-name":0},"findings":[{"rule":"missing-id","role":"Button","id":null,"name":"Refresh","path":["SetupPage","VesselPanel"],"scope":"SetupPage","occurrences":1,"message":"Interactable Button (Invoke) has no AutomationId."},{"rule":"duplicate-id","role":"Button","id":"SaveButton","name":"Save","path":["SetupPage"],"scope":"SetupPage","occurrences":2,"message":"'SaveButton' appears 2 times in page scope 'SetupPage'; ids are unique within a page unless they are template parts."}]}}
  ```

- Fix a finding in the view, or list it in the annotation worklist; do not suppress it in the UI driver. Audit one app page (`--scope SetupPage`) or one Sample gallery group at a time to keep the list readable: in the gallery, click the group's nav item, then `audit --main-window`. Each gallery group's page is `<Group>Page` (`FoundationsPage`), so `--scope FoundationsPage` audits the group without the gallery chrome. `pwsh -NoProfile -File dev/ui-scripts/audit-summary.ps1 [-Target sample|app|both]` audits every gallery group and app page in turn, saves the raw lines, and prints counts by rule with the non-`missing-id` findings in full.

## Capture shared visual baselines

Run `pwsh -NoProfile -File dev/capture-views.ps1` for changes to shared chrome, page gates, dock panes, containers, styles, or the toolkit. It writes PNGs and `capture.json` under `dev/captures/<label>/`.

- Runs in passive mode by default and sizes the window to the monitor work area without activating it; pass `-Active` for an active-mode, truly maximized run.

- `-Set spine` captures the core app views; `-Set full` adds deeper app states, popup/chart presentations, and one Sample gallery shot per group.
- Compare before/after corpora by eye. They are not byte-stable or pixel-regression tests.
- Rendered chart anchors are `CoChartSurfacePanel`, `GnssChartSurfacePanel`, `VrfChartSurfacePanel`, and `AnalysisChartSurfacePanel`.
- Popup-opening capture views must be `Volatile` and should clean up after themselves.
- Gallery nav items are `<GalleryGroup>NavItem` (`FoundationsNavItem`, `ActionsNavItem`, `InputsNavItem`, `QuantitiesNavItem`, `ConventionsNavItem`, `CollectionsNavItem`, `CardsNavItem`, `SectionsNavItem`, `ProgressNavItem`, `NavigationNavItem`, `FeedbackNavItem`, `OverlaysNavItem`, `ChromeNavItem`); the theme switch is `ThemeToggleButton`. Specimen ids are the `Id` of each `g:Specimen` under `geodc/src/geoDC.App.UI.Sample/Specimens/<GalleryGroup>/`.
