# Design records

Dated records of geoDC designs: the problem, the options considered, the decisions taken and when, and the parts
still to build. They moved here from `geodc/docs/design/`, so open issues that cite `docs/design/<record>.md` mean
these files.

geoDC's own docs describe what the code does now and give the reasons a developer needs beside it. Use a record for
the history and for designed work that is not built yet, and check it against `geodc/docs` and the code before
relying on it: a record can be ahead of or behind `dev`.

| Record | Topic | Living docs | Issues |
|---|---|---|---|
| [angle-position-conventions.md](angle-position-conventions.md) | The GEOdc Standard frame, absolute-direction conventions, Euler and gravity roll, device profiles by value, the C-O hand-off resolver, report and export conventions, delivery slices | `geodc/docs/domain-context.md` > Sign Convention, `geodc/docs/data-model.md` | snt0/geodc#473 and its sub-issues |
| [shore-control-workbench.md](shore-control-workbench.md) | Why Shore Control is a tier-1 page per job plus a Register window, its user-facing names, basic and advanced CRS presentation, epoch pre-fill, the map's offline fallback, open questions | `geodc/docs/shore-control.md` | snt0/geodc#61 and its sub-issues |

When a design changes, add a dated note at the top of its record saying what replaced it rather than rewriting it.
Move a decision's reasoning into `geodc/docs` when a developer changing the code needs it, and delete a record once
the living docs describe it fully and no open issue cites it.
