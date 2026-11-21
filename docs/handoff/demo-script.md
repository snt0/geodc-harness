# Handoff demo script

A script for walking a developer through geoDC with the app on one side and the code on the other. Each stop gives
what to do in the app, what to say, and the hops from the screen to the viewmodel to the domain and persistence code.

Checked against geoDC `4f793ea9` (`dev`, 2026-11-15). The code hops were read from the source. The app steps were
written from the code and docs on Linux and have not been run, so rehearse them once on Windows and fix any wording
that doesn't match the screen.

Paths are relative to the geoDC repository root. A folder given as `Features/…`, `Shell/…` or `Workspace/…` is under
`src/geoDC.App/`. Types are given by name: jump to them with Go to Type (Ctrl+T in Visual Studio and Rider) instead
of browsing folders.

## Before the meeting

1. Build a Debug build of the app. Debug has the text inspector (Ctrl+Shift+I), which the tour uses.
2. Create both sample projects from the harness root:

   ```pwsh
   pwsh -NoProfile -WorkingDirectory geodc -File dev/ui.ps1 sample create
   pwsh -NoProfile -WorkingDirectory geodc -File dev/ui.ps1 sample create --lifecycle
   ```

   `sample.gdcproj` has one job with a pitch calibration cycle, a survey to align and a Shore Control set.
   `sample-lifecycle.gdcproj` has work items in every workflow state. Use the lifecycle project for the tour, and
   work on a copy so that a save during the demo doesn't change the original.
3. In the copy, find one calibration in each state before the meeting: one not yet calculated with its datasets
   attached, one calculated, and one finalised that has a verification. Note their names here: ________.
4. Open the IDE on `geoDC.slnx` with these files pinned, in this order: `docs/codebase.md`,
   `CalibrationShellPage.xaml`, `CalibrationPageViewModel.cs`, `AttemptComputation.cs`, `CalibrationComputer.cs`,
   `AttemptLifecycle.cs`, `CalibrationAttemptSaveHandler.cs`.
5. Have a terminal open at the geoDC root for the find-text tool and one test run.

## Running order

The core path is stops 1 to 6 and takes about 40 minutes. Stops 7 to 9 are optional and independent.

| Stop | Shows | Minutes |
| --- | --- | --- |
| 1. Purpose and map | What the software is for and how the solution is layered | 5 |
| 2. Screen to code | The method for finding the code behind anything on screen | 5 |
| 3. Opening a project | Package, database, workspace | 5 |
| 4. Compute a C-O | One feature from button to arithmetic and back | 12 |
| 5. Finalise, edit, reopen | The lifecycle and the alteration guard | 7 |
| 6. Save | Viewmodels to domain entities to SQLite | 5 |
| 7. Verification and recalibration | The second half of the cycle | 5 |
| 8. Report | Request builder and Typst | 5 |
| 9. Survey positions, Shore Control | Per-job positions, CRS transforms | 5 each |

## 1. Purpose and map

**Say.** geoDC replaces GEOBASE for vessel dimensional control. A surveyor sets up the vessel's reference frame,
records where sensors sit for a job, calibrates the INS heading, pitch and roll, verifies the corrections after the
crew applies them, and produces the client report. There was no formal specification. The requirements were GEOBASE's
behaviour plus the extensions agreed with the client while it was built.

**Show.** `docs/domain-context.md`, the list under "A DimCon survey delivers". Then the nav bar in the app: Setup,
Survey, Calibration, Verification, Shore Control, GNSS Check, Reports. Each is one deliverable or one step towards it.

**Show.** The layer diagram at the top of `docs/codebase.md`. Three points to make:

- `geoDC.Domain` has no EF, DI, logging or UI. The survey rules and arithmetic are there and are tested on their own.
- `geoDC.App.Core` and `geoDC.App.UI` know nothing about the domain. They are a reusable shell and control toolkit.
- `geoDC.App` joins them. Each nav bar page is one folder under `src/geoDC.App/Features/`.

**The link back to GEOBASE in code.** Open `tests/geoDC.Domain.Tests/Calibration/CalibrationComputerGeobaseReferenceTests.cs`.
It holds rows from a real GEOBASE project and asserts that geoDC computes the same angles and C-Os to nine decimal
places. This is the closest thing the project has to an executable specification.

## 2. Screen to code

**Say.** Every string on screen comes from a YAML file, and that gives one reliable way from the screen into the code.

**In the app.** On the Calibration page press Ctrl+Shift+I and hover over the "Compute C-O" button. The inspector
shows the strings entry behind it.

**In the terminal.**

```pwsh
dotnet run --project tools/geoDC.FindText -- Compute C-O
```

It prints the entry (`src/geoDC.App/Strings/calibration.yaml`, key `actions.computeCo`), the generated member
(`CalibrationText.ActionsComputeCo`) and every line of C# and XAML that uses it.

**Then the four hops, which are the same for every page:**

| Hop | Where | For this button |
| --- | --- | --- |
| Text | `src/geoDC.App/Strings/<area>.yaml` | `calibration.yaml`, `actions.computeCo` |
| View | `src/geoDC.App/Features/<Area>/<Name>Page.xaml` | `CalibrationShellPage.xaml` |
| Viewmodel | Beside the view, `<Name>PageViewModel*.cs` | `CalibrationPageViewModel` and its partial files |
| Rules | `src/geoDC.Domain/<Module>/` | `CalibrationComputer`, `AttemptLifecycle` |

`docs/finding-things.md` has the same method for popups, menus, grid columns and charts.

## 3. Opening a project

**In the app.** Start the app. The project selector opens first. Open the copy of the lifecycle sample.

**Say.** A project is one `.gdcproj` file. It is a package holding a SQLite database. Opening it extracts and checks
the package, then loads the whole project into memory as viewmodels. Pages read and edit that in-memory workspace.
Nothing is written to the database until Save.

**In the code.**

1. `App.xaml.cs`, `OnStartup`: builds the host and shows the selector. Services are registered in
   `src/geoDC.App/Startup/AppComposition.cs`. Its `AddViews` lists the pages.
2. `ProjectSessionService.OpenAsync` (`Features/Launch`): the open sequence.
3. `ProjectPackageService.OpenOrCreateAsync` (`src/geoDC.Infrastructure/ProjectPackages`): extracts the package and
   validates integrity and compatibility. `docs/project-packages.md` describes the format.
4. `WorkspaceModuleCoordinator.LoadAsync` (`src/geoDC.App/Workspace`): runs every `IModuleLoadHandler` in order.
5. One handler per aggregate in `src/geoDC.App/Workspace/Handlers`, for example `CalibrationAttemptLoadHandler`.
   Each reads domain entities through EF Core and fills a collection on `AppProjectWorkspace`.
6. `AppProjectWorkspace`: the collections every page shares (`Jobs`, `Vessels`, `Instruments`,
   `CalibrationAttempts`, `DataSeriesHeaders` and the rest). They are undo-tracked.

**Expect this question.** "So the domain entities aren't what the UI edits?" Correct. The UI edits viewmodels in the
workspace. The load handlers build them from domain entities, and the save handlers build or update domain entities
from them (stop 6). Rules that must hold in both places are domain types that both sides call, such as
`AttemptLifecycle`.

## 4. Compute a C-O

This is the main slice. Go slowly, and keep the app and the IDE side by side.

**In the app.** Calibration page. Select the calibration that is not yet calculated. Point at three things: the two
control point datasets and the observed dataset in the data input section, the header's accent button reading
"Compute C-O", and the empty result panel. Press the button. The header now shows the C-O, sigma and pair count, the
chart draws, and the accent button changes to "Finalise".

**Say.** A calibration compares what the sensor reported with what the survey says the angle was. Two prisms on the
vessel are tracked from shore. Their positions give the true heading, pitch or roll. Computed minus observed is the
C-O, the correction the crew enters.

**In the code.**

1. View: `CalibrationShellPage.xaml`. Find `AttemptLifecycleCommandBar`. The accent button binds `Command` to
   `LifecyclePrimaryCommand` and `Label` to `LifecyclePrimaryLabel`. The page's `DataContext` is set in
   `CalibrationShellPage.xaml.cs` from the injected `CalibrationPageViewModel`.
2. Which command: `CalibrationPageViewModel.Lifecycle.cs`. `LifecyclePrimary` asks `CalibrationLifecycle.PrimaryFor`
   for the next step, and `LifecyclePrimaryCommand` maps it to a command. One button walks the cycle: Compute,
   Finalise, Create verification, View verification.
3. The command: `ComputeSelectedSessionAsync` in `CalibrationPageViewModel.cs`. Read it top to bottom. It checks the
   attempt isn't locked, calls `AttemptComputation.ComputeCalibration`, asks the alteration service to confirm
   (`ConfirmWorkflowAsync`), stores the result on the attempt, records the calculation, logs, and sets the status
   message.
4. Orchestration: `AttemptComputation.ComputeCalibration` (`Features/Attempts`). About 30 lines. It loads the three
   series, pairs the two control point series by time (`SeriesMatcher.Match`), and calls the domain four times.
5. Arithmetic: `CalibrationComputer` (`src/geoDC.Domain/Calibration`), under 300 lines with no dependencies:
   - `ComputeVrfBaseline`: the angle of the prism baseline in the vessel frame.
   - `ComputeAngles`: the true angle for each matched pair of prism positions.
   - `Compare` and `ComputeCO`: computed minus observed plus the existing C-O, wrapped to ±180°.
   - `ComputeStatistics`: mean, sigma and range, unwrapped about the circular mean so headings either side of north
     average correctly.
6. Sign conventions: in `AttemptComputation`, `ObservedInStandard` converts the imported series with
   `ImportedAngleSeries.InStandard`. Every calculation runs in one convention, GEOdc Standard. Back in the command,
   `DisplayConvention.FromStandard` converts the result for display. `docs/domain-context.md` > Sign Convention
   explains why the four conventions are kept apart.
7. Back to the screen: the result is set as `LastComputedResult` on `CalibrationAttemptViewModel`. The header metrics
   and `AttemptResultPanel` (`Features/Attempts/Controls`) bind to properties derived from it.

**Tests to show.** `tests/geoDC.Domain.Tests/Calibration/CalibrationComputerTests.cs` and the GEOBASE reference
tests from stop 1 run anywhere:

```pwsh
dotnet test tests/geoDC.Domain.Tests --filter "FullyQualifiedName~CalibrationComputer"
```

The page's behaviour is covered by `tests/geoDC.App.Tests/Features/Calibration/CalibrationPageViewModelTests.cs`
(Windows).

**Expect these questions.**

- "Why is `CalibrationPageViewModel` so large?" It is about 2,800 lines in the main file plus four partials. The
  page has many inputs and states. The rules it applies are small and live elsewhere: `CalibrationLifecycle`,
  `AttemptComputation`, `CalibrationComputer`. Say which parts you would split first if asked.
- "Why is `AttemptComputation` in the app and not in `geoDC.Application`?" It takes viewmodels as input. Have your
  own answer ready on whether that should move.
- "Is the result saved?" The inputs and the calculation stamp are saved. The result is recomputed from the stored
  inputs when the attempt is opened, and `CalibrationLifecycle.StageOf` marks the attempt stale if it cannot be
  reproduced.

## 5. Finalise, edit, reopen

**In the app.** On the calculated attempt press "Finalise". The header mark fills in, the inputs lock and the accent
button becomes "Create verification". Now try to change one of the finalised attempt's inputs, for example the
measured position of one of its prisms on the Survey page. The app names the finalised work that depends on it and
asks for confirmation. Cancel it. Back on the Calibration page, use Reopen from the header's overflow menu.

**Say.** Finalised work is what the report and the crew rely on, so its inputs cannot change silently. Every edit
goes through a guard that knows what depends on what. Editing an input either invalidates the calculations that used
it or, for finalised work, requires an explicit reopen. A project with finalised work whose inputs changed cannot be
saved until that is resolved.

**In the code.**

1. `CompleteSelectedSessionAsync` and `UnlockSelectedSessionAsync` in `CalibrationPageViewModel.cs`. Both are a few
   lines: they call `_alterations.FinaliseAsync` and `_alterations.ReopenAsync`.
2. `WorkspaceAlterationService` (`src/geoDC.App/Workspace/Alterations`). `FinaliseAsync` shows the pattern every
   lifecycle change uses: check integrity, get a token from the guard, take a checkpoint, apply the transition, record
   it, and roll back to the checkpoint on failure.
3. `AttemptLifecycle` (`src/geoDC.Domain/Common/Workflow`). The whole state machine is one immutable record with four
   moves: `Calculate`, `Finalise`, `Reopen`, `Invalidate`. Each requires the adjacent state, so finalised work cannot
   be invalidated without being reopened first. Calibrations, verifications and GNSS checks all use it.
4. `AlterationGuard` and `WorkItemDependencies` (`src/geoDC.Application/Alterations`): which work depends on which
   input, and what a change does to it. `docs/work-item-alterations.md` is the reference.

**Expect this question.** "Why are the stored fields called `CompletedAtUtc` and `ApprovedAtUtc`?" They hold the
calculation and finalisation stamps under their earlier names. `AttemptLifecycle.FromStoredFields` is the one place
that maps them.

## 6. Save

**In the app.** Press Ctrl+S. Then open Diagnostics > Project fingerprint.

**Say.** Save writes the workspace back through the same ordered pipeline that loaded it. The fingerprint is a hash
of the project's stored domain data. It is reproducible from the data alone, so two people can confirm they hold the
same project, and tests can assert that a workflow produced the expected data.

**In the code.**

1. `MainShellViewModel.SaveProjectAsync` (`src/geoDC.App/Shell`) calls `ProjectFileWorkflowService.SaveAsync`
   (`Features/Launch`).
2. `WorkspaceModuleCoordinator.SaveAsync` runs every `IModuleSaveHandler` in order inside one unit of work.
3. `CalibrationAttemptSaveHandler` (`Workspace/Handlers`). For a new attempt it constructs the domain
   `CalibrationAttempt` from the viewmodel, replays its lifecycle transitions onto the entity, and adds it to
   `GeoDcDbContext`. `IDENTITY_CONVENTIONS.md` in the same folder explains how viewmodel and entity identities stay
   matched.
4. `GeoDcDbContext` (`src/geoDC.Infrastructure/Persistence`) with its audit interceptor, then
   `ProjectPackageService.SaveAsync` repacks the `.gdcproj`.
5. Fingerprint: `FingerprintPopupViewModel` (`Shell/Popups`) calls `IProjectFingerprintGenerator`, which is backed
   by `ProjectFingerprint` (`src/geoDC.Infrastructure/Diagnostics`).

## 7. Verification and recalibration

**In the app.** From the finalised calibration press "Create verification" or "View verification". On the
Verification page show the acceptance limits and the Pass or Fail outcome. On a failed verification show Recalibrate.

**Say.** Calibration and verification are one cycle. The crew applies the C-O, the surveyor measures again, and the
residual has to be inside the limits. A failure starts a new calibration that supersedes the old one, and the chain
stays visible for audit.

**In the code.**

1. `CreateVerification` in `CalibrationPageViewModel.cs` asks `IJobContentNavigator` to start a verification for the
   calibration. Navigation between pages is in `src/geoDC.App/Messaging`.
2. `VerificationPageViewModel.ComputeSelectedSessionAsync` calls `AttemptComputation.ComputeVerification`. It runs
   the same `CalibrationComputer` steps as a calibration, then `AcceptanceCriteria.Evaluate`
   (`src/geoDC.Domain/Verification`), which is ten lines: fail if the absolute mean or the sigma exceeds its limit.
3. `Recalibrate` in `VerificationPageViewModel.Lifecycle.cs` calls `RecalibrationWorkflow.Recalibrate`.
   `CalibrationSupersession` (`Features/Calibration`) keeps the links between the old calibration, the failed
   verification and the new calibration.

## 8. Report

**In the app.** Reports page. Show the contents list, then Generate, and open the PDF. Find the calibration from
stop 4 in it.

**Say.** The report is the client deliverable. It is built from the saved project, not from the in-memory workspace,
so the page warns when there are unsaved changes. Its provenance section records the application and template
versions it was made with.

**In the code.**

1. `ReportsPageViewModel.GenerateAsync` (`Features/Reports`), then its `RenderAsync`: two lines, build a request and
   render it.
2. `IJobReportRequestBuilder` and `ReportRequest` (`src/geoDC.Application`): the contract. `JobReportRequestBuilder`
   (`src/geoDC.Infrastructure/Reporting`) reads the job from the database and fills it.
3. `TypstReportServiceAdapter.GenerateAsync` writes the request as JSON and runs the Typst CLI over the templates in
   `src/geoDC.Infrastructure/Reporting/Templates/job-report`. `sections/calibrations.typ` is the section just shown.
4. The same two calls run headless, on Linux too:

   ```pwsh
   dotnet run --project tools/geoDC.ReportCli -- <project.gdcproj> --list-jobs
   ```

`docs/reporting.md` covers the request, the templates and how Typst is provisioned.

## 9. Optional stops

**Survey: positions belong to a job.** On the Survey page select a sensor mount and show its measured position, then
the sensor attached to it. In the code: `FeatureState` (`src/geoDC.Domain/VesselFrame`) holds the position for one
job, and `InstrumentInstallation` (`src/geoDC.Domain/Instruments`) links an instrument to a feature state for a job.
`Instrument` has no position, because the physical unit moves between jobs and vessels. The page is
`SurveyPageViewModel` and its partials. A measured position is changed through `WorkspaceFeatureMeasurementService`,
which goes through the same alteration guard as stop 5.

**Shore Control: CRS transforms.** On the Shore Control page use Show in to view the points in another CRS. In the
code: `ShoreControlTransformViewModel.TransformAsync` (`Features/ShoreControl/Workbench`) calls
`ICoordinateTransformService`, implemented by `ProjCoordinateTransformService`
(`src/geoDC.Infrastructure/Crs`), which calls PROJ through `geoDC.Proj`. `docs/shore-control.md` and
`docs/coordinate-transforms.md` are the references.

**Import.** From a dataset slot on the Calibration page choose Import. The wizard is `DataImportWizardViewModel`
(`Features/Import`). `DataSeriesImportWorkflow` validates the settings and calls `IFileIngestor` and
`IDataSetExtractor`, implemented in `src/geoDC.Infrastructure/Ingestion`.

## How the code was written

Say this plainly, early or when asked. Most of the code was written by AI coding agents working to your direction,
issue by issue. What keeps it consistent:

- The convention pages in `docs/`, which the agents and people both follow.
- Convention tests that fail the build on drift: `tests/geoDC.Infrastructure.Tests/Conventions` and
  `tests/geoDC.App.Tests/Conventions`.
- The checks in `docs/codebase.md` > Checks before merging and releases.
- The issue history in the geoDC repository, which records what was asked for and what was done.

Decide before the meeting whether the new developer is expected to work the same way, and say so.

## Rehearsal checklist

- [ ] Each app step above works on the lifecycle sample copy, and the names in "Before the meeting" are filled in.
- [ ] You can do stop 4 from the button to `CalibrationComputer.ComputeStatistics` and back without notes.
- [ ] You can state the seven rules in `docs/domain-context.md` > Rules That Hold Everywhere and point at where two
      of them are enforced (`AttemptLifecycle`, `InstrumentInstallation`).
- [ ] You have your own answer for the three "expect this question" items in stop 4.
- [ ] You know what is unfinished: the open issues and epics, and what has only been checked by compiling.
