# geoDC and GEOBASE: lineage table

geoDC has no formal specification. Its requirements were GEOBASE's behaviour plus what was agreed with the client
while geoDC was built. This table stands in for a specification at handoff: for each area it sets what GEOBASE did
beside what geoDC does, says whether geoDC kept, changed or added it, and gives the reason.

The geoDC column is drafted from the geoDC docs and code at `4f793ea9` (`dev`, 2026-11-15). The other three columns
are for the project owner to fill in. A GEOBASE cell is pre-filled only where the repository records a fact about
GEOBASE, and each of those is marked "(recorded)" with its source in [Sources](#sources). Check them before relying
on them.

For **Relation**, write one of: kept, changed, added, dropped.

## Project and jobs

| Area | GEOBASE | geoDC | Relation | Why |
| --- | --- | --- | --- | --- |
| Project file | | One `.gdcproj` package per project, holding a SQLite database, attachments and a manifest with checksums. Integrity and schema version are checked on open. Optional encryption. A package at another schema version is rejected. | | |
| Jobs | | A project holds a vessel and its jobs. A job is one visit: its people, equipment, dates, survey, calibrations, checks and report. It moves Planned, Active, Survey Complete, Job Complete, and can be closed separately. The Landing page shows each job's progress. | | |
| Comparison between surveys | | Each measured position is compared with the previous job's under horizontal and vertical tolerances. A shift beyond tolerance is flagged as drift and stays pending until someone accepts it with a name and reason. | | |
| Migrating GEOBASE projects | | None. An importer for GEOBASE projects was built and later removed. | | |

## Vessel frame and sensors

| Area | GEOBASE | geoDC | Relation | Why |
| --- | --- | --- | --- | --- |
| Vessel reference frame | | Named points (features) in groups that stand for real assemblies. The origin (CRP) is a designated point pinned at (0,0,0). The axis orientation is confirmed and locked. A frame counts as established for a job once the orientation is confirmed and the CRP and one other reference point have positions. | | |
| Position axes | (recorded) +X starboard, +Y forward, +Z up. | Everything is stored and computed in GEOdc Standard: +X forward, +Y starboard, +Z down. Each vessel has a display convention, which can be any axis naming including GEOBASE's. It changes labels and entry only. Entered positions are converted, and the convention they were entered in is recorded. | | |
| Frame transformations | | Translate to a new origin, align axes from surveyed baselines by least squares, or enter a rigid transform. Each goes through a review that needs a reason, is recorded append-only, and is corrected by reversing it. A rotation sends dependent calculations back to In progress. | | |
| Sensors and positions | | An instrument is the physical unit and has no position. Its position belongs to an installation: that instrument on that mount for that job. On a new job a position is carried forward or measured again. | | |
| Field records | | Notes and photos attached to the job's points and work. | | |

## Data

| Area | GEOBASE | geoDC | Relation | Why |
| --- | --- | --- | --- | --- |
| Importing datasets | | A three-step wizard for delimited text and Excel. File type, delimiter, decimal separator and timestamp format are detected, and the operator chooses when a format is ambiguous. One file can give several datasets: every sensor's angles, or several prisms side by side. Each dataset keeps the angle convention it was imported in. | | |
| Inspecting and editing data | | A Data analysis popup with charts. Series are paired by timestamp, by an instrument index, or by aligning their motion to find the sensor's latency. Every edit to rows is recorded, can be undone, and is refused while finalised work depends on the series. | | |

## Calibration and verification

| Area | GEOBASE | geoDC | Relation | Why |
| --- | --- | --- | --- | --- |
| C-O calculation | (recorded) The same calculation: geoDC's results are pinned to rows from a real GEOBASE project. | Computed angle from two tracked prisms, less the baseline's angle in the vessel frame, plus grid convergence for heading. C-O is computed minus observed plus the existing C-O. Mean, sigma and range are taken about the circular mean. Heading, pitch and roll are each calibrated on their own. | | |
| Roll definition | (recorded) Roll derived as atan(−Δz / Δx), a gravity-style tilt. The design record says this needs checking. | Prisms give gravity roll and most sensors report Euler roll. A roll attempt can take a pitch source and compare both sides as Euler roll. Without one it still calculates, records that roll was not converted, and states the error bound when a largest pitch is given. | | |
| Angle sign conventions | (recorded) Internal signs match GEOdc Standard. | Four conventions are kept apart: the project's display convention, the one each dataset was imported in, GEOdc Standard for every calculation, and the one a result is exported in. Conventions can be saved under a name, and a project keeps its own copy of each one it uses. | | |
| Handing the C-O to the crew | | A device profile records what the receiving software expects on each axis: its convention, and whether it wants a correction or a mount angle, which have opposite signs. Every C-O for hand-off is shown with both. The existing and applied C-O are entered as the crew reads them off the device. | | |
| Verification | | Repeats the measurement after the crew applies the C-O. The residual is judged against limits on the absolute mean and the sigma. Pass or fail is separate from the workflow, so either can be finalised. Recalibrate starts a new calibration from a verification, carrying its applied C-O forward as the existing C-O. | | |
| Work-item lifecycle | | Calibrations, verifications and GNSS checks share one lifecycle: In progress, Calculated, Finalised, moving one step at a time. Changing an input invalidates the calculations that used it. Finalised work must be reopened first, with a confirmation that lists what depends on it. A project cannot be saved while finalised work has unconfirmed input changes. Work can be voided and restored, and stays on record. | | |

## Shore control and GNSS

| Area | GEOBASE | geoDC | Relation | Why |
| --- | --- | --- | --- | --- |
| Shore control | (recorded) Entered points with their CRS and epoch, transformed them to another CRS and epoch, and kept them as a set for a dock or site. Had a shore control editor and a projection request panel. Parity with this was the target for geoDC 2026.1. | Sets live in a register on the user's machine that outlives projects. A job works on its own copy, taken into the project with the CRS definitions it needs, and the operator pushes, pulls or ignores differences. A copy that fed finalised results is locked. Entered coordinates are never overwritten by transformed ones. Show in views a set in another CRS. A map shows the points. | | |
| Station types | (recorded) A fixed classification of how a mark's coordinates were established. | The same classification, shown on the report's station description sheet. | | |
| CRS transformations | | Done by PROJ through geoDC's own binding, with bundled transformation grids. A transformation that cannot be done to the required standard is refused, not approximated. Datum realisation and epoch are part of every comparison. | | |
| GNSS spot checks | | Compares where the antenna should be, from the vessel frame and shore control, with what the GNSS reports, as differences in easting, northing and height against optional limits. Uses the shared lifecycle. | | |

## Output and assurance

| Area | GEOBASE | geoDC | Relation | Why |
| --- | --- | --- | --- | --- |
| Client report | | A PDF per job, rendered with Typst from the saved project. Sections cover identification, the job, personnel, equipment, the reference frame, feature positions, shore control, calibrations, verifications, GNSS checks and provenance. It can also be rendered from the command line without the app, in which case it has no charts. | | |
| Charts | | Interactive charts in the app, drawn with D3, and the same charts exported for the report. | | |
| Audit | | Every saved change writes an append-only audit entry with the actor, the before and after values and an optional reason. Finalising, reopening, voiding, frame transformations and drift acceptance record who and why. | | |
| Reproducibility | | A project fingerprint is a hash of the stored domain data. It does not depend on the clock or the machine, so two copies of a project can be shown to be the same. | | |
| Platform | | A Windows desktop application on .NET 10 and WPF, installed from an MSI. Light and dark themes. All text is held in translatable catalogues, and it ships in English. | | |

## Sources

Where each geoDC cell comes from, in the geoDC repository unless marked as a harness path.

| Area | Read |
| --- | --- |
| Project file | `docs/project-packages.md` |
| Jobs, comparison between surveys, vessel frame, sensors | `docs/domain-context.md`, `docs/data-model.md` |
| Position axes, sign conventions, device profiles | `docs/domain-context.md` > Sign Convention; harness `docs/design/angle-position-conventions.md` |
| Importing, inspecting and editing data | `docs/data-analysis.md` |
| C-O calculation, roll, verification | `docs/data-model.md` > Work Items; `src/geoDC.Domain/Calibration/CalibrationComputer.cs` |
| Work-item lifecycle | `docs/work-item-alterations.md` |
| Shore control, station types | `docs/shore-control.md`; harness `docs/design/shore-control-workbench.md` |
| CRS transformations | `docs/coordinate-transforms.md`, `docs/proj-native.md` |
| Client report, charts | `docs/reporting.md`, `docs/charting.md` |
| Audit, reproducibility | `docs/data-model.md` > Audit; `src/geoDC.Infrastructure/Diagnostics/ProjectFingerprint.cs` |
| Platform | `docs/codebase.md`, `docs/installer.md`, `docs/string-resources.md` |

The recorded GEOBASE facts come from four places:

- Position axes, roll derivation, sign match and the removed importer: harness
  `docs/design/angle-position-conventions.md`.
- Shore control and the 2026.1 parity target: harness `docs/design/shore-control-workbench.md`.
- Station types: `src/geoDC.Domain/ShoreControl/ShoreControlStationType.cs`.
- The calculation match: `tests/geoDC.Domain.Tests/Calibration/CalibrationComputerGeobaseReferenceTests.cs`.
