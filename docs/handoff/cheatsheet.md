# Handoff cheat sheet

Short answers to "how is this calculated" and "where does this come from", written to be searched with Ctrl+F. Each
entry starts with the words someone is likely to use, gives the answer, then the types to open (Ctrl+T in Visual
Studio or Rider).

Checked against geoDC `4f793ea9` (`dev`, 2026-11-15). Paths are relative to the geoDC repository root, and
`Features/…`, `Shared/…`, `Shell/…` and `Workspace/…` are under `src/geoDC.App/`.

## Architecture in ten lines

1. **Layers point inward.** Measurements, then Domain, then Application, then Infrastructure and the app. A lower
   layer never references a higher one.
2. **The domain is pure.** `geoDC.Domain` has no database, DI, logging or UI. Survey rules and arithmetic live there
   as plain types and static functions, and are tested on their own.
3. **Application holds contracts.** Interfaces and request types. Infrastructure implements them: EF Core and
   SQLite, file import, PROJ, Typst.
4. **The UI toolkit knows no domain.** `geoDC.App.Core` and `geoDC.App.UI` are a reusable shell and control set.
   Only `geoDC.App` maps domain types to screens.
5. **The app is organised by feature.** One folder per nav bar page under `Features/`. Pages are MVVM: XAML binds to
   a page viewmodel, with no logic in code-behind.
6. **Pages edit an in-memory workspace.** Opening a project loads it into viewmodels on `AppProjectWorkspace`. Save
   writes them back through ordered handlers. Nothing reaches the database between saves.
7. **Results are derived, inputs are stored.** A calibration, verification or GNSS result is recomputed from stored
   inputs, on screen and in the report.
8. **One convention for calculation.** Everything is stored and computed in GEOdc Standard. Other conventions exist
   only at the edges: import, display and export.
9. **Every change to work goes through a guard.** It knows what depends on what, invalidates calculations, and
   protects finalised work.
10. **Everything is reproducible and audited.** No clock or machine state feeds a calculation, report or
    fingerprint, and every saved change writes an audit entry.

## The general answers

**Where does a value on screen come from?** Follow four hops. The label's text is in `Strings/<area>.yaml`. The view
is `Features/<Area>/<Name>Page.xaml`, where the value is a `{Binding …}`. The binding names a property on the page
viewmodel beside it. That property reads either a workspace viewmodel (stored data) or a result object (calculated).
`docs/finding-things.md` has the method.

**How do I find the code behind any text?** Hover it with the text inspector (Ctrl+Shift+I, Debug builds), or run
`dotnet run --project tools/geoDC.FindText -- <words on screen>`.

**Is this value stored or calculated?** Inputs are stored: positions, datasets, match settings, corrections, limits,
lifecycle stamps. Results are calculated each time: C-O, sigma, residual, pass or fail, GNSS differences, drift.

**Where is stored data loaded and saved?** `Workspace/Handlers`: one `…LoadHandler` and `…SaveHandler` per
aggregate. The database is `GeoDcDbContext` (`src/geoDC.Infrastructure/Persistence`).

**Where is it calculated?** In a static domain type: `CalibrationComputer`, `GnssComputer`, `SeriesMatcher`,
`RollConversion`, `BaselineAlignmentSolver`. The app calls them through `AttemptComputation`
(`Features/Attempts`). The report calls them through `EfCalibrationService`, `EfVerificationService` and
`EfGnssCheckService` (`src/geoDC.Infrastructure`).

**Why are there two calculation paths?** The page computes from the workspace's unsaved viewmodels. The report
computes from the saved database. Both call the same domain functions, so the arithmetic is written once.

## Calibration

**C-O, offset, correction: how is it calculated?** For each matched sample, computed angle minus observed angle plus
the existing C-O, wrapped into [−180°, 180°). The headline C-O is the mean of those values.
`CalibrationComputer.ComputeCO`, `CalibrationComputer.ComputeStatistics`, called from
`AttemptComputation.ComputeCalibration`.

**Computed angle: where does it come from?** From the two prisms tracked from shore. It is the angle of the line
from CP1 to CP2 in the world, minus that line's angle in the vessel frame, plus the baseline correction, plus grid
convergence for heading. `CalibrationComputer.ComputeAngles`.

**Heading: how is the computed heading derived?** Grid bearing of CP1 to CP2, `atan2(ΔEasting, ΔNorthing)`, minus
the baseline's bearing from the bow in the vessel frame, `atan2(Δstarboard, Δforward)`. Wrapped into [0°, 360°).
`CalibrationComputer.DeriveWorldAngle`, `CalibrationComputer.ComputeVrfBaseline`.

**Pitch: how is the computed pitch derived?** Elevation of the CP1 to CP2 line above horizontal,
`atan2(ΔHeight, horizontal distance)`, minus the baseline's rise towards the bow in the vessel frame. Same two
functions.

**Roll: how is the computed roll derived?** As pitch, but the vessel-frame angle is the baseline's rise towards
port. The prisms give gravity roll. See "Euler roll" below.

**Observed angle: where does it come from?** From the vessel's own log of the sensor, imported as a dataset and
attached to the attempt's observed slot. It is converted from its import convention to GEOdc Standard before any
arithmetic. `ImportedAngleSeries.InStandard`.

**VRF baseline angle: what is it?** The angle of the prism baseline in the vessel frame, from the two prisms'
surveyed positions. The pages and the report show it so it can be checked against another package.
`CalibrationComputer.ComputeVrfBaseline` and `ReadVrfBaseline`.

**Prism positions, CP1, CP2: where do they come from?** The prisms are features on the Survey page. Their vessel
positions are the job's `FeatureState` records. Their world positions over time are two imported position datasets.

**Baseline correction: what is it?** A manual angle the surveyor can add to every computed angle. Stored on the
attempt's sample (`ControlPointSample`).

**Existing C-O: what is it?** The correction already applied in the sensor while the data was recorded. The crew
reads it off the device, the page takes it in the device's terms, and it is stored as a GEOdc Standard correction.
Adding it makes the result the total correction, not the change.

**Grid convergence: where does it come from?** Heading only. Either typed, or computed from the job's Shore Control
copy: the meridian convergence of its projected CRS at the mean position of its points, through PROJ. It is added to
the computed heading. `ShoreControlGridConvergence` (`src/geoDC.Application/Calibration`),
`ICoordinateTransformService.GridConvergenceAsync`.

**Sigma, standard deviation: how is it calculated?** Population standard deviation of the per-sample C-O values,
dividing by n. `CalibrationComputer.ComputeStatistics`, `COStatistic`.

**Mean, min, max, range.** Mean is the arithmetic mean of the per-sample values. Range is max minus min. Angles are
first unwrapped about their circular mean, so 359° and 1° average to 0°. `CircularAngles`.

**Pairs, samples used: what is the count?** The number of matched samples that produced a C-O value. Every one is
used. There is no outlier rejection inside the calculation.

**Outliers, excluded rows: how are they handled?** In the Data analysis popup, before calculation. Rows the operator
excludes take no part in matching. Three preview rules are offered: sigma from mean, median absolute deviation, and
residual from a linear trend. `DataSeriesAnalyzer.PreviewOutliers`.

**Result window, time span.** The first and last timestamps of the matched samples behind the result.
`ResultWindowFormat`.

**Matching, pairing, time tolerance: how are samples paired?** One to one, by equal index, by the number an index
ends in, or by timestamp within a tolerance after adding an offset to the second series. Closest candidates pair
first. The settings are stored on the attempt. `SeriesMatcher.Match`, `SeriesMatchConfiguration`, `MatchStrategy`.

**Latency, time offset, motion alignment: how is it found?** By trying a grid of offsets and choosing the one that
gives the smallest scatter between the computed and observed angles. It is a suggestion the operator applies, and
the chosen offset is stored so nothing searches again. `MotionAlignmentAnalyzer.SuggestOffset`,
`AlignmentOffsetSearch`.

**Euler roll, gravity roll, pitch source: what is the conversion?** `g = asin(sin r · cos p)`, where g is gravity
roll, r is Euler roll and p is pitch. With a pitch dataset, the computed roll is converted to Euler roll at each
sample. Without one the result is marked not converted, with an error bound if a largest pitch was entered.
`RollConversion`, `CalibrationComputer.ComputedInStandard`, `UnconvertedRollBound`.

**Sign convention, positive direction: which is used?** GEOdc Standard for all arithmetic: heading clockwise, pitch
bow up, roll starboard down. A dataset keeps the convention it was imported in and is converted on the way in. The
project's display convention only changes what is shown. `AttitudeConvention`, `DisplayedCalibrationResult`.

**Why does the C-O on screen have the opposite sign to the stored one?** The device profile says the receiving
software wants a mount angle, which is the negated correction, or a different export convention. The stored value
is always the GEOdc Standard correction. `CoSemanticsPresenter` (`Shared/Conventions`), `DeviceProfile`.

**Correction value or mount angle: what is the difference?** A correction is added to the sensor's readings. A mount
angle is subtracted by the receiving software, so it is the negation. The device profile says which one each axis
expects.

**Decimal places, precision, rounding.** Stored values and statistics are `decimal`. Trigonometry runs in `double`
and is converted back. Displayed places are a preference: 3 for angles, 5 for coordinates, 3 for distances by
default. Settings > Display & precision, `PreferenceDefaults`.

**How do we know the calculation is right?** `CalibrationComputerGeobaseReferenceTests` checks it against rows from
a real GEOBASE project to nine decimal places. `CalibrationComputerTests` covers the cases around it.

**Warnings and findings on the page: where do they come from?** Diagnostic rules per page, for example a baseline
off its axis plane, reversed prisms or unconverted roll. `Features/Calibration/Diagnostics`,
`PrismBaselineFindings`, `BaselineSkew`.

## Verification

**Residual: how is it calculated?** Exactly as a C-O, with the applied C-O in place of the existing C-O.
`AttemptComputation.ComputeVerification`.

**Applied C-O: what is it?** The correction the crew entered after the calibration, read off the device.

**Pass, fail, outcome: how is it decided?** Fail when the absolute mean residual exceeds its limit or the sigma
exceeds its limit. A value equal to its limit passes. `AcceptanceCriteria.Evaluate`.

**Acceptance limits, tolerance: where do they come from?** Entered on the verification and stored with it:
`MaxMeanAbsoluteDegrees` and `MaxSigmaDegrees`.

**Recalibrate: what does it copy?** The verification's installation, axis, datasets and settings into a new
calibration, with the applied C-O as the new existing C-O. `RecalibrationWorkflow`.

**Superseded: what does it mean?** A newer calibration of the same sensor axis replaced this one. It is a link to
the replacement, not a state. `CalibrationSupersession`.

## Lifecycle and edits

**Draft, Calculated, Finalised, Stale: what sets the state?** Two stored stamps. A calculation stamp means
Calculated. A finalisation stamp as well means Finalised. Neither means In progress. Stale is the page's word for
an attempt whose calculation was withdrawn by an input change. `AttemptLifecycle`, `CalibrationLifecycle.StageOf`.

**Why did my result disappear after an edit?** The edit changed an input, so the guard invalidated the calculation.
Recompute it. `WorkspaceAlterationService`, `AlterationGuard`, `WorkItemDependencies`.

**Why can't I edit this? Locked.** The attempt is finalised. Reopen it first. Editing something a finalised attempt
depends on asks for confirmation and lists the affected work.

**Why can't I save?** Finalised work has an input that changed without confirmation. Reopen that work or undo the
change. `WorkspaceAlterationService.GetChangedFinalisedWork`, with `LifecycleSaveGuard` as the backstop at save.

**Void: what is it?** A recorded decision that an attempt is not valid for use, with who, when and why. It stays in
the project. `VoidRecord`.

**Who and when: where do names and times come from?** The current actor from `ISystemActorProvider`, and UTC time
at the moment of the action. Stored on the attempt and in the audit log.

## Survey and vessel frame

**Position, offset, X Y Z: where does it come from?** Typed, pasted or imported on the Survey page, or carried
forward from the previous job. Stored per job on `FeatureState` in GEOdc Standard axes: +X forward, +Y starboard,
+Z down.

**Why do the axes on screen differ from the stored ones?** The vessel's display convention relabels and reorders
axes for display and entry. It moves nothing. `PositionConvention`, `VesselDisplayConventions`.

**Sensor position: where is it stored?** On the installation's feature state for the job, never on the instrument.
`InstrumentInstallation`, `FeatureState`.

**CRP, origin.** The primary point of the reference-frame group, pinned at (0, 0, 0). `CrpOriginPin`.

**Established: when is the frame established?** The orientation is confirmed, the CRP has a position in the job,
and one other reference point has one. `VrfEstablishment`.

**Drift, delta, shift since last survey: how is it calculated?** Current position minus the previous job's
position, per axis, with horizontal and 3D distances. It is flagged when the horizontal or vertical shift exceeds
its tolerance: 5 mm for structure points and 10 mm for others by default. Carried-forward positions are never
flagged. `CrossSurveyDelta`, `PositionShiftTolerance`, `PositionShiftDefaults`.

**Transformation, translate, align, rotate: how is it calculated?** A rigid transform, rotation then translation,
applied to every measured and carried-forward position in the job. Align fits the rotation to surveyed baselines:
the primary baseline exactly, the others by least squares. `BaselineAlignmentSolver`, `VrfTransformation`,
`VrfTransformationReviewer`.

## Shore Control and GNSS

**Coordinates in another CRS: how are they transformed?** By PROJ, in strict mode. A transformation that would
ignore a datum difference, drop an epoch or skip a missing grid is refused with a reason.
`ProjCoordinateTransformService` (`src/geoDC.Infrastructure/Crs`), `docs/coordinate-transforms.md`.

**Are transformed coordinates saved?** No. A point stores what the operator entered, in the CRS they stated.
Transformed values are derived for display.

**Register or copy: which am I looking at?** The register is the shared store on this machine. A job's copy is a
snapshot inside the project, and calculations only ever read the copy. `docs/shore-control.md`.

**GNSS check, ΔE ΔN ΔH: how is it calculated?** Observed minus reference on each axis, per matched point, with
horizontal distance `sqrt(ΔE² + ΔN²)` and 3D distance. Both series are first brought into one comparison frame.
`GnssComputer.ComputeDelta`, `GnssCheckComputation`, `GnssCheckCalculator`.

**GNSS pass or fail.** A point fails when its horizontal distance or its absolute height difference exceeds the
limit. The check passes only when every point passes. With no limits it is unassessed. `GnssAcceptanceCriteria`.

**GNSS average, max, worst point.** Mean and maximum of the points' horizontal distances and absolute height
differences, and the points with the largest of each. `GnssCheckComputation`.

**Comparison frame: where does it come from?** Set on the check. If unset when a reference series is bound, it
defaults to the reference's CRS if projected, otherwise its UTM zone. `GnssComparisonFrameDefaults`,
`GnssComparisonBasis`.

## Data, report and project

**Dataset, series: where do the rows come from?** The import wizard reads a delimited text file or Excel workbook.
Rows are stored as imported, with their original values kept beside any edit. `DataSeriesImportWorkflow`,
`DataSeriesHeader`, `DataSeriesRow`, `docs/data-analysis.md`.

**Timestamps: how are they read?** The format is detected from the values and the operator chooses when it is
ambiguous. Everything is stored in UTC. A column of bare times needs a date from the operator and is never dated
from the clock. `TimestampFormatInference`.

**Edited data: can I see what changed?** Every edit is recorded with the operator and the rows it changed.
`DataSeriesEdit`.

**Report values: where do they come from?** From the saved project, not the screen. The builder reads the job from
the database and recomputes each result with the same domain functions. `JobReportRequestBuilder`,
`JobReportData`, templates in `src/geoDC.Infrastructure/Reporting/Templates/job-report`.

**Why does the report differ from the screen?** Unsaved changes. The Reports page warns about them.

**Fingerprint: how is it calculated?** A hash of a canonical JSON projection of the stored domain data, ordered by
business keys with IDs replaced by surrogates. Audit timestamps are left out. `ProjectFingerprint`.

**Audit: what is recorded?** Every saved change: the actor, the entity, the action and before and after snapshots.
`AuditLogEntry`, `AuditSaveChangesInterceptor`.

**Job progress, stages on the Landing page.** Derived from the job's data each time, not stored.
`ProjectLandingJobStageBuilder`.

**Project file: what is in a `.gdcproj`?** A compressed archive with `project.sqlite`, a manifest with checksums,
and managed files such as photos. `ProjectPackageService`, `docs/project-packages.md`.

**Charts: where does chart data come from?** Assembled in C# from the same series and results, then drawn by D3 in
a WebView. `ChartDataAssembler`, `docs/charting.md`.

**Logs: where are they?** `geodc-*.log` in the per-user data folder, and Diagnostics > Logs in the app.
`docs/logging.md`.

## If you don't know

Say you'll trace it, then do it live: hover the value with the text inspector, find the binding in the page XAML,
open the viewmodel property, and follow it to the domain type. That takes about a minute and shows the developer
the method they will use themselves.
