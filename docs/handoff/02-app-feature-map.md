# geoDC.App feature map and restructure plan

Verified @ e9a8766 (`origin/dev`). Read-only investigation report 2 of 5 for the handoff epic (#780). Paths are relative to the repository root unless they start with a folder name under `src/geoDC.App/`.

## 0. Headline findings

| # | Finding | Evidence |
|---|---|---|
| 1 | `src/geoDC.App` is 572 `.cs`/`.xaml` files, 84,967 lines. 419 files (73%) belong to one of 16 product areas; 153 are cross-cutting (shell, startup, workspace, messaging, shared services, theming, menus). | Appendix A counts |
| 2 | Popup XAML is found at runtime by **implicit typed `DataTemplate` lookup**: `PopupSession.ContentTemplate` is never set by the app (grep `ContentTemplate =` in `src/geoDC.App` returns nothing), so the frame's `ContentPresenter` resolves `DataType="{x:Type …ViewModel}"` from `Application.Resources`. Moving a template to any dictionary that is still merged into `App.xaml` changes nothing. | `src/geoDC.App.UI/Themes/Controls.Popups.xaml:151-154`, `src/geoDC.App.UI/UI/Popups/PopupSession.cs:57`, `src/geoDC.App/App.xaml:15-20` |
| 3 | There is **no `Themes/Generic.xaml`** in geoDC.App and no registry of templates. The only registration points are the six aggregator dictionaries merged by `App.xaml` and `services.AddSingleton<IPopupDefinition, …>()` in `App.xaml.cs:296-333`. | `App.xaml:15-20`, `Themes/PopupTemplates.xaml:5-31` |
| 4 | The `Menus/` folder is **already organised by feature** (`Menus/<Name>/<Name>ContextMenuViewModel.cs` + `<Name>MenuTemplate.xaml`, each merged by pack URI from `Themes/MenuTemplates.xaml`). It is the template for the proposed structure and proves the pack-URI-per-feature approach already works in this app. | `Themes/MenuTemplates.xaml:1-29`, `Menus/MenuRegistrations.cs:26-32` |
| 5 | Namespaces follow folders everywhere except `Popups/VrfTransformations/*.cs` (12 files declare `geoDC.App.Popups`). `.editorconfig:101` sets `dotnet_style_namespace_match_folder = true:refactor` (not enforced). Under a namespace-follows-folder policy the move touches 610 `using geoDC.App.*` lines in the app, 472 in tests, 106 `xmlns` declarations (32 of them app-internal, the other 74 are `geoDC.App.UI.Markup`) and 31 `x:Class` values. | Section 4 |
| 6 | Seven tests read app XAML **from disk by path** (`tests/geoDC.App.Tests/Views/AppXamlSource.cs:44-56`, `SettingsSectionKeyTests.cs:76`, `DiagnosticsMenuTests.cs:80`, `ShellShortcutsTests.cs:74-75`, `ShellMenuTests.cs:23-25`, `SwitcherKeyConventionTests.cs:34,84`, `AttemptPageAutomationIdTests.cs:110-111`). Three hard-code `Views/Shell/Menus` or `Themes/Templates/Popups`; one depends on file names being unique across the project. | Section 6 |
| 7 | Popup co-location is behaviour-neutral if templates stay `DataType`-keyed `ResourceDictionary` files merged from one aggregator. Converting to `UserControl`s is also behaviour-neutral for the popup pipeline, but changes the UIA tree (an extra `UserControl` node; `AutomationId`s on inner elements survive) and is unnecessary work. | Section 3 |
| 8 | Recommended sequencing: one PR per area (16 small PRs plus one "scaffold" PR), **not** a big bang. The scaffold PR establishes `Features/` and the per-feature dictionary pattern; area PRs then move files with `git mv` and only namespace edits. Collisions: #453 (Survey), #306/#307 (attempt pages), #515 (#527 popup size classes), #88 (composition root). | Section 7 |

## 1. How the app is wired today (what a file move can break)

### 1.1 Resource dictionary merge chain

```
App.xaml:12-20  Application.Resources.MergedDictionaries
  ├─ layout:LayoutDictionary / theme:ThemeDictionary / branding:BrandingDictionary   (toolkit tokens + control styles)
  ├─ /geoDC.App;component/Themes/StartupTemplates.xaml    → Templates/Startup/{RecentProjectEntry,ProjectSelector,NewProject}Template.xaml   (relative Source)
  ├─ /geoDC.App;component/Themes/PopupTemplates.xaml      → Templates/Popups/*.xaml (27 files, PopupTemplates.xaml:5-31, relative Source)
  ├─ /geoDC.App;component/Themes/SetupPopupTemplates.xaml → Templates/Setup/*.xaml (4 files)
  ├─ /geoDC.App;component/Themes/SharedTemplates.xaml     → Templates/Shared/*.xaml (4 files)
  ├─ /geoDC.App;component/Themes/OutputLog.xaml           (keyed styles + OutputLogEntryTemplate)
  └─ /geoDC.App;component/Themes/MenuTemplates.xaml       → /geoDC.App;component/Menus/<Name>/<Name>MenuTemplate.xaml (13 files, absolute pack URIs)
                                                            each of which merges /geoDC.App;component/Menus/MenuItemStyles.xaml
```

- Relative `Source=` paths inside the aggregators (`Themes/PopupTemplates.xaml:5-31`, `SetupPopupTemplates.xaml:5-8`, `SharedTemplates.xaml:5-8`, `StartupTemplates.xaml:5-7`) resolve against the aggregator's own location. Moving a leaf dictionary breaks the aggregator line; moving the aggregator breaks every line.
- One leaf dictionary merges another with a relative path: `Themes/Templates/Popups/SequentialDataImportWizardPopupTemplate.xaml:12` → `WizardPopupSharedStyles.xaml`. The two must move together.
- 12 context-menu templates merge `Menus/MenuItemStyles.xaml` by absolute pack URI (`Menus/*/…MenuTemplate.xaml`), which survives moving the menu template but not moving `MenuItemStyles.xaml`.
- Merge order matters only for `StaticResource` lookups between dictionaries. Cross-file keyed resources in the app are few (Section 6.3): `OutputLogEntryTemplate` (defined `Themes/OutputLog.xaml:41`, consumed `LogsPopupTemplate.xaml:69` by `DynamicResource`, so order-independent), `JobPersonnelAssignmentTemplate`/`JobEquipmentAssignmentTemplate` (`Templates/Setup/JobAssignmentTemplates.xaml:9,49` → `Views/Shell/SetupPage.xaml`), `CheckedCaptionStyle` (`Templates/Shared/CheckedCaptionStyle.xaml` → `Views/Attempts/DatasetSlot.xaml`, `Views/Shell/CalibrationShellPage.xaml`), `ImportWizardPanelStyle` and friends (`WizardPopupSharedStyles.xaml` → the wizard template). All other `x:Key`s (31 total under `Themes/`+`Menus/`) are consumed inside their own file.

### 1.2 Dependency injection (two hand-maintained graphs)

| Registration | Where | What moves with a file |
|---|---|---|
| Popup definitions (38) | `App.xaml.cs:296-333` `services.AddSingleton<IPopupDefinition, X>()` | A `using` line only; the registration is by type, not path |
| Pages (8) | `App.xaml.cs:370-377` `services.AddTransient<SetupPage>()` etc. | `using geoDC.App.Views.Shell;` (`App.xaml.cs:48`) |
| Page viewmodels, shell singletons | `Startup/AppServiceRegistrations.cs:160-179` `AddPageViewModels` | `using` lines `AppServiceRegistrations.cs:1-30` |
| Workspace handlers (22) | `AppServiceRegistrations.cs:67-92` `AddModuleHandlers` | same |
| Workflows, catalogues | `AppServiceRegistrations.cs:98-113` `AddWorkItemWorkflows` | same |
| Diagnostic rules (12) | `AppServiceRegistrations.cs:132-154` — note two rules are disambiguated by **partial namespace** `ViewModels.Calibration.Diagnostics.RollNotConvertedRule` (`:138,140,150,152`); a namespace change edits these | same |
| Context menus (13) | `Menus/MenuRegistrations.cs:35-54` `AddContextMenu<VM, Target>()` | `using` lines `MenuRegistrations.cs:1-22` |
| Test graph | `tests/geoDC.App.Tests/Infrastructure/TestAppHost.cs:67-123` repeats the module calls and adds stubs | `using` lines `TestAppHost.cs:1-50` (37 of them `geoDC.App.*`) |

Issue #88 (shared composition root) is the standing request to collapse these; a feature restructure makes "one `AddXxxFeature()` per area" the natural shape.

### 1.3 Navigation

- Pages are resolved **by CLR type** through the container: `INavigationService.Navigate<TPage>()` → `NavigationService.cs:45-56` → `scope.ServiceProvider.GetRequiredService(pageType)` (`src/geoDC.App.Core/Services/NavigationService.cs:119`). There is no name- or URI-based page registry.
- Call sites: `MainWindow.xaml:173-217` nav bar (`CommandParameter="{x:Type shell:ShoreControlPage}"` etc.), `Views/Shell/Menus/ViewMenu.xaml:49-55`, `ViewModels/MainShellViewModel.cs:105,215-235,449,462,472`. The `xmlns:shell="clr-namespace:geoDC.App.Views.Shell"` declaration in those two XAML files is the only thing a page move touches.
- `MainShellViewModel.cs:744-752` maps page type → log name with `nameof(...)`: compile-checked, no string coupling.
- Probe reserved ids `LandingTab…ReportsTab` (`tools/geoDC.Probe/AutomationConventions.cs:13-16`) are AutomationIds in `MainWindow.xaml`, not type names.

### 1.4 Text generator

`Snt.Lib.Wpf.Text.Generators` is wired by `buildTransitive/Snt.Lib.Wpf.Text.Generators.targets`: `AdditionalFiles Include="Strings\**\*.yaml"` plus `@(Page)` and `@(ApplicationDefinition)`. It analyses **every XAML page in the project regardless of folder** (its `XamlTextAnalyzer` emits `SNTTXT0xx` diagnostics, `docs/string-resources.md:126`). Moving XAML does not change its inputs. The YAML folder name `Strings/` is fixed by the targets; keep it at the project root.

`tests/geoDC.Infrastructure.Tests/Conventions/StringResourceConventionTests.cs:12,135` scans `src/**/*.cs|*.xaml` for uses of each generated member — folder-agnostic.

## 2. Worked chain: Shore Control

Shore Control has a page, a modeless "Register" workbench window, four dialogs and an import popup, so it exercises every link.

| Step | Link | Evidence (path:line) |
|---|---|---|
| 1 | **Menu / nav entry** → navigates by page type | `Views/Shell/Menus/ToolsMenu.xaml:18-20` (`ShoreControlButton` → `OpenShoreControlCommand`); `Views/Shell/Menus/ViewMenu.xaml:51-55` (`ShoreControlPageButton`, `CommandParameter="{x:Type shell:ShoreControlPage}"`); `MainWindow.xaml:203-205` (nav bar tab, `ShoreControlText.PageNavLabel`); `MainWindow.xaml:120` (title-bar status) |
| 2 | **Shell command** | `ViewModels/MainShellViewModel.cs:469-472` `OpenShoreControl()` → `_navigationService.Navigate<ShoreControlPage>()`; `:219` `IsShoreControlActive`; `:662` `CanOpenShoreControl` |
| 3 | **Navigation resolves page from DI** | `src/geoDC.App.Core/Services/NavigationService.cs:56,119`; registration `App.xaml.cs:375` `services.AddTransient<ShoreControlPage>()`; hosted by `MainWindow.xaml:220-222` `<ContentControl Content="{Binding CurrentPage}">` |
| 4 | **Page view** | `Views/Shell/ShoreControlPage.xaml:1-16` (`x:Class="geoDC.App.Views.Shell.ShoreControlPage"`, `xmlns:shoreControl="clr-namespace:geoDC.App.Views.ShoreControl"`, `xmlns:viewModels="clr-namespace:geoDC.App.ViewModels.ShoreControl"`, `AutomationId="ShoreControlPage"`); composes `Views/ShoreControl/{SetSummary,PointNavigator,PointTable,PointInspector,Map}View` (`ShoreControlPage.xaml`, 5 `<shoreControl:…>` elements) |
| 5 | **Page viewmodel** injected into the view's constructor | `Views/Shell/ShoreControlPage.xaml.cs:10-14`; VM registered `Startup/AppServiceRegistrations.cs:168`; `ShoreControlJobStatus` singleton `:169` |
| 6 | **Page opens the Register window** | button `Views/Shell/ShoreControlPage.xaml:128` (`OpenRegisterCommand`) and `:69,107` (`ReviewCommand`); `ViewModels/ShoreControl/ShoreControlPageViewModel.cs:697-715` → `_popups.ShowAsync<ShoreControlRegisterPopupRequest, ShoreControlRegisterPopupResult>(…)` |
| 7 | **Popup contracts** (request/result records) | `Popups/ShoreControlRegisterPopupContracts.cs:23-26` |
| 8 | **Popup definition** (presentation, actions, VM creation) | `Popups/WorkflowPopupDefinitions.cs:388-418` `ShoreControlRegisterPopupDefinition` — `SizeClass = Workbench`, `AutomationId = "ShoreControlRegisterPopup"`, `IsModal = false`; `CreateViewModel` uses `ActivatorUtilities` (`:405-411`); the four sibling definitions are `:267-386` (`ImportShoreControlPoints`, `ShoreControlPointEditor`, `ShoreControlForm`, `ShoreControlChooser`) |
| 9 | **Popup viewmodel** | `ViewModels/ShoreControl/ShoreControlRegisterViewModel.cs:64` (ctor takes the request + `AppProjectWorkspace`); dialog VMs live beside their definitions: `Popups/ShoreControlChooserPopup.cs:19`, `Popups/ShoreControlFormPopup.cs:36`, `Popups/ShoreControlPointEditorPopup.cs:28`, `Popups/ImportShoreControlPointsPopupViewModel.cs:51` |
| 10 | **Popup template** (typed, no key) | `Themes/Templates/Popups/ShoreControlRegisterPopupTemplate.xaml:7-9` `<DataTemplate DataType="{x:Type viewModels:ShoreControlRegisterViewModel}"><shoreControl:ShoreControlRegisterView/>` — the body is a `UserControl` `Views/ShoreControl/ShoreControlRegisterView.xaml:1-16` (`AutomationId="ShoreControlRegisterPanel"`) that reuses the same five sub-views as the page; `ShoreControlFormPopupTemplates.xaml:12` templates the dialog VM **and** four form VMs from `ViewModels/ShoreControl` (`IShoreControlForm` implementations chosen by type) |
| 11 | **Template registration** | `Themes/PopupTemplates.xaml:15-18` (four `Source="Templates/Popups/…ShoreControl…"` lines) ← `App.xaml:16` |
| 12 | **Runtime lookup** | `PopupService` → `WindowPopupHostStrategy(Immersive)` (`App.xaml.cs:337`) → `FullPopupWindow`/`PopupWindow` creates `PopupFrame { Session = session }` (`src/geoDC.App.UI/UI/Popups/PopupWindow.cs:81-88`) → frame template's `ContentPresenter Content=Session.ContentViewModel ContentTemplate=Session.ContentTemplate` (`src/geoDC.App.UI/Themes/Controls.Popups.xaml:151-154`); `ContentTemplate` is null (`PopupSession.cs:57`, never assigned in the app), so WPF's implicit `DataTemplate` lookup walks to `Application.Resources` |
| 13 | **DI registration of definitions** | `App.xaml.cs:329-333` (five `AddSingleton<IPopupDefinition, ShoreControl…>()`) |
| 14 | **Workspace / persistence** | `Workspace/Handlers/ShoreControlSnapshot{Load,Save}Handler.cs` registered `AppServiceRegistrations.cs:87-88`; `Workspace/ShoreControlSnapshotCheckpoint.cs`; `Messaging/ShoreControlChangedMessage.cs` |
| 15 | **Strings** | `Strings/shoreControl.yaml` → `ShoreControlText` (used in 11 `ViewModels/ShoreControl` files, 4 templates, `WorkflowPopupDefinitions.cs`, `MainWindow.xaml:205`) |
| 16 | **Tests** | `tests/geoDC.App.Tests/ViewModels/ShoreControl/` (8 files: `ShoreControlRegisterViewModelTests.cs:581,597` build the request; `ShoreControlPopups.cs` is a scripted `IPopupService` for the dialogs; `ShoreControlRegisterFixture.cs`), `tests/geoDC.App.Tests/Popups/ShoreControlPointEditorPopupViewModelTests.cs`, `tests/geoDC.App.Tests/Workspace/ShoreControlSnapshot{Checkpoint,Persistence}Tests.cs`; all bind by `using geoDC.App.Popups; using geoDC.App.ViewModels.ShoreControl;` |

**Hops for a new developer today** ("I see the Register window; where is its XAML?"): UIA id `ShoreControlRegisterPopup` → grep → `WorkflowPopupDefinitions.cs:398` (a 418-line file holding 14 definitions for two areas) → VM type → grep `DataType=…ShoreControlRegisterViewModel` → `Themes/Templates/Popups/ShoreControlRegisterPopupTemplate.xaml` → `Views/ShoreControl/ShoreControlRegisterView.xaml`. Four hops across four top-level folders. With the files in `Features/ShoreControl/`, the same grep lands in one folder.

**Places that must change when a Shore Control file moves** (the break list): `Themes/PopupTemplates.xaml:15-18` (or its replacement), `App.xaml.cs:44,48,329-333,375` usings, `AppServiceRegistrations.cs:17,87-88,168-169` usings, `MainWindow.xaml:10` and `Views/Shell/Menus/ViewMenu.xaml:10` (`xmlns:shell`), every `xmlns:viewModels="clr-namespace:geoDC.App.ViewModels.ShoreControl"` (11 XAML files) and `xmlns:shoreControl="clr-namespace:geoDC.App.Views.ShoreControl"` (4 files), `x:Class` in 10 XAML files, test usings (12 files), `TestAppHost.cs:30`.

## 3. Popup co-location

### 3.1 How a popup template is found (exact mechanism)

1. `PopupDefinition<TRequest,TResult,TViewModel>` builds a `PopupSession` whose `ContentViewModel` is the viewmodel instance (`src/geoDC.App.UI/UI/Popups/PopupSession.cs:52`). `ContentTemplate` (`:57`) is an optional explicit override; **nothing in `src/geoDC.App` sets it** and `DialogOptions.ContentTemplate` (`src/geoDC.App.UI/UI/Dialogs/DialogOptions.cs:35`) is unused by the app.
2. Every host (overlay `PopupHost`, `PopupWindow`, `FullPopupWindow`) shows a `PopupFrame` bound to the session (`PopupWindow.cs:81-88`).
3. The frame's control template presents the body as `<ContentPresenter Content="{Binding Session.ContentViewModel}" ContentTemplate="{Binding Session.ContentTemplate}"/>` (`Controls.Popups.xaml:151-154`). With a null `ContentTemplate`, `ContentPresenter` performs the standard implicit lookup: a `DataTemplate` whose `DataType` equals the content's runtime type, searched up the element tree, then `Application.Resources` (which holds the merged `PopupTemplates.xaml`), then the theme.
4. Therefore the only requirements are: (a) the template is a **typed** `DataTemplate` (`DataType="{x:Type …}"`, no `x:Key`), (b) it is reachable from `Application.Resources` or from a resource scope above the frame, and (c) there is exactly one such template for the type. There is no registry, no key convention, and no name-based lookup. Toolkit popups work the same way (`src/geoDC.App.UI/Themes/Templates/DialogPopupTemplate.xaml:10`, `LoadingPopupTemplate.xaml:8`).

Secondary templates inside popup dictionaries (e.g. `SensorPopupTemplates.xaml` templates `SensorPropertiesForm` and `MountPointPopupOption`; `ShoreControlFormPopupTemplates.xaml` templates four `IShoreControlForm` types; `ImportSurveyPointsPopupTemplate.xaml` templates `ImportGroupRule`) are also typed and application-wide, so they must move **with** their popup, and two areas must never template the same type.

### 3.2 Constraints from `docs/ui/popups.md` and the host

- Content templates must not contain action buttons; footer actions come from `CreateActions` (`popups.md` > Actions live in the footer). Co-location does not change this.
- `ScrollsBody = false` content owns its own `ScrollViewer` and declares `Height` for `Detached` (`popups.md` > Who scrolls the body). Unaffected.
- `PopupPresentation.AutomationId` on the frame and per-action ids are the UIA contract; content ids live inside the template. A template moved as a dictionary keeps the identical visual/UIA tree. A template converted to a `UserControl` adds one `UserControl` node between the frame's `ContentPresenter` and the content, which the probe (`ElementResolver` by exact `AutomationId`, `tools/geoDC.Probe/ElementResolver.cs:8`) ignores, but which changes `TreeDumper` output and screenshots' tree depth.
- Esc handling via `PopupHost.HandlesEscape` attached property and `IPopupCloseGuard` are VM/host concerns, location-independent.
- Detached/Immersive popups are separate windows, so they rely on `Application.Resources`, not on `MainWindow` resources; a per-feature dictionary therefore **must be merged at application level** (or into the toolkit's `ThemeResources`), never only into a page's `Resources`.

### 3.3 Options

| Option | Behaviour | Cost | Verdict |
|---|---|---|---|
| A. Keep each `…PopupTemplate.xaml` as a `ResourceDictionary`, move it into the feature folder, merge it from **one per-feature dictionary** `Features/<Area>/<Area>Templates.xaml`, and merge those from `App.xaml` (or from a single `Themes/AppTemplates.xaml`) | Identical (same typed templates in `Application.Resources`) | `git mv` + edit the `Source=` lines; add 16 lines to `App.xaml` | **Recommended.** Mirrors the existing `Menus/` pattern exactly |
| B. Same as A but merge from the feature's page `Resources` | Breaks `Detached`/`Immersive` popups and any popup opened before the page exists (settings, selector) | — | Reject |
| C. Convert templates to `UserControl`s with a one-line typed `DataTemplate` each (the Register already does this: `ShoreControlRegisterPopupTemplate.xaml:7-9` → `ShoreControlRegisterView`) | Pipeline-neutral; UIA tree gains a `UserControl` node per popup; large templates (`DataAnalysisPopupTemplate.xaml` 1120 lines, `SequentialDataImportWizardPopupTemplate.xaml` 1208) gain code-behind files and `x:Class` names | 27 new `.xaml.cs` files; `AttemptPageAutomationIdTests` and the probe audits unaffected | Optional later; **not** part of the move. Do it only where a template is reused or needs code-behind |
| D. Register templates per feature in C# (`Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/geoDC.App;component/Features/…") })` from an `AddXxxFeature()` DI module) | Identical at runtime; moves the "registration" next to the DI module (`#88` direction) | Loses design-time resource resolution in the XAML designer; hides merge order in code | Defensible, but A is simpler for a Visual Studio developer |

Pack URI form for a per-feature dictionary from `App.xaml`: `<ResourceDictionary Source="/geoDC.App;component/Features/ShoreControl/ShoreControlTemplates.xaml" />` — the same form `App.xaml:15-20` and `Themes/MenuTemplates.xaml:15-27` already use. Inside the feature dictionary, relative `Source="Popups/ShoreControlRegisterPopupTemplate.xaml"` works as it does today in `PopupTemplates.xaml`.

## 4. Target structure

### 4.1 Proposed tree

```
src/geoDC.App/
  App.xaml, App.xaml.cs, MainWindow.xaml(.cs), GlobalUsings.cs
  Strings/                         (unchanged; the generator's targets fix this name)
  Features/
    Landing/                        ProjectLandingPage.xaml(.cs), ProjectLanding*.cs                       (8)
    Setup/                          SetupPage.xaml(.cs), Setup/Job/Project VMs, Popups/ (3 editors + JobAssignmentTemplates),
                                    Menus/{Job,Personnel,Equipment}, messages, SetupTemplates.xaml           (39)
    Survey/                         SurveyPage.xaml(.cs), Survey VMs, VesselFrame/ (entity VMs), FieldRecords/, FieldPhotos/,
                                    Popups/ (feature, group, sensor, instrument, measurement, import points, photo preview),
                                    Menus/{SurveyFeature,FeatureState,FeatureGroup,InstrumentInstallation,Sensor},
                                    Transformations/ (panel VM, 5 tool popups, menu, templates), SurveyTemplates.xaml  (82 + 17)
    Calibration/                    CalibrationShellPage.xaml(.cs), VMs, Diagnostics/, Menu, clone workflow, messages  (24)
    Verification/                   …                                                                      (23)
    Gnss/                           …                                                                      (19)
    Attempts/                       Views/ (the 6 shared attempt controls), shared attempt VMs (today ViewModels/Common),
                                    WorkItemClone popup, clone-availability services, WorkItemContextMenuViewModel,
                                    AttemptsTemplates.xaml                                                 (38)
    ShoreControl/                   ShoreControlPage.xaml(.cs), Register/ (RegisterView + sub-views), VMs, Popups/ (5),
                                    MapHosting + MapAssets, snapshot checkpoint, message, ShoreControlTemplates.xaml   (64)
    Data/                           DataAnalysis popup (VM parts + template), DataSeries/ VMs, SeriesMatch popup,
                                    edit session/log, DataTemplates.xaml                                   (20)
    Import/                         DataImportWizard VM + ImportRunner, TimestampFormatEntry popup, wizard templates + shared
                                    styles, DataSeriesImportWorkflow, ImportColumnAnalyzer, recent-files service       (19)
    Reports/                        ReportsShellPage.xaml(.cs), ReportsPageViewModel, ReportContentRow, destination picker (6)
    Settings/                       SettingsDialog VM/definition/request/template, presenter, theme preference, binder  (13)
    Launch/                         ProjectSelector + NewProject (VMs, definitions, templates), session/recent/file services,
                                    RecentProjectsMenu, RecentProject menu, ThirdPartyNotices popup, LaunchTemplates.xaml (33)
  Shell/                            MainShellViewModel, DiagnosticsMenuViewModel, ActiveJobSwitcher, Menus/ (7 menu bar files +
                                    ShellShortcuts), ShellCommandPolicy, About/KeyboardShortcuts/Logs/Fingerprint popups,
                                    OutputLog.xaml, Diagnostics/ (bundle, summaries, watchdog), ShellTemplates.xaml     (51)
  Startup/                          composition + bootstraps only: AppServiceRegistrations, *Bootstrap.cs, logging sinks,
                                    data paths, ThirdPartyNotices build task                                 (15)
  Workspace/                        AppProjectWorkspace, Handlers/, Alterations/, selection coordinator (unchanged)   (41)
  Messaging/                        JobContent* navigation request/target/kind + IJobContentNavigator (cross-area)     (6)
  Menus/                            EntityContextMenuViewModel, CopySummaryField, MenuRegistrations, MenuItemStyles.xaml (4)
  Shared/                           Conventions/ (VMs, catalogue, choice template, VesselDisplayConventions), References/,
                                    Charting/, WebHosting/, Converters/, Behaviors/, Crs/, file/folder services       (44)
  Themes/                           AppTemplates.xaml (one aggregator merging each Features/*/…Templates.xaml and Shell), or
                                    delete and list the 17 dictionaries directly in App.xaml                 (1)
```

Flat (`Features/ShoreControl/ShoreControlPage.xaml` next to `ShoreControlPageViewModel.cs`) is preferable to `Features/ShoreControl/{Views,ViewModels,Popups}/` for small areas; the three sub-folders earn their place only in Survey (82 files) and Shore Control (64). Rule: **a view, its viewmodel and its popup definition/contracts sit in the same folder; a sub-folder appears only when it holds five or more files** (`Popups/`, `Menus/`, `Diagnostics/`, `Register/`).

### 4.2 Rule for shared files

1. A file used by exactly one area lives in that area, even if its name sounds generic (`AttemptDataUseWatcher` → Data; `MapHosting` → Shore Control; `AtomicFile` stays shared only because it is I/O infrastructure).
2. A file used by the three attempt pages and nothing else lives in `Features/Attempts/` (seeded by `workflow.yaml`), not in a generic folder.
3. Workspace **entity** viewmodels (`FeatureViewModel`, `JobViewModel`, `DataSeriesHeaderViewModel`, …) live with the area that owns the aggregate's load/save handler pair (Survey ← VesselFrame/Instrument, Setup ← Job/ProjectMetadata, Data ← DataSeries, Shore Control ← snapshots). The handlers themselves stay in `Workspace/Handlers/` because `AppProjectWorkspace` and `WorkspaceModuleOrder` are one pipeline.
4. Anything used by two or more non-attempt areas goes to `Shared/` (conventions, references, CRS picker, charting, converters) or `Messaging/` (job-content navigation).
5. Strings are the tie-breaker: the `XxxText` class a file uses names its area (`Popups/ThirdPartyNoticesPopupViewModel.cs` uses `LaunchText` ×7 → Launch; `Services/ProjectFileWorkflowService.cs` uses `LaunchText` ×26 → Launch).

### 4.3 Namespace policy

`docs/coding-style.md` has **no** namespace-vs-folder rule (it covers file-scoped namespaces, `:25`). `.editorconfig:101` carries `dotnet_style_namespace_match_folder = true:refactor`, i.e. IDE0130 as a refactoring hint, not a build warning. The codebase nevertheless follows folders for 560 of 572 files; the only exception is the 12 files in `Popups/VrfTransformations/` that declare `geoDC.App.Popups`.

Recommendation: **namespaces follow folders** (`geoDC.App.Features.ShoreControl`, `geoDC.App.Shell`, `geoDC.App.Shared.Conventions`). Reasons: Visual Studio's "Sync namespace with folder" and move-type refactorings do this automatically; a developer who finds a type by namespace finds its folder; keeping old namespaces would leave 419 files whose namespace lies about their location, which is the discoverability problem the epic is solving. Consequences, with counts measured at e9a8766:

| Artefact | Count | Notes |
|---|---:|---|
| `namespace` declarations in moved files | 419 | mechanical (Visual Studio does it on move) |
| `using geoDC.App.*` lines in `src/geoDC.App/**/*.cs` (excluding `geoDC.App.UI`/`geoDC.App.Core`) | 610 | `dotnet format` / IDE "remove and sort usings" after the rename fixes most |
| `using geoDC.App.*` lines in `tests/geoDC.App.Tests` | 472 | top: `Workspace` 52, `ViewModels.Setup` 46, `Popups` 38, `Services` 32, `ViewModels.Calibration` 30, `ViewModels.Survey` 28, `Startup` 28 |
| `xmlns:*="clr-namespace:geoDC.App.*"` in app XAML | 32 app-internal (`Popups` 20, `ViewModels.ShoreControl` 11, `Views.Attempts` 8, `ViewModels` 8, `Converters` 7, `Views.Behaviors` 6, `ViewModels.Setup` 6, `Views.ShoreControl` 4, …) | plus 74 `geoDC.App.UI.Markup` that do not change |
| `x:Class` declarations | 31 | pages (8), menus (7), attempt controls (6), Shore Control views (8), `App`, `MainWindow` |
| Pack URIs `/geoDC.App;component/…` | 6 in `App.xaml`, 13 in `Themes/MenuTemplates.xaml`, 12 `MenuItemStyles` merges, 1 relative merge in the wizard template | pack URIs reference **paths**, so they change even if namespaces do not |
| `AppServiceRegistrations.cs:138,140,150,152` partial-namespace rule names | 4 | become `Features.Calibration.Diagnostics.RollNotConvertedRule` etc. |
| `InternalsVisibleTo` | 1 (`geoDC.App.csproj:56`) | unchanged (assembly-level) |

If the team prefers zero C# churn, the alternative is "move files, keep namespaces" and suppress IDE0130; it halves the diff but leaves every `geoDC.App.Popups` type scattered across eight feature folders and is **not** recommended.

Design-time `d:DataContext` declarations (`ShoreControlPage.xaml:13`) use the same `xmlns:viewModels` prefixes and are included in the 32 count.

## 5. Tests

`tests/geoDC.App.Tests` (164 files, 40,325 lines) mirrors the **current kind-based layout**: `ViewModels/{Calibration,Setup,ShoreControl,Survey,…}`, `Popups/`, `Services/`, `Startup/`, `Views/`, `Workspace/`, `Menus/`, plus `Infrastructure/` (`TestAppHost`, `ScriptedPopupService`, `GeneratedPackages`), `Stubs/`, `Workflows/` (end-to-end). Test namespaces are `geoDC.App.Tests.<folder>` (`ShoreControlPopups.cs:6`, `ShoreControlPointEditorPopupViewModelTests.cs:11`). Survey tests are split between `ViewModels/Survey/` (1 file) and nine `Survey*Tests.cs` files directly under `ViewModels/`.

`tests/geoDC.App.UI.Tests` (137 files) tests the toolkit only and never references `geoDC.App`; it does not move.

Recommendation: move tests **in step, per area PR**, to `tests/geoDC.App.Tests/Features/<Area>/` (with `Shell/`, `Startup/`, `Workspace/`, `Shared/`, `Infrastructure/`, `Stubs/`, `Workflows/` kept). Reasons: the test project's only couplings to app layout are `using` lines (472) and the seven path-reading convention tests (Section 6.1); moving a test file is a `git mv` plus a namespace line; and leaving tests kind-based while the app is feature-based doubles the lookup cost the epic is trying to remove. `Infrastructure/TestAppHost.cs` must be updated in every area PR anyway (its usings, `:1-50`), so the test move adds no extra touch points.

## 6. Hazards: anything that depends on location, name or namespace

### 6.1 Tests that read source files from disk

| Test | What it reads | Breaks on |
|---|---|---|
| `tests/geoDC.App.Tests/Views/AppXamlSource.cs:14-19,44-56` | every `*.xaml` under `src/geoDC.App` (recursive, excluding `obj`/`bin`) | nothing; folder-agnostic |
| `Views/AttemptPageAutomationIdTests.cs:45-69,110-111` | page files by **bare file name** (`CalibrationShellPage.xaml`) via `AppXamlSource.Files.Single(name)` and follows `<attempts:(\w+)` (`:43`) to `<Name>.xaml` | two XAML files with the same name anywhere in the project (`.Single` throws). Keep file names unique project-wide, or scope the search |
| `Views/SettingsSectionKeyTests.cs:76-77` | `src/geoDC.App/Themes/Templates/Popups/SettingsDialogTemplate.xaml` (hard-coded) | moving the settings template → update the path |
| `Views/DiagnosticsMenuTests.cs:47-80` | `src/geoDC.App/Views/Shell/Menus/DiagnosticsMenu.xaml`, `MainWindow.xaml` | moving the menu bar files |
| `Views/ShellShortcutsTests.cs:74-75` | `MainWindow.xaml` + `Views/Shell/Menus/*.xaml` | same |
| `Views/ShellMenuTests.cs:23-25` | `MainWindow.xaml`, `Views/Shell/Menus/ViewMenu.xaml` | same |
| `Views/SwitcherKeyConventionTests.cs:34,84` | `MainWindow.xaml` + all `*.xaml` | nothing |
| `Views/DateBindingConventionTests.cs`, `NumericTextBindingConventionTests.cs` | `AppXamlSource.Files` + reflection over `typeof(SurveyPageViewModel).Assembly` (`AppXamlSource.cs:28`) | nothing (type anchor only) |
| `tests/geoDC.Infrastructure.Tests/Conventions/StringResourceConventionTests.cs:12,60,135` | `src/geoDC.App/Strings/*.yaml` and all `src/**/*.cs|xaml` | renaming `Strings/` |

### 6.2 Reflection, type names, string-based resolution

| Site | Mechanism | Risk |
|---|---|---|
| `src/geoDC.App.Core/Services/NavigationService.cs:119` | `GetRequiredService(pageType)` | none; type-based |
| `ViewModels/MainShellViewModel.cs:744-752` | `nameof(Page)` → display name | none; compile-checked |
| `Workspace/WorkspaceModuleCoordinator.cs:44,68`, `ViewModels/Calibration/CalibrationPageViewModel.cs:1083`, `Logging/ActivityLogSink.cs:16` | `GetType().Name` in **log messages** only | log text unchanged (names, not namespaces) |
| `Popups/VrfTransformations/VrfTransformationIssueText.cs:123` | `GetType().FullName` in an exception message | message text changes with namespace; no behaviour |
| `AppServiceRegistrations.cs:138,140,150,152` | partially qualified type names to disambiguate `RollNotConvertedRule`/`PrismBaselineRule` (one each in Calibration and Verification) | must be edited with the namespace; or rename the rules (`CalibrationRollNotConvertedRule`) |
| `Assembly.*`, `GetManifestResourceStream`, `Type.GetType(string)` in `src/geoDC.App` | **none found** | — |
| `tools/geoDC.Probe` | resolves by exact `AutomationId` (`ElementResolver.cs:8,38,47`), window `ClassName` (`GeoDcSession.cs:222`, a Win32 class, not a CLR type), reserved shell ids (`AutomationConventions.cs:13-16`) | none, provided AutomationIds and the frame/UserControl tree are unchanged |
| Context-menu router | `AddContextMenu<VM, Target>()` resolves by data-context **type** at right-click (`Menus/MenuRegistrations.cs:29-32`) | none |
| `AppXamlSource.PropertyNamesWhere` | reflection over all public properties in the assembly | none |
| CI workflows (`.github/workflows/*.yml`) | no `paths:` filters on `src/geoDC.App` sub-folders | none |

### 6.3 Resource keys and pack URIs

- 31 `x:Key`s under `Themes/`+`Menus/`; cross-file consumers listed in Section 1.1. Keys are global in `Application.Resources`: two features defining the same key would silently shadow each other (last merged wins). Prefix feature-local keys (`ImportWizard…` already does) or scope them inside the `DataTemplate.Resources`.
- `PopupFrameStyle` is looked up by key from the toolkit (`PopupWindow.cs:83`); app-side nothing.
- Pack URIs: `App.xaml:15-20`, `Themes/MenuTemplates.xaml:15-27`, 12 × `Menus/*/…MenuTemplate.xaml` (`MenuItemStyles.xaml`), the wizard's relative merge, and `Content Include="MapAssets\**"`/`ChartAssets\**` in `geoDC.App.csproj:30-32` (served to WebView2 by output-folder name: `Mapping/MapHosting.cs`, `Charting/ChartHosting.cs`). Moving `MapAssets/` would change the virtual-host mapping; leave both asset folders at the project root or update the csproj `Content` items and the host mapping together.

### 6.4 Tooling

- **Husky** (`.husky/task-runner.json`): pre-commit runs `dotnet format whitespace --folder --include ${staged}` and `dev/format-xaml.ps1` over staged and **renamed/copied** files (`--diff-filter=RC`), then re-stages them. A move PR therefore reformats every moved file on commit; `docs/coding-style.md:7` documents this. Expect whitespace-only noise unless files are already formatted (they are, per the CI Format job).
- **XamlStyler** (`Settings.XamlStyler`): attribute ordering only; no namespace or path rules. It will reorder a changed `xmlns` block alphabetically, so renaming `xmlns:viewModels` to `xmlns:shoreControl` moves the line.
- **Text generator**: inputs are `Strings\**\*.yaml` + all `@(Page)`; folder-agnostic. `SntTextGeneratedNamespace` is a project property, not folder-derived.
- **StaTestRunner** (`tests/StaTestRunner.cs`, injected by `Directory.Build.props:65`): no assembly or resource loading by name; no coupling.
- **AutomationId conventions**: ids are literals in XAML and in `PopupPresentation.AutomationId`/`PopupAction` (`WorkflowPopupDefinitions.cs:398`, `:306`, etc.); the role vocabulary (#439, this commit) is enforced by `tools/geoDC.Probe/AutomationAudit.cs` against the live tree. A pure move cannot change an id. `AttemptPageAutomationIdTests` guards the three attempt pages' ids from source.
- **Skills/docs that cite paths**: `.agents/skills/geodc-popup-workflow-builder/references/popup-patterns.md:17`, `geodc-wpf-ui-builder/SKILL.md:12` and `references/ui-patterns.md:6`, `geodc-mvvm-page-builder/references/viewmodel-patterns.md:5-11`, `geodc-project-session-workflow-builder/references/project-session-patterns.md` (6 refs), `docs/shell-menus.md:9`, `docs/coding-style.md` (2), `AGENTS.md` (1, `Workspace/Handlers/IDENTITY_CONVENTIONS.md`). The `Themes/MenuTemplates.xaml:1-10` header comment and `Menus/MenuRegistrations.cs:26-32` doc comment describe the folder convention and need rewording.

## 7. Sequencing and collisions

### 7.1 Recommendation: scaffold PR + one PR per area

| | One PR per area (recommended) | One big-bang PR |
|---|---|---|
| Review | each PR is a `git mv` set + namespace/using edits for ~20-80 files; reviewable with `git diff -M` | 572 renames; `-M` detection still works but review is impractical |
| Open branches | #453, #306/#307, #515 follow-ups touch Survey and attempt files; sequencing those areas last limits rebase pain to those branches | every open branch conflicts at once |
| Blame/history | `git log --follow` works per file either way; `.git-blame-ignore-revs` (README) can list the move commits | same |
| Intermediate state | mixed layout for a few weeks; acceptable because each area is coherent once moved | none |
| Risk of a half-done migration | real; mitigate by ordering and by a tracking issue listing the 16 areas | none |

Order (least coupled, fewest open PRs first): 0. **Scaffold**: add `Features/`, `Shell/`, `Shared/`; move the shell (menu bar, `MainShellViewModel`, help popups), fix the seven path-reading tests, replace `Themes/PopupTemplates.xaml` + `SetupPopupTemplates.xaml` + `SharedTemplates.xaml` + `StartupTemplates.xaml` + `MenuTemplates.xaml` with per-feature dictionaries merged from `App.xaml`, split `Popups/WorkflowPopupDefinitions.cs` and `WorkflowPopupContracts.cs` by area, fix the 12 `VrfTransformations` namespaces. 1. Launch, Settings, Reports, Landing (small, no open work). 2. Shore Control (#48 epic is open but its UI work landed under #515/#526). 3. Data, Import. 4. Setup. 5. Attempts, then Calibration, Verification, GNSS (after or coordinated with #306/#307). 6. Survey + Transformations last (after #453's first extraction lands, or branch #453 from the moved layout).

Each area PR: `git mv` → namespace sync → `using` cleanup → feature dictionary → DI usings → tests moved and usings fixed → `dotnet build` + `dotnet test tests/geoDC.App.Tests --filter FullyQualifiedName~<Area>` on Windows → probe smoke of the area's pages/popups (AutomationIds unchanged) → doc/skill path updates.

### 7.2 Collisions with open issues

| Issue | Files it will touch | Interaction |
|---|---|---|
| #453 Decompose `SurveyPageViewModel` (3,356 lines, 7 partials in `ViewModels/Survey/`) | `ViewModels/Survey/SurveyPageViewModel*.cs`, new services | Move Survey last, or do #453's extractions inside `Features/Survey/` after the move; never in parallel |
| #306 Attempt pages: share session/control-point/import logic | `ViewModels/{Calibration,Verification,GnssCheck}/*PageViewModel.cs`, `Services/WorkItemCloning/*CloneWorkflow.cs`, `ViewModels/Common/*` | The proposed `Features/Attempts/` folder is where #306's shared helpers (`AttemptSessionList`, `SeriesSlotImporter`, `WorkItemCloneWorkflow<T>`) belong; create the folder in the scaffold PR so #306 can target it |
| #307 Attempt pages: shared controls (`Views/Attempts/*`, all merged) | `Views/Attempts/`, `AttemptPageAutomationIdTests` | Closed slices; only the `<attempts:` xmlns prefix and the test's file-name lookup are affected |
| #515 UiPage epic — #527 popup size classes, #522 list rows, #529 skill/design-system resync | `Popups/*Definition*.cs` (`SizeClass` declarations), templates, skills | #527 edits definitions the restructure moves; sequence #527 before the Attempts/Survey PRs or rebase it. #529 should wait for the restructure so skill paths are written once |
| #88 Shared composition root | `App.xaml.cs:217-377`, `AppServiceRegistrations.cs`, `TestAppHost.cs` | Natural follow-on: each feature folder gets `AddXxxFeature(this IServiceCollection)` registering its page, VMs, popup definitions, menus and (if chosen) its resource dictionary; `TestAppHost` then calls the same modules |
| #613 String resources by meaning | `Strings/*.yaml` | Independent of folders; but if yaml files are merged or renamed, the "strings seed the areas" rule and `XxxText` class names shift. Decide #613's file set before using it as the area list, or accept that `workflow.yaml` already models the `Attempts` area |
| #461 / demo button removal | `Startup/DemoProjectGenerator.cs`, `IDemoProjectGenerator.cs` | Remove before moving Launch, so the files are deleted rather than moved |

## Appendix A. File-to-area map (572 files)

Assignment signals, in order: folder and type name; the generated `XxxText` class the file uses (`Strings/<area>.yaml`); consumers found by grep across `src/geoDC.App` and `tests/geoDC.App.Tests`; DI module and navigation entry. "Attempts (workflow)" is the shared area seeded by `workflow.yaml`; "Transformations" is a sub-area of Survey seeded by `transformations.yaml`.

### A.0 Files not assigned with confidence

| File(s) | Assigned to | Why uncertain | Decision |
|---|---|---|---|
| `Popups/WorkflowPopupDefinitions.cs` (418 lines, 14 classes) | Survey (`:10-265`, 9 definitions) + Shore Control (`:267-418`, 5) | one file, two areas; must be split before moving | Split in #848 into `SurveyPopupDefinitions.cs` and `ShoreControlPopupDefinitions.cs`; moved to `Features/Survey/Popups/` (#854) and `Features/ShoreControl/Popups/` (#850) |
| `Popups/WorkflowPopupContracts.cs` (25 types) | Survey | all contracts are feature/sensor/group/measurement/import-points; name suggests "workflow" (= attempts) but content is Survey | Survey: renamed `SurveyPopupContracts.cs` (#848), now `Features/Survey/Popups/` (#854) |
| `ViewModels/VesselFrame/*` (6) | Survey | workspace entity VMs loaded by `VesselFrameLoadHandler`; consumed by Survey (×5), handlers, menus, `VesselViewModel` by Setup (×2) and `MainShellViewModel`. Alternative: `Workspace/Model/` | Survey: `Features/Survey/VesselFrame/` (#854) |
| `ViewModels/FieldRecords/*` (3), `Themes/Templates/Shared/FieldNoteTemplate.xaml`, `ViewModels/Setup/FieldNoteRemoval.cs` | Survey / Setup | `FieldNoteViewModel` is used by Setup and Landing (job notes) as well as Survey; field photos are Survey-only | Shared: the three viewmodels and `FieldNoteTemplate.xaml` are in `Shared/FieldRecords/`, because Setup and Landing use them too (#854). `FieldNoteRemoval` stays with Setup in `Features/Setup/` (#852) |
| `ViewModels/DataSeries/{DataSeriesHeaderViewModel,DataSeriesRowViewModel,IDataSeriesRowCellEditor,DataSeriesConsumerPresenter}.cs` | Data | dataset entity VMs used by all three attempt pages, Import and the handlers; Data owns the aggregate | Data: `Features/Data/` (#851) |
| `ViewModels/Setup/FeatureStateViewModel.cs` | Setup (kept) | used by 20 files across Survey, Calibration, Verification, Workspace, menus; the aggregate (job-scoped feature state) is loaded by `JobLoadHandler`. Could equally sit in Survey beside `FeatureViewModel` | Setup: `Features/Setup/` (#852) |
| `ViewModels/Setup/ActiveJobSwitcher.cs` | Shell | only `MainShellViewModel` and DI reference it (title-bar job menu); `SetupText` usage would argue Setup | Shell: `Shell/` (#848) |
| `Services/CalibrationTargetSelector.cs`, `CalibrationFeatureStatePicker.cs` | Attempts | named "Calibration" but used by Verification, GNSS clone workflows, Data and popups | Attempts: `Features/Attempts/` (#853) |
| `Services/WorkItemCloning/SensorCalibrationWorkflow.cs` | Calibration | started from Survey's sensor context menu (`Menus/Sensor`), creates a calibration | Calibration: `Features/Calibration/` (#853) |
| `Services/WorkItemCloning/RecalibrationWorkflow.cs` | Verification | opened from the verification menu; produces a calibration | Verification: `Features/Verification/` (#853) |
| `Popups/ThirdPartyNoticesPopup*` (3) + template | Launch | opened from the project selector **and** from Help > About; strings live in `launch.yaml` | Launch: `Features/Launch/` (#849) |
| `ViewModels/RecentProjectsMenu.cs` | Launch | File > Open recent (shell menu) backed by the recent-project service; `ShellText` ×2 | Launch: `Features/Launch/` (#849) |
| `Workspace/{DataSeriesEditLog,WorkspaceDataSeriesEditSession,AttemptDataUseWatcher}.cs` | Data | only the Data analysis popup uses them, but they are workspace services; moving them out of `Workspace/` is optional | Data: moved out of `Workspace/` to `Features/Data/` (#851) |
| `Workspace/ShoreControlSnapshotCheckpoint.cs` | Shore Control | same reasoning | Shore Control: `Features/ShoreControl/` (#850) |
| `Workspace/VesselDisplayConventions.cs` | Conventions (shared) | used by Survey, Calibration and two menus | Shared: `Shared/Conventions/` (#855) |
| `Mapping/MapHosting.cs` + `MapAssets/` | Shore Control | single consumer (`ShoreControlMapViewModel`), but `MapAssets\**` is a csproj `Content` glob served by output path (`geoDC.App.csproj:32`); keep `MapAssets/` at the root or update the glob and the virtual-host mapping together | `MapHosting` is in `Features/ShoreControl/Workbench/` (#850); `MapAssets/` stays at the project root for the csproj glob |
| `Services/{IDataExportDestinationPicker,DataExportDestinationPicker}.cs` | Shared services | generic name, single consumer (Shore Control ×4), `ShellText` strings | Shared: `Shared/Files/` (#855) |
| `Services/SystemActorAttestation.cs` | Workspace infrastructure | no consumer found in `src/geoDC.App` (possibly used via `geoDC.Application` interfaces or tests only) | Workspace: `Workspace/` (#855) |
| `Startup/{IThemePreferenceService,ThemePreferenceService,PreferenceRuntimeBinder}.cs` | Settings | theme preference is also driven by the View menu (shell) | Settings: `Features/Settings/` (#849) |
| `Themes/Templates/Shared/CheckedCaptionStyle.xaml` | Attempts | consumed by `DatasetSlot` and `CalibrationShellPage` only | Attempts: `Features/Attempts/` (#853) |
| `Startup/UndoCommandBootstrap.cs` | Shell | wires Ctrl+Z/Y and the Edit menu; also a bootstrap | Shell: `Shell/` (#848) |

The Decision column records where each file went in the restructure (#804). Four other placements differ from Appendix A:

- `ViewModels/Setup/SignConventionDirectionMapping.cs` is in `Shared/Conventions/`: Conventions, Calibration, Survey, Import, Landing and Setup all use it (#852, #855).
- `ViewModels/Common/DisplayPreferenceFormats.cs` is in `Shared/`: Data, Conventions and the VRF popups use its extension members as well as the attempt pages (#853).
- `MainWindow.xaml(.cs)` stays at the project root, as in the §4.1 tree, rather than in `Shell/` (#848).
- The Shore Control sub-views that both the page and the Register window host are in `Features/ShoreControl/Workbench/`, not `Register/` (#850).

### A.1 Counts and per-area tables

| Area / bucket | Files |
|---|---:|
| Landing | 8 |
| Setup | 39 |
| Survey | 82 |
| Transformations | 17 |
| Calibration | 24 |
| Verification | 23 |
| GNSS | 19 |
| Attempts (workflow) | 38 |
| Shore Control | 64 |
| Data | 20 |
| Import | 19 |
| Reports | 6 |
| Settings | 13 |
| Launch | 33 |
| Conventions (shared) | 11 |
| References (shared) | 2 |
| Shell | 51 |
| Menus (context-menu infrastructure) | 4 |
| Messaging (job-content navigation) | 6 |
| Startup (bootstrap) | 15 |
| Workspace infrastructure | 41 |
| Shared services | 31 |
| Theming (aggregator) | 5 |
| Survey + Shore Control (split) | 1 |
| **Total** | **572** |


### Landing (8 files)

| Current folder | File | Note |
|---|---|---|
| ViewModels/Setup | `ProjectLandingJobFacts.cs` |  |
| ViewModels/Setup | `ProjectLandingJobRowViewModel.cs` |  |
| ViewModels/Setup | `ProjectLandingJobStageBuilder.cs` |  |
| ViewModels/Setup | `ProjectLandingPageViewModel.cs` |  |
| ViewModels/Setup | `ProjectLandingStageViewModel.cs` |  |
| ViewModels/Setup | `ProjectResourceCounts.cs` | only ProjectLandingPageViewModel uses it |
| Views/Shell | `ProjectLandingPage.xaml` |  |
| Views/Shell | `ProjectLandingPage.xaml.cs` |  |

### Setup (39 files)

| Current folder | File | Note |
|---|---|---|
| Menus/Equipment | `EquipmentContextMenuViewModel.cs` |  |
| Menus/Equipment | `EquipmentMenuTemplate.xaml` |  |
| Menus/Job | `JobContextMenuViewModel.cs` |  |
| Menus/Job | `JobMenuTemplate.xaml` |  |
| Menus/Personnel | `PersonnelContextMenuViewModel.cs` |  |
| Menus/Personnel | `PersonnelMenuTemplate.xaml` |  |
| Messaging | `JobActionMessage.cs` |  |
| Messaging | `JobEquipmentActionMessage.cs` |  |
| Messaging | `JobPersonnelActionMessage.cs` |  |
| Themes/Templates/Setup | `JobAssignmentTemplates.xaml` |  |
| Themes/Templates/Setup | `JobEquipmentEditorPopupTemplate.xaml` |  |
| Themes/Templates/Setup | `JobPersonnelEditorPopupTemplate.xaml` |  |
| Themes/Templates/Setup | `ProjectMetadataEditorPopupTemplate.xaml` |  |
| ViewModels/Setup | `FeatureStateViewModel.cs` |  |
| ViewModels/Setup | `FieldNoteRemoval.cs` | used by Setup and Landing pages |
| ViewModels/Setup | `JobCompletionStatusDisplay.cs` |  |
| ViewModels/Setup | `JobEquipmentAssignmentViewModel.cs` |  |
| ViewModels/Setup | `JobEquipmentEditorPopupDefinition.cs` |  |
| ViewModels/Setup | `JobEquipmentEditorPopupViewModel.cs` |  |
| ViewModels/Setup | `JobEquipmentEditorRequest.cs` |  |
| ViewModels/Setup | `JobEquipmentEditorValues.cs` |  |
| ViewModels/Setup | `JobPersonnelAssignmentViewModel.cs` |  |
| ViewModels/Setup | `JobPersonnelEditorPopupDefinition.cs` |  |
| ViewModels/Setup | `JobPersonnelEditorPopupViewModel.cs` |  |
| ViewModels/Setup | `JobPersonnelEditorRequest.cs` |  |
| ViewModels/Setup | `JobPersonnelEditorValues.cs` |  |
| ViewModels/Setup | `JobSetupReadiness.cs` | used by Setup and Landing |
| ViewModels/Setup | `JobViewModel.cs` |  |
| ViewModels/Setup | `ProjectMetadataEditorPopupDefinition.cs` |  |
| ViewModels/Setup | `ProjectMetadataEditorPopupViewModel.cs` |  |
| ViewModels/Setup | `ProjectMetadataEditorRequest.cs` |  |
| ViewModels/Setup | `ProjectMetadataEditorValues.cs` |  |
| ViewModels/Setup | `ProjectMetadataViewModel.cs` |  |
| ViewModels/Setup | `SetupPageViewModel.cs` |  |
| ViewModels/Setup | `SetupPopupTitles.cs` |  |
| ViewModels/Setup | `SetupPrimaryAction.cs` |  |
| ViewModels/Setup | `SignConventionDirectionMapping.cs` |  |
| Views/Shell | `SetupPage.xaml` |  |
| Views/Shell | `SetupPage.xaml.cs` |  |

### Survey (82 files)

| Current folder | File | Note |
|---|---|---|
| FieldPhotos | `FieldPhotoFiles.cs` | import pipeline used only by Survey |
| FieldPhotos | `FieldPhotoImageProcessor.cs` | import pipeline used only by Survey |
| FieldPhotos | `FieldPhotoImportWorkflow.cs` | import pipeline used only by Survey |
| FieldPhotos | `FieldPhotoImporter.cs` | import pipeline used only by Survey |
| FieldPhotos | `IFieldPhotoSourcePicker.cs` | import pipeline used only by Survey |
| Menus/FeatureGroup | `FeatureGroupContextMenuViewModel.cs` |  |
| Menus/FeatureGroup | `FeatureGroupMenuTemplate.xaml` |  |
| Menus/FeatureState | `FeatureStateContextMenuViewModel.cs` |  |
| Menus/FeatureState | `FeatureStateMenuTemplate.xaml` |  |
| Menus/InstrumentInstallation | `InstrumentInstallationContextMenuViewModel.cs` |  |
| Menus/InstrumentInstallation | `InstrumentInstallationMenuTemplate.xaml` |  |
| Menus/Sensor | `SensorContextMenuViewModel.cs` |  |
| Menus/Sensor | `SensorMenuTemplate.xaml` |  |
| Menus/SurveyFeature | `SurveyFeatureContextMenuViewModel.cs` |  |
| Menus/SurveyFeature | `SurveyFeatureMenuTemplate.xaml` |  |
| Messaging | `FeatureGroupActionMessage.cs` |  |
| Messaging | `FeatureGroupMembershipChangedMessage.cs` |  |
| Messaging | `SurveyFeatureActionMessage.cs` |  |
| Messaging | `SurveyInstallationActionMessage.cs` |  |
| Messaging | `SurveySensorActionMessage.cs` |  |
| Popups | `AddMultipleFeaturesPopupViewModel.cs` |  |
| Popups | `AttachInstrumentPopupViewModel.cs` |  |
| Popups | `CreateFeatureGroupPopupViewModel.cs` |  |
| Popups | `CreateFeaturePopupViewModel.cs` |  |
| Popups | `DeviceProfileOptions.cs` |  |
| Popups | `EditFeaturePopupViewModel.cs` |  |
| Popups | `FeatureMeasurementPopupViewModel.cs` |  |
| Popups | `FieldPhotoPreviewPopupContracts.cs` |  |
| Popups | `FieldPhotoPreviewPopupDefinition.cs` |  |
| Popups | `FieldPhotoPreviewPopupViewModel.cs` |  |
| Popups | `ImportGroupRuleCandidateViewModel.cs` |  |
| Popups | `ImportSurveyPointsPopupViewModel.cs` |  |
| Popups | `PositionEntryViewModel.cs` | shared by the feature/sensor/measurement popups |
| Popups | `SensorPopupViewModels.cs` |  |
| Popups | `SensorPropertiesEditing.cs` |  |
| Popups | `SurveyPointImportCodeRules.cs` |  |
| Popups | `VesselFramePopupTypes.cs` |  |
| Popups | `WorkflowPopupContracts.cs` | all 25 types are feature/sensor/group/measurement/import-points contracts |
| Themes/Templates/Popups | `AddMultipleFeaturesPopupTemplate.xaml` |  |
| Themes/Templates/Popups | `AttachInstrumentPopupTemplate.xaml` |  |
| Themes/Templates/Popups | `CreateFeatureGroupPopupTemplate.xaml` |  |
| Themes/Templates/Popups | `CreateFeaturePopupTemplate.xaml` |  |
| Themes/Templates/Popups | `EditFeaturePopupTemplate.xaml` |  |
| Themes/Templates/Popups | `FeatureMeasurementPopupTemplate.xaml` |  |
| Themes/Templates/Popups | `FieldPhotoPreviewPopupTemplate.xaml` |  |
| Themes/Templates/Popups | `ImportSurveyPointsPopupTemplate.xaml` |  |
| Themes/Templates/Popups | `PositionEntryTemplate.xaml` |  |
| Themes/Templates/Popups | `SensorPopupTemplates.xaml` |  |
| Themes/Templates/Shared | `FieldNoteTemplate.xaml` | FieldNoteViewModel; also used by Setup/Landing notes |
| ViewModels/FieldRecords | `FieldNoteViewModel.cs` | FieldNoteViewModel also used by Setup/Landing (job notes) |
| ViewModels/FieldRecords | `FieldPhotoViewModel.cs` | FieldNoteViewModel also used by Setup/Landing (job notes) |
| ViewModels/FieldRecords | `FieldRecordSetViewModel.cs` | FieldNoteViewModel also used by Setup/Landing (job notes) |
| ViewModels/Survey | `FeatureStateSourceText.cs` |  |
| ViewModels/Survey | `InstrumentInstallationViewModel.cs` |  |
| ViewModels/Survey | `InstrumentViewModel.cs` |  |
| ViewModels/Survey | `ReviewGroupSummary.cs` |  |
| ViewModels/Survey | `SensorLocationRow.cs` |  |
| ViewModels/Survey | `SurveyDeltaViewModel.cs` |  |
| ViewModels/Survey | `SurveyFeatureGroupRow.cs` |  |
| ViewModels/Survey | `SurveyFeatureGroupViewModel.cs` |  |
| ViewModels/Survey | `SurveyFeatureItemViewModel.cs` |  |
| ViewModels/Survey | `SurveyLifecycle.cs` |  |
| ViewModels/Survey | `SurveyPageViewModel.Frame.cs` |  |
| ViewModels/Survey | `SurveyPageViewModel.Groups.cs` |  |
| ViewModels/Survey | `SurveyPageViewModel.Header.cs` |  |
| ViewModels/Survey | `SurveyPageViewModel.Inspector.cs` |  |
| ViewModels/Survey | `SurveyPageViewModel.Points.cs` |  |
| ViewModels/Survey | `SurveyPageViewModel.Scene.cs` |  |
| ViewModels/Survey | `SurveyPageViewModel.cs` |  |
| ViewModels/Survey | `SurveyPointGrouping.cs` |  |
| ViewModels/Survey | `SurveyQueueGroupViewModel.cs` |  |
| ViewModels/Survey | `SurveySensorGroupViewModel.cs` |  |
| ViewModels/Survey | `VesselDirectionMapping.cs` |  |
| ViewModels/Survey | `VrfGroupToggleViewModel.cs` |  |
| ViewModels/VesselFrame | `FeatureGroupMembershipViewModel.cs` | workspace entity VMs (Feature, FeatureGroup, Vessel, ReferenceFrame) owned by the VRF aggregate; consumed by Workspace handlers, menus, Setup |
| ViewModels/VesselFrame | `FeatureGroupViewModel.cs` | workspace entity VMs (Feature, FeatureGroup, Vessel, ReferenceFrame) owned by the VRF aggregate; consumed by Workspace handlers, menus, Setup |
| ViewModels/VesselFrame | `FeatureViewModel.cs` | workspace entity VMs (Feature, FeatureGroup, Vessel, ReferenceFrame) owned by the VRF aggregate; consumed by Workspace handlers, menus, Setup |
| ViewModels/VesselFrame | `ReferenceFrameViewModel.cs` | workspace entity VMs (Feature, FeatureGroup, Vessel, ReferenceFrame) owned by the VRF aggregate; consumed by Workspace handlers, menus, Setup |
| ViewModels/VesselFrame | `VesselFrameDisplayText.cs` | workspace entity VMs (Feature, FeatureGroup, Vessel, ReferenceFrame) owned by the VRF aggregate; consumed by Workspace handlers, menus, Setup |
| ViewModels/VesselFrame | `VesselViewModel.cs` | workspace entity VMs (Feature, FeatureGroup, Vessel, ReferenceFrame) owned by the VRF aggregate; consumed by Workspace handlers, menus, Setup |
| Views/Shell | `SurveyPage.xaml` |  |
| Views/Shell | `SurveyPage.xaml.cs` |  |

### Transformations (17 files)

| Current folder | File | Note |
|---|---|---|
| Menus/VrfTransformation | `VrfTransformationContextMenuViewModel.cs` |  |
| Menus/VrfTransformation | `VrfTransformationMenuTemplate.xaml` |  |
| Messaging | `VrfTransformationActionMessage.cs` |  |
| Popups/VrfTransformations | `AlignVrfBaselinesPopupViewModel.cs` |  |
| Popups/VrfTransformations | `EnterRigidTransformPopupViewModel.cs` |  |
| Popups/VrfTransformations | `IVrfTransformationPreviewSink.cs` |  |
| Popups/VrfTransformations | `ReverseVrfTransformationPopupViewModel.cs` |  |
| Popups/VrfTransformations | `TranslateVrfPopupViewModel.cs` |  |
| Popups/VrfTransformations | `VrfTransformationDetailsPopupViewModel.cs` |  |
| Popups/VrfTransformations | `VrfTransformationDisplay.cs` |  |
| Popups/VrfTransformations | `VrfTransformationIssueText.cs` |  |
| Popups/VrfTransformations | `VrfTransformationPopupContracts.cs` |  |
| Popups/VrfTransformations | `VrfTransformationPopupDefinitions.cs` |  |
| Popups/VrfTransformations | `VrfTransformationReviewViewModel.cs` |  |
| Popups/VrfTransformations | `VrfTransformationToolIssues.cs` |  |
| Themes/Templates/Popups | `VrfTransformationPopupTemplates.xaml` |  |
| ViewModels/Survey | `VrfTransformationsPanelViewModel.cs` | the panel on the Survey page that opens the tool popups |

### Calibration (24 files)

| Current folder | File | Note |
|---|---|---|
| Menus/Calibration | `CalibrationContextMenuViewModel.cs` |  |
| Menus/Calibration | `CalibrationMenuTemplate.xaml` |  |
| Messaging | `CalibrationAttemptActionMessage.cs` |  |
| Services | `CalibrationBaselinePreviewBuilder.cs` |  |
| Services/WorkItemCloning | `CalibrationAttemptFactory.cs` |  |
| Services/WorkItemCloning | `CalibrationCloneWorkflow.cs` |  |
| Services/WorkItemCloning | `SensorCalibrationWorkflow.cs` | started from Survey's sensor menu; creates a calibration |
| ViewModels/Calibration | `CalibrationAttemptGrouping.cs` |  |
| ViewModels/Calibration | `CalibrationAttemptViewModel.cs` |  |
| ViewModels/Calibration | `CalibrationLifecycle.cs` |  |
| ViewModels/Calibration | `CalibrationPageViewModel.Angle.cs` |  |
| ViewModels/Calibration | `CalibrationPageViewModel.GridConvergence.cs` |  |
| ViewModels/Calibration | `CalibrationPageViewModel.Lifecycle.cs` |  |
| ViewModels/Calibration | `CalibrationPageViewModel.PitchSource.cs` |  |
| ViewModels/Calibration | `CalibrationPageViewModel.cs` |  |
| ViewModels/Calibration | `CalibrationSampleViewModel.cs` |  |
| ViewModels/Calibration | `CalibrationSupersession.cs` |  |
| ViewModels/Calibration/Diagnostics | `CalibrationDiagnosticContext.cs` |  |
| ViewModels/Calibration/Diagnostics | `MissingInputsRule.cs` |  |
| ViewModels/Calibration/Diagnostics | `PrismBaselineRule.cs` |  |
| ViewModels/Calibration/Diagnostics | `PrismPairingReversedRule.cs` |  |
| ViewModels/Calibration/Diagnostics | `RollNotConvertedRule.cs` |  |
| Views/Shell | `CalibrationShellPage.xaml` |  |
| Views/Shell | `CalibrationShellPage.xaml.cs` |  |

### Verification (23 files)

| Current folder | File | Note |
|---|---|---|
| Menus/Verification | `VerificationContextMenuViewModel.cs` |  |
| Menus/Verification | `VerificationMenuTemplate.xaml` |  |
| Messaging | `CreateVerificationForCalibrationMessage.cs` |  |
| Messaging | `VerificationAttemptActionMessage.cs` |  |
| Services/WorkItemCloning | `RecalibrationWorkflow.cs` | opened from the verification menu/page |
| Services/WorkItemCloning | `VerificationCloneWorkflow.cs` |  |
| ViewModels/Verification | `VerificationAttemptFilter.cs` |  |
| ViewModels/Verification | `VerificationAttemptViewModel.cs` |  |
| ViewModels/Verification | `VerificationLifecycle.cs` |  |
| ViewModels/Verification | `VerificationPageViewModel.Chart.cs` |  |
| ViewModels/Verification | `VerificationPageViewModel.Lifecycle.cs` |  |
| ViewModels/Verification | `VerificationPageViewModel.PitchSource.cs` |  |
| ViewModels/Verification | `VerificationPageViewModel.Steps.cs` |  |
| ViewModels/Verification | `VerificationPageViewModel.cs` |  |
| ViewModels/Verification | `VerificationPresentationViewModel.cs` |  |
| ViewModels/Verification | `VerificationSampleViewModel.cs` |  |
| ViewModels/Verification/Diagnostics | `NearLimitRule.cs` |  |
| ViewModels/Verification/Diagnostics | `PrismBaselineRule.cs` |  |
| ViewModels/Verification/Diagnostics | `ResidualDriftRule.cs` |  |
| ViewModels/Verification/Diagnostics | `RollNotConvertedRule.cs` |  |
| ViewModels/Verification/Diagnostics | `VerificationDiagnosticContext.cs` |  |
| Views/Shell | `VerificationShellPage.xaml` |  |
| Views/Shell | `VerificationShellPage.xaml.cs` |  |

### GNSS (19 files)

| Current folder | File | Note |
|---|---|---|
| Menus/GnssCheck | `GnssCheckContextMenuViewModel.cs` |  |
| Menus/GnssCheck | `GnssCheckMenuTemplate.xaml` |  |
| Messaging | `GnssCheckAttemptActionMessage.cs` |  |
| Services/WorkItemCloning | `GnssCheckCloneWorkflow.cs` |  |
| ViewModels/GnssCheck | `GnssCheckAttemptFilter.cs` |  |
| ViewModels/GnssCheck | `GnssCheckAttemptViewModel.cs` |  |
| ViewModels/GnssCheck | `GnssCheckLifecycle.cs` |  |
| ViewModels/GnssCheck | `GnssCheckPageViewModel.DataInput.cs` |  |
| ViewModels/GnssCheck | `GnssCheckPageViewModel.Lifecycle.cs` |  |
| ViewModels/GnssCheck | `GnssCheckPageViewModel.cs` |  |
| ViewModels/GnssCheck | `GnssMatchedPointViewModel.cs` |  |
| ViewModels/GnssCheck | `GnssPresentationViewModel.cs` |  |
| ViewModels/GnssCheck/Diagnostics | `EnsembleFrameRule.cs` |  |
| ViewModels/GnssCheck/Diagnostics | `GnssDiagnosticContext.cs` |  |
| ViewModels/GnssCheck/Diagnostics | `HeightOffsetRule.cs` |  |
| ViewModels/GnssCheck/Diagnostics | `HeightsUntransformedRule.cs` |  |
| ViewModels/GnssCheck/Diagnostics | `UnpairedFixesRule.cs` |  |
| Views/Shell | `GnssCheckShellPage.xaml` |  |
| Views/Shell | `GnssCheckShellPage.xaml.cs` |  |

### Attempts (workflow) (38 files)

| Current folder | File | Note |
|---|---|---|
| Menus | `WorkItemContextMenuViewModel.cs` |  |
| Popups | `WorkItemClonePopupContracts.cs` |  |
| Popups | `WorkItemClonePopupDefinition.cs` |  |
| Services | `CalibrationFeatureStatePicker.cs` | used by Calibration, Verification, Data and popups |
| Services | `CalibrationTargetSelector.cs` | used by all clone workflows, Calibration and Data |
| Services/WorkItemCloning | `WorkItemCloneAvailability.cs` |  |
| Services/WorkItemCloning | `WorkItemCloneSelectionStore.cs` |  |
| Services/WorkItemCloning | `WorkItemInputValidity.cs` |  |
| Themes/Templates/Popups | `WorkItemClonePopupTemplate.xaml` |  |
| Themes/Templates/Shared | `CheckedCaptionStyle.xaml` | used by DatasetSlot and CalibrationShellPage only |
| Themes/Templates/Shared | `SeriesMatchSummaryTemplate.xaml` | SeriesMatchSummary used by the three attempt pages |
| ViewModels/Common | `AnglePreviewStatistics.cs` |  |
| ViewModels/Common | `AttemptComputation.cs` |  |
| ViewModels/Common | `AttemptInputRejectedException.cs` |  |
| ViewModels/Common | `AttemptPrisms.cs` |  |
| ViewModels/Common | `ChartEnumOptions.cs` |  |
| ViewModels/Common | `CoChartOptionsViewModel.cs` |  |
| ViewModels/Common | `ControlPointSampleViewModel.cs` |  |
| ViewModels/Common | `DatasetSlotChoices.cs` |  |
| ViewModels/Common | `DisplayPreferenceFormats.cs` |  |
| ViewModels/Common | `IEditLock.cs` |  |
| ViewModels/Common | `PrismBaselineFindings.cs` |  |
| ViewModels/Common | `ResultWindowFormat.cs` |  |
| ViewModels/Common | `SeriesMatchSummary.cs` |  |
| ViewModels/Common | `SeriesPairingPreview.cs` |  |
| ViewModels/Common | `WorkItemClonePopupViewModel.cs` |  |
| Views/Attempts | `AttemptChartSection.xaml` |  |
| Views/Attempts | `AttemptChartSection.xaml.cs` |  |
| Views/Attempts | `AttemptLifecycleCommandBar.xaml` |  |
| Views/Attempts | `AttemptLifecycleCommandBar.xaml.cs` |  |
| Views/Attempts | `AttemptListPane.xaml` |  |
| Views/Attempts | `AttemptListPane.xaml.cs` |  |
| Views/Attempts | `AttemptResultPanel.xaml` |  |
| Views/Attempts | `AttemptResultPanel.xaml.cs` |  |
| Views/Attempts | `AttemptResultStatus.xaml` |  |
| Views/Attempts | `AttemptResultStatus.xaml.cs` |  |
| Views/Attempts | `DatasetSlot.xaml` |  |
| Views/Attempts | `DatasetSlot.xaml.cs` |  |

### Shore Control (64 files)

| Current folder | File | Note |
|---|---|---|
| Mapping | `MapHosting.cs` | Leaflet host used only by ShoreControlMapViewModel; MapAssets/ go with it |
| Messaging | `ShoreControlChangedMessage.cs` |  |
| Popups | `ImportShoreControlPointsPopupViewModel.cs` |  |
| Popups | `ShoreControlChooserPopup.cs` |  |
| Popups | `ShoreControlFormPopup.cs` |  |
| Popups | `ShoreControlImportPopupContracts.cs` |  |
| Popups | `ShoreControlPointEditorPopup.cs` |  |
| Popups | `ShoreControlRegisterPopupContracts.cs` |  |
| Themes/Templates/Popups | `ImportShoreControlPointsPopupTemplate.xaml` |  |
| Themes/Templates/Popups | `ShoreControlFormPopupTemplates.xaml` |  |
| Themes/Templates/Popups | `ShoreControlPointEditorPopupTemplate.xaml` |  |
| Themes/Templates/Popups | `ShoreControlRegisterPopupTemplate.xaml` |  |
| ViewModels/ShoreControl | `IShoreControlForm.cs` |  |
| ViewModels/ShoreControl | `IShoreControlWorkbenchHost.cs` |  |
| ViewModels/ShoreControl | `ShoreControlActor.cs` |  |
| ViewModels/ShoreControl | `ShoreControlCanvasView.cs` |  |
| ViewModels/ShoreControl | `ShoreControlChooserChoice.cs` |  |
| ViewModels/ShoreControl | `ShoreControlCoordinateDelta.cs` |  |
| ViewModels/ShoreControl | `ShoreControlCoordinateLayout.cs` |  |
| ViewModels/ShoreControl | `ShoreControlCrsLookup.cs` |  |
| ViewModels/ShoreControl | `ShoreControlDecommissionForm.cs` |  |
| ViewModels/ShoreControl | `ShoreControlDetailsForm.cs` |  |
| ViewModels/ShoreControl | `ShoreControlJobCopies.cs` |  |
| ViewModels/ShoreControl | `ShoreControlJobStatus.cs` |  |
| ViewModels/ShoreControl | `ShoreControlMapViewModel.cs` |  |
| ViewModels/ShoreControl | `ShoreControlPageViewModel.cs` |  |
| ViewModels/ShoreControl | `ShoreControlPointDescriptorForm.cs` |  |
| ViewModels/ShoreControl | `ShoreControlPointEditorViewModel.cs` |  |
| ViewModels/ShoreControl | `ShoreControlPointRowViewModel.cs` |  |
| ViewModels/ShoreControl | `ShoreControlPointSync.cs` |  |
| ViewModels/ShoreControl | `ShoreControlPrimaryAction.cs` |  |
| ViewModels/ShoreControl | `ShoreControlRegisterSession.cs` |  |
| ViewModels/ShoreControl | `ShoreControlRegisterViewModel.cs` |  |
| ViewModels/ShoreControl | `ShoreControlRevisionRowViewModel.cs` |  |
| ViewModels/ShoreControl | `ShoreControlSetDetailsViewModel.cs` |  |
| ViewModels/ShoreControl | `ShoreControlSetEntryViewModel.cs` |  |
| ViewModels/ShoreControl | `ShoreControlSnapshotRowViewModel.cs` |  |
| ViewModels/ShoreControl | `ShoreControlStationTypes.cs` |  |
| ViewModels/ShoreControl | `ShoreControlSyncRunner.cs` |  |
| ViewModels/ShoreControl | `ShoreControlTargetColumn.cs` |  |
| ViewModels/ShoreControl | `ShoreControlTransformCoordinateForm.cs` |  |
| ViewModels/ShoreControl | `ShoreControlTransformRowViewModel.cs` |  |
| ViewModels/ShoreControl | `ShoreControlTransformTable.cs` |  |
| ViewModels/ShoreControl | `ShoreControlTransformViewModel.cs` |  |
| ViewModels/ShoreControl | `ShoreControlWorkbenchViewModel.cs` |  |
| Views/Shell | `ShoreControlPage.xaml` |  |
| Views/Shell | `ShoreControlPage.xaml.cs` |  |
| Views/ShoreControl | `ShoreControlJobCopyView.xaml` |  |
| Views/ShoreControl | `ShoreControlJobCopyView.xaml.cs` |  |
| Views/ShoreControl | `ShoreControlMapView.xaml` |  |
| Views/ShoreControl | `ShoreControlMapView.xaml.cs` |  |
| Views/ShoreControl | `ShoreControlPointInspectorView.xaml` |  |
| Views/ShoreControl | `ShoreControlPointInspectorView.xaml.cs` |  |
| Views/ShoreControl | `ShoreControlPointNavigatorView.xaml` |  |
| Views/ShoreControl | `ShoreControlPointNavigatorView.xaml.cs` |  |
| Views/ShoreControl | `ShoreControlPointTableView.xaml` |  |
| Views/ShoreControl | `ShoreControlPointTableView.xaml.cs` |  |
| Views/ShoreControl | `ShoreControlRegisterView.xaml` |  |
| Views/ShoreControl | `ShoreControlRegisterView.xaml.cs` |  |
| Views/ShoreControl | `ShoreControlSetSummaryView.xaml` |  |
| Views/ShoreControl | `ShoreControlSetSummaryView.xaml.cs` |  |
| Views/ShoreControl | `ShoreControlShowInView.xaml` |  |
| Views/ShoreControl | `ShoreControlShowInView.xaml.cs` |  |
| Workspace | `ShoreControlSnapshotCheckpoint.cs` | only ShoreControl viewmodels use it |

### Data (20 files)

| Current folder | File | Note |
|---|---|---|
| Popups | `DataAnalysisPopupContracts.cs` |  |
| Popups | `DataAnalysisPopupDefinition.cs` |  |
| Popups | `SeriesMatchPopupContracts.cs` |  |
| Popups | `SeriesMatchPopupDefinition.cs` |  |
| Themes/Templates/Popups | `DataAnalysisPopupTemplate.xaml` |  |
| Themes/Templates/Popups | `SeriesMatchPopupTemplate.xaml` |  |
| ViewModels/Data | `DataAnalysisPopupViewModel.Navigation.cs` |  |
| ViewModels/Data | `DataAnalysisPopupViewModel.Quality.cs` |  |
| ViewModels/Data | `DataAnalysisPopupViewModel.TimeShift.cs` |  |
| ViewModels/Data | `DataAnalysisPopupViewModel.Tools.cs` |  |
| ViewModels/Data | `DataAnalysisPopupViewModel.cs` |  |
| ViewModels/DataSeries | `DataSeriesConsumerPresenter.cs` | DataSeriesHeader/Row VMs are consumed by every attempt page and the handlers |
| ViewModels/DataSeries | `DataSeriesHeaderViewModel.cs` | DataSeriesHeader/Row VMs are consumed by every attempt page and the handlers |
| ViewModels/DataSeries | `DataSeriesRowViewModel.cs` | DataSeriesHeader/Row VMs are consumed by every attempt page and the handlers |
| ViewModels/DataSeries | `IDataSeriesRowCellEditor.cs` | DataSeriesHeader/Row VMs are consumed by every attempt page and the handlers |
| ViewModels/DataSeries | `SeriesMatchEditViewModel.cs` | DataSeriesHeader/Row VMs are consumed by every attempt page and the handlers |
| ViewModels/DataSeries | `SeriesMotionAlignmentViewModel.cs` | DataSeriesHeader/Row VMs are consumed by every attempt page and the handlers |
| Workspace | `AttemptDataUseWatcher.cs` | only DataAnalysisPopupViewModel uses it |
| Workspace | `DataSeriesEditLog.cs` | edit log for the Data analysis popup; could stay in Workspace |
| Workspace | `WorkspaceDataSeriesEditSession.cs` | could stay in Workspace |

### Import (19 files)

| Current folder | File | Note |
|---|---|---|
| Popups | `DataImportWizardPopupContracts.cs` |  |
| Popups | `DataImportWizardPopupDefinition.cs` |  |
| Popups | `TimestampFormatEntryPopupContracts.cs` |  |
| Popups | `TimestampFormatEntryPopupDefinition.cs` |  |
| Popups | `TimestampFormatEntryPopupViewModel.cs` |  |
| Services | `DataSeriesImportWorkflow.cs` |  |
| Services | `IRecentImportFilesService.cs` |  |
| Services | `ImportColumnAnalyzer.cs` |  |
| Services | `RecentImportFilesService.cs` |  |
| Themes/Templates/Popups | `SequentialDataImportWizardPopupTemplate.xaml` |  |
| Themes/Templates/Popups | `TimestampFormatEntryPopupTemplate.xaml` |  |
| Themes/Templates/Popups | `WizardPopupSharedStyles.xaml` | merged by the wizard template with a relative Source |
| ViewModels/Common | `WizardFlowViewModelBase.cs` | only consumer is DataImportWizardViewModel |
| ViewModels/Data | `DataImportWizardViewModel.cs` |  |
| ViewModels/Data | `ImportAngleColumnViewModel.cs` |  |
| ViewModels/Data | `ImportControlPointColumnsViewModel.cs` |  |
| ViewModels/Data | `ImportConventionProfile.cs` |  |
| ViewModels/Data | `ImportFormatSettingsViewModels.cs` |  |
| ViewModels/Data | `ImportRunnerViewModel.cs` |  |

### Reports (6 files)

| Current folder | File | Note |
|---|---|---|
| Services | `IReportDestinationPicker.cs` |  |
| Services | `ReportDestinationPicker.cs` |  |
| ViewModels/Reports | `ReportContentRow.cs` |  |
| ViewModels/Reports | `ReportsPageViewModel.cs` |  |
| Views/Shell | `ReportsShellPage.xaml` |  |
| Views/Shell | `ReportsShellPage.xaml.cs` |  |

### Settings (13 files)

| Current folder | File | Note |
|---|---|---|
| Startup | `ISettingsPresenter.cs` |  |
| Startup | `IThemePreferenceService.cs` | theme preference; also read by the shell View menu |
| Startup | `PreferenceRuntimeBinder.cs` |  |
| Startup | `SettingsDialogPopupDefinition.cs` |  |
| Startup | `SettingsDialogRequest.cs` |  |
| Startup | `SettingsPresenter.cs` |  |
| Startup | `ThemePreferenceService.cs` |  |
| Themes/Templates/Popups | `SettingsDialogTemplate.xaml` | SettingsSectionKeyTests reads it by absolute path |
| ViewModels/Settings | `ISettingField.cs` |  |
| ViewModels/Settings | `SettingField.cs` |  |
| ViewModels/Settings | `SettingOption.cs` |  |
| ViewModels/Settings | `SettingsDialogViewModel.cs` |  |
| ViewModels/Settings | `SettingsSections.cs` |  |

### Launch (33 files)

| Current folder | File | Note |
|---|---|---|
| Menus/RecentProject | `RecentProjectContextMenuViewModel.cs` |  |
| Menus/RecentProject | `RecentProjectMenuTemplate.xaml` |  |
| Messaging | `OpenRecentProjectRequestMessage.cs` |  |
| Messaging | `RecentProjectRemovedMessage.cs` |  |
| Popups | `ThirdPartyNoticesPopupContracts.cs` | LaunchText; opened from the selector and from About |
| Popups | `ThirdPartyNoticesPopupDefinition.cs` |  |
| Popups | `ThirdPartyNoticesPopupViewModel.cs` |  |
| Services | `IProjectFileWorkflowService.cs` |  |
| Services | `ProjectFileWorkflowService.cs` | LaunchText x26 |
| Services | `ProjectPackageLockCoordinator.cs` | LaunchText x19 |
| Startup | `DemoProjectGenerator.cs` | project selector / new project / session |
| Startup | `IDemoProjectGenerator.cs` | project selector / new project / session |
| Startup | `INewProjectDialogService.cs` | project selector / new project / session |
| Startup | `IProjectFileDialogService.cs` | project selector / new project / session |
| Startup | `IProjectSelector.cs` | project selector / new project / session |
| Startup | `IProjectSessionService.cs` | project selector / new project / session |
| Startup | `IRecentProjectService.cs` | project selector / new project / session |
| Startup | `NewProjectDialogService.cs` | project selector / new project / session |
| Startup | `NewProjectPopupDefinition.cs` | project selector / new project / session |
| Startup | `NewProjectViewModel.cs` | project selector / new project / session |
| Startup | `ProjectFileDialogService.cs` | project selector / new project / session |
| Startup | `ProjectSelector.cs` | project selector / new project / session |
| Startup | `ProjectSelectorModels.cs` | project selector / new project / session |
| Startup | `ProjectSelectorPopupDefinition.cs` | project selector / new project / session |
| Startup | `ProjectSelectorViewModel.cs` | project selector / new project / session |
| Startup | `ProjectSessionService.cs` | project selector / new project / session |
| Startup | `RecentProjectSummaryReader.cs` | project selector / new project / session |
| Startup | `UserSettingsRecentProjectService.cs` | project selector / new project / session |
| Themes/Templates/Popups | `ThirdPartyNoticesPopupTemplate.xaml` |  |
| Themes/Templates/Startup | `NewProjectTemplate.xaml` |  |
| Themes/Templates/Startup | `ProjectSelectorTemplate.xaml` |  |
| Themes/Templates/Startup | `RecentProjectEntryTemplate.xaml` |  |
| ViewModels | `RecentProjectsMenu.cs` | File > Open recent; shared by selector and shell |

### Conventions (shared) (11 files)

| Current folder | File | Note |
|---|---|---|
| Services/Conventions | `ConventionDeletedMessage.cs` |  |
| Services/Conventions | `ConventionLibraryCatalogue.cs` |  |
| Themes/Templates/Shared | `ConventionLibraryChoiceTemplate.xaml` |  |
| ViewModels/Conventions | `AxisConventionToken.cs` |  |
| ViewModels/Conventions | `CoSemanticsPresenter.cs` |  |
| ViewModels/Conventions | `ConventionLibraryChoiceViewModel.cs` |  |
| ViewModels/Conventions | `ConventionLibraryChoices.cs` |  |
| ViewModels/Conventions | `PitchSeriesDefaults.cs` |  |
| ViewModels/Conventions | `PositionConventionToken.cs` |  |
| ViewModels/Conventions | `RollConversionFinding.cs` |  |
| Workspace | `VesselDisplayConventions.cs` | used by Survey, Calibration and two menus |

### References (shared) (2 files)

| Current folder | File | Note |
|---|---|---|
| References | `ReferencedEntity.cs` |  |
| References | `ReferencedEntityPresenter.cs` |  |

### Shell (51 files)

| Current folder | File | Note |
|---|---|---|
| (root) | `MainWindow.xaml` |  |
| (root) | `MainWindow.xaml.cs` |  |
| Diagnostics | `ActivityLogFileSaver.cs` | Diagnostics menu back-ends (bundle, summaries, layout watchdog) |
| Diagnostics | `DiagnosticBundleSaver.cs` | Diagnostics menu back-ends (bundle, summaries, layout watchdog) |
| Diagnostics | `LayoutStallDetector.cs` | Diagnostics menu back-ends (bundle, summaries, layout watchdog) |
| Diagnostics | `LayoutStallWatchdog.cs` | Diagnostics menu back-ends (bundle, summaries, layout watchdog) |
| Diagnostics | `ProjectDiagnosticSummary.cs` | Diagnostics menu back-ends (bundle, summaries, layout watchdog) |
| Diagnostics | `RuntimeDiagnosticSummary.cs` | Diagnostics menu back-ends (bundle, summaries, layout watchdog) |
| Logging | `ActivityLogClearing.cs` | Logs popup / Diagnostics menu |
| Popups | `AboutPopupContracts.cs` |  |
| Popups | `AboutPopupDefinition.cs` |  |
| Popups | `AboutPopupViewModel.cs` |  |
| Popups | `FingerprintPopupContracts.cs` | Diagnostics menu |
| Popups | `FingerprintPopupDefinition.cs` |  |
| Popups | `FingerprintPopupViewModel.cs` |  |
| Popups | `KeyboardShortcutsPopupContracts.cs` |  |
| Popups | `KeyboardShortcutsPopupDefinition.cs` |  |
| Popups | `KeyboardShortcutsPopupViewModel.cs` |  |
| Popups | `LogsPopupContracts.cs` |  |
| Popups | `LogsPopupDefinition.cs` |  |
| Popups | `LogsPopupViewModel.cs` |  |
| Services | `DiagnosticBundleDestinationPicker.cs` |  |
| Services | `IDiagnosticBundleDestinationPicker.cs` | Diagnostics menu |
| Services | `ShellCommandPolicy.cs` |  |
| Startup | `UndoCommandBootstrap.cs` | Edit menu Undo/Redo bindings |
| Themes | `OutputLog.xaml` | OutputLogEntryTemplate consumed by LogsPopupTemplate via DynamicResource |
| Themes/Templates/Popups | `AboutPopupTemplate.xaml` |  |
| Themes/Templates/Popups | `FingerprintPopupTemplate.xaml` |  |
| Themes/Templates/Popups | `KeyboardShortcutsPopupTemplate.xaml` |  |
| Themes/Templates/Popups | `LogsPopupTemplate.xaml` |  |
| ViewModels | `DiagnosticsMenuViewModel.cs` |  |
| ViewModels | `MainShellViewModel.cs` |  |
| ViewModels/Setup | `ActiveJobSwitcher.cs` | title-bar job menu; only MainShellViewModel uses it |
| Views/Shell/Menus | `DiagnosticsMenu.xaml` |  |
| Views/Shell/Menus | `DiagnosticsMenu.xaml.cs` |  |
| Views/Shell/Menus | `EditMenu.xaml` |  |
| Views/Shell/Menus | `EditMenu.xaml.cs` |  |
| Views/Shell/Menus | `FileMenu.xaml` |  |
| Views/Shell/Menus | `FileMenu.xaml.cs` |  |
| Views/Shell/Menus | `HelpMenu.xaml` |  |
| Views/Shell/Menus | `HelpMenu.xaml.cs` |  |
| Views/Shell/Menus | `ProjectMenu.xaml` |  |
| Views/Shell/Menus | `ProjectMenu.xaml.cs` |  |
| Views/Shell/Menus | `ShellShortcut.cs` |  |
| Views/Shell/Menus | `ShellShortcutId.cs` |  |
| Views/Shell/Menus | `ShellShortcuts.cs` |  |
| Views/Shell/Menus | `ShortcutTextExtension.cs` |  |
| Views/Shell/Menus | `ToolsMenu.xaml` |  |
| Views/Shell/Menus | `ToolsMenu.xaml.cs` |  |
| Views/Shell/Menus | `ViewMenu.xaml` |  |
| Views/Shell/Menus | `ViewMenu.xaml.cs` |  |

### Menus (context-menu infrastructure) (4 files)

| Current folder | File | Note |
|---|---|---|
| Menus | `CopySummaryField.cs` |  |
| Menus | `EntityContextMenuViewModel.cs` |  |
| Menus | `MenuItemStyles.xaml` | merged by each menu template via pack URI |
| Menus | `MenuRegistrations.cs` | AddContextMenu registry |

### Messaging (job-content navigation) (6 files)

| Current folder | File | Note |
|---|---|---|
| Messaging | `JobContentKind.cs` |  |
| Messaging | `JobContentNavigationMessage.cs` |  |
| Messaging | `JobContentNavigationRequest.cs` |  |
| Messaging | `JobContentTarget.cs` |  |
| Services | `IJobContentNavigator.cs` |  |
| Services | `JobContentNavigator.cs` |  |

### Startup (bootstrap) (15 files)

| Current folder | File | Note |
|---|---|---|
| (root) | `App.xaml` | merges every template aggregator |
| (root) | `App.xaml.cs` | composition root, 38 popup definitions, 8 pages |
| (root) | `GlobalUsings.cs` |  |
| Logging | `ActivityLogSink.cs` |  |
| Logging | `BindingErrorTraceListener.cs` |  |
| Services | `ApplicationDataPaths.cs` |  |
| Services | `RegistryApplicationDataPathProvider.cs` |  |
| Startup | `AppCultureBootstrap.cs` |  |
| Startup | `AppServiceRegistrations.cs` | DI modules |
| Startup | `ConventionTextBootstrap.cs` | fills convention text from conventions.yaml |
| Startup | `DataGridTextBootstrap.cs` | fills UiDataGridText from data.yaml |
| Startup | `LogLevelBootstrap.cs` |  |
| Startup | `TextInlinesBootstrap.cs` | glossary.yaml |
| Startup | `VesselAxesTextBootstrap.cs` |  |
| ThirdPartyNotices | `GenerateThirdPartyNotices.cs` | build-time task |

### Workspace infrastructure (41 files)

| Current folder | File | Note |
|---|---|---|
| Services | `IWorkspaceSelectionCoordinator.cs` |  |
| Services | `SystemActorAttestation.cs` | no consumers found in src/geoDC.App |
| Services | `WorkspaceSelectionCoordinator.cs` |  |
| Workspace | `AppProjectWorkspace.cs` |  |
| Workspace | `CrpWorkspaceGuard.cs` |  |
| Workspace | `WorkspaceModuleCoordinator.cs` |  |
| Workspace/Alterations | `IWorkspaceWorkItem.cs` |  |
| Workspace/Alterations | `ReportStandingInvalidation.cs` |  |
| Workspace/Alterations | `WorkItemLifecycleCheckpoint.cs` |  |
| Workspace/Alterations | `WorkItemMatchSlot.cs` |  |
| Workspace/Alterations | `WorkspaceAlterationConfirmation.cs` |  |
| Workspace/Alterations | `WorkspaceAlterationService.cs` |  |
| Workspace/Alterations | `WorkspaceAlterationSnapshot.cs` |  |
| Workspace/Alterations | `WorkspaceAlterationTargets.cs` |  |
| Workspace/Alterations | `WorkspaceFeatureMeasurementService.cs` |  |
| Workspace/Alterations | `WorkspaceMatchingService.cs` |  |
| Workspace/Handlers | `CalibrationAttemptLoadHandler.cs` |  |
| Workspace/Handlers | `CalibrationAttemptSaveHandler.cs` |  |
| Workspace/Handlers | `ConventionDefinitionSaveHandler.cs` |  |
| Workspace/Handlers | `CrsDefinitionSaveHandler.cs` |  |
| Workspace/Handlers | `DataSeriesLoadHandler.cs` |  |
| Workspace/Handlers | `DataSeriesRowsFingerprint.cs` |  |
| Workspace/Handlers | `DataSeriesSaveHandler.cs` |  |
| Workspace/Handlers | `FieldRecordsLoadHandler.cs` |  |
| Workspace/Handlers | `FieldRecordsSaveHandler.cs` |  |
| Workspace/Handlers | `GnssCheckAttemptLoadHandler.cs` |  |
| Workspace/Handlers | `GnssCheckAttemptSaveHandler.cs` |  |
| Workspace/Handlers | `InstrumentLoadHandler.cs` |  |
| Workspace/Handlers | `InstrumentSaveHandler.cs` |  |
| Workspace/Handlers | `JobLoadHandler.cs` |  |
| Workspace/Handlers | `JobSaveHandler.cs` |  |
| Workspace/Handlers | `LifecycleSaveGuard.cs` |  |
| Workspace/Handlers | `PersistenceIdentity.cs` |  |
| Workspace/Handlers | `ProjectMetadataLoadHandler.cs` |  |
| Workspace/Handlers | `ProjectMetadataSaveHandler.cs` |  |
| Workspace/Handlers | `ShoreControlSnapshotLoadHandler.cs` |  |
| Workspace/Handlers | `ShoreControlSnapshotSaveHandler.cs` |  |
| Workspace/Handlers | `VerificationAttemptLoadHandler.cs` |  |
| Workspace/Handlers | `VerificationAttemptSaveHandler.cs` |  |
| Workspace/Handlers | `VesselFrameLoadHandler.cs` |  |
| Workspace/Handlers | `VesselFrameSaveHandler.cs` |  |

### Shared services (31 files)

| Current folder | File | Note |
|---|---|---|
| Behaviors | `DataGridScrollTarget.cs` |  |
| Charting | `ChartContextMenuRequest.cs` |  |
| Charting | `ChartHosting.cs` |  |
| Charting | `ChartImageExport.cs` |  |
| Charting | `ChartPalette.cs` |  |
| Charting | `HeadlessChartRenderer.cs` |  |
| Charting | `IChartImageExport.cs` |  |
| Charting | `SvgImageFactory.cs` |  |
| Converters | `AppConverters.cs` |  |
| Converters | `BindingProxy.cs` |  |
| Converters | `DecimalPlacesPrecisionConverter.cs` |  |
| Services | `AtomicFile.cs` | only Reports uses it today |
| Services | `DataExportDestinationPicker.cs` |  |
| Services | `IDataExportDestinationPicker.cs` | used by Shore Control only, named generically |
| Services | `IFileLauncher.cs` |  |
| Services | `ILastUsedFolderService.cs` |  |
| Services | `LastUsedFolderPurpose.cs` |  |
| Services | `LastUsedFolderService.cs` |  |
| Services | `ShellFileLauncher.cs` |  |
| Services | `TemporaryDirectory.cs` |  |
| Services/Crs | `CrsPickerCatalogue.cs` |  |
| Services/Crs | `CrsPickerEntries.cs` |  |
| Views/Behaviors | `CrsPickerBindings.cs` |  |
| Views/Behaviors | `DataViewColumnCaptions.cs` |  |
| WebHosting | `CompositionWebViewBrowser.cs` |  |
| WebHosting | `IWebViewBrowser.cs` |  |
| WebHosting | `WebViewEnvironment.cs` |  |
| WebHosting | `WebViewHost.cs` |  |
| WebHosting | `WebViewMessage.cs` |  |
| WebHosting | `WebViewPage.cs` |  |
| WebHosting | `WebViewSurface.cs` |  |

### Theming (aggregator) (5 files)

| Current folder | File | Note |
|---|---|---|
| Themes | `MenuTemplates.xaml` | merged from App.xaml:20; 13 context-menu dictionaries |
| Themes | `PopupTemplates.xaml` | merged from App.xaml:16; 27 popup dictionaries |
| Themes | `SetupPopupTemplates.xaml` | merged from App.xaml:17 |
| Themes | `SharedTemplates.xaml` | merged from App.xaml:18 |
| Themes | `StartupTemplates.xaml` | merged from App.xaml:15 |

### Survey + Shore Control (split) (1 files)

| Current folder | File | Note |
|---|---|---|
| Popups | `WorkflowPopupDefinitions.cs` | 14 definitions: 9 Survey (lines 10-265), 5 Shore Control (lines 267-418) |