---
name: geodc-branch-wrap-up
description: Wrap up a geoDC feature branch before review or merge by reviewing the work against its GitHub issue, bringing documentation, XML docs and logging up to standard, cleaning up the diff, and checking every acceptance criterion with evidence. Use when geoDC work is done, ready for review, ready to merge, starting or updating a pull request, or the user asks what remains before merging.
---

# Wrap up a geoDC branch

Work through these sections in order. Do not infer readiness from a passing build.

## 1. Establish the scope

- Find the issue the branch resolves: the branch name, commit messages, the pull request body, or ask. Read the issue, its comments, its parent epic and any linked issues, and note its acceptance criteria and out-of-scope list.
- Read the whole branch diff against its base (`git diff $(git merge-base origin/dev HEAD)...HEAD`, or the stacked base). Note every file and behaviour it changes, including ones the issue does not mention.
- If there is no issue and the change is more than a small fix, say so. Offer to file one rather than inventing criteria.

## 2. Documentation

- Reconcile the docs the change affects: `geodc/docs/domain-context.md` and `geodc/docs/data-model.md` for domain terms and invariants, `geodc/docs/project-packages.md` for persisted formats, `geodc/docs/string-resources.md` conventions for app copy in `Strings/*.yaml`, `geodc/docs/ui/` for shared controls, `AGENTS.md`, and canonical `.agents/skills`. After a skill change run `pwsh -NoProfile -File dev/sync-agent-skills.ps1 -Write`, then again without `-Write`.
- Docs describe the system as it is now, written to `docs/writing.md`. Remove statements the change made stale rather than appending "now" or "previously" notes.
- Check the XML documentation on new and changed members against `geodc/docs/coding-style.md` > Comments and Documentation and `docs/writing.md`. Every public member has a summary that says something the name does not. There are no issue numbers, history, review or process notes, or plans in source comments.

## 3. Code cleanup

- Remove debugging leftovers, commented-out code, unused members, and temporary scaffolding.
- Check logging against `geodc/docs/logging.md` > Where Logging Is Expected. New I/O, external processes, user-initiated operations and swallowing `catch` blocks log at the right level with canonical property names, and no exception is logged twice.
- Check the repository conventions the diff touches: explicit types, nullability without `!`, cancellation, NUlid IDs, `PersistenceIdentity.ShouldInsert(...)`, typed theme/layout resources, and generated string resources.
- Run `git diff --check` and the relevant tests. On Linux, state that Windows-only test projects are compile-verified only.
- Run the harness's `geoDC CI` workflow on the pushed branch (`docs/github-practices.md` > CI) and report its result; geoDC has no CI of its own.

## 4. Requirements

Go through the issue's acceptance criteria one at a time and mark each as **met**, **partial**, **deliberately dropped**, or **not reached**. Give the evidence for each: a test name, a UI driver run, a manual check, or reasoning. Say which kind it is. When a criterion needs the developer to look at the UI, give them a handoff script (`geodc-ui-driver-handoff`).

Also state whether any of these changed, and how that was checked:

- sign conventions or angle semantics;
- pass/fail logic;
- project fingerprint reproducibility;
- immutability of approved results;
- persisted formats or package compatibility.

For UI work, state whether the surface can be driven by `geoDC.UiDriver`, and add demand-driven AutomationIds or peers where needed. Treat the eight shell tab IDs as reserved.

## 5. Visual capture (optional)

Ask whether the change warrants a capture, and do not start a long capture unprompted. Recommend `dev/capture-views.ps1 -Set full` for work on shared chrome, layout, styles or the toolkit. Capture from a clean tree after the final commit, with the window maximised. Report the label, view count, failures and fixture fingerprints. A capture is an eyeballing baseline. Do not recapture the same build, and do not promote a capture without the developer.

## 6. Hand over

Write the result where reviewers will see it, not in a local notes file:

- **Pull request base:** `dev`, unless the branch builds on another unmerged branch. Check with `git log --oneline origin/dev..HEAD`: if it lists commits from another open pull request's branch, target that branch, name the pull request it stacks on in the body, and once the lower one is squash-merged, merge `dev` into this branch and retarget it to `dev`. See `geodc/docs/github-practices.md` > Stack sequential pull requests.
- **Pull request body**, following `docs/pull-request-template.md`:
  - what changed and why;
  - the acceptance-criteria table with evidence;
  - Testing > Done: what was verified and where (Linux compile-only, Windows test run, UI driver);
  - Testing > Still to verify: the steps someone still has to take;
  - the capture label, or why there was none.
- **Issue body:**
  - add the pull request to the current-state note as pending, naming the criteria it is expected to meet;
  - correct stale evidence.

  Leave those criteria unchecked until the pull request merges. Whoever merges it, or the next session to work on the issue, ticks the met criteria and rewords partly met ones so that they name what remains.

  Record remaining work as an unchecked criterion or as a linked follow-up issue (a sub-issue where an epic owns the work), never only in a comment. See `docs/github-practices.md` > Keeping issues current.
- **Pull request:** `Closes #N` when every criterion is met, or when each one left unmet has been explicitly dropped or moved to a linked issue (recorded in the issue body); otherwise `Part of #N`, listing the criteria that stay open.
- **Issue comments:** only for decisions that need the owner, or to link the follow-ups. File follow-up issues rather than widening the branch.

End the chat report with:

- the pull request link;
- whether the issue will close on merge, and if not, why it stays open and what remains (matching its unchecked criteria);
- the docs changed;
- open questions for the developer;
- any follow-up issues.

Keep it under 40 lines.
