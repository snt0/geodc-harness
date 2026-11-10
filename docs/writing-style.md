# Writing comments and docs for geoDC

geoDC is maintained by a C# developer who is new to the survey domain, so its comments and docs are written for that
person. Agents' first drafts tend to be accurate but dense, clipped and oddly phrased, which makes the codebase feel
alien. Follow this guide whenever you write or change a comment, an XML doc, or a page under `geodc/docs/`. The short,
human-facing version is `geodc/docs/coding-style.md` > Comments and Documentation; this one adds the patterns to
watch for.

## The reader

A competent .NET/WPF developer, new to dimensional control surveys and to geoDC. They read a doc when they need to do
something (add a page, change the schema, cut a release) or understand a concept (what a CRP is, how alterations
propagate). They read a comment when they are about to change the code next to it.

## Voice

- Plain, direct English, as a colleague would write it. Full sentences, mostly 10-25 words.
- One idea per sentence. Prefer two sentences to one sentence joined by a semicolon.
- Say what something does before the exceptions and edge cases.
- Prefer prose paragraphs for explanation; use bullets for genuine lists (steps, options, a set of rules).
- "You" for instructions to the reader is fine. Don't over-hedge, and don't sell.
- British spelling, as the codebase already uses (behaviour, metres, finalise).

## Patterns to remove (the "alien" tells)

Watch for these in your drafts, and fix them when you meet them in existing text. None is banned outright; the problem is density and repetition.

1. **Semicolon chains.** `X does A; B does C; otherwise D.` Split into sentences, or into a short list.
2. **Em dashes as glue.** At most one per paragraph, and only for a real aside. Use a comma, a colon, parentheses or
   a new sentence instead.
3. **Stacked parentheticals.** `the job's set (JobSet, persisted in job_set) of points (FeatureState)`. Keep the one
   identifier the reader needs; move the rest or drop it.
4. **Bold lead-ins on every bullet** (`- **Thing:** text`) when the list isn't a glossary or a set of named options.
5. **Defensive negative framing.** "It never X, so Y can't Z unless W." Say what happens: "Y only does Z when W."
6. **Tic words and phrases**, when they add nothing: deliberately, intentionally, on purpose, exactly, precisely,
   load-bearing, seam, surface (as a verb or "the API surface"), canonical (unless it is the real domain term),
   honest, verbatim, single source of truth, first-class, carries, names (as a verb: "the result names the file"),
   reads as, whatever, rather than (in every other sentence), "so that ... never".
7. **Meta text.** "This page describes...", "Part of the X guide.", "See below.", "as described above". Start with
   the content. A one-line opener saying when to read the page is fine if it is useful.
8. **Exhaustive enumerations** of every property, field, column, parameter or edge case in prose. The code and its
   XML docs hold that. A doc gives the concepts, the rules and the non-obvious parts, then points at the type.
9. **Over-compressed rules.** Sentences that pack three conditions and two consequences. Unpack them; an example
   often helps more than precision.
10. **Process and history.** Issue numbers, "previously", "now", "no longer", "was changed to", "per review",
    "we decided". Describe the current state. (Design records under `geodc/docs/design/` may keep dates and decisions.)
11. **Restating the code.** Comments that say what the next line plainly does. Delete them.
12. **Over-explaining trivia.** A three-line rationale on a one-line private helper. Cut to what a reader needs.

## What to keep

- Every fact a reader needs to change the code safely: units, frames, sign conventions, nullability, invariants,
  thread or lifetime rules, why the obvious alternative is wrong. Rewrite these, never drop them.
- Domain terms (CRP, VRF, GEOdc Standard axes, attempt, finalise, shore control) and identifiers in backticks where
  the reader will search for them.
- Accuracy. Never invent behaviour. If you can't tell what a comment means from the code, leave it as it is and
  note it in your report.
- Good comments. When you edit near existing text, fix what is hard to read and leave the rest; a focused diff beats
  a rewrite.

## Format rules

- Keep each file's line endings (most files are CRLF) and encoding.
- XML docs stay valid XML: escape `<`, `>`, `&` in text, keep `<summary>`, `<param>` (names must match),
  `<see cref="..."/>` and `<inheritdoc/>`. Continuation lines inside a `<summary>` use `///     ` (three slashes, five
  spaces).
- `geoDC.Measurements`, `geoDC.Domain` and `geoDC.Application` require a `<summary>` on every public member (CS1591 is
  a build error there). Rewrite a summary; don't delete it.
- `CommentConventionTests` fails on issue references (`#123`), `TODO`/`FIXME`/`HACK`, and history wording ("stopgap",
  "used to be", "per review") in comments.
- Wrap Markdown and comments at 120 characters.

## Docs: keep, trim, merge or delete

When you write or change a page, ask: would the new developer read this, and would they understand it on the first read?

- **Delete** a page nobody would read: a finished implementation plan, a log of what was built, an API listing that
  the code's XML docs already cover. Report the deletion and every page that links to it.
- **Trim** reference sections that mirror the code into a short overview: what the thing is for, the rules that
  matter, the traps, and which type or file to open.
- **Merge** a page whose content belongs in another, and say where it went.
- **Clarify** the rest: rewrite dense paragraphs, unpack rules, add a short example where it helps.
- Each page should open with what it covers and when to read it, in a sentence or two.
- Keep links working. If you remove or rename a heading that other pages link to (`page.md#anchor`), fix the links
  or report them.
- Aim for pages that are noticeably shorter and easier, not just reworded. Don't drop a real rule or procedure to
  get there.

## Before and after

Comment:

> Before: `/// <summary>Only the job changed; the offer is to push.</summary>`
> After: `/// <summary>Only the job changed, so the app offers to push it to the store.</summary>`

Doc bullet:

> Before: `` - `UiIconButton`: a `UiButton` that shows only a glyph — the inherited `Symbol` (`FluentIcons`) and `Label`,
> plus the inherited `Variant`, `Intent` and `Size`. The face is square per `Size` with no text padding (a padded
> `UiButton` that small clips its glyph away), and `Label` becomes the automation name and the tooltip unless either is
> set explicitly — so an icon-only action is never unnamed. ``
>
> After: `` - `UiIconButton` is a `UiButton` that shows only its `Symbol`. The face is square, and `Label` becomes the
> tooltip and the automation name, so screen readers and the UI driver can still find an icon-only button. ``

Paragraph:

> Before: "Records are never edited or deleted. `VrfTransformationHistory` reads a job's records as a stack — a
> transformation pushes, a reversal pops its target — and only the top (the most recent transformation still in
> effect) may be reversed, because rigid transforms do not commute."
>
> After: "Transformation records are append-only. `VrfTransformationHistory` treats a job's records as a stack: applying
> a transformation pushes one, and reversing it pops it. Only the most recent transformation still in effect can be
> reversed, because undoing an older one would mean reordering rotations, and rotations don't commute."
