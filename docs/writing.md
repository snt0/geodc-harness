# Writing comments, XML documentation and docs

How agents write prose in geoDC: comments, XML documentation and the pages under `geodc/docs`. The enforced rules
(required summaries, what `CommentConventionTests` rejects) are in `geodc/docs/coding-style.md` > Comments and
Documentation; this page is the rest. User-facing app copy follows `geodc/docs/copy-style.md` instead.

## The reader

Write for a reader who has the code but not its history. A test: would the text still be true and useful if the git
history were deleted? If not, it belongs in the commit message, the pull request or an issue.

That reader is a competent .NET/WPF developer who is new to dimensional control surveys and to geoDC. They open a doc
to do something (add a page, change the schema, make a release) or to understand a concept (what a CRP is, how
alterations propagate), and they read a comment when they are about to change the code next to it.

## Style

Write as you would explain the code to a colleague sitting next to you.

- Use plain, full sentences, with one idea in each. Two short sentences read better than one long one.
- Avoid chains of clauses joined by semicolons or em dashes. Split them, or use a short list.
- Say what the code does before the exceptions and edge cases.
- Don't list every property, parameter or case the code already shows. Explain the rules and the surprises, and
  point at the type.
- Describe the code as it is, in the present tense. Leave out how it got there.
- Keep identifiers in backticks so a reader can search for them, and use the domain's own terms, the same in the
  code, `geodc/docs/domain-context.md` and `geodc/docs/data-model.md`.
- State rules, not commentary: no asides on how important something is, what was considered, or who decided.

## Patterns to watch for

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
    "we decided". Describe the current state. (Design records under `docs/design/` may keep dates and decisions.)
11. **Restating the code.** Comments that say what the next line plainly does. Delete them.
12. **Over-explaining trivia.** A three-line rationale on a one-line private helper. Cut to what a reader needs.

## What stays out of source and docs

These go in commit messages, pull requests or issues:

- Issue or pull request numbers (`#57`, "see issue 82"). Write the context the number stood for, or leave it out.
- History: "previously", "used to", "no longer", "was changed to", "the old X did", "renamed from".
- Process notes: "per review", "as discussed", "we decided", "considered and tabled", "stopgap", "for now", and
  references to a person, a session or a phase of work.
- Plans and placeholders: "until X lands", "not yet", `TODO`. If work remains, file an issue
  (`docs/github-practices.md`) and describe the current limitation as a fact ("Definitions are label-only and carry
  no WKT2.").

The exception in `geodc/docs` is the schema history table in `geodc/docs/project-packages.md`. Dated decisions and
designed but unbuilt work go in this harness's `docs/design/` records, not in geoDC.

## Docs pages

- A page describes current behaviour. When a change makes a statement stale, rewrite or remove it; don't append a
  "now" or "previously" note.
- When the reason for a design matters to someone changing it, give the reason beside what it explains, as a fact
  about the code, not as the story of the decision.
- Put an explanation longer than the member it documents, or one spanning several types, in `geodc/docs` and leave a
  short pointer in the code.

## Does a page earn its place?

When you write or change a page, ask: would the new developer read this, and would they understand it on the first read?

- **Delete** a page nobody would read: a finished implementation plan, a log of what was built, an API listing that
  the code's XML docs already cover. Fix every page that links to it.
- **Trim** reference sections that mirror the code into a short overview: what the thing is for, the rules that
  matter, the traps, and which type or file to open.
- **Merge** a page whose content belongs in another, and say where it went.
- **Clarify** the rest: rewrite dense paragraphs, unpack rules, add a short example where it helps.
- Each page should open with what it covers and when to read it, in a sentence or two.
- Keep links working. If you remove or rename a heading that other pages link to (`page.md#anchor`), fix the links.
- Aim for pages that are noticeably shorter and easier, not just reworded. Don't drop a real rule or procedure to
  get there.

## Changes and renames

- Before a broad change, search for the affected terms across `geodc/src`, `geodc/tests` and `geodc/docs`, and update
  every page and comment they appear in.
- Don't bulk-rename just to make the repository more uniform. Rename a family of types only once its meaning and the
  scope of the change are agreed (`docs/code-vocabulary.md`).

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
