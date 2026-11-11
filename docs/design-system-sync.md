# Design-System Sync

The geoDC design system is built from a screenshot corpus of the Sample gallery. The scripts and the corpus tool live
in this harness and work on the geoDC clone at `geodc/`:

- `dev/capture-ds-previews.ps1` (Windows) runs the harness's `tools/geoDC.GalleryCorpus` to render every registered
  specimen offscreen, dark then light, captures the specimens flagged `RequiresLiveWindow` from the running gallery
  through the [UI driver](ui-driver.md), and writes `<OutDir>/dark/<Id>.png`, `<OutDir>/light/<Id>.png` and
  `<OutDir>/manifest.json`, then zips them (default `dev/captures/ds-previews`, gitignored).
- `dev/sync-design-system.ps1` (any OS) turns that corpus into an updated copy of the design system.

geoDC's [`docs/ui/sample-gallery.md`](../geodc/docs/ui/sample-gallery.md) covers the specimens themselves: how to
write and register a page, the taxonomy and the id rules the corpus files its images by.

## Screenshot corpus

`dotnet run --project tools/geoDC.GalleryCorpus` (Windows, from the harness root) writes a screenshot of every
registered specimen to `<OutDir>/dark/<Id>.png` and `<OutDir>/light/<Id>.png`, with a `<OutDir>/manifest.json`
describing them. There is no list of ids to maintain.

To check a template or style change without running the app, render only the specimens it touches:

```text
dotnet run --project tools/geoDC.GalleryCorpus -- --id ButtonSizesPanel,ButtonDisabledPanel
dotnet run --project tools/geoDC.GalleryCorpus -- --control UiButton
```

Only the pages holding those specimens are rendered. The images go to the harness's
`dev/captures/specimens/<theme>/<Id>.png` and their paths are printed. Add `--no-build` after the first run to skip
the rebuild.

How it works:

- The tool lays each page out offscreen at a gallery section's width, in a hidden host that supplies the window's
  background, text colour, font and ClearType. It renders the page once and crops out each specimen. The tool is not
  DPI-aware, so layout is always at 96 dpi and `--dpi` only sets the capture resolution. Two runs on the same geoDC
  commit write identical files.
- An element inside a specimen with its own AutomationId ending in `Panel` (a flush section in a stack, the toolbar
  in a dock pane) is captured too, as a `detail` of that specimen.
- A specimen whose subject only exists in the live gallery window, such as a popup launcher, sets
  `RequiresLiveWindow="True"` in geoDC. The manifest lists it without an image, and `dev/capture-ds-previews.ps1`
  captures it from the running gallery with the UI driver's `screenshot`.
- The manifest records the geoDC commit, DPI and themes, and for each specimen or detail its id, control, group,
  page, caption and one capture per theme with the file, pixel size and a hash of the pixels.
- The gallery starts from `Program.Main`, not `StartupUri`, because the tool creates the Sample `App` for its
  resources and would otherwise open the gallery window.

The rendering code is in `tools/geoDC.GalleryCorpus` beside the tool's `Program`, and reads only the Sample gallery's
public types. `tests/geoDC.GalleryCorpus.Tests` (Windows) renders every geoDC page offscreen and fails when a specimen
without `RequiresLiveWindow` produces no image, so a specimen that reaches for the window is caught. It also checks
that two exports are identical. The harness CI runs it against geoDC's `dev`.

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