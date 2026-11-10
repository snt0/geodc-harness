# Page Frame

> Moved from `geodc/docs/ui/page-layout.md` on 2026-11-10. The frame as built is described there; open work is
> tracked in snt0/geodc#515.

## Why the frame looks like this

The frame was agreed on 2026-10-22 after a design review of every page. Before it, the shell was stable but each
page invented its own shape below the nav band:

- There was no page header. A page's identity, its selected record and their state were shown in a dock pane
  header, a kicker, a section header, or not at all.
- Primary actions had no home. Calibration's Recompute and Complete lived inside the Results tab, so they vanished
  while the operator edited inputs, and results and inputs were never visible together.
- Left panes were 320, 380 or 400 wide and content caps were 1100, 1240 or 1320. Nothing collapsed at the 1200 px
  minimum window width.
- Each tool window had its own shell and width.

The owner decided on 2026-10-22 that page commands live in the header's command bar, under the nav band. The two
alternatives considered, a full-width ribbon toolbar and page commands placed only in the panes, were rejected, and
`UiRibbonGroup` and `UiRibbonAction` left page chrome as a result. A captioned group of stacked tiles is the
full-width ribbon this frame replaced.

Resizable panes were not adopted. The width tokens and width-driven collapse cover the 1200 px minimum, and
splitters can be added later if users ask for them. The pane and content widths are the most common values found
in that review, kept as role tokens (`Size.Pane.*`, `Size.Content.*`) so they can be re-tuned in one place. The
header (56 px) and command bar (28 px) heights are each used by one control, so they live in those controls' styles
rather than as tokens.
