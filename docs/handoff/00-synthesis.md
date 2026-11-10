# 00 — Synthesis of the handoff investigation

Verified @ geoDC `e9a8766` (`origin/dev`), Snt.Lib `d545ca2`. Epic: snt0/geodc#780.

Five read-only reports were produced in parallel. This page reconciles them against the decisions recorded in the
epic, lists where the evidence overturns or sharpens a decision, and proposes the sub-issues. Read the numbered
reports for the evidence; this page only cites them.

| Report | Covers |
|---|---|
| [01](01-snt-lib-vendoring.md) | What geoDC uses from Snt.Lib, the exact copy list, destinations, generator wiring, PR sequence |
| [02](02-app-feature-map.md) | How `geoDC.App` is wired, the Shore Control chain, popup template lookup, feature-folder proposal, 572-file map |
| [03](03-project-boundaries.md) | Project catalogue, the six small projects, demo-project removal, probe review, CLI surfaces, tests, solution filters |
| [04](04-discoverability-audit.md) | Four tasks played as a new developer (40 hops, 4 dead ends), text-system mitigations, the "finding things" page |
| [05](05-infra-and-docs.md) | File-by-file split classification, CI dependencies, submodule mechanics, docs audit, codebase reference design |

## 1. Decisions the evidence changes

These are the places where a decision in the epic should be revisited. Each needs a yes or no from the owner.

| # | Epic said | Evidence | Recommendation |
|---|---|---|---|
| 1 | Outer repo wraps geoDC as a **git submodule** | A submodule gives a detached HEAD after every update, a pointer bump per geoDC commit, a second PR per change, and Claude Code worktrees of the outer repo do not carry the submodule. Nothing in the tree needs a `.git` directory, so either form works technically (05 §3). | **Nested, gitignored clone** of geoDC on `dev` inside the harness repo, with a bootstrap script. Same single working directory, same `geodc/` path prefix, none of the pointer costs. Use a submodule only if a recorded harness↔geoDC pairing is wanted. |
| 2 | `GalleryCorpus` moves to the harness | It is a one-file exe in `geoDC.slnx` referencing `geoDC.App.UI.Sample`; the rendering code and its test live in `src`/`tests` and stay regardless. Moving the exe needs an outer csproj with a `../geodc/src/…` reference and no `Directory.Build.props` (03 §2, 05 §1.1). | **Keep the project in geoDC**; move only the drivers `dev/capture-ds-previews.ps1`, `dev/capture-views.ps1`, `dev/sync-design-system.ps1`. |
| 3 | Generator folds into `geoDC.App.UI.Generators` | Technically safe (the two generators' file filters do not overlap), but it attaches YamlDotNet to the toolkit generator and makes `App.UI` load the XAML text analyzer for files it has no strings in (01 §5). | New `src/geoDC.App.Text.Generators` beside the existing generator project. |
| 4 | "Agent infra" = whatever `.publishignore` strips | `.publishignore` is wrong in both directions and the generated `main` is already red because of it: CI still calls the stripped `dev/sync-agent-skills.ps1`, and `geoDC.Probe.Tests` reads the stripped `dev/probe-scripts/` as test data (05 §0, §1.1). `.husky/` is developer tooling the README tells everyone to install. | `dev/probe-scripts/`, `dev/probe.ps1`, `.husky/`, `GalleryCorpus` **stay**. `docs/publishing.md` is **deleted** (it is not in `.publishignore` today). The `agent-skills` CI job is deleted together with the script it runs. |
| 5 | Self-hosted runners are the owner's harness | The reusable selector job itself runs on a self-hosted Linux runner (`select-runner.yml:41`). With it offline, every geoDC CI run queues for up to 24 h. Everything else already falls back to hosted runners when the status token is absent (05 §2.2). | Change the selector default to `ubuntu-latest` so geoDC CI is hosted-first and complete on its own; keep the desktop Windows runners as an optional accelerator documented in the harness, or retire them. |
| 6 | Remove the demo-project button | Removal reverses #189, work item 2 of the open epic #461, and the change is still listed as Unreleased in `CHANGELOG.md` (03 §3). The `Demo` generator preset becomes orphaned. | Proceed, and in the same PR: note on #461 that the demo is dropped and presets remain the single source for the probe fixture and tests only; decide whether `--preset=demo` stays (lean: remove). |
| 7 | Restructure `geoDC.App` by feature | Confirmed feasible and behaviour-neutral. Popup templates are found by implicit typed `DataTemplate` lookup from `Application.Resources`, with no registry or key, so a per-feature dictionary merged from `App.xaml` changes nothing at runtime. `Menus/` already uses exactly this per-feature pattern (02 §0, §3). | Proceed as **one scaffold PR plus one PR per area**, not a big bang. Namespaces follow folders (560 of 572 files already do). Tests move in step per area. |

## 2. Decisions the evidence confirms, with the detail that matters

- **Vendoring Snt.Lib is low-risk.** The pinned package `0.10.0-preview.8` is exactly Snt.Lib HEAD `d545ca2`, confirmed by the nuspec repository commit and the DLL informational version. The copy is about 5,800 lines in five groups (panels 1,931; converters 1,371; WPF text runtime 759; text primitives 357; generator 1,360) plus 3,161 lines of xUnit tests to port to NUnit. No geoDC converter duplicates an Snt one. The generated `<Name>Text` class names stay, so the 1,580 XAML and 2,109 C# member references do not change (01 §1, §2, §6).
- **One build trap.** The generator's inputs come from the NuGet package's `buildTransitive` targets today. A project-reference analyzer gets none of that and does not load its own NuGet dependencies, so `geoDC.App.csproj` must declare the YAML and Page `AdditionalFiles` itself and surface `YamlDotNet.dll` as an analyzer item, or every `text:` reference fails with CS8032 (01 §5). Verify on a clean clone before touching XAML.
- **The XAML text analyzer only resolves `clr-namespace:` xmlns.** Mapping the generated namespace through an `XmlnsDefinition` URI would turn every `FormatText`/`MatchText` use into an error. Keep `xmlns:text` as `clr-namespace:geoDC.App.Text` (01 §4).
- **Panel names collide in C#.** Snt's `Grid` and `StackPanel` clash with `System.Windows.Controls` in any C# file that imports both. XAML is unaffected. Renaming to `UiGrid`/`UiStackPanel`/`UiWrapPanel`/`UiAutoGrid` costs nothing extra inside the same regex pass and matches the toolkit's `Ui*` convention (01 §5). Owner's call.
- **Style gate.** geoDC's `.editorconfig` makes `var` an error and CI enforces style; the vendored set has 186 `var`s. Do a verbatim-copy commit followed by a `dotnet format` commit so reviewers can diff the copy against Snt.Lib (01 §6).
- **Text-system legibility, ranked by cost** (04 §6): (1) two csproj lines on `geoDC.App` to emit generated files under `obj/Generated`, exactly as `geoDC.App.UI` already does; (2) a one-page `docs/finding-things.md`; (3) generator changes once vendored: put `file › key.path: "text"` in each member's XML summary and the match/plural case texts in the `…Map` member's remarks; (4) a `find-text` script lifting the derivation already in `StringResourceConventionTests`; (5) an analyzer or test extension for literal popup titles. All keep localisation possible; the only thing that inlines text is the generated `const string`, which is a one-line generator change later (01 §4).
- **Six popup titles bypass YAML.** `Edit Feature`, `Attach Instrument`, `Complete Measurement`, `Import Survey Points`, `Project Fingerprint` and `Edit Project Metadata` are C# literals in popup definitions; `XamlLiteralTextConventionTests` covers XAML only (04 §5 #4). Small fix, real localisation gap.
- **The by-kind split is the first wall, the text system the second.** Of 40 hops across seven lookups, four were dead ends: three from the text system (ambiguous common words such as "No job selected" in eight files, CRLF-anchored grep, match cases having no member) and one from `Popups/*.cs` versus `Themes/Templates/Popups/*.xaml`. The menu task, the only one with a doc linked from where you start, had zero dead ends (04 Appendix A).
- **Small projects** (03 §2): `geoDC.Reporting.Contracts` holds no interfaces despite the docs saying so; merge into `geoDC.Application` keeping the namespace unless an out-of-process generator is still planned. `tests/geoDC.Testing` is one file; replace with the linked-file mechanism `StaTestRunner.cs` already uses. `geoDC.App.UI.Generators` cannot merge (analyzers must be their own assembly) and only needs a line in the docs. `ReportCli` is the model CLI (usage text, exit codes, testable `RunAsync`).
- **Branding has a concrete job.** The app ships with the default .NET icon: no `ApplicationIcon`, no installer icon. Product and company strings are six scattered literals plus the installer. All SVG assets are Git LFS objects, so a developer without `git lfs` gets blank logos and a failing report render (03 §2). Branding should own a multi-resolution `.ico` and a product-name constant pair; the README needs a Git LFS line.
- **Probe** (03 §4): well documented in code but agent-framed (JSON-only stdout, the only prose is the skill, no `docs/` page). `Program.cs` is 1,147 lines of top-level statements with verb dispatch duplicated. Proposed name `geoDC.UiDriver` (exe `geodc-ui`); verbs, JSON fields and AutomationIds unchanged. Six sample scripts listed. `Probe.Tests` hand-links 32 source files; a `*.Core` split would replace the list with one reference.
- **Solution filters** (03 §7): `geoDC.Core.slnf`, `geoDC.App.slnf`, `geoDC.Toolkit.slnf`, `geoDC.Tools.slnf`. CI keeps the full `slnx`.
- **Codebase reference** (05 §5): one file, `docs/codebase.md`, by project in dependency order under five boundary headings, each project section following one template (purpose, key folders and types, entry points, how tested, related doc). CLI usage is one section with the exact command lines; the usage paragraphs are already drafted in 03 §5. Only six of 39 doc pages are agent-only; the rest stay as linked detail. AGENTS.md's "Domain rules", "Solution boundaries" and build commands are the backbone of the new reference and appear nowhere else today.

## 3. Housekeeping found in passing

- `geoDC.slnx` lists three files that do not exist (`GlobalUsings.Library.cs`, `GlobalUsings.Wpf.cs`, `StaTestDispatcher.cs`); Visual Studio shows them as missing (03 §8).
- `dotnet-tools.json` restores the ReSharper CLI (`jb`) that nothing uses (03 §8).
- `src/geoDC.App/Converters/AppConverters.cs:17-19` says the library converters use prefix `conv:`; every file uses `snt:` (04 §1).
- Two visibility idioms coexist in popup templates: converter and `Style`+`DataTrigger` (04 §1).
- `README.md` on the generated `main` has dead links to `AGENTS.md`, `docs/cloud-sessions.md` and `.agents/skills` (05 §1.1).
- Husky hooks exit 2 without `git-lfs` and need PowerShell 7; the build downloads Typst through PowerShell on first build (03 §8).
- `InternalsVisibleTo` from `geoDC.Domain` to `geoDC.Infrastructure` has no recorded reason (03 §8).
- Repository-wide convention tests live under `tests/geoDC.Infrastructure.Tests/Conventions/` because that project runs on Linux (03 §6).

## 4. Revised sequence and proposed sub-issues

Order unchanged in shape: self-contained first, moves second, split last. Each line is one sub-issue of #780; the
PR count in brackets is the report's estimate.

**A. Prerequisites (independent, can start now)**
1. Remove the demo-project button and the App → `TestDataGenerator.Core` reference; note on #461. [1 PR] (03 §3)
2. Emit generated text files on `geoDC.App`; fix the stale `conv:` comment; move the six literal popup titles to `popups.yaml` and extend the convention test to `Title =`/`SubTitle =` literals. [1 PR] (04 §5, §6)
3. Solution hygiene: fix the three phantom `slnx` entries, drop `jb`, add solution filters, README Git LFS and quiet-build notes. [1 PR] (03 §7, §8)

**B. Vendor Snt.Lib** (01 §7)
4. Panels, converters and `LambdaConverter` into `geoDC.App.UI`, verbatim copy then format commit, port the four test files; decide `Ui*` panel names first. [1 PR]
5. Text stack atomically: `geoDC.App.Core/Text`, `geoDC.App.UI/UI/Text`, new `geoDC.App.Text.Generators`, csproj wiring with explicit `AdditionalFiles` and the YamlDotNet analyzer item, `Snt.Generated.Text` → `geoDC.App.Text`, remaining `snt:` → `ui:`, analyzer constants, port the six text and generator test files, `docs/string-resources.md`. Removes all `Snt.Lib.*` references and the private feed. [1 PR]
6. Generator legibility: key path and text in member XML docs, case texts in `…Map` remarks, optional `#region` per top-level key; `find-text` script. [1 PR] (04 §6)

**C. Restructure `geoDC.App`** (02 §7)
7. Scaffold: `Features/`, `Shell/`, `Shared/`; move the shell; split `WorkflowPopupDefinitions.cs` and `WorkflowPopupContracts.cs` by area; replace the five aggregator dictionaries with per-feature dictionaries merged from `App.xaml`; fix the twelve `VrfTransformations` namespaces and the seven path-reading tests. [1 PR]
8. Area moves, tests in step, small first: Launch, Settings, Reports, Landing; Shore Control; Data, Import; Setup; Attempts then Calibration, Verification, GNSS (coordinated with #306/#307); Survey and Transformations last (after #453's first extraction). [about 10 PRs]
9. Small projects: merge `Reporting.Contracts` into `Application`; replace `geoDC.Testing` with a linked file. [1 PR] (03 §2)
10. Branding: `.ico` for app and installer, product-name constants, record the Typst colour pairing. [1 PR] (03 §2)
11. Probe: rename to `geoDC.UiDriver`, split `Program.cs`, `*.Core` for the UIA-free logic, `--format text` ergonomics, six sample scripts, a `docs/` page. [2 PRs] (03 §4)

**D. Docs** (05 §4, §5; 04 §7)
12. `docs/codebase.md` with the CLI section; fold `branding-assets.md`; absorb AGENTS.md's human sections into it and into `coding-style.md`, `ui/README.md`, `domain-context.md`; rewrite README "Documentation"; add the doc to the `slnx`. [1 PR]
13. `docs/finding-things.md` plus one-line pointer READMEs in `Strings/`, the popup folders and `Views/`. [1 PR]

**E. Split** (05 §2, §3)
14. geoDC CI hosted-first: selector default `ubuntu-latest`, delete the `agent-skills` job and skip-paths, delete `publish-main.yml`, `dev/publish-main.ps1`, `.publishignore`, `docs/publishing.md`; decide the `main` branch model (lean: `main` becomes a fast-forward release branch after a one-time reset to `dev`). [1 PR]
15. Harness repo: create it, move `AGENTS.md` (agent sections only), `.agents/`, `.claude/`, `.codex/`, `dev/cloud-setup.sh`, the capture and sync scripts, `docs/cloud-sessions*.md`, the sample-gallery capture section, optionally `dev/runners.ps1`; prefix skill paths with `geodc/`; hooks `cd geodc`; bootstrap script; verify one cloud session with geoDC attached as a second repository before deleting anything from geoDC. [harness PRs + 1 geoDC PR]

## 5. Owner decisions (2026-11-01)

| # | Question | Decision |
|---|---|---|
| 1 | Nested clone instead of submodule (§1 #1) | **Nested clone.** |
| 2 | `GalleryCorpus` project stays, drivers move (§1 #2) | **Stays**; only the capture and sync scripts move. |
| 3 | Separate `geoDC.App.Text.Generators` project (§1 #3) | **Separate project.** |
| 4 | Rename the vendored panels to `UiStackPanel`, `UiWrapPanel`, `UiAutoGrid`, `UiGrid` | **Rename.** |
| 5 | Runners (§1 #5) | **Retire** the self-hosted runners; geoDC CI runs on hosted runners only. |
| 6 | `main` after publish-main | **Reset `main` to `dev` once; `main` is the release branch** (fast-forward from `dev`). |
| 7 | `Reporting.Contracts` | **Merge into `geoDC.Application`**, namespace kept. The Typst renderer is the only generator; no external one is planned. |
| 8 | `demo` generator preset | **Dropped** with the button. |
| 9 | Probe name | **`geoDC.UiDriver`.** |
| 10 | Namespaces follow folders in the restructure | **Yes.** |

Sub-issues are filed under #780 from §4 with these decisions applied.
