# Angle and Position Conventions

> Moved from `geodc/docs/design/` on 2026-11-10. The current behaviour is in `geodc/docs`; open work is tracked in snt0/geodc#473.


The design of geoDC's convention system for vessel positions and attitude angles, agreed on 2026-10-17 during
requirements triage, before implementation. The current behaviour is described in
[domain-context.md](../../geodc/docs/domain-context.md) > Sign Convention and in [data-model.md](../../geodc/docs/data-model.md). This record
keeps the reasoning: why there is one canonical frame, why conventions are absolute directions, how roll is
defined, and what was left out.

## Status

When this record was last updated, the following were built:

- the convention primitives and the single table of physical meanings;
- feature positions and VRF transformations stored in GEOdc Standard axes (schema 7);
- imported series read for calculation only through `InStandard`, with gravity and Euler roll converted using the
  pitch source a roll sample stores (the pitch source arrived in schema 8, not schema 7);
- device profiles stored by value on instruments (schema 12), read by reports and fingerprints. Catalogue v1 ships
  only GEOdc Standard until the other entries' inferred values are confirmed or accepted;
- conventions on screen: point entry and survey-point import take a convention picked by direction, positions are
  shown in the vessel's display convention, and the project display convention is an Euler `AttitudeConvention`
  in which the calibration and verification pages show computed angles and take the baseline correction.

Not yet built: the VRF transformation tools still work in GEOdc Standard axes, there is no single C-O hand-off
resolver (§8), and reports and exports do not yet carry their own conventions.

## 1. Problem

A position or attitude value means nothing without its convention: which way each axis points, which direction
of rotation is positive, and how roll is measured. Before this design, that context travelled beside values rather
than with them:

- **The physical meaning was re-derived in several places.** `AxisSign` meant "same as, or opposite to, GEOdc
  Standard", and its physical meaning lived only in the docs. The UI direction mapping and toolkit captions, the
  report's Typst wording, two C-O hand-off implementations and the test-data generator each re-derived it, and one
  of those copies had roll backwards.
- **Some stored conventions were never applied.** Series import conventions and sample calculation conventions
  were persisted and fingerprinted, but no calculation read them.
- **Device profiles were not reproducible.** The fingerprint included the instrument's profile code, but the code
  resolved against a catalogue compiled into the app. A catalogue edit in a later release would change report
  hand-off values without changing stored data or the fingerprint.
- **Three sign systems shared one vocabulary:** `SignConvention`, `ZyxAngles` (signed in the frame's own axes) and
  `VrfAxes`. Positions were stored in each vessel's chosen axes, and `Point3D` carries no frame.
- **Device conventions could only negate an axis.** They could not express axis swaps, the roll definition, mount
  direction or output range.

Several earlier sign bugs had the same root cause.

## 2. Principles

1. **One canonical frame.** Everything geoDC stores, computes and fingerprints is in GEOdc Standard. Conventions
   exist only at the boundaries: input, display, export and reports.
2. **Physical meaning is defined once,** as absolute directions. Every caption, diagram and report phrase is
   generated from that definition.
3. **A value crosses a boundary only with its convention.** Code that needs canonical values gets them through an
   accessor that converts. Nothing hands out raw numbers as if they were canonical.
4. **Conventions are relabellings, not transformations.** Offsets, rotations and coarse mounts are frame
   transformations (VRF transformation, rigid transform) or alignment, and are modelled separately.
5. **Reproducible by value.** Anything that affects a report or a result is stored in the project by value, not by
   reference to a catalogue in the app.

## 3. The canonical frame: GEOdc Standard

- **Positions:** +X forward, +Y starboard, +Z down (right-handed).
- **Attitude:** rotations about the vessel's physical axes. Heading turns about the vertical, positive bow to
  starboard (clockwise seen from above). Pitch turns about the port–starboard axis, positive bow up. Roll turns
  about the fore–aft axis, positive starboard down. These are the right-hand rotations about +Z, +Y and +X.
- **Composition:** attitude and VRF transformation rotations are Tate-Bryant Z-Y-X: R = Rz(heading) · Ry(pitch) ·
  Rx(roll). Every vendor source that states an order uses this one.
- **Canonical roll is Euler (rotation-plane) roll.** §6 covers gravity roll and the conversion.

A vessel's own axis directions stop being a storage frame and become the vessel's display position convention (§7).

## 4. Position conventions

A position convention says which physical direction each of a system's x, y and z axes points: "+X starboard, +Y
forward, +Z up". Mathematically it is a signed permutation of the canonical axes. All 48 are allowed, including the
24 reflections, because real systems use left-handed frames (HYPACK, CARIS, EIVA NaviEdit).

```text
PositionConvention
  Name
  XDirection, YDirection, ZDirection   // each one of Forward, Aft, Starboard, Port, Up, Down
  invariant: the three directions lie on three different physical axes
  ToStandard(Point3D) / FromStandard(Point3D)   // exact; no rounding beyond decimal arithmetic
  IsRightHanded                                 // informational; reflections are valid conventions
```

- **The operator thinks in directions, so the UI asks for directions,** such as "+X points to: Starboard". A
  convention is never entered as a matrix.
- **Every entered or imported position states its convention.** The entry control shows a small vessel diagram
  marking where the point lands, as a sanity check. For example, x = −10 under "+X down" is 10 m above the origin.
- **Storage:** feature positions are stored canonical, and the convention a value was entered in is recorded with
  the measurement for audit. Position series, such as GNSS, are not VRF positions: they are in a CRS and have their
  own CRS handling.

## 5. Attitude conventions

An attitude convention states, for each physical axis, which direction is positive, as an absolute direction:

```text
AttitudeConvention
  Name
  Heading: Clockwise | CounterClockwise
  Pitch:   BowUp | BowDown
  Roll:    StarboardDown | StarboardUp
  RollDefinition: Euler | Gravity          // §6
  ToStandard(angle, axis [, pitch]) / FromStandard(...)
```

- **Absolute directions replace the relative `AxisSign`.** "Positive" no longer means "whatever Standard means".
  Each convention names the physical direction, and Standard is one instance (Clockwise, BowUp, StarboardDown,
  Euler). There is nothing left to re-derive, which removes the class of bug where a copy had roll backwards.
- **One source for wording.** A single domain table maps each direction to its captions, diagram arrow and report
  phrase. The UI toolkit enum and the Typst wording are generated from it or tested against it.
- **Independent of position labelling.** A device that numbers its position axes differently (Seapath's (Y, X, −Z),
  NovAtel's X right) does not change what its pitch means. When a system defines angles as rotations about its own
  numbered axes, its catalogue entry records the resulting physical directions.
- **Heading range is not part of an input convention.** Incoming headings are wrapped, so 0–360 and ±180 read the
  same. Range is an output choice (§8).

## 6. Roll definition: Euler and gravity roll

There are two ways to measure roll, and they agree when pitch is zero:

- **Euler (rotation-plane) roll** is the rotation about the vessel's fore–aft axis, measured in the plane square to
  that axis, which tilts with pitch. It is Tate-Bryant roll and the canonical definition.
- **Gravity (horizontal-plane) roll** is the tilt of the port–starboard axis from the horizontal: what a spirit
  level laid athwartships reads. Some outputs offer it (TSS1, Sonardyne Lodestar "gravity angles", the Kongsberg
  EM "horizontal" roll plane, POS MV's per-port formula select).

The relation is g = asin(sin r · cos p), where r is Euler roll, g gravity roll and p pitch. Pitch is the same in
both. The difference is about tan r · p² / 2: under 0.001° at 2° roll and 1° pitch, about 0.04° at 10° roll and 5°
pitch.

Decisions:

- **geoDC's computed roll is gravity roll.** `CalibrationComputer` takes the tilt of the port–starboard prism
  baseline from the horizontal.
- **Each attitude convention records its `RollDefinition`.** The default is Euler, which most sensors output.
- **Converting needs a pitch value at the same instant.** A roll attempt has no pitch baseline, so the pitch comes
  from a pitch series of the observed sensor, matched by time and converted to Standard.
- **The pitch source is a stored input of the roll attempt,** so the calculation is reproducible. A roll sample
  stores the pitch series (by default the one imported with the observed roll series) and its own pairing
  configuration, or, with no pitch series, the operator's maximum |pitch| for the error bound, which may be empty.
- **Both sides are compared in Euler roll.** Computed gravity roll is converted with r = asin(sin g / cos p), and an
  observed gravity-roll series is converted the same way.
- **Without a pitch series the attempt still calculates.** It records that no conversion was applied, and a finding
  states the error bound from the stored maximum |pitch|, or says the bound is unknown.

## 7. Display conventions

Everything on screen uses one convention. Additional views are explicit and read-only.

- **Vessel display position convention.** All views of positions use it: grids, inputs, charts and the 3-D scene.
  It defaults to Standard, and replaced the per-vessel `ReferenceFrame` axis directions.
- **Project display attitude convention.** The existing `ProjectMetadata.DisplayConvention`, re-expressed as an
  `AttitudeConvention` and applied everywhere angles are shown.
- **Instrument display panel.** An instrument can carry its own display conventions. An extra read-only panel shows
  its positions and angles in them, such as a Seapath's lever arms in (Y, X, −Z). The default comes from the
  instrument's catalogue entry (§9).
- **Captions always name the convention** wherever a value is shown in a convention other than Standard.

## 8. Outputs: C-O hand-off, exports and reports

A C-O is computed in Standard: a correction added to readings, about the physical axes. Handing it to a device means
converting it to that device's expectations. The design calls for one domain service, the C-O hand-off resolver,
used by the UI, the report and exports, in place of the two copies in `CoSemanticsPresenter` and
`JobReportRequestBuilder`. Per axis it applies:

1. **The export attitude convention:** a sign change into the device's directions.
2. **The alignment semantics:**
   - `CorrectionOffset`: the value is added to readings (Qinsy, EIVA C-O).
   - `MountAngle`: the sensor's mounting relative to the vessel (POS MV, NovAtel, Kongsberg, HYPACK). It is the
     negation of the correction.
   - `ErrorSubtracted`: an error the software subtracts from readings (CARIS "Applied = Recorded − Error").
     Numerically the negation of the correction, but defined on the reading.
3. **The mount direction** (`MountAngle` only): `VesselToSensor` (Sonardyne, POS MV "IMU wrt reference") or
   `SensorToVessel` (NovAtel, SBG, iXblue roll). The same physical mounting has opposite signs in the two.
4. **The output range** for heading (0–360 or ±180), and formatting.

The resolver returns the value with a description of what it physically means, generated from §5's table, and
display, report and export show both.

**Reports** (modelled only): a report has a convention, so a whole report can be in Standard or in one client's
convention, with per-device overrides. Both are stored with the report request so the snapshot taken at generation
captures them. **Exports** state the convention of every angle and position column in the header.

## 9. Device profiles and the catalogue

```text
DeviceProfile (persisted by value in the project)
  CatalogueCode, CatalogueVersion            // provenance, nullable for custom profiles
  Manufacturer, Model
  ImportAttitude: AttitudeConvention         // including RollDefinition
  ImportPosition: PositionConvention         // lever arms / offsets as the device states them
  ExportAttitude: AttitudeConvention
  Alignment: per axis { Semantics, MountDirection? }
  HeadingOutputRange
  DisplayPosition / DisplayAttitude          // the instrument display panel's defaults
```

- **Pinned by value.** When an instrument gets a profile, the project stores a copy. Reports and fingerprints read
  the copy, never the app's catalogue, so a catalogue change in a later release cannot alter an old project. The
  profile is part of the fingerprint's instrument projection.
- **The catalogue** is a researched list of common systems, including those that match Standard. Each entry records
  whether each value is documented or inferred, with its sources, and a manufacturer and model key. A new instrument
  gets the entry whose manufacturer and model match. At most one entry per manufacturer is that manufacturer's
  default, used when only the manufacturer matches. With no match, the instrument has no profile until the operator
  chooses one. An entry ships only when its values are documented or explicitly accepted.
- The catalogue is a JSON data file embedded in the domain assembly, not code, so another profile set can be loaded
  from the same format. Custom profiles are stored the same way.
- **A correction from the research:** the original Kongsberg MRU entry's bow-down pitch is not supported by any
  Kongsberg document found, and is corrected when the catalogue is rebuilt.

## 10. Values with their conventions at the boundaries

The type-level guarantee:

- **Imported series** keep their raw values and their convention. The only reader for calculation is
  `InStandard(pitchSource?)`, which applies the attitude convention and, for gravity-roll series, the roll
  conversion. The raw values stay available for display ("as imported") and audit.
- **Calculation inputs** (`ComputeCO` and its relatives) accept only Standard values produced by that accessor.
  There is no overload that takes bare observed angles.
- **Results** are Standard, and the type says so (for example a `StandardAngle`, or an attitude value tagged with
  its convention). A bare `Angle` stays for internal maths only.
- **Outputs** go through the hand-off resolver (§8) or a display or export formatter that takes a target
  convention. Nothing formats an attitude value without one.

## 11. Persistence, schema and compatibility

The owner decided to bump the project schema and **not** migrate older packages.

- **Schema 7** stores feature positions and VRF transformations in Standard axes, turns the vessel's axis directions
  into its display position convention, stores attitude conventions as absolute directions plus roll definition
  (replacing the `name|±|±|±` encoding), stores device profiles by value, records entry conventions with measured
  positions, and records the pitch source of roll samples (§6). In the event the pitch source and the by-value
  profiles landed in later schema versions (see Status).
- **No backwards compatibility.** A package at another schema version is rejected, as before. Projects are
  regenerated or re-imported, and [project-packages.md](../../geodc/docs/project-packages.md) records the schema history.
- **Every project's fingerprint changes.** The `ProjectFingerprint` projection covers the new fields: the profile by
  value, display conventions, entry conventions and the pitch source.
- **Test data covers the hard cases.** Generator scenarios exercise a left-handed position convention, a bow-down
  pitch device and a gravity-roll device, so a wrong conversion fails a test instead of only changing a label.
- **The GEOBASE importer wrote Standard.** (The importer was later removed from geoDC.) GEOBASE's vessel frame is +X starboard, +Y forward, +Z up. The importer
  writes Standard positions (geoDC x = GEOBASE y, y = GEOBASE x, z = −GEOBASE z), records GEOBASE's frame as the
  vessel's display position convention, and keeps angle values unchanged, because GEOBASE's internal convention
  matches Standard signs. GEOBASE derives roll as atan(−Δz / Δx), a gravity-style tilt, so its roll definition needs
  checking.

## 12. Out of scope

- **Coarse sensor mounts** (a sensor rotated 90° or 180°). geoDC does fine alignment only and assumes the coarse
  alignment is right. A warning for C-Os that suggest otherwise is a separate piece of work.
- **Agreement diagnostics** (an inverted sign, a swapped axis, a time offset) use these conventions but are not part
  of this design.
- **Heading reference** (true, grid, magnetic) stays dataset metadata. Grid convergence keeps its own handling, and
  magnetic declination is not modelled.
- **Units** are handled by the import parsers.

## 13. Delivery order

The design was delivered in slices, each with a test that would have caught the bugs it fixed:

1. Convention primitives and the single meaning table, with a cross-layer test pinning every built-in convention and
   axis to its physical words.
2. The canonical frame and schema 7: Standard-axis storage, the vessel display position convention, VRF
   transformations in Standard, fingerprint updates, and the generator, UI driver sample project and GEOBASE
   importer on the new schema.
3. Tagged series and the calculation path: `InStandard`, the roll-definition conversion and the stored pitch source.
4. Device profiles by value and catalogue v1, with provenance, manufacturer defaults and the Kongsberg correction.
5. The C-O hand-off resolver.
6. Display: the vessel and project display conventions app-wide, the instrument display panel, and the point-entry
   convention picker with its vessel diagram.
7. Consumers: defaults by instrument name, DMS display, report conventions in the report request, and export column
   conventions.
