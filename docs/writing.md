# Writing comments, XML documentation and docs

How agents write prose in geoDC: comments, XML documentation and the pages under `geodc/docs`. The enforced rules
(required summaries, what `CommentConventionTests` rejects) are in `geodc/docs/coding-style.md` > Comments and
Documentation; this page is the rest. User-facing app copy follows `geodc/docs/copy-style.md` instead.

## The reader

Write for a reader who has the code but not its history. A test: would the text still be true and useful if the git
history were deleted? If not, it belongs in the commit message, the pull request or an issue.

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

## Changes and renames

- Before a broad change, search for the affected terms across `geodc/src`, `geodc/tests` and `geodc/docs`, and update
  every page and comment they appear in.
- Don't bulk-rename just to make the repository more uniform. Rename a family of types only once its meaning and the
  scope of the change are agreed (`docs/code-vocabulary.md`).
