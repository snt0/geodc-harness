# 04 — Discoverability audit: finding what you see on screen

Verified @ e9a8766 (origin/dev). Read-only investigation for geoDC handoff epic #780. Nothing in the repository was
modified. All paths are relative to `/home/user/geodc` unless they start with `/home/user/snt.lib`.

Method: I played a C# developer new to this codebase, with Visual Studio habits but only grep and a file viewer
available (Linux, no WPF, no VS). Each task starts from on-screen text only. A "hop" is one file opened or one search
run; a "dead end" is a hop that produced nothing I used. Where I guessed wrong, the guess is recorded.

---

## 1. Task A — hide a row on a popup

### A1. Edit Feature popup, row "Category"

What the user sees: title **Edit Feature**, subtitle *Rename or recategorise 'P01'.*, two rows **Code** and
**Category**, buttons **Cancel** / **Save**.

| Hop | What I did | Result |
|---|---|---|
| 1 | `grep -rn "Edit Feature" src` | One hit: `src/geoDC.App/Popups/WorkflowPopupDefinitions.cs:47` `Title = "Edit Feature"`. **Surprise:** the title is a C# literal, not YAML. The subtitle on line 48 is an interpolated literal too. Nothing in `Strings/` matched. |
| 2 | Read `WorkflowPopupDefinitions.cs:40-64` | `EditFeaturePopupDefinition : PopupDefinition<EditFeaturePopupRequest, EditFeaturePopupResult, EditFeaturePopupViewModel>`. No mention of any XAML file. |
| 3 | `ls src/geoDC.App/Popups/` expecting a `.xaml` beside the viewmodel | **Dead end.** `Popups/` holds 55 `.cs` files and zero XAML. |
| 4 | `grep -rn "Category" src/geoDC.App/Strings` | 9 hits across `survey.yaml` and `popups.yaml`. The one for this popup is `popups.yaml:26` `fields.category: Category` but nothing on screen tells you that; `survey.yaml:395 addSensor.featureCategory` and `survey.yaml:571` say the same word. Note: `grep ": Category$"` returned **nothing** because every file under `Strings/` is CRLF (`file` reports "CRLF line terminators"); the `$` anchor fails. That cost me one extra search. |
| 5 | Derive the member: file `popups.yaml` → `PopupsText`; key `fields.category` → `FieldsCategory` (docs/string-resources.md "Entry Patterns"). `grep -rn "PopupsText.FieldsCategory" src` | 3 hits: `Themes/Templates/Popups/EditFeaturePopupTemplate.xaml:17`, `CreateFeaturePopupTemplate.xaml:22`, `CreateFeatureGroupPopupTemplate.xaml:16`. The file name disambiguates. |
| 6 | Open `src/geoDC.App/Themes/Templates/Popups/EditFeaturePopupTemplate.xaml` | 26 lines. `DataTemplate DataType="{x:Type popups:EditFeaturePopupViewModel}"` (line 9); the row is `<ui:UiPropertyRow Title="{x:Static text:PopupsText.FieldsCategory}">` lines 17-23. No `Visibility` on it today. |
| 7 | Find the viewmodel: `xmlns:popups="clr-namespace:geoDC.App.Popups"` (line 5) → `src/geoDC.App/Popups/EditFeaturePopupViewModel.cs` | 72 lines, `ObservableObject` with `Code`, `FeatureType`, `AvailableTypes`, validation. **No bool exists** that could drive the row. |
| 8 | Where would the condition come from? `src/geoDC.App/Popups/WorkflowPopupContracts.cs:52-61` (`EditFeaturePopupRequest`) and the one caller `src/geoDC.App/ViewModels/Survey/SurveyPageViewModel.cs:1476-1477`. | If the rule depends on the feature, the request record needs a new field and the caller must supply it. |

Where I would add the condition:
- `EditFeaturePopupViewModel.cs`: `public bool ShowCategory => ...;` (or `[ObservableProperty] bool`).
- `EditFeaturePopupTemplate.xaml:17`: `Visibility="{Binding ShowCategory, Converter={snt:TrueToVisible}}"`.

**8 hops, 2 dead ends** (hop 3, and the CRLF-anchored grep in hop 4). A shortcut that works once you know the
convention: `grep -rln "EditFeaturePopupViewModel" src --include=*.xaml` goes straight to the template in one hop,
because every popup template is keyed by viewmodel type (`DataType="{x:Type popups:...}"`). Nothing told me that.

How the template actually reaches the screen (needed for confidence, not for the change): `App.xaml:16` merges
`Themes/PopupTemplates.xaml`, which merges 27 per-popup dictionaries (`PopupTemplates.xaml:5-31`, Edit Feature on
line 8); the frame's body is a `ContentPresenter` bound to `Session.ContentViewModel` in
`src/geoDC.App.UI/Themes/Controls.Popups.xaml:151-154`, so WPF picks the implicit `DataTemplate` by type. Three
files, two projects, to answer "why does this XAML show up at all".

### A2. Sensor popup, row "Serial number" (a row that is near rows already conditionally visible)

What the user sees: title **Sensor properties**, subtitle *Changes apply to this sensor in every job…*, rows Name,
Manufacturer, Model, **Serial number**, and for GNSS a **Antenna Z offset (m)** row, for angle sensors **Angles** and
**Device profile** rows.

| Hop | What I did | Result |
|---|---|---|
| 1 | `grep -rn "Sensor properties" src/geoDC.App/Strings` | `survey.yaml:319` `sensorEditor.title`. |
| 2 | `grep -rn "SurveyText.SensorEditorTitle" src` | `WorkflowPopupDefinitions.cs:134` → `EditSensorPopupDefinition` → `EditSensorPopupViewModel`. |
| 3 | `grep -rln "EditSensorPopupViewModel" src --include=*.xaml` | `Themes/Templates/Popups/SensorPopupTemplates.xaml` (line 90). |
| 4 | Open it | The Edit template (lines 90-96) is only `<ContentControl Content="{Binding Form}"/>`. The rows live in a **second** DataTemplate for `popups:SensorPropertiesForm` (lines 11-88). One indirection the file comment (line 10) does explain. |
| 5 | Serial row | Line 28: `Title="{x:Static text:SurveyText.SensorEditorSerial}"`. Its siblings use `SurveyText.SensorFormName/Manufacturer/Model` (lines 13-23): two YAML sections feed one form. `grep "Serial number" Strings` gives two candidates (`survey.yaml:330` `sensorEditor.serial`, `survey.yaml:549` `createFeature.serial`); I had to open the XAML to know which. |
| 6 | Existing pattern on the next row | Lines 33-35: `Visibility="{Binding IsGnss, Converter={snt:TrueToVisible}}"`; lines 40-42 `IsAngleSensor`. |
| 7 | Viewmodel | `src/geoDC.App/Popups/SensorPopupViewModels.cs:27` `SensorPropertiesForm`; `IsAngleSensor`/`IsGnss` at lines 91-93 are computed from `Kind`; `ShowHeadingImport` etc. at 99-101 show how a multi-input condition is written, with `[NotifyPropertyChangedFor]` on the inputs (lines 68-84). |
| 8 | Where is `snt:TrueToVisible` defined? `grep -rn "TrueToVisible" src` finds only usages. | **Dead end inside the repo.** It is in the private library: `/home/user/snt.lib/src/Snt.Lib.Wpf/Converters/TrueToVisible.cs` (a `BooleanToVisibility` markup extension, `BooleanToVisibility.cs:13`). Go To Definition in VS would land in metadata. Siblings in use: `FalseToVisible`, `NotNullToVisible`, `NotNullOrEmptyToVisible`, `EqualityToVisibility` (`SurveyPage.xaml:908`, `DataAnalysisPopupTemplate.xaml:427`). |

**8 hops, 1 dead end.** Note the second visibility idiom in the same folder:
`AddMultipleFeaturesPopupTemplate.xaml:68-79` uses a `Style` with a `DataTrigger` instead of a converter, as do
`CreateFeaturePopupTemplate.xaml:86-91` and `CreateFeatureGroupPopupTemplate.xaml:62-67`. A newcomer copying "the
pattern" will find two. The comment in `src/geoDC.App/Converters/AppConverters.cs:17-19` says the library converters
use XAML prefix `conv:`; every XAML file uses `snt:`. The comment is stale.

Also: the "Serial number" caption's member `SensorEditorSerial` is used twice (`SensorPopupTemplates.xaml:28` and the
Survey page inspector `Views/Shell/SurveyPage.xaml:903`). Hiding the row in the popup is local; rewording the caption
is not.

---

## 2. Task B — change a label

### B1. Simple entry: "Calibrations" (list header on the Calibration page)

1. `grep -rn "Calibrations" src/geoDC.App/Strings` → 2 hits; `calibration.yaml:58` `navigation.calibrations: Calibrations`.
2. Derive: `calibration.yaml` → `CalibrationText`; `navigation.calibrations` → `NavigationCalibrations`.
3. `grep -rn "NavigationCalibrations" src` → one usage, `Views/Shell/CalibrationShellPage.xaml:196`.

Two greps and a mental rename. Reliable. The derivation rule in `docs/string-resources.md` ("Nested keys concatenate
into PascalCase") matched the generator: `/home/user/snt.lib/src/Snt.Lib.Wpf.Text.Generators/TextResourceGenerator.cs:855-859`
`ToPascalCase` upper-cases after any non-alphanumeric and after `_`, and the class name is
`ToPascalCase(fileName) + "Text"` (line 70). So `shoreControl.yaml` → `ShoreControlText`, `title3DDistance` →
`Title3DDistance` (confirmed by the repo's own test table, `tests/geoDC.Infrastructure.Tests/Conventions/StringResourceConventionTests.cs:98-104`).

### B2. Match entry: "No job selected" (Calibration page gate)

1. `grep -rn "No job selected" src` → **9 YAML hits in 8 files** plus 3 in the Sample gallery. On the Calibration page the one that applies is `calibration.yaml:94`, the `false:` case of `pageGate.title` (`match: bool`). Nothing on screen distinguishes it from `survey.yaml:110`, `setup.yaml:3`, `landing.yaml:37`, `shoreControl.yaml:12`, `reports.yaml:58`, `gnss.yaml:65`, `verification.yaml:82`. You pick by page → file-name convention (calibration page → `calibration.yaml`), which works here and is documented in the file table of `docs/string-resources.md`.
2. Derive: `pageGate.title` → `PageGateTitle`. But that member is a **method** `PageGateTitle(bool)`; XAML binds the **`PageGateTitleMap`** property. If you grep `PageGateTitle` without the suffix you still find it (`CalibrationShellPage.xaml:179` `{snt:MatchText {Binding HasActiveJob}, {x:Static text:CalibrationText.PageGateTitleMap}}`), so a prefix grep is the safe habit.
3. The case text itself ("No job selected") has **no member of its own** to grep for. In the generated code the case becomes a resource key string `"PageGateTitle_False"` inside a private dictionary (generator line 267 comment: cases are `Member_Case`; `EmitBoolean` at line 589-596 only references keys). So the on-screen text and the member you bind are separated by one more hop even with generated code in front of you.

Four hops; the ambiguity in hop 1 is the real cost, not the derivation.

### B3. Format/plural entry: "Compare (3)" (Shore Control Register view switch)

1. The number on screen is substituted at run time, so `grep "Compare (3)"` finds nothing. Guessing the stable prefix `grep -rn '"Compare (' src/geoDC.App/Strings` → `shoreControl.yaml:101` `register.viewComparePending: "Compare ({count:int})"`.
2. Derive: `RegisterViewComparePending(int count)` method + `RegisterViewComparePendingKey` constant (+ `…Message`).
3. `grep -rn "RegisterViewComparePending" src` → one usage, `ViewModels/ShoreControl/ShoreControlRegisterViewModel.cs:198` (C#; a XAML usage would have been `{snt:FormatText {x:Static text:ShoreControlText.RegisterViewComparePendingKey}, …}`).

Reliable once you know to strip the placeholder. A plural entry (`one:`/`other:`) is the same shape with `…Map`.

### Would Visual Studio help?

- **Generated files on disk:** no for the app. `src/geoDC.App/geoDC.App.csproj` sets neither `EmitCompilerGeneratedFiles` nor `CompilerGeneratedFilesOutputPath` (lines 42-53 are the only PropertyGroup); `Directory.Build.props` and `Directory.Build.targets` do not either. The only project that emits is `src/geoDC.App.UI/geoDC.App.UI.csproj:6-7` (`$(BaseIntermediateOutputPath)Generated`), for its own generators. So `grep` of the text you see finds YAML only, never a `.g.cs`.
- **Go To Definition on `CalibrationText.ActionsNew`:** lands somewhere readable. VS opens the in-memory generated document (also browsable under Dependencies › Analyzers › Snt.Lib.Wpf.Text.Generators › `CalibrationText.g.cs`). What you see, per `TextResourceGenerator.cs:530-556`: a simple entry is `public const string ActionsNew = "+ New";` preceded by `/// <summary>Gets the generated text.</summary>` and a `[GeneratedTextResourceAttribute(0)] … ActionsNewKey` property. The text is there as the const value; the **YAML file and key path are not** in any comment. A format entry is a method whose body contains the composite format string. A match/plural entry is a `…Map` property built from `Resources.GetKey("Member_Case")` calls; the case texts are only in the `Resources` dictionary at the top of the class (`Emit`, lines 486-500), so you scroll.
- **Find All References:** C# references, yes (Roslyn). XAML `x:Static` usages: I could not run VS to verify and would not promise it. The repository itself does not rely on it: `StringResourceConventionTests.cs:21-27` scans `.cs` and `.xaml` as text with a regex `\b([A-Z][A-Za-z0-9]*Text)\.([A-Za-z0-9_]+)\b`, which is exactly the grep a developer should run.
- **Hover (Quick Info):** on a const, VS shows the value, so hovering `CalibrationText.ActionsNew` in C# shows `"+ New"`. In the XAML editor hover on `x:Static` members is less dependable.

---

## 3. Task C — add a column to a data grid

Target: the Shore Control page's point table (visible columns Point, Source, Epoch, three target-axis columns,
Note). Goal: add a column.

| Hop | What I did | Result |
|---|---|---|
| 1 | `grep -rn "columnPoint" src/geoDC.App/Strings` | **Three** keys named `columnPoint: Point` in `shoreControl.yaml` (lines 108 `snapshot.`, 180, 303 `transform.`). The on-screen word "Point" cannot pick between them; "transform" is not a word the user sees. I guessed `snapshot` first (**wrong**: that is the job-copy compare view), then tried each member. |
| 2 | `grep -rn "TransformColumnPoint" src` | `Views/ShoreControl/ShoreControlPointTableView.xaml:110`. |
| 3 | Open the view | `ui:UiDataGrid` lines 72-205, `ItemsSource="{Binding Transform.Rows}"` (77), `d:DataContext` is `ShoreControlWorkbenchViewModel` (15). Columns: `DataGridTemplateColumn`s with a cell `DataTemplate DataType="{x:Type viewModels:ShoreControlTransformRowViewModel}"` (115, which handed me the row type for free), then `ui:UiDataGridNumberColumn`s whose headers bind through a `BindingProxy` (`TransformProxy`, lines 153-169) because the axis names are dynamic. |
| 4 | Row type and source | `ViewModels/ShoreControl/ShoreControlWorkbenchViewModel.cs:136` `Transform` → `ShoreControlTransformViewModel.cs:88` `ObservableCollection<ShoreControlTransformRowViewModel> Rows`; filled by `LoadAsync(ShoreControlSet?)` (177) → `RebuildRows()` (186). |
| 5 | Who shows this view | Two hosts: `Views/Shell/ShoreControlPage.xaml:302` and `Views/ShoreControl/ShoreControlRegisterView.xaml:276`, both `DataContext="{Binding Workbench}"`. A new column appears in both places. |
| 6 | Column vocabulary | `docs/ui/data-grids.md:17-63`: `UiDataGridTextColumn` (`IsMono`, `Alignment`) and typed columns (`UiDataGridLengthColumn`, `…AngleColumn`, `…NumberColumn`, `…TimestampColumn`). Found because the task brief named the page; nothing in the view links to it. |
| 7 | Hidden coupling | Copy and Export do **not** read the grid's columns: `ShoreControlTransformViewModel.cs:238-261` calls `ShoreControlTransformTable.ToTabSeparated/ToCsv(rows, Target, TargetAxes)`. A grid-only column will be missing from Ctrl+C and the CSV unless that class changes too. Not discoverable from the XAML. |

Recipe derived: (a) YAML `transform.columnXxx` in `shoreControl.yaml`; (b) a property on
`ShoreControlTransformRowViewModel` set in `RebuildRows`; (c) `<ui:UiDataGridTextColumn Header="{x:Static text:ShoreControlText.TransformColumnXxx}" Binding="{Binding Xxx}"/>`
in `ShoreControlPointTableView.xaml`; (d) `ShoreControlTransformTable` for copy/export. **7 hops, 1 wrong guess.**

Side note: `grep -rn "<ui:UiDataGrid" src/geoDC.App/Views` finds only three views; six more grids live in
`Themes/Templates/Popups/*.xaml` (e.g. `SequentialDataImportWizardPopupTemplate.xaml:416,846,1121,1167`). "Grids are
in Views" is a wrong mental model here.

---

## 4. Task D — add a menu item that opens an existing popup

Scenario: add **Tools › Shore Control register…** opening the existing register popup (defined at
`WorkflowPopupDefinitions.cs:396`, request `Popups/ShoreControlRegisterPopupContracts.cs:23`).

| Hop | What I did | Result |
|---|---|---|
| 1 | `AGENTS.md:9` says read `docs/shell-menus.md` first. Its "Where menus live" (`docs/shell-menus.md:7-14`) | one XAML per menu under `src/geoDC.App/Views/Shell/Menus/`; copy under `shell.yaml` `menu.<menu>`; shortcuts only in `ShellShortcuts`; availability from `ShellCommandPolicy`; AutomationId `{Noun}Button`. Even without the doc, `MainWindow.xaml:49` carries a comment pointing at the folder. |
| 2 | `Views/Shell/Menus/ToolsMenu.xaml:17-21` | The one existing item is the model: `Command="{Binding OpenShoreControlCommand}"`, `Header="{x:Static text:ShellText.MenuToolsShoreControl}"`, `Symbol=…`. |
| 3 | `Strings/shell.yaml:53-55` `menu.tools` | add `register: "Shore Control _register…"` → `ShellText.MenuToolsRegister`. |
| 4 | Command home: menus bind to `MainShellViewModel` (`HelpMenu.xaml:12`). `ViewModels/MainShellViewModel.cs:305-316` | `ShowKeyboardShortcutsAsync`/`ShowAboutAsync` are the model: `[RelayCommand] … await _popupService.ShowAsync<Req, Res>(new Req())`. A project-scoped command copies `OpenShoreControl` (`MainShellViewModel.cs:469-473`, `CanExecute = nameof(CanOpenShoreControl)`) for the policy rule. |
| 5 | Request shape | `ShoreControlRegisterPopupRequest(Ulid? JobId, ShoreControlRegisterFocus Focus, string? PointName)`; the existing caller `ViewModels/ShoreControl/ShoreControlPageViewModel.cs:713` shows what to pass. |
| 6 | Registration | Already done: `App.xaml.cs` registers every `IPopupDefinition` in one block (lines 296-325). Nothing to add. |
| 7 | Bookkeeping | `docs/shell-menus.md:70` inventory table must gain a row (convention, not enforced); `tests/geoDC.App.Tests/Views/ShellMenuTests.cs` only checks the View menu against the nav bar; `ShellShortcutsTests` applies only if a shortcut is added. |

**7 hops, 0 dead ends.** This is the best-documented path in the four tasks because the doc is linked from
`AGENTS.md` and the XAML carries a pointer comment. It is the model the others should copy.

---

## 5. Cross-cutting findings

| # | Mechanism | Evidence | Cost | Cheapest fix | Proper fix |
|---|---|---|---|---|---|
| 1 | **By-kind folders** split one popup across `Popups/*.cs` (viewmodel, contracts, definition), `Themes/Templates/Popups/*.xaml` (view), `App.xaml.cs` (DI). | A1 hop 3 dead end; `ls Popups/` shows 55 `.cs`, 0 `.xaml`. | 1-2 hops per popup, every time. | A `README.md` in `Popups/` and in `Themes/Templates/Popups/` each pointing at the other, and the grep `--include=*.xaml <ViewModelName>`. | Co-locate: `Popups/EditFeature/{ViewModel,Definition,Contracts}.cs` + `Template.xaml`, keep `PopupTemplates.xaml` as the merge list. Pure file moves; `PopupTemplates.xaml` `Source=` paths change. |
| 2 | **DataTemplate-by-type** popups: no name links definition → XAML; the frame resolves it implicitly (`Controls.Popups.xaml:151-154`). | A1 hop 6; `PopupTemplates.xaml:5-31`. | Unknowable without the convention. | One line in `docs/ui/popups.md` "Wiring" and in the newcomer page: "the template is `DataType="{x:Type popups:<ViewModel>}"` under `Themes/Templates/Popups/`". The doc today never names that folder (`grep "Templates/Popups" docs` hits only `charting.md:199`). | Add `/// <seealso>` comments on each `…PopupDefinition` naming its template file, or a `[PopupTemplate("…xaml")]`-style attribute checked by a convention test. |
| 3 | **Generated text** with no files on disk; member derivation by rule; case texts have no member. | B2, B3; `geoDC.App.csproj` lacks `EmitCompilerGeneratedFiles`. | +1 derivation per label, +1 ambiguity hop for common words (9× "No job selected", 9× "Category", 3× `columnPoint`). | Section 6 (1) and (2). | Section 6 (3). |
| 4 | **C# literal titles escape the YAML rule.** `XamlLiteralTextConventionTests` covers XAML only. | `WorkflowPopupDefinitions.cs:47-48,163-164,191-192,247`; `FingerprintPopupDefinition.cs:18-19`; `ProjectMetadataEditorPopupDefinition.cs:16`; `ProjectSelectorPopupDefinition.cs:15`. | Grep *works* on these, which is the irony: it works because the rule was broken. Localisation is incomplete. | List them in CHANGELOG/issue; move to `popups.yaml`. | Extend the convention test to `Title =`/`SubTitle =` string literals in `src/geoDC.App`. |
| 5 | **Converter-heavy visibility**, two idioms side by side, converters defined in a private library. | A2 hop 8; `TrueToVisible.cs` in snt.lib; `AddMultipleFeaturesPopupTemplate.xaml:68-79` DataTrigger idiom. | +1 hop and a "which idiom" pause. | Fix the stale `conv:` comment in `AppConverters.cs:17-19` to `snt:` and list the five converters in use. | Prefer `snt:TrueToVisible`/`FalseToVisible`/`NotNullToVisible`/`EqualityToVisibility` and retire the DataTrigger idiom where it only toggles Visibility (a `UiPropertyRow.IsVisibleWhen` bool DP on the toolkit control would remove converters from popups entirely). |
| 6 | **Resource dictionary merge chains**: `App.xaml` → `PopupTemplates.xaml` → per-popup file; styles like `PopupFormBodyStyle` come from another project (`geoDC.App.UI/Themes/Controls.Popups.xaml:20`) via `ThemeDictionary`. | A1 "how it reaches the screen". `ResourceDictionaryConventionTests` exists to police load order, which shows the chain bites. | Only when something does not render. | Comment at the top of `PopupTemplates.xaml`: "add new popup templates here; styles come from geoDC.App.UI". | Keep; it is standard WPF. Document once. |
| 7 | **Names that are not guessable**: `Transform` for the point table; `sensorEditor.*` vs `sensorForm.*` feeding one form; `SensorEditorSerial` shared by popup and page; `Views/Shell/*ShellPage.xaml` vs `Views/Shell/SetupPage.xaml`. | C hop 1 wrong guess; A2 hop 5. | 1 wrong guess each. | Section comments in YAML naming the surface ("# The Points tab table on the Shore Control page"). | Rename YAML sections after the screen the user sees (`pointTable:`), accepting a one-time member rename. |
| 8 | **Docs exist but are not linked from where you are.** `docs/string-resources.md` is linked from `README.md:64`, `AGENTS.md:113`, `coding-style.md:215`, `copy-style.md`, never from a YAML file or template. `docs/ui/data-grids.md` is not referenced from any view. | Hops 6 in C; the brief had to tell me about the docs. | A newcomer who starts in Solution Explorer never meets them. | A `README.md` (or `_README.yaml` comment header) in `Strings/`, `Popups/`, `Themes/Templates/Popups/`, `Views/` with one line and a link. | Section 7 page, linked from `README.md` top and from `AGENTS.md` "Orient by task". |
| 9 | **CRLF** files defeat `$`-anchored grep and some editors' regex. | `file Strings/*.yaml` → CRLF, BOM. | One failed search. | Note it in the newcomer page; prefer `grep -rn "text"` without anchors, or `rg`. | `.gitattributes` with `* text=auto eol=lf` (check the XAML styler and the probe scripts first). |

---

## 6. Text-system mitigations, ranked by cost

All keep localisation possible; none proposes inlining strings.

| Rank | Mitigation | Cost | Benefit | Localisation |
|---|---|---|---|---|
| 1 | **Emit generated files to disk** in `geoDC.App`: `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` + `<CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)Generated</CompilerGeneratedFilesOutputPath>` (exactly what `geoDC.App.UI.csproj:6-7` already does). | Two lines; add `obj/**/Generated` to any search scope. Watch for the known double-definition error when the folder is also globbed as `Compile`; the `obj/` location avoids it. | `grep "+ New"` now hits `obj/…/Generated/Snt.Lib.Wpf.Text.Generators/…/CalibrationText.g.cs` with the member name on the same line (`public const string ActionsNew = "+ New";`). For format entries the composite is in the method body; for match/plural the text is in the `Resources` dictionary keyed `Member_Case`, so you get the member in one more in-file search. VS "Find in Files" and the terminal both work. | Unchanged: YAML stays the source. |
| 2 | **Dev script** `dev/find-text.ps1 "No job selected"`: scan `Strings/*.yaml`, print file, key path, class.member(s) by the same rules as `StringResourceConventionTests.FindEntries`, then grep `src` for `Class.Member` and `text:Class.Member`. | Half a day; the derivation code already exists in the test (`StringResourceConventionTests.cs`, `EntriesIn`) and could be lifted into a tiny console tool under `tools/`. | Collapses B1-B3 to one command, and lists all usages (the ambiguity in B2 becomes a 9-row table you choose from). | Unchanged. |
| 3 | **Generator changes** (now vendored, so allowed): (a) put the YAML key path and file in the XML doc, e.g. `/// <summary>calibration.yaml › actions.new: "+ New"</summary>` instead of the uniform "Gets the generated text." (`TextResourceGenerator.cs:532-556`, 561, 574, 591, 599, 612); (b) for match/plural, emit the case texts into the `…Map` member's `<remarks>` so Go To Definition shows them beside the member; (c) emit `#region <section>` per top-level YAML key, or one `partial class` file per top-level key (`CalibrationText.Actions.g.cs`), so the generated file reads like the YAML; (d) emit `#line` directives pointing at the YAML line so VS errors and Go To Definition can land in the YAML. | (a)-(c) a day each including tests; (d) more, and `#line` into a non-C# file is unusual (works for diagnostics, less so for navigation). | Hover and Go To Definition then carry the key path; combined with (1), grep of the text hits a line that also names the key. | Unchanged; the comments are English source metadata, not UI text. |
| 4 | **Analyzer / EditorConfig**: the package already ships `XamlTextAnalyzer` (SNTTXT100-102, `/home/user/snt.lib/src/Snt.Lib.Wpf.Text.Generators/XamlTextAnalyzer.cs`) that validates XAML `x:Static` references against generated members, so XAML usages are already visible to Roslyn. A further analyzer could flag string literals assigned to `PopupPresentation.Title/SubTitle` (finding #4). EditorConfig cannot help navigation; `.editorconfig` has no generated-code entries today. | Analyzer for literal titles: a day. | Closes the YAML bypass; no discoverability gain by itself. | Improves it (catches un-localised text). |
| 5 | **Doc page** "Finding the text you see on screen" (section 7 outline): the file-name rule, the PascalCase rule, the `Map`/`Key`/`Message` suffixes, "grep the member prefix", the CRLF caveat, and the popup-template-by-type rule. Link from `README.md`, `AGENTS.md`, and a one-line header comment in every `Strings/*.yaml`. | An hour. | Removes the "I did not know there was a rule" cost; does not remove hops. | Unchanged. |

**How Match/Plural/Format read in generated code today** (from `TextResourceGenerator.cs`): every scalar gets
`XKey` (`TextResourceKey`, with `[GeneratedTextResourceAttribute(n)]`), then either `public const string X = "…"` (no
placeholders) or `public static string X(T a, …) => string.Format(CurrentCulture, "…{0}…", a, …)` plus `XMessage`.
Enum/bool/string matches get `XMap { get; } = new(Dictionary<Enum, TextResourceKey>{…})` + `string X(Enum value) =>
XMap.Resolve(value).Format(CurrentCulture)`. Plurals get `XMap` (`NumberTextMap`) + `string X(int count)`. The XML
doc comments are uniform boilerplate ("Gets the generated text.", "Gets the generated Boolean-to-text map.",
"Formats the generated plural text for a count."); none names the YAML file, key path, or case texts. The class
summary is "Provides generated single-culture text resources for this YAML file." without the file name (line 484).
That is the single cheapest generator improvement.

Recommended order: (1) today, (5) this week, (3a)+(3b) when the generator is vendored, (2) if (1)+(3) are not enough,
(4) as a follow-up to finding #4.

---

## 7. What a newcomer "how to find things" page should contain

One page, `docs/finding-things.md`, linked from `README.md` and `AGENTS.md` "Orient by task". Rules, not prose:

**Start from the words on screen.** `grep -rn "<the words>" src/geoDC.App/Strings` (no `$` anchors; files are CRLF).
Pick the file by area (table in `docs/string-resources.md`): page name → `<page>.yaml`; popups → `popups.yaml` or the
owning page's file; menus → `shell.yaml` `menu.*`. Member = `<File>Text.` + key path in PascalCase; for `match:` or
`plural:` entries bind `…Map` in XAML and call `…(value)` in C#; for `{placeholders}` use `…Key` in XAML and `…(args)`
in C#. Then `grep -rn "<File>Text.<Member>" src` for every usage. If the words are not in any YAML, grep `src` for the
literal: a few popup titles are still C# literals (`WorkflowPopupDefinitions.cs`).

| On-screen thing | Where it lives | Lookup rule |
|---|---|---|
| **Page** (nav bar tab) | `src/geoDC.App/Views/Shell/<Name>ShellPage.xaml` (Setup, Landing and ShoreControl drop "Shell"); viewmodel `ViewModels/<Area>/<Name>PageViewModel.cs`; registered `App.xaml.cs` (`AddTransient<…Page>()`), navigated from `MainWindow.xaml` nav items. | `d:DataContext` at the top of the page names the viewmodel. Sub-views for a page sit in `Views/<Area>/`. |
| **Popup** | Viewmodel, request/result and `…PopupDefinition` in `src/geoDC.App/Popups/` (setup popups in `ViewModels/Setup/`); XAML in `Themes/Templates/Popups/<Name>PopupTemplate.xaml`, merged by `Themes/PopupTemplates.xaml`; DI in `App.xaml.cs`. | Title → `…PopupDefinition.Define()`; the definition's third generic argument is the viewmodel; `grep -rln "<ViewModel>" --include=*.xaml` is the template. A row is a `ui:UiPropertyRow Title=…`; hide it with `Visibility="{Binding Flag, Converter={snt:TrueToVisible}}"` and a bool on the viewmodel. |
| **Menu item** | `Views/Shell/Menus/<Menu>Menu.xaml`; copy `shell.yaml` `menu.<menu>`; commands on `MainShellViewModel`; shortcuts in `Views/Shell/Menus/ShellShortcuts.cs`; inventory and rules in `docs/shell-menus.md`. | Header text → `ShellText.Menu<Menu><Item>`; follow the `Command` binding to the shell viewmodel. |
| **Label / caption** | `Strings/<area>.yaml` → `Snt.Generated.Text.<Area>Text` (generated, not on disk unless emitted). | Rule above. Shared captions (`popups.yaml` `fields.*`) are used by several popups; check all usages before rewording. |
| **Grid column** | Page grids in `Views/<Area>/*.xaml`, popup grids in `Themes/Templates/Popups/*.xaml`; always `ui:UiDataGrid`; column types in `docs/ui/data-grids.md`. | Header text → YAML → `grep` the member to the view; `ItemsSource` → the collection's element type is the row viewmodel; the cell `DataTemplate DataType` often names it directly. Check for a separate copy/export table class (`…Table.ToCsv`). |
| **Chart** | Contracts in `geoDC.Reporting.Contracts`/app `Charting/`, renderers in `ChartAssets/` (D3 in WebView2), config popups in `Themes/Templates/Popups/DataAnalysisPopupTemplate.xaml`; recipe in `docs/charting.md` "Adding a chart config option". | Chart text is in YAML like everything else; chart drawing is JavaScript, not XAML. |
| **Visibility / enabled logic** | Converters: `snt:TrueToVisible`, `FalseToVisible`, `NotNullToVisible`, `NotNullOrEmptyToVisible`, `EqualityToVisibility` (library `Snt.Lib.Wpf`, not in this repo); app-specific ones in `Converters/AppConverters.cs`. | Grep the bound property name in the viewmodel; computed bools use `[NotifyPropertyChangedFor]` on their inputs. |
| **Styles and tokens** | `geoDC.App.UI/Themes/*.xaml` via `ThemeDictionary`/`LayoutDictionary` in `App.xaml`; `docs/ui/theming.md`. | A `StaticResource` key not in the current file is in `geoDC.App.UI`. |

Close the page with the three convention tests a change will trip and what they want:
`StringResourceConventionTests` (every YAML entry used), `XamlLiteralTextConventionTests` (no literal copy in XAML),
`ShellMenuTests`/`ShellShortcutsTests` (menus and shortcuts in step).

---

## Appendix A — Hop ledger

| Task | Hops | Dead ends / wrong guesses | What cost the most | What saved the most |
|---|---|---|---|---|
| A1 Edit Feature row | 8 | 2 (no XAML beside the VM; CRLF-anchored grep) | Not knowing templates are keyed by viewmodel type under `Themes/Templates/Popups/` | The title was a C# literal (`WorkflowPopupDefinitions.cs:47`), so grep found the definition at once; this is luck, not design |
| A2 Sensor row | 8 | 1 (converter not in repo) | Form split across two DataTemplates and two YAML sections | `sensorEditor.title` is a unique string; `IsGnss` row two lines below is the pattern to copy |
| B1 simple label | 3 | 0 | None | File-name and PascalCase rules hold exactly (`ToPascalCase`, generator line 855) |
| B2 match label | 4 | 0, but 9 candidates | Same words in 8 YAML files; case text has no member | Page → file convention; grep the member prefix without the `Map` suffix |
| B3 format label | 3 | 0 | Run-time substitution hides the YAML text | Guessing the stable prefix |
| C grid column | 7 | 1 (`snapshot.columnPoint` instead of `transform.columnPoint`) | Three identical `columnPoint` keys; copy/export columns in a separate class | Cell `DataTemplate DataType` named the row type |
| D menu item | 7 | 0 | None | `AGENTS.md:9` → `docs/shell-menus.md`; `MainWindow.xaml:49` comment; `ToolsMenu.xaml` is a one-item model |

Totals: 40 hops, 4 dead ends or wrong guesses, across seven lookups. Three of the four misses are the text system
(ambiguous words, CRLF, no member for a match case); the fourth is the by-kind folder split. None were WPF itself.

## Appendix B — What `CalibrationText.g.cs` looks like (reconstructed from the generator)

Nothing generated is on disk, so this is reconstructed from `TextResourceGenerator.cs:477-620`. It is what a developer
sees after Go To Definition, or in a file after mitigation (1):

```csharp
// <auto-generated />
#nullable enable
namespace Snt.Generated.Text;

/// <summary>Provides generated single-culture text resources for this YAML file.</summary>
public static class CalibrationText
{
    private static readonly global::Snt.Lib.Wpf.Text.TextResourceMap Resources = new(
        new global::System.Collections.Generic.Dictionary<string, string>
        {
            ["ActionsNew"] = "+ New",
            ["PageGateTitle_True"] = "No angle sensors installed",
            ["PageGateTitle_False"] = "No job selected",
            ["NavigationSearchPlaceholder"] = "Search calibrations…",
            // … every scalar in the file, cases keyed Member_Case …
        });

    /// <summary>Gets the binding key for <see cref="ActionsNew"/>.</summary>
    [global::Snt.Lib.Wpf.Text.GeneratedTextResourceAttribute(0)]
    public static global::Snt.Lib.Wpf.Text.TextResourceKey ActionsNewKey { get; } = Resources.GetKey("ActionsNew", 0);
    /// <summary>Gets the generated text.</summary>
    public const string ActionsNew = "+ New";

    /// <summary>Gets the generated Boolean-to-text map.</summary>
    public static global::Snt.Lib.Wpf.Text.BooleanTextMap PageGateTitleMap { get; } =
        new(Resources.GetKey("PageGateTitle_True"), Resources.GetKey("PageGateTitle_False"));
    /// <summary>Looks up the generated text for a Boolean value.</summary>
    public static string PageGateTitle(bool value) => PageGateTitleMap.Resolve(value).Format(CurrentCulture);
}
```

Observations a newcomer would make:
- The simple entry is self-describing: member and text on one line. Mitigation (1) alone makes it greppable.
- The match entry's texts are 40-plus lines away from its members, under keys (`PageGateTitle_False`) that appear
  nowhere in app code. Grep of "No job selected" lands on the dictionary line; you then search the same file for
  `PageGateTitle` to find the members. Mitigation (3b) (case texts in the `…Map` member's `<remarks>`) removes that.
- No comment names `calibration.yaml` or the key path `pageGate.title`. The class summary (generator line 484) is the
  same sentence in all 20 classes. Mitigation (3a) is a one-line change per emit site.
- A format entry (`gnss.yaml` `dataInput.rows`) becomes `public static string DataInputRows(int count) =>
  string.Format(CurrentCulture, "{0} rows", count);` — the composite format `{0} rows` is greppable, the YAML
  placeholder name `count` survives only as the parameter name.

## Appendix C — Not verified from this environment

- Visual Studio behaviour (Go To Definition into generated documents, Find All References across XAML `x:Static`,
  Quick Info in the XAML editor). The statements in section 2 are from VS's documented behaviour for source generators
  and should be checked once on a Windows machine with the solution open; the test-side evidence
  (`StringResourceConventionTests.cs:21-27` uses a regex scan) stands regardless.
- Whether `EmitCompilerGeneratedFiles` in `geoDC.App` collides with any `Compile` glob. `geoDC.App.csproj` has no
  custom `Compile` items apart from the `ChartAssets\node_modules\**` removals (lines 33-37), so the default
  `obj/` location should be safe, as it evidently is for `geoDC.App.UI`.
- The run-time look of the popups. Row captions and titles were taken from the YAML and the definitions, not from a
  screenshot; `dev/capture-views.ps1` and the UI probe (`docs` › UI probe) are the way to confirm on Windows.
