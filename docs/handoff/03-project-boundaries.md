# 03 · Project boundaries, small projects, tools and tests

Verified @ e9a8766 (origin/dev). Read-only investigation for the geoDC handoff epic (#780). Line counts are
`*.cs` + `*.xaml` excluding `bin/`, `obj/`, `node_modules/`. The checkout is a shallow clone (65 commits; every
project first appears at `39b12e7`, the squash boundary), so "introducing commit" evidence below comes from docs,
design records and issue bodies, not `git log`.

## 1. Project catalogue

### src (12)

| Project | Purpose | Lines | Project refs | Package refs | TFM / Windows | Consumed by |
|---|---|---|---|---|---|---|
| geoDC.Measurements | Dependency-free units (`Length`, `Angle`, points), ISO timestamps, CRS primitives | 1,892 | — | — | net10.0 / no | Domain, App.UI |
| geoDC.Domain | Pure domain model (no EF, DI, logging, UI); embeds `DeviceProfileCatalogue.json` | 15,599 | Measurements | NUlid | net10.0 / no | Application, Infrastructure, GeobaseImport, TDG.Core, Domain.Tests (`InternalsVisibleTo` Infrastructure, `src/geoDC.Domain/geoDC.Domain.csproj:21-22`) |
| geoDC.Application | Use-case abstractions and interfaces | 7,762 | Domain, Reporting.Contracts | EF Core, DI.Abstractions, NUlid | net10.0 / no | App.Core, Infrastructure, App, GeobaseImport, ReportCli, TDG.Core |
| geoDC.Proj | P/Invoke PROJ binding + committed native builds (win-x64, win-arm64, linux-x64) and grids | 1,747 | — | — | net10.0 / no (`AllowUnsafeBlocks`) | Infrastructure only, Proj.Tests |
| geoDC.Infrastructure | EF Core SQLite persistence, ingestion, packages, Typst reporting adapter, audit | 14,191 | Application, Domain, Proj | ExcelDataReader, EF Core (+Sqlite), Hosting.Abstractions, NUlid, ZstdNet | net10.0 / no | App, GeobaseImport, ReportCli, TDG.Core |
| geoDC.Reporting.Contracts | Report DTOs/enums (`ReportRequest`, `JobReportData`, …) | 584 | — | NUlid | net10.0 / no | Application; via it Infrastructure, App, ReportCli, Reporting.Tests |
| geoDC.App.Core | Domain-free shell/MVVM/workspace/app-service primitives (`IUiDispatcher`, …) | 1,860 | Application | CommunityToolkit.Mvvm, DI/Logging.Abstractions, Snt.Lib.General | net10.0 / no | App.UI, App, tests/geoDC.Testing |
| geoDC.App.UI | Domain-free WPF toolkit (`https://schemas.geodc/ui`), theme tokens, fonts | 50,580 | App.Core, Measurements, App.UI.Generators (analyzer) | CommunityToolkit.Mvvm, FluentIcons.Wpf, Snt.Lib.Wpf, DI/Logging.Abstractions | net10.0-windows / yes | App, Branding, Sample, TDG.Wpf |
| geoDC.App.UI.Generators | Roslyn incremental generator: `Tokens.*.xaml` → `ThemeResourceKey`/`LayoutResourceKey` catalogues | 484 | — | Microsoft.CodeAnalysis.CSharp | netstandard2.0 / no | App.UI (as Analyzer, `src/geoDC.App.UI/geoDC.App.UI.csproj:24-26`) |
| geoDC.App.UI.Branding | Theme-aware brand dictionary (`BrandingDictionary`) over `assets/*.svg` | 217 | App.UI | SharpVectors.Wpf | net10.0-windows / yes | App only |
| geoDC.App.UI.Sample | Toolkit gallery (specimens, playground, offscreen corpus renderer) | 14,143 | App.UI | CommunityToolkit.Mvvm | net10.0-windows WinExe / yes | GalleryCorpus, App.UI.Tests |
| geoDC.App | The application: startup, shell, views, viewmodels, workflows, WebView2 charts/map | 84,967 | Branding, App.UI, App.Core, Application, Infrastructure, **tools/TDG.Core** | Hosting, Serilog ×5, SharpVectors, Mvvm, Snt.Lib.Wpf, Snt.Lib.Wpf.Text.Generators, WebView2 | net10.0-windows10.0.17763.0 WinExe / yes (`RuntimeIdentifiers win-x64`, bundles Typst) | App.Tests |

### tools (7)

| Project | Purpose | Lines | Project refs | Package refs | TFM / Windows | Consumed by |
|---|---|---|---|---|---|---|
| geoDC.TestDataGenerator.Core | Scenario model, presets, generator writing real `.gdcproj` through Infrastructure | 4,299 | Application, Domain, Infrastructure | DI.Abstractions, Hosting, NUlid | net10.0 / no | App (demo), App.Tests, Reporting.Tests, TDG.Cli, TDG.Wpf, TDG.Tests |
| geoDC.TestDataGenerator.Cli | System.CommandLine front end over Core | 617 | TDG.Core | Hosting, System.CommandLine 2.0.13 | net10.0 Exe / no | TDG.Tests, `dev/seed-probe-fixture.ps1` |
| geoDC.TestDataGenerator.Wpf | WPF front end over Core (nav-rail Project/Scenario/Naming/Output) | 1,374 | TDG.Core, App.UI | CommunityToolkit.Mvvm | net10.0-windows WinExe / yes | nobody (manual) |
| geoDC.GeobaseImport | One-off GEOBASE → `.gdcproj` migration CLI | 2,809 | Application, Domain, Infrastructure | Hosting, NUlid | net10.0 Exe / no | GeobaseImport.Tests, App.Tests (`GeobaseImportWorkflowTests`) |
| geoDC.ReportCli | Headless job-report renderer (same builder/renderer as Reports page) | 371 | Application, Infrastructure | NUlid | net10.0 Exe / no (bundles Typst) | Reporting.Tests (`InternalsVisibleTo`) |
| geoDC.Probe | UI Automation driver for the app and the Sample gallery (exe `probe`) | 9,512 | — (deliberately no geoDC ref) | FlaUI.UIA3 5.0.0 | net10.0-windows Exe, `UseWPF` for PNG encoding / yes | Probe.Tests (32 source files linked, not referenced), `dev/probe.ps1`, `dev/capture-*.ps1` |
| geoDC.GalleryCorpus | Offscreen screenshot corpus of every Sample specimen (exe `gallery-corpus`) | 245 | App.UI.Sample | — | net10.0-windows Exe / yes | `dev/capture-ds-previews.ps1` |

### tests (13)

| Project | Covers | Lines | Project refs | TFM | Runs on Linux |
|---|---|---|---|---|---|
| geoDC.Domain.Tests | Domain | 11,899 | Domain | net10.0 | yes |
| geoDC.Application.Tests | Application | 1,857 | Application | net10.0 | yes |
| geoDC.Infrastructure.Tests | Infrastructure **plus repo-wide source conventions** (`Conventions/`, 8 files scanning `src/`) | 8,004 | Infrastructure | net10.0 | yes |
| geoDC.Proj.Tests | Proj (against committed linux-x64 build) | 813 | Proj | net10.0 | yes |
| geoDC.Reporting.Tests | Infrastructure reporting + ReportCli end to end | 1,868 | TDG.Core, ReportCli | net10.0 (bundles Typst) | yes |
| geoDC.App.Core.Tests | App.Core | 1,888 | App.Core, geoDC.Testing | net10.0 | yes |
| geoDC.GeobaseImport.Tests | GeobaseImport | 1,009 | GeobaseImport | net10.0 | yes |
| geoDC.TestDataGenerator.Tests | TDG.Core + Cli | 3,017 | TDG.Cli, TDG.Core | net10.0 | yes |
| geoDC.Probe.Tests | Probe's UIA-free logic | 4,724 | none (links 32 `.cs` files, `tests/geoDC.Probe.Tests/geoDC.Probe.Tests.csproj:13-46`) | net10.0 | yes |
| geoDC.App.UI.Tests | App.UI + Sample (incl. `SpecimenCorpusTests`) | 28,148 | App.UI, Sample, geoDC.Testing | net10.0-windows, `UseWPF` | **no** |
| geoDC.App.Tests | App (+ generated-package and GEOBASE workflows) | 40,325 | App, TDG.Core, GeobaseImport, geoDC.Testing; links `GeobaseFixtures.cs` | net10.0-windows10.0.17763.0 | **no** |
| geoDC.Testing | Shared helper: `ImmediateUiDispatcher` (1 file) | 70 | App.Core | net10.0 | n/a (library) |

Line totals: src 193,026 · tools 19,227 · tests 103,622.

### Dependency graph (arrows point to the dependency)

```
Measurements ◄── Domain ◄── Application ◄── App.Core ◄── App.UI ◄── Branding
      ▲            ▲    ▲        ▲   ▲          ▲           ▲  ▲         ▲
      │            │    │        │   │          │           │  │         │
      └────────────┼────┼────────┼───┼──────────┼───────────┘  │         │
                   │    │        │   │          │              │         │
Reporting.Contracts┘    │        │   │          │              │         │
                        │        │   │          │              │         │
Proj ◄── Infrastructure ┘        │   │          │              │         │
            ▲   ▲   ▲   ▲        │   │          │              │         │
            │   │   │   └── tools/TDG.Core ◄──┬─┼── tools/TDG.Cli      │
            │   │   │            ▲            │ │   tools/TDG.Wpf ──────┤
            │   │   │            └────────────┼─┼── src/geoDC.App ──────┴── (App.UI, App.Core, Application, Infrastructure, Branding)
            │   │   └── tools/GeobaseImport   │ │
            │   └────── tools/ReportCli       │ │
            │                                 │ └── App.UI.Sample ◄── tools/GalleryCorpus
tools/Probe (no geoDC references; FlaUI only) │
App.UI.Generators ──(analyzer)──► App.UI      │
tests/geoDC.Testing ──► App.Core              │
```

The one inversion a newcomer trips over: `src/geoDC.App` depends on `tools/geoDC.TestDataGenerator.Core`
(`src/geoDC.App/geoDC.App.csproj:9`) solely for the demo button (§3). After removal, no `src` project depends on
`tools`.

## 2. Small projects: decide or record

| Project | Files | Why separate (recorded where) | If merged into nearest neighbour | Recommendation |
|---|---|---|---|---|
| geoDC.Reporting.Contracts | `Contracts/ReportRequest.cs` (54), `ReportResult.cs` (38), `ReportEnums.cs` (68), `JobReport/JobReportData.cs` (351), `JobReport/JobReportContract.cs` (73), empty `GlobalUsings.cs` | AGENTS.md "report-generator DTO and interface boundary"; README "DTOs and interfaces". In fact it holds **no interface** (`grep interface` → none); the interfaces are in `src/geoDC.Application/Abstractions/Reporting/` (`IReportServiceAdapter`, `IJobReportRequestBuilder`) and the renderer in `src/geoDC.Infrastructure/Reporting/`. No design record under `docs/design/` mentions it. | Into Application under `Reporting/Contracts/`, namespace kept: nothing breaks (JSON in `ReportRequestJson.cs` carries no assembly names; `GenerateDocumentationFile`/CS1591 already apply to both, `Directory.Build.props:26-29`). Gains: one project fewer, one `PackageReference` fewer. Loses: the option of shipping contracts to an out-of-process generator that does not exist today. | **Merge into geoDC.Application** keeping the `geoDC.Reporting.Contracts` namespace, unless the owner still plans an external generator; then keep and write that reason into AGENTS.md. |
| tests/geoDC.Testing | `ImmediateUiDispatcher.cs` (70) | Shared `IUiDispatcher` stub for App.Core/App/App.UI tests; `Directory.Build.targets:3-5` auto-adds `global using geoDC.Testing` when referenced. Sibling helper `tests/StaTestRunner.cs` uses the same namespace but is a **linked file** injected by `Directory.Build.props:51-53` only into `UseWPF` test projects. | Delete the project and link the file the way `StaTestRunner.cs` is linked (condition on a `ProjectReference` to App.Core in `Directory.Build.targets`, where references are visible). Removes one project and three csproj references; no test changes. | **Merge into the linked-file mechanism** (one convention for shared test helpers instead of two). |
| geoDC.App.UI.Branding | `Themes/Branding.xaml` (95), `Branding.Light.xaml` (38), `Branding.Dark.xaml` (38), `Themes/BrandingDictionary.cs` (46), csproj linking `assets/*.svg` | `docs/branding-assets.md`: keeps the reusable toolkit and the Sample gallery brand-free; App merges it at `src/geoDC.App/App.xaml:14`. Only consumer is App (`MainWindow.xaml:48`, `ProjectSelectorTemplate.xaml:40,52`). | Into App.UI: toolkit gains GEOsolutions artwork + SharpVectors. Into App: natural (single consumer) but loses the "brand lives in one dictionary" discipline the owner wants to extend. | **Keep (owner decision)** and move the items listed below in. |
| geoDC.App.UI.Generators | `TokenCatalogGenerator.cs` (217), `TokenXamlParser.cs` (254), `IsExternalInitPolyfill.cs`, `GlobalUsings.cs` | A Roslyn analyzer must be its own netstandard2.0 assembly; documented only at `docs/ui/theming.md:160`; absent from AGENTS.md "Solution boundaries". CI builds it alone before `dotnet format` (`.github/workflows/ci.yml:110-116`). | Cannot merge. | **Keep**; add one line to AGENTS.md boundaries and README layout table. |
| tools/geoDC.GalleryCorpus | `Program.cs` (245), `app.manifest` | Offscreen render of every specimen for the design-system corpus (`docs/ui/sample-gallery.md:51-70`). The rendering code it drives lives in **src** (`src/geoDC.App.UI.Sample/Gallery/Corpus/*`, 475 lines) and is tested by `tests/geoDC.App.UI.Tests/Gallery/SpecimenCorpusTests.cs`. | Into Sample as a `--corpus` switch: removes a project but puts a CLI into a WinExe. | **Move to the harness repo (owner decision)** together with `dev/capture-ds-previews.ps1` and `dev/sync-design-system.ps1`. Keep `Gallery/Corpus` and `SpecimenCorpusTests` here (they guard that specimens render without a window). Note `docs/ui/sample-gallery.md:56` ("render one specimen without the app") then needs the harness checkout; `dev/capture-views.ps1` (probe-driven app view corpus) is the same family and is not in the move list: decide. |
| tools/geoDC.ReportCli | `Program.cs` (21), `JobReportArguments.cs` (75), `JobReportCommand.cs` (275) | `docs/reporting.md:210-235`: Linux-capable end-to-end check of the report pipeline; `Reporting.Tests/JobReportCommandTests.cs` runs it in-process via `InternalsVisibleTo`. | Into Infrastructure: a console entry in a library. | **Keep (owner decision)**. It is the model the other CLIs should follow (usage text, exit codes 0/1/2, testable `RunAsync(args, out, err, ct)`). |

### Branding: what lives outside it today

| Item | Where | Should move in? |
|---|---|---|
| Brand navy `#021D48` and grey `#747476` | `src/geoDC.Infrastructure/Reporting/Templates/job-report/common.typ:6-7` (Typst cannot read XAML; duplicate by design) | No; record the pairing in `docs/branding-assets.md`. |
| Report logo | `src/geoDC.Infrastructure/geoDC.Infrastructure.csproj:28-31,48-53` copies `assets/GEOdc logo - blue on transparent.svg` beside the template | No (Linux build path); document. |
| Application icon | **None.** No `ApplicationIcon` in `src/geoDC.App/geoDC.App.csproj`; installer icon is optional and absent (`installer/geoDC.Installer/geoDC.Installer.wixproj:26-31`, `Package.wxs:94-96`). The app ships with the default .NET icon. | **Yes**: Branding should own a multi-resolution `geoDC.ico` (rendered from the SVG) that App's `ApplicationIcon` and the installer both reference. |
| Product strings | `launch.yaml:19` "A GEOsolutions Product"; `shell.yaml:3-5` window title "geoDC"/"geoDC *"; `App.xaml.cs:77` AppData folder "geoDC", `:167` "geoDC is already running.", `:249` `ApplicationMetadata.FromAssembly("geoDC", …)`; `ThirdPartyNotices.targets:55` `ProductName="geoDC"`; `MapHosting.cs:126` user agent; Sample `MainWindow.xaml:9` "geoDC UI Sample" | Partly: a `Brand.ProductName`/`Brand.Company` constant pair in Branding (or App.Core, since Branding is WPF-only and `ApplicationMetadata` is not) would replace the six literals. |
| Installer identity | `installer/geoDC.Installer/Package.wxs:7-8` `Name="geoDC" Manufacturer="GEOsolutions"`, registry `Software\GEOsolutions\geoDC` (`:24-29`) | No (WiX cannot consume XAML); document as the second copy. |
| `Brand.Brush.Foreground` | Used once, `ProjectSelectorTemplate.xaml:52` | Fine where it is. |
| Git LFS | All `*.svg` are LFS objects (`.gitattributes:6`); in this clone each asset is a 130-byte pointer. A developer without `git lfs` builds Branding with pointer text as the image and gets blank logos and a failing report render (`docs/cloud-sessions.md:26`). | Call this out in README "Getting started". |

## 3. Demo-project removal

Everything on the path, with the reference to delete or simplify:

| File | Lines | What |
|---|---|---|
| `src/geoDC.App/Startup/DemoProjectGenerator.cs` | 1-131 | Delete. Only file in `src/geoDC.App` using `geoDC.TestDataGenerator.*` (`:2-4`, `:45`). |
| `src/geoDC.App/Startup/IDemoProjectGenerator.cs` | 1-14 | Delete. |
| `src/geoDC.App/Startup/AppServiceRegistrations.cs` | 53 | DI registration. |
| `src/geoDC.App/Startup/ProjectSessionService.cs` | 33, 82-83, 104-109 | Ctor parameter, `createDemo` flag, generate-then-open branch. |
| `src/geoDC.App/Startup/ProjectSelectorViewModel.cs` | 159-164 | `CreateDemoProjectCommand`. |
| `src/geoDC.App/Startup/ProjectSelectorModels.cs` | 10, 70-73, 80-83 | `ProjectSelectorAction.CreateDemo = 3`, `RequestCreateDemo`, `CreateDemo` factories. |
| `src/geoDC.App/Startup/ProjectSelector.cs` | 44 | Pattern on `CreateDemo`. |
| `src/geoDC.App/Startup/NewProjectDialogService.cs` | 17, 44-45 | Guard and result mapping. |
| `src/geoDC.App/Startup/NewProjectPopupDefinition.cs` | 13, 16-17, 31 | `isDemo` title/subtitle/primary action. |
| `src/geoDC.App/Startup/NewProjectViewModel.cs` | 44, 73, 90, 131-139 | `IsDemo`, dialog title, file name, default names. |
| `src/geoDC.App/Services/ProjectFileWorkflowService.cs` | 177 | Pattern on `CreateDemo`. |
| `src/geoDC.App/Themes/Templates/Startup/ProjectSelectorTemplate.xaml` | 164-168 | Button with AutomationId `CreateDemoProjectButton` (removed, not renamed; no probe script, skill or `dev/capture-views.ps1` references it). |
| `src/geoDC.App/Themes/Templates/Startup/NewProjectTemplate.xaml` | 13-15 | `MatchText {Binding IsDemo}`. |
| `src/geoDC.App/Strings/launch.yaml` | 17 (`createDemo`), 94-131 (seven `match: bool` blocks whose `true` case is the demo), 134 (`fileName` demo-project), 136-139 (`demoProjectName`, `demoVesselName`, `demoSampleDataFolder`) | Collapsing the `match` blocks changes generated `LaunchText.*` from methods to properties; callers above follow. |
| `src/geoDC.App/geoDC.App.csproj` | 9 | Remove the `tools/geoDC.TestDataGenerator.Core` reference. |
| `tests/geoDC.App.Tests/Startup/DemoProjectCreationTests.cs` | 1-37 | Delete (the only test of the path). |
| `tests/geoDC.App.Tests/Startup/ProjectSessionCleanupOrderTests.cs` | 133 | `Substitute.For<IDemoProjectGenerator>()` ctor argument. |
| `docs/test-data-generator.md` | 30 (`demo` preset row), 37-51 ("Create demo project" section incl. the `src/`→`tools/` layering record) | Rewrite. |
| `CHANGELOG.md` | 15-17 | Unreleased "Added" entry for #189; replace or drop. |
| `tools/geoDC.TestDataGenerator.Core/Scenarios/ScenarioPresets.cs` | 57-60, 75 | `Demo` preset (same options as `Full`); decide whether `--preset=demo` stays (CLI help lists it, `GeneratorCommandLine.cs:52`; `ScenarioPresetsTests` and `docs/test-data-generator.md:30` name it). |

After removal, **nothing else in `src/geoDC.App` needs TestDataGenerator.Core**: `grep TestDataGenerator src/geoDC.App`
hits only `DemoProjectGenerator.cs` and the csproj. `tests/geoDC.App.Tests` keeps its own reference
(`geoDC.App.Tests.csproj:11`) for `Infrastructure/GeneratedPackages.cs`, `Workflows/GeneratedPackageWorkflowTests.cs`
and `Workflows/SelectionCascadeTests.cs:139`, which open generated packages through the app; those are unaffected.
Coverage of the remaining create path: `ProjectSessionCleanupOrderTests`, the startup tests under
`tests/geoDC.App.Tests/Startup/` (Windows only).

Cross-check with #461 ("Epic: Generator presets as the single source for the demo project, probe fixture and test
scenarios", open, 3/5 sub-issues done): its work item 2 is #189, "Create demo project runs the Demo preset
in-process", which is what `DemoProjectGenerator.cs` implements and `CHANGELOG.md:15` announces as unreleased.
Removing the button reverses #189 before release, orphans the `Demo` preset, and makes #461's "Done when" (one
preset list naming the demo) wrong. The epic needs a note: demo dropped; presets remain the single source for the
probe fixture (`dev/seed-probe-fixture.ps1:34`) and tests only.

## 4. Probe (`tools/geoDC.Probe`, 9,512 lines; `tests/geoDC.Probe.Tests`, 4,724 lines)

### Architecture

- **Entry point**: top-level statements in `Program.cs` (1,147 lines, 30 local `static` functions). Verb
  dispatch happens twice: `Program.cs:26-55` (standalone) and `:397-440` (`DispatchVerb` for `do` steps). The
  usage text is one 40-line string concatenation (`:63-102`).
- **Command surface** (`Program.cs:63-102`, `.agents/skills/wpf-probe/SKILL.md:65-98`): session verbs `attach`
  (`--launch [--project] [--passive]`), `kill`, `status`, `rebuild [--then <script>]`, `logs`; read verbs `tree`
  (`--interactable`, `--diff`), `find`, `audit`, `screenshot`; action verbs `click`, `settext`, `select`, `scroll`,
  `scrollto`, `key`, `wait`, `assert`, `settle`; and `do`, which runs steps separated by `++` or from a `--file`
  script, with `run`, `pause` and `--report <md>`. Every verb prints one JSON line; `--format text` is opt-in.
- **Attach model** (`GeoDcSession.cs:8-66`, `ProbeLease.cs:7-20`, `ProbeTarget.cs:21-26`): the probe launches
  `src/geoDC.App/bin/<cfg>/net10.0-windows10.0.17763.0/geoDC.App.exe` or the Sample gallery itself, records a
  PID lease under `.probe/` in the working directory (`ProbeStateDirectory.cs`), and later invocations talk only
  to that leased process through FlaUI UIA3 and Win32 `EnumWindows` (`GeoDcSession.cs:70-93`). It links no geoDC
  assembly; `GEODC_BACKGROUND_LAUNCH` is duplicated by name (`GeoDcSession.cs:15-19`). `--passive` keeps the app
  from taking the foreground (`PassiveMode.cs`). `app.manifest` pins PerMonitorV2 DPI so coordinates are physical
  pixels.
- **Script format** (`DoInvocation.cs:7-30`, `dev/probe-scripts/`): one step per line in the inline grammar; `#`
  comments; `@target app|sample`; `@var name=default`; `${name}` substitution with `--var`; `run lib/<script>
  k=v` expands another script at parse time; scripts end with `assert`. Three top-level scripts and ten `lib/`
  state scripts exist; a test parses them all (`DoInvocationTests.cs`).
- **External dependency**: FlaUI.UIA3 5.0.0 only (`Directory.Packages.props:10`); `UseWPF` is for
  `PngBitmapEncoder` (`geoDC.Probe.csproj:9-11`).
- **Today's human path**: `pwsh -NoProfile -File dev/probe.ps1 <verb>` (`dev/probe.ps1`, builds on demand, stdout
  JSON-only "so agents can parse stdout" `:5`), fixtures from `dev/seed-probe-fixture.ps1`. The only prose is the
  skill (358 lines, written for agents: "Ask the user before invoking the probe", one call per tool call) and
  AGENTS.md "UI probe". `docs/` has no page for it; `docs/publishing.md:21` keeps it on `main` only because the
  solution builds it.

### Legibility review

| Finding | Evidence |
|---|---|
| Agent framing in code and shim | "agent" in `ElementResolver.cs:10`, `JsonOutput.cs:268`, `ProbeTrace.cs:6`, `Program.cs:1018`, `UiaNodeSnapshot.cs:45`, `UiaTreeFilter.cs:18`; skill cross-references in `AutomationConventions.cs:8`, `ProbeTarget.cs:30`; `dev/probe.ps1:5`. JSON-by-default output is an agent contract; a developer types `--format text` every time. |
| Oversized files | `Program.cs` 1,147 (top-level statements), `ElementResolver.cs` 737, `DoInvocation.cs` 571, `Interactions.cs` 437, `GeoDcSession.cs` 433. |
| Verb and option names a C# developer stumbles on | `settext`/`scrollto` (no separator) beside `scroll`, `key`; `settle` (a sleep); `do` with `++` separators; `rebuild --then`; `--passive`/`--active`; "lease", "worktree", "stale-build" in output; `logs` reads the app's Serilog file. |
| Type names | Verb-shaped types `DoInvocation`, `DoStep`, `DoReport`, `RebuildThen`; three element classes `ElementResolver`/`ElementObserver`/`ElementLookupAnalyzer`; `TextRenderer` renders console text, not glyphs; `ProbeRegister` vs `ProbeLease` vs `ProbeLeaseRecord`; `GeoDcSession` casing differs from `geoDC`. |
| XML docs | Public types are documented throughout (spot-check of `Interactions.cs`, `TreeDumper.cs`, `ElementResolver.cs`, `Screenshotter.cs` found no undocumented public method); no `GenerateDocumentationFile`, so nothing enforces it. |
| Test wiring | `geoDC.Probe.Tests` hand-links 32 files (`.csproj:13-46`) to stay net10.0. Splitting the UIA-free logic into a `*.Core` library (or a shared project) replaces the list with one reference. |
| Environment assumptions | `#Requires -Version 7` (`dev/probe.ps1:1`, `capture-*.ps1:1`): VS "Developer PowerShell" is Windows PowerShell 5.1 unless pwsh is installed. Fixtures are gitignored (`.gitignore:37`) and must be seeded first. |

### Proposed name

**`geoDC.UiDriver`** (assembly/exe `geodc-ui`, shim `dev/ui.ps1`, scripts `dev/ui-scripts/*.ui`). It is a UI
Automation driver (it launches, attaches to and drives the app over UIA), and "driver" is the word WebDriver/
Appium/WinAppDriver readers already know; "probe" reads as diagnostics-only and nowhere says UI. Runner-up:
`geoDC.UiAutomation` (clashes with `System.Windows.Automation` in conversation). Rename scope: folder, csproj,
`RootNamespace`/`AssemblyName`, `ProbeTarget`/`ProbeLease`/`ProbeStateDirectory` (`.probe/` state folder,
`.gitignore:44`), the skill, AGENTS.md, `.publishignore:19-21`, `dev/capture-views.ps1`, `dev/seed-probe-fixture.ps1`
comments, and the `Probe` preset name in `ScenarioPresets.cs:36` (keep the preset name: fixture fingerprints are
keyed to it, `dev/seed-probe-fixture.ps1:21-25`). Verbs, JSON fields and AutomationIds stay.

### Sample scripts for a normal workflow (ids from the existing `lib/` scripts; grids to confirm with `find`)

1. `app-smoke.probe` — after `attach --launch`: `assert --id LandingTab --selected`, `tree --interactable --depth 2`, `screenshot --out dev/captures/smoke.png`.
2. `app-page-tour.probe` — `run lib/app-goto-page page=<X>` + `screenshot --id <X>` for Setup, Survey, Calibration, Verification, ShoreControl, Gnss, Reports.
3. `app-select-job-and-read-grid.probe` — `run lib/app-select-job job='Initial survey'`, `run lib/app-open-survey-section section=FeaturesTab`, `tree --scope <FeaturesGrid id> --depth 3` to read rows, `assert --id <row/cell id> --name '<feature>'`.
4. `app-calibration-results-screenshot.probe` — `run lib/app-open-attempt tab=ResultsTab`, `screenshot --id ResultsTab --pad 16 --highlight <result id>`.
5. `app-about-popup.probe` — open Help › About (menu ids in `docs/shell-menus.md`), `assert --id <AboutProductNameText> --name geoDC`, close with `CancelButton --scope PopupFooterPanel --await-gone PopupPanel`.
6. `sample-popup-roundtrip.probe` (exists) — keep as the gallery example; add `--format text` and `--report` to its header so a person sees a readable result.

Related open issues: #127 "Epic: WPF probe agent ergonomics" (10/13 done; remaining #798 pause-window Windows run,
#439, #132 in-process binding-error channel) and #439 "Settle the AutomationId role vocabulary and bring existing
ids into line with the probe audit" (37 ids deferred; Windows `audit` baseline pending).

## 5. CLI tools for the reference doc

| Tool | Command line | Inputs → outputs | Existing doc |
|---|---|---|---|
| geoDC.ReportCli | `geoDC.ReportCli <project.gdcproj> [--job <code\|id>] [--output <report.pdf>] [--list-jobs] [-h]` (`JobReportArguments.cs:16-21`, `JobReportCommand.cs:30-31`); values after space or `=`; `-o` alias | `.gdcproj` (read only, opened into a scratch workspace) → PDF (default `<code> job report.pdf` beside the project, written via temp file); exit 0/1/2 | `docs/reporting.md:210-235`; AGENTS.md "Build and test" |
| geoDC.TestDataGenerator.Cli | `geoDC.TestDataGenerator.Cli [<output-dir>] [--preset full\|probe\|demo\|lifecycle-coverage] [--list-presets] [--with/--without <parts>] [--seed] [--samples] [--sample-interval] [--motion-amplitude] [--start] [--extra-*] [--calibrations-per-axis] [--verifications-per-calibration] [--gnss-checks] [--display-convention] [--sensor-convention] [--vessel-axes] [--misalignment] [--project-name] [--vessel-name] [--client-name] [--contractor-name] [--register <path>] [--no-raw] [--export-json] [--export-project-json]` (System.CommandLine, `GeneratorCommandLine.cs:35-187`) | Options → `TestProject.gdcproj`, `raw/*.csv`, optional `shore-control.sqlite`; prints preset, seed, counts, domain and project fingerprints (`Program.cs:46-101`) | `docs/test-data-generator.md:272-358` (complete option table) |
| geoDC.GeobaseImport | `geoDC.GeobaseImport <source.geodc\|.geobase\|.gb> [<destination.gdcproj>] [--overwrite] [--inspect]` (`Program.cs:10-33`) | GEOBASE package → one `.gdcproj` per vessel; prints summary and notices; exit 0/1/2 | `docs/geobase-import.md:1-40` |
| geoDC.TestDataGenerator.Wpf | `dotnet run --project tools/geoDC.TestDataGenerator.Wpf` (no arguments; `App.xaml.cs`) | Same options as the CLI through a nav-rail window (Project / Scenario / Naming / Output, `MainViewModel.cs:14-20`); writes the same files, with "Replace existing project package" | `docs/test-data-generator.md:338-346` |
| geoDC.Probe | see §4 (`Program.cs:63-102`) | Running app/gallery → JSON lines, PNG screenshots, `.probe/` tree snapshots, Markdown `do --report` | skill only; no `docs/` page |

Usage paragraphs, ready to lift:

- **Report CLI.** Renders the job report of a saved project exactly as the Reports page would, without opening the
  app: `dotnet run --project tools/geoDC.ReportCli -- C:\surveys\vessel.gdcproj --job MOB-001`. With one job in
  the project `--job` can be omitted; `--list-jobs` prints codes, ids and names and stops. The PDF lands beside the
  project unless `--output` names another file, the project file is never modified, and reports carry no charts
  (the WebView2 renderer is not available headless). Exit 0 on success, 1 when the project cannot be opened or the
  render fails (renderer messages on stderr), 2 on bad arguments. Runs on Windows and Linux; needs the bundled
  Typst (`Directory.Build.targets` fetches it on first build).
- **Test-data generator (CLI).** Builds a complete, internally consistent synthetic project (vessel, features,
  instruments, calibration/verification cycles, GNSS check, shore control, notes, photos) and its raw CSV inputs
  from a named preset: `dotnet run --project tools/geoDC.TestDataGenerator.Cli -- C:\temp\demo --preset=full`.
  Every option layers on the preset; `--list-presets` shows the four presets with seed and purpose. Output is
  `TestProject.gdcproj`, `raw/` CSVs per job and, for scenarios with a register, `shore-control.sqlite`. The
  printed domain and project fingerprints are identical for identical options, which is how fixtures are kept
  stable. `--help` prints the full option list.
- **Test-data generator (WPF).** The same generator behind a window for people who prefer toggles to flags:
  pick a preset in Scenario, adjust counts and names in the Project and Naming sections, choose the output
  folder and whether to overwrite, and press Generate. It produces the same files as the CLI.
- **GEOBASE import.** One-off migration of projects from GEOBASE, geoDC's predecessor:
  `dotnet run -c Release --project tools/geoDC.GeobaseImport -- "C:\surveys\Old Vessel.geodc"`. The container
  (`.geodc`/`.geobase` ZIP or `.gb` gzip JSON) is detected from content; the destination defaults to the source
  name with `.gdcproj`; multi-vessel sources become one package per vessel; `--inspect` lists vessels and
  problems without writing and `--overwrite` replaces an existing destination. Every item not carried over is
  printed and also saved into the project as a field note.
- **UI driver (probe).** Launches geoDC (or the toolkit gallery) against a seeded fixture and lets you inspect
  and drive it from the command line: `pwsh dev/probe.ps1 attach --launch`, then `tree --interactable`,
  `click --id SetupTab`, `screenshot --id SurveyPage`, or `do --file dev/probe-scripts/app-open-calibration.probe
  --format text`. It talks to the app only through UI Automation, so it exercises what a user sees, and `assert`
  steps make a script pass or fail. Windows only; needs PowerShell 7 and `dev/seed-probe-fixture.ps1` once.

## 6. Tests layout

**Mapping**: one test project per src project for Domain, Application, Infrastructure, Proj, App.Core, App.UI (which
also covers Sample), App; one per tool for GeobaseImport, TestDataGenerator (Core+Cli), Probe; `Reporting.Tests`
cuts across Infrastructure reporting, Contracts and ReportCli; Measurements, Branding, Generators, TDG.Wpf,
GalleryCorpus have no dedicated tests (Measurements is covered indirectly by Domain.Tests).

**Shared infrastructure**: `Directory.Build.props:39-53` injects into every `*.Tests` project the NUnit/NSubstitute/
coverlet packages, `GlobalUsings.Tests.cs` (two global usings), `NUnitAssemblySettings.cs`
(`FixtureLifeCycle.InstancePerTestCase`), `geoDC.runsettings` (quiet NUnit adapter) and, for `UseWPF` projects
only, `tests/StaTestRunner.cs` (a background STA dispatcher, namespace `geoDC.Testing`). `tests/geoDC.Testing` adds
`ImmediateUiDispatcher`. The three root-level files sit at the repository root while `StaTestRunner.cs` sits in
`tests/`; `geoDC.slnx:48-50` lists them under `/tests/` together with a `StaTestDispatcher.cs` that **does not
exist**.

**Linux vs Windows** (`docs/cloud-sessions.md:16-24`): everything runs on Linux except `geoDC.App.Tests` and
`geoDC.App.UI.Tests`. CI runs `dotnet test geoDC.slnx` on a Windows runner (`ci.yml:151,204`; `installer.yml:253`).

**One-paragraph explanation for a newcomer** (feasible): "Each `src` and `tools` project has a `tests/<name>.Tests`
twin, plus `Reporting.Tests` for the report pipeline end to end. Packages, global usings, NUnit settings and the WPF
STA runner are injected by `Directory.Build.props`, so a test csproj is five lines. Projects with `-windows` target
frameworks (App, App.UI) run only on Windows; the rest run anywhere, which is why CI and cloud sessions run those
individually." Two things make it less explicable: STA is handled two ways (`StaTestRunner` in 98 App.UI.Tests
files vs `[Apartment(ApartmentState.STA)]` in 7 App.Tests files, because App.Tests does not set `UseWPF` and so
never receives the runner), and repository-wide convention tests (comments, log templates, XAML keys, string
resources, 8 files) live under `tests/geoDC.Infrastructure.Tests/Conventions/` although they scan `src/geoDC.App`
and `src/geoDC.App.UI` — presumably because that project runs on Linux.

**Duplication to flag**:

| Duplicate | Copies |
|---|---|
| `TestConventions.cs` | `Domain.Tests`, `Infrastructure.Tests`, `Reporting.Tests` (identical 10 lines); `App.Tests` (superset, 22 lines) |
| "generator in its own service graph" host | `App.Tests/Infrastructure/GeneratedPackages.cs:22-47`, `App.Tests/Workflows/SelectionCascadeTests.cs:139`, `Reporting.Tests/ReportProjectHost.cs:28-44`, `TestDataGenerator.Tests/TestGeneratorHost.cs:20`, and `src/geoDC.App/Startup/DemoProjectGenerator.cs:42-53` |
| Repository-root discovery | `Infrastructure.Tests/Conventions/SourceTree.cs:10`, `Probe.Tests/AutomationConventionsTests.cs:62`, `Probe.Tests/DoInvocationTests.cs:604`, `App.Tests/Views/AppXamlSource.cs` |
| Linked sources instead of references | `App.Tests` links `GeobaseImport.Tests/GeobaseFixtures.cs` (`.csproj:17`); `Probe.Tests` links 32 probe files |

## 7. Solution filters (.slnf)

Visual Studio builds only the projects a filter loads, so each filter lists its transitive dependencies.

| Filter | Projects | Who uses it |
|---|---|---|
| `geoDC.Core.slnf` (runs on Linux) | Measurements, Domain, Application, Reporting.Contracts, Proj, Infrastructure; Domain.Tests, Application.Tests, Infrastructure.Tests, Proj.Tests, Reporting.Tests (+ TDG.Core, ReportCli it needs) | Domain/persistence/report work; cloud sessions |
| `geoDC.App.slnf` | geoDC.App, App.Core, App.UI, App.UI.Generators, Branding, Sample; App.Tests, App.UI.Tests, App.Core.Tests, geoDC.Testing; dependencies Application, Domain, Measurements, Reporting.Contracts, Infrastructure, Proj, GeobaseImport (App.Tests), TDG.Core (App.Tests; App until §3) | Day-to-day app work |
| `geoDC.Toolkit.slnf` | App.UI, App.UI.Generators, App.Core, Sample, Measurements, Application, Domain, Reporting.Contracts; App.UI.Tests, App.Core.Tests, geoDC.Testing | Control/theme work in the gallery (fast, no app) |
| `geoDC.Tools.slnf` | TDG.Core/Cli/Wpf, GeobaseImport, ReportCli, Probe (GalleryCorpus until moved); TDG.Tests, GeobaseImport.Tests, Probe.Tests; dependencies Application, Domain, Measurements, Reporting.Contracts, Infrastructure, Proj, App.UI (+App.Core, Generators) for TDG.Wpf | Tooling work |

**Full-solution assumptions found**: CI restores/builds/tests `geoDC.slnx` (`ci.yml:108,199,204`;
`installer.yml:247-253`; `nuget-cache.yml:60`) and runs `dotnet format style geoDC.slnx` (`ci.yml:122`);
`docs/publishing.md:21` keeps the probe on `main` "because `geoDC.slnx` builds it" (a filter does not change that,
removing the project from the slnx would); `dev/probe.ps1:28` and the probe's `rebuild` build csproj files
directly; `Directory.Build.props`/`.targets` have no solution dependency (they key on project names and `UseWPF`).
Nothing stops filters being added; CI should keep the full slnx.

## 8. Other oddities a newcomer would question

| Item | Evidence | Note |
|---|---|---|
| Solution lists files that do not exist | `geoDC.slnx:29-30` `GlobalUsings.Library.cs`, `GlobalUsings.Wpf.cs`; `:50` `StaTestDispatcher.cs` — none tracked | VS shows three broken items; remove. |
| `Directory.Build.rsp` | `-verbosity:quiet` for every command-line build (`:3-4`) | `dotnet build` prints nothing on success; worth a README line. VS ignores it. |
| `dotnet-tools.json` | `jetbrains.resharper.globaltools` 2025.3.3 (`jb`) is used by no script, hook, workflow or doc (only a prose mention in `docs/coding-style.md:8`); `nbgv` only by `dev/release.ps1:60`; `xstyler` by `dev/format-xaml.ps1:54`; `husky` by the hooks | `dotnet tool restore` downloads ReSharper CLI for nothing; drop it or document. |
| `global.json` | SDK `10.0.302`, `rollForward latestFeature`, `allowPrerelease true` | Fine; VS 2026 with .NET 10 SDK required. |
| `nuget.config` | Second feed `https://nuget.sthornton.co.uk` mapped to `Snt.*` (`Snt.Lib.General`, `Snt.Lib.Wpf`, `Snt.Lib.Wpf.Text.Generators` `0.10.0-preview.8`, `Directory.Packages.props:33-35`) | A personal feed is a hard dependency of the build (the string-resource generator comes from it); the newcomer needs access and the owner needs a continuity plan. |
| `Settings.XamlStyler` | 2-space XAML, one attribute per line, consistent with `.editorconfig:209-212`; enforced by the hook and CI "Verify XAML formatting" | Fine; mention that VS's XAML Styler extension should load this file. |
| Husky | `.husky/pre-commit` runs `dotnet format whitespace` and `pwsh dev/format-xaml.ps1` per staged file; `.husky/post-checkout` bootstraps husky and all four hooks shell out to `git-lfs` and **exit 2 if it is missing** | Needs pwsh 7 and git-lfs on PATH or commits fail. |
| Git LFS | `.gitattributes:6` `*.svg` in LFS; `* text=auto eol=crlf` | See Branding; CRLF checkout on all platforms is deliberate. |
| `tools/typst/` | Gitignored (`.gitignore:31`); `Directory.Build.targets:30-36` runs `pwsh dev/fetch-typst.ps1` during build to download a GitHub release; `GeoDCSkipTypstFetch=true` opts out | A build that needs PowerShell and internet surprises VS users behind a proxy; `typst.pin` is the marker. |
| `tools/preview/` | Node ES-module project (`package.json` has only `private`/`type`), `server.mjs`, `preview.test.js`, no README, not in the solution; documented at `docs/charting.md:226-240` | Invisible from VS; needs a README or a pointer in README's layout table (currently "chart preview"). |
| `tests/geoDC.Infrastructure.Tests/Conventions/` | Repo-wide style guards (`CommentConventionTests`, `LogTemplateConventionTests`, `XamlResourceKeyConventionTests`, `StringResourceConventionTests`, `ResourceDictionaryConventionTests`) | Consider a `geoDC.Conventions.Tests` project so the name says what it is. |
| `InternalsVisibleTo` from Domain to Infrastructure | `src/geoDC.Domain/geoDC.Domain.csproj:22` | A production assembly seeing another's internals; record why. |
| Two Windows TFMs | App and App.Tests `net10.0-windows10.0.17763.0` (WebView2 composition, `geoDC.App.csproj:40-41`); every other WPF project `net10.0-windows` | Explained in the csproj comment; worth a line in AGENTS.md. |
| Agent-only surface on `dev` | `.agents/skills` (12 skills) mirrored to `.claude/skills`, `.claude/hooks`, `.codex/rules`, `dev/sync-agent-skills.ps1` parity check in CI, `.publishignore` strips them for `main` | The VS newcomer works on `dev` and sees all of it; README already frames AGENTS.md as "written for coding agents". |
| AGENTS.md "Solution boundaries" is incomplete | Lists neither Branding, Generators, the tools, nor `tests/geoDC.Testing` | Update alongside the project changes above. |
