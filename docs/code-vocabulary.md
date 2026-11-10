# Code Vocabulary

> Moved from `geodc/docs/code-vocabulary.md` on 2026-11-10. Code paths below (`src`, `tests`) are geoDC's.

The words geoDC uses to name types, and what each one means. Read this before you choose a role suffix for a new
type, or rename a family of types.

The **Settled** sections are rules: use those terms as described. The **Overloaded suffixes** need judgement, and
each entry says what to prefer instead.

## Naming Grammar

Names read from the specific concept to the technical role:

```text
{Qualifier}{Concept}{Role}
```

Examples are `RecentProjectSummaryReader`, `CalibrationAttemptSaveHandler`, `ProjectMetadataEditorPopupViewModel`
and `RegistryApplicationDataPathProvider`.

- **Qualifier** marks a particular implementation, medium, scope or presentation (`Ef`, `Json`, `Registry`, `Recent`,
  `Popup`). Leave it out when it adds nothing.
- **Concept** is the domain or design subject (`CalibrationAttempt`, `ProjectPackage`, `Theme`). Use the domain
  model's terms.
- **Role** says what the type does or represents (`Request`, `Result`, `Service`, `Provider`, `ViewModel`). End with
  one clear role.

Avoid names built from several vague nouns, such as `DataManager`, `ModelService` or `ContextHelper`.

## Settled Domain Vocabulary

The glossary in [`domain-context.md`](../geodc/docs/domain-context.md#glossary) is the authority for real-world terms. These are
the distinctions that affect code names:

| Term | Use |
|---|---|
| `VesselReferenceFrame` / `Vrf` | The coordinate frame fixed to the vessel. Spell it out in central domain types. Use `Vrf` inside a compound name where the meaning is already clear. |
| `Feature` | A named point of interest in the VRF that persists across jobs. |
| `FeatureState` | A feature's state or position in one job. |
| `Instrument` | A physical asset that persists across jobs. |
| `InstrumentInstallation` | Where an instrument is mounted in one job. Position never goes directly on `Instrument`. |
| `CalibrationAttempt` | One attempt at computing and approving a C-O. |
| `VerificationAttempt` | The measurement after a correction is applied, which produces a residual. |
| `GnssCheckAttempt` | One GNSS spot check of an antenna's position. |
| `ShoreControlSet` / `ShoreControlPoint` / `ShoreControlPointRevision` | A dock or site, its physical marks, and the attested statements of where each mark is. Revisions are append-only. A revision holds the coordinate with its `CrsReference` and epoch, the point holds identity and lifecycle, and the set holds metadata that is itself revisioned. |
| `GeodeticCoordinate` / `CoordinateKind` | One position in a geodetic CRS: projected, geographic or geocentric. Local engineering grids and local east-north-up frames are the projected kind, with their own axis names, and the toolkit's `UiCoordinateAxes` sets how axes are named and ordered for display. Use it where the ordinates have CRS meaning, and keep `Point3D` for positions in the vessel frame. The CRS reference is a separate value beside it, not part of the coordinate. |
| `SignConvention` | What an angle's sign means. Make it clear in compound names whether a convention is for display, import, calculation or export. |
| `Job` | The work done during one vessel visit. Don't use `Project` when the scope is one visit. |
| `ProjectPackage` | The saved `.gdcproj` file. Keep it distinct from the extracted workspace and from project metadata. |

## Settled Technical Roles

| Role | Meaning |
|---|---|
| `Request` | Immutable input to a use case, workflow, renderer or popup. Prefer a record. |
| `Result` | Immutable outcome of an operation. Use a more precise noun such as `Summary` or `Snapshot` when that is what it is. |
| `Options` | Choices the caller makes about how an operation or subsystem behaves. |
| `Settings` | Preferences that are saved or that the user can edit. |
| `Metadata` | Descriptive identity or provenance of another object, not its working contents. |
| `Summary` | A reduced read model for lists, status or inspection. |
| `Snapshot` | A complete copy of the relevant state at a point in time. |
| `Preview` | What an operation would do, computed by the same rules as the real thing, without changing anything. Committing then does what the preview showed. Example: `DataSeriesAlignmentPreview`. |
| `Suggestion` | A computed proposal for the operator to review. It is never applied automatically, and it reports the bounds it searched and the diagnostics that say how far to trust it. Example: `DataSeriesAlignmentSuggestion`. |
| `Scorer` | Rates one candidate for a search, which owns the candidates, the ranking and the diagnostics. A new criterion is a new scorer, not a new search. A scorer prepares its inputs once so each call is cheap. Example: `IAlignmentOffsetScorer`. |
| `Session` | The only way to change one kind of live state in the open workspace. It finds what depends on the change, previews it, rechecks and applies it, and records undo. Example: `IDataSeriesEditSession`. |
| `Patch` | What one recorded operation changed on one item: only the changed fields, with their values before and after. Reversing it writes back the before values instead of computing an inverse. Example: `DataSeriesRowPatch`. |
| `Log` | The ordered records of operations for one scope, holding those not yet saved. Saved records are only appended to. Example: `DataSeriesEditLog`. |
| `Watcher` | Observes state it doesn't own and raises one coarse event so a view can re-read. It makes no decisions. Example: `AttemptDataUseWatcher`. |
| `Descriptor` | An immutable description of what a value means (role, quantity, unit, how it wraps), derived from the metadata that owns it. It holds no display text; a presenter turns it into words. Example: `DataSeriesColumnDescriptor`. |
| `Entry` | One item in an ordered log, cache, manifest, menu or similar collection. Name the collection it belongs to. |
| `ViewModel` | Bindable presentation state and commands. Prefix it with the page, popup or item it represents. |
| `Page` / `Window` | A concrete WPF page or window. Use `Popup` for popup content and workflows, and `Dialog` only for a real dialog. |
| `Definition` | Declares how something is built and presented. Used mainly for `PopupDefinition` types. |
| `Handler` | A participant an orchestrator calls for a named phase, `Load` or `Save`. Put the phase in the name. |
| `Factory` | Creates instances when construction needs runtime inputs, owns a scope, or must be replaceable. |
| `Provider` | Supplies a value or capability that belongs to the environment, the host, a device or the current context. |
| `Converter` | Converts between representations without owning a workflow. WPF converters use the same suffix. |
| `Presenter` | Turns domain or configuration meaning into consistent user-facing text or visuals. It is not another word for a view model. |
| `Analyzer` | Inspects or computes over data it is given, without owning the surrounding workflow. Prefer `{Concept}Analyzer` to `{Concept}AnalysisService`. |
| `Repository` | A persistence abstraction for storing an aggregate, where repository semantics really apply. Don't rename every persistence service to it. |

## Design-System Vocabulary

Names in `geoDC.App.UI` describe reusable presentation meaning and stay free of domain terms.

| Term | Use |
|---|---|
| `Ui` prefix | A reusable type from `geoDC.App.UI`, such as `UiButton`, `UiIntent` or `UiSection`. Don't use it for app-local views or view models. |
| `Theme` | Colour, brush, typography and visual theme. |
| `Layout` | Spacing, thickness, sizing and page layout. |
| `Intent` | The shared semantic colour ramp: neutral, accent, success, warning, danger or in progress. Don't create separate colour vocabularies for domain concepts. |
| `Tone` | How much emphasis a text or control gets. |
| `Status` | A domain or workflow lifecycle value. Map it to a UI intent instead of embedding brushes. |
| `Variant` | A different visual form of the same control. |
| `Appearance` | A presentation mode that changes how the same value is drawn. |
| `Panel` | A WPF layout container. Don't use it for any bounded region. |
| `Specimen` | One captioned example in the Sample gallery. A control's specimens live in a `SpecimenPage` named `{Control}Specimens`, filed under a `GalleryGroup`, and specimen IDs end in `Panel`. See [`ui/sample-gallery.md`](../geodc/docs/ui/sample-gallery.md). |
| `Playground` | The Sample gallery's live editor for one control: an instance plus an editor for each public dependency property, built by `PropertyPlaygroundViewModel` from a `PlaygroundSetup`. See [`ui/sample-gallery.md`](../geodc/docs/ui/sample-gallery.md). |
| `Button`, `Input`, `Combo`, `Picker`, `Choice`, `Toggle`, `Tab`, `List`, `Item`, `Text`, `Badge`, `Page`, `Panel`, `Popup` | The only roles an AutomationId may end in. See [Automation ID roles](#automation-id-roles). |

## Automation ID roles

An AutomationId is PascalCase `{Noun}{Role}`. The noun says what the element is about. The role, the last word, says
what kind of element it is, whichever control implements it. The UI driver's `audit` verb checks IDs against its own
copy of this table (`AutomationConventions.Roles`), so add a new role to both at once.

| Role | Names | Examples |
|---|---|---|
| `Button` | Anything clicked to act: buttons, icon and ribbon buttons, menu buttons, clickable cards. | `SaveButton`, `AccentIconButton`, `VoidedButton` |
| `Input` | A value typed or edited in place: text, number, epoch and coordinate boxes. | `CloneNameInput`, `CrsPickerEpochInput` |
| `Combo` | A drop-down list of options. | `TargetSensorCombo` |
| `Picker` | A composite control for choosing one structured value (a CRS, a date, a sign convention). Its parts take their own roles. | `ImportCrsPicker`, `EquipmentCalibrationExpiryPicker` |
| `Choice` | A visible single-choice group: segmented options, radio groups, view-mode selectors. | `ImportSourceChoice`, `CanvasViewChoice` |
| `Toggle` | An on/off control: check boxes, switches, toggle buttons. | `AssignCrpToggle`, `DockPaneExpandToggle` |
| `Tab` | A navigation destination or section key: shell tabs, tab items, nav-bar items and settings sections. | `DataInputTab`, `GeneralTab` |
| `List` | A collection of rows or items: lists, data grids, nav bars, steppers. | `AttemptList`, `PointTableList` |
| `Item` | One addressable entry that is not data-bound: a menu item, a checklist row, a gallery nav item. Data-bound items are found by Name instead. | `SensorEditMenuItem`, `FoundationsNavItem` |
| `Text` | Read-only text or a value shown to read: labels, metrics, coordinate displays, status and validation messages. | `ShoreControlStatusText`, `PersonnelText` |
| `Badge` | A small status marker: count badges, delta chips, help badges, status icons. | `ShoreControlPendingBadge`, `FeatureDeltaBadge` |
| `Page` | A top-level page or a Sample gallery group page. | `SetupPage`, `InputsPage` |
| `Panel` | A region that groups other elements: sections, cards, empty states, scroll regions, chart hosts. | `DataInputPanel`, `FeatureListEmptyPanel` |
| `Popup` | A popup's own frame, when its definition sets one (`PopupPresentation.AutomationId`). | `AboutPopup`, `AddSensorPopup` |

Choose the role from what the element does for the person using it, not from its WPF type. A `CheckBox` is a
`Toggle`, a `UiMetric` is `Text`, and a `UiEmptyState` is a `Panel`. `Box`, `Metric`, `Section`, `State`, `Surface`,
`Card`, `Display`, `Icon` and similar type words are not roles.

Template element names (`x:Name`) inside `geoDC.App.UI` templates are not AutomationIds, and the audit doesn't check
them. WPF still reports an element's `x:Name` as its AutomationId when none is set, though, so avoid WPF type words
there too: `HelperText` and `UnitText`, not `HelperTextBlock` or `UnitLabel`, and `PathInput`, not `PathTextBox`.

## Overloaded Suffixes

These words are so broad that they hide a type's role. Each entry says what to prefer.

### `Service`

`Service` covers persistence ports, workflows, UI coordination, notifications, preferences, file dialogs, analysis
and package operations. You often have to open the file to learn what a `*Service` does. Prefer a role that says
what the type is responsible for:

- `Analyzer`, `Selector` or `Extractor`, when analysis, choosing or extraction is the whole job.
- `Workflow` for multi-step application orchestration.
- `Queries` for read-only access, as in `IFeatureStateHistoryQueries`.
- `Store` or `Repository`, only when the abstraction really behaves like one.
- `Navigator`, `Presenter`, `Exporter` or `Importer`, when that is the single responsibility.
- `Service`, when the capability is cohesive and no more precise role fits.

Don't strip `Service` from names mechanically. Some are conventional and clear, such as `INotificationService`.

| Instead of | Use | When |
|---|---|---|
| `*AnalysisService` | `*Analyzer` | The type's job is analysis. |
| `*SelectionService` | `*Selector` | The type picks from candidates. |
| `*PickerService` | `*Picker` | The type asks the user to choose. |
| `*ExtractionService` | `*Extractor` | Rename the Application interface and the Infrastructure implementation together. |
| `*ImportWorkflowService` | `*ImportWorkflow` | Always, because `Workflow` already says it orchestrates. |
| Persistence-facing `*Service` | `*Repository`, `*Queries`, or keep `*Service` | Decide from the aggregate and whether it reads or writes. |
| UI infrastructure: notification, navigation, popup, theme | Keep `*Service` | These names are conventional and understood. |

### `Model`

Don't use bare `Model` as a suffix (`ViewModel` is fine): it doesn't say whether the type is a domain entity, a
transport contract, a read model or a presentation model. Choose `ViewModel`, `Request`, `Result`, `Summary`,
`Snapshot` or the domain noun instead.

### `Data`

`Data` makes sense in chart contracts (`IChartData`, `ScatterChartData`) but is too broad elsewhere. Name the actual
contents (`Samples`, `Rows`, `Observations`, `Coordinates`) or the role (`Request`, `Result`, `Summary`).

### `State` and `Status`

- `Status` means a lifecycle or classified condition, usually an enum with defined transitions.
- `State` means the fuller state of an entity, operation or UI component.
- Keep domain compounds such as `FeatureState`, whose meaning is precise and documented.
- Don't use either as a suffix for a Boolean flag or a bag of unrelated properties.

### `Context`

Use `Context` for an explicit scope passed through an operation (`ProjectPackageContext`, a diagnostic context) or
one the framework establishes (`GeoDcDbContext`). Don't use it for a bag of dependencies. A cohesive set of
dependencies should usually be constructor parameters or a contract with a specific name.

### `Manager`, `Helper`, and `Utility`

Don't use these. Name the capability or operation instead.

## Review and Rename Process

Before you consolidate a family of names:

1. List every declaration and public member that uses the term, across `src`, `tests`, tools, XAML, serialization
   and documentation.
2. Group the uses by meaning, not by spelling.
3. Agree the target definition and its exceptions, and record them in this page.
4. Rename one coherent family at a time, including interfaces, implementations, registrations, tests, file names,
   XAML bindings and templates, and documentation.
5. Treat serialized names, package manifests, the database schema, string-resource keys and report contracts as
   compatibility boundaries. Don't bulk-rename them without a migration plan.
6. Build and run the focused tests for every affected layer. Use the UI driver when visible UI or AutomationIds
   change.

Keep vocabulary changes separate from formatting changes. A rename should make a boundary clearer, not just make the
repository more uniform.
