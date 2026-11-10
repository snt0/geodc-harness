# Shore Control Workbench

> Moved from `geodc/docs/design/` on 2026-11-10. The current behaviour is in `geodc/docs`; open work is tracked in snt0/geodc#61.


Decisions behind the Shore Control page and the Shore Control Register window, taken between early October 2026 and
2026-10-29. How the feature works now is described in [shore-control.md](../../geodc/docs/shore-control.md).

## The problem

The 2026.1 target was parity with GEOBASE: enter points with their CRS and epoch, transform them to any other CRS and
epoch, and keep them as a set that represents a dock or other site. Three facts constrained the design:

- **Source coordinates are the truth.** A point's stored revision is what the operator entered or imported, in the
  CRS they said it was in. Transformed values are derived, so they are never written back as the point's
  coordinates. A set wanted in another CRS becomes a new set, with provenance.
- **A set outlives any job.** Sets live in a shared store, and each job works on its own snapshot. The operator must
  always know which of the two they are looking at.
- **No coordinate is shown without its CRS.** A coordinate without its reference is a number, not a position.

## Where the page sits

**Options:** a section (tab) of the Setup page, or a destination of its own on the shell's nav bar.

**Chosen (early October 2026):** a tier-1 destination, `ShoreControlTab`, between Verification and GNSS.

**Why:** the toolkit separates tiers by addressability ([ui/controls.md](../../geodc/docs/ui/controls.md#view-switchers)). The GNSS
page needs to send the operator to a specific set, the report needs to name one, and the workbench is a feature in
its own right. A section of Setup would only be reachable by already being on Setup.

## One page per job, and a separate register window

**First design (early October 2026):** one page in the GNSS page's shape. A list on the left held every set the job
could see (sets in the store and copies held only in the job), and a workspace on the right had tier-2 tabs: Points,
Transform, Snapshot, History and Details. Operators knew that shape from the GNSS, calibration and verification
pages.

**Revised (2026-10-19), after the first page shipped:** the list put sets that had nothing to do with the job beside
the one it used, and left it unclear which set was the job's. The page was split in two:

- the **Shore Control page** shows only the job's one current copy, on the Workbench page frame;
- the **Shore Control Register window**, a modeless tool window, holds every set in the store.

**Why:** a job uses one set at a time, so the job's page shows only that one, and the job's set is named everywhere,
including the title bar. Work on the register as a whole does not need a job, so it moved to a window that the launch
window could also open. The Transform tab became Show in on the table, the Snapshot tab became the Register window's
Compare view, and History and Details moved into the inspector.

## User-facing names

**Chosen (2026-10-19):** the shared store is the **Shore Control Register**, a set is a **Shore Control**, and its
points are **Shore Control Points**. Push and pull read as **Send to register** and **Take into job**.

**Why:** "store" read as a technical term. The code keeps the domain names (`ShoreControlSet`, store, push,
pull).

## Basic and advanced CRS presentation

**Chosen:** two modes in the CRS picker. Basic lists familiar names ("ETRS89 / UTM 31N") and applies the configured
default realisation. Advanced lists realisations ("ETRF2000 / UTM zone 31N"), ensembles and the details of each.

**Why:** basic hides vocabulary, never a decision: a basic pick is stored as a realisation, and switching to advanced
shows exactly what basic chose, with nothing re-resolved. The picker is the only place a CRS is chosen anywhere in the
app, so the vocabulary is the same everywhere.

## Epochs are never left blank

**Chosen:** a coordinate in a dynamic frame always starts with an epoch: the set default's when it has one, else
today's, and today's for a new revision, which is a new observation. A typed epoch is never corrected.

**Why:** a coordinate in a dynamic frame cannot be transformed without an epoch; the transform service refuses
it.

## The map's offline fallback

**Options:** fall back to a separate plan view of the points when map tiles cannot be loaded, or keep the map and draw
the points without a basemap.

**Chosen (2026-10-29):** the map is its own fallback. With no tiles it keeps the points placed on a blank background
under an offline note, and recently seen tiles are cached on disk.

**Why:** the decision records no further reason. The static plan-view chart remains the report's.

## What stays on the page

**Chosen:** the provenance lines under the Show in table, the comparison row and the revision diff stay page-local
rather than becoming toolkit controls.

**Why:** a toolkit control needs a stable interaction contract. Promote one when a second page needs it; the GNSS page
is the likely first candidate for the provenance lines.

## Still open

- GEOBASE's shore control editor and projection request panel should be compared with Show in and the CRS
  presentation before the advanced transform view is built.
- Whether the Show in target should default to the job's comparison frame when one exists.
- The exact wording of the ensemble and conventional-accuracy chips.
