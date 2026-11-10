# Design-System Sync

The geoDC design system is built from a screenshot corpus of the Sample gallery. Both scripts live in this harness
and work on the geoDC clone at `geodc/`:

- `dev/capture-ds-previews.ps1` (Windows) runs geoDC's `tools/geoDC.GalleryCorpus` to render every registered specimen
  offscreen, dark then light, captures the specimens flagged `RequiresLiveWindow` from the running gallery through the
  UI driver, and writes `<OutDir>/dark/<Id>.png`, `<OutDir>/light/<Id>.png` and `<OutDir>/manifest.json`, then zips
  them (default `dev/captures/ds-previews`, gitignored).
- `dev/sync-design-system.ps1` (any OS) turns that corpus into an updated copy of the design system.

geoDC's [`docs/ui/sample-gallery.md`](../geodc/docs/ui/sample-gallery.md) > Screenshot corpus describes the corpus
itself: how specimens are rendered, the manifest and the tests that guard it.

## Updating the design system

The geoDC design system (the claude.ai Design System artifact `https://claude.ai/artifact/4CrD8jXTeCmSnUSTYGeuUm`) is generated from the corpus. Updating it after a UI change is: run the capture, run the sync, review the diff, publish.

1. **Capture** on Windows: `pwsh -NoProfile -File dev/capture-ds-previews.ps1` from the harness root. A cloud session asks for a Remote Control run on the Windows machine and copies the zip back.
2. **Pull** the design system's `project/` files into a folder, e.g. `<ds>/project/...`: every `components/*/README.md` and `preview.html`, `archived/**`, `README.md`, `design-system.json` and the PNGs under `assets/_preview/` (an agent reads them with the Artifact tool's `read` and `paths`, `out_dir` set to `<ds>`).
3. **Sync** (any OS): `pwsh -NoProfile -File dev/sync-design-system.ps1 -DesignSystem <ds> -Corpus dev/captures/ds-previews -Out dev/captures/ds-sync -By <name>`. It writes the updated system to `<Out>/project`, plus `changes.json` (paths to write and delete) and `changes.md` (cards added, archived and regrouped; warnings).
   - Every card is filed under its page's `GalleryGroup`, and the README's Component index is rebuilt in gallery order with the group descriptions from `geodc/src/geoDC.App.UI.Sample/Gallery/GalleryGroup.cs`.
   - A card's figures and its README's `## Specimens` list are regenerated from the manifest: one figure per specimen, then its details. The card's summary, related line and the rest of its README are kept.
   - A registered control with no card gets one, its summary and dependency properties read from its source in `geodc/src/geoDC.App.UI`. A card whose control has no specimen page keeps its figures and takes its group from `$CardGroups` in the script; one whose control is gone moves to `archived/`.
   - Screenshots are copied by id; a screenshot no card (or archived card) shows is deleted. `design-system.json`'s `lastChange` records the commit. A run with nothing to change writes nothing.
4. **Review** `changes.md` and `git diff --no-index <ds>/project <Out>/project`, and fix prose the new specimens made stale.
5. **Publish** the `changes.json` writes and deletes to the artifact under `project/`, at most 255 paths a call, with `design-system.json` alone in the last call after re-reading it.

When a control is renamed or a page registers a type that isn't the card's name, add it to `$CardNames`; when a control loses its page, add it to `$CardGroups`.