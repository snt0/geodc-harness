---
name: geodc-test-data-builder
description: Generate or refine connected geoDC synthetic test projects, raw CSV inputs, deterministic scenario fingerprints, UI driver fixtures, and generator scenarios. Use for demo/test data, reproducible .gdcproj fixtures, calibration/verification signal cases, sign-convention datasets, generator CLI or WPF changes, and persisted fingerprint verification.
---

# geoDC Test Data Builder

Use the existing generator for connected domain data; do not hand-edit SQLite/package contents or assemble unrelated aggregate fragments when the scenario model can express the case.

## Route the task

- Read `geodc/docs/test-data-generator.md` before changing generator behavior or choosing nontrivial flags.
- Use `geodc/tools/geoDC.TestDataGenerator.Cli` for scripted output and `geodc/tools/geoDC.TestDataGenerator.Wpf` for an interactive generator UI.
- Use `geodc/tools/geoDC.TestDataGenerator.Core` when adding scenario options, domain graph content, signal behavior, raw files, or persistence mapping.
- Use the UI driver's `fixture seed` only for the canonical UI-driver fixtures. Do not substitute an arbitrary `.gdcproj` into UI driver workflows.

## Invariants

- Keep the generated graph operationally valid: installations and feature states are job-scoped; calibration and verification form one cycle; GNSS checks remain diagnostic; shore control retains CRS, datum, and epoch.
- Keep angle-series import/calculation conventions and project display conventions explicit. Exercise cross-convention behavior intentionally, not accidentally.
- Generate physically consistent calibration/verification series through the existing motion/session model so recomputed C-O values remain meaningful.
- Preserve the mixed lifecycle states that make the default project useful for UI and workflow testing unless the requested fixture needs a narrower state.
- Express work-item state as a `ScenarioAttemptLifecycle`, which the seeder replays through domain transitions. When tests need every workflow state, readiness and outcome, use `IncludeLifecycleCoverage` rather than writing lifecycle columns.
- Treat the random seed as domain reproducibility, not byte reproducibility. Fresh ULIDs, timestamps, SQLite, tar, and zstd bytes can differ.
- Use `ScenarioFingerprint` for input-graph equality and `ProjectFingerprint` for generate-persist-read-back equality.

## Safe output behavior

The CLI replaces `TestProject.gdcproj` and matching generated CSV names in its output directory. Use a fresh task-specific directory unless the user explicitly selected an existing target. Changing generator code does not authorize regenerating the tracked UI driver fixture.

## Verification

Test option validation, graph relationships, signal/C-O behavior, sign-convention cases, raw outputs, and both fingerprints at the narrowest useful layer. For persistence changes, verify the full generated package can be read back and retains its project fingerprint.
