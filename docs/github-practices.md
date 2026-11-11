# GitHub practices

How agents working from this harness use issues, labels, branches and pull requests in the geoDC repository, and
how geoDC's CI runs from here. Branch and push rules are in `AGENTS.md` > Cloud sessions and > Branches and pull
requests. geoDC itself carries no process documentation; `geodc/docs/codebase.md` > Checks before merging and
releases lists the checks a change must pass.

## Labels

Labels come in four families. An issue normally carries one **type** label, any **layer** and **area** labels
that apply, and one **effort** label once it has been sized.

| Family | Labels | Meaning |
|---|---|---|
| Type | `bug` | Something isn't working as intended. |
| | `feature` | New capability. |
| | `improvement` | Better behaviour or UX for an existing capability. |
| | `refactor` | Restructures code without changing behaviour. |
| | `tech-debt` | Known shortcut or convention gap to pay down. It can sit next to `refactor`. |
| | `epic` | Groups sub-issues; it is not worked on directly. |
| Layer | `layer:domain` | `geoDC.Domain` and `geoDC.Measurements`. |
| | `layer:application` | `geoDC.Application` abstractions, including the report contracts. |
| | `layer:infrastructure` | `geoDC.Infrastructure`: persistence, packages, ingestion, reporting adapters. |
| | `layer:ui` | Views, controls and theming (`geoDC.App`, `geoDC.App.UI`). |
| | `layer:testing`, `layer:tooling` | Test infrastructure; developer tools, the UI driver and `dev/` scripts. |
| Area | `area:calibration` (covers verification), `area:gnss`, `area:reporting`, `area:shore-control` | The domain workflow it affects. |
| Effort | `effort:low`, `effort:medium`, `effort:high` | Rough size. |
| Flags | `needs-design` | Needs a design decision before implementation. |
| | `client-request` | Raised by a client. |

- Use the existing labels. If an issue needs a label that doesn't exist (for example a layer or area with no
  label yet), raise it with the repository owner instead of creating one. Agents must not create, rename or
  delete labels.
- Don't use labels to record status. State lives in the issue's open or closed status and in linked pull
  requests.

## Issues

- **Before filing, search the open issues.** Extend or link an existing issue rather than opening a near
  duplicate. In particular, check the epics and the standing `refactor` and `tech-debt` issues.
- **Link related issues.** Attach the issue to its parent epic as a sub-issue where one exists. Reference
  related, overlapping or blocking issues by number, and say how they relate (for example "builds on #298",
  "overlaps #92").
- **One concern per issue.** Each issue should be small enough to land in one pull request. Larger work
  becomes an `epic` with sub-issues.
- **Epics use native sub-issues.** An epic or work package attaches its children as GitHub sub-issues, so
  progress shows on the epic. A checklist in the body may give the pickup order, but it does not replace the
  links. An issue has exactly one parent. When work moves between epics, re-parent the issue (with
  `replace_parent` in the API) rather than listing it under both.
- **Give enough context, and no more.** A reader who wasn't in the conversation should be able to tell what is
  wrong or wanted, why it matters, and where to start. Keep it short: the evidence and the decision points,
  not a narrative of how the issue was found.
- **Ground claims in code.** Cite `path:line` evidence and the commit it was checked against (for example
  "verified @ `ac375db`"), so a later reader can tell whether the code has moved.
- **End with acceptance criteria** as a checklist.
- **Out of scope.** Say what the issue deliberately leaves alone, especially areas another piece of work owns.
- **Bugs:** a bug issue states the observed behaviour, the expected behaviour, and why it matters (for
  example, which rule in `geodc/docs/domain-context.md` > Rules That Hold Everywhere it breaks).
- **Refactors:** a refactor issue states what gets simpler or shared, what must not change (behaviour,
  AutomationIds, fingerprints, package compatibility), and how that will be checked.
- Issues written by an agent end with the tool's attribution line.

## Keeping issues current

The issue body is the place a reader looks to learn what is asked and how much of it is done. Comments,
pull request bodies and chat reports are history, so they do not replace it.

- **Update the issue as work lands.** While a pull request that delivers part of an issue is open, the
  issue body's current-state note names it as pending, with the criteria it is expected to meet. Leave
  those criteria unchecked, because the pull request may be revised or abandoned. When it merges, update
  the issue body:
  - tick the criteria it met;
  - reword a partly met criterion so that it names what remains;
  - update the "Current state (verified @ `sha`)" note to name the merged pull requests.

  Stale `path:line` evidence is corrected at the same time.
- **No dangling work in comments.** If a review, a closing note or a pull request turns up remaining work,
  put it where it will be tracked:
  - an unchecked acceptance criterion on the issue that owns it;
  - a sub-issue;
  - a new issue attached to the right epic.

  A comment may link to it, but a "one loose end…" comment on its own, especially on a closed issue, gets
  lost.
- **Check before you record.** Confirm a leftover against the current `dev` before filing it or calling it
  resolved. Later pull requests often finish work that an earlier one deferred.
- **Close deliberately.** Close an issue when every acceptance criterion is met. If one is dropped or moved,
  say so in the closing comment and link the issue that now owns it. Close an epic when all its sub-issues
  are closed, or close it once its remaining children have moved to another epic.
- **Pull requests say whether they finish the issue.** Use `Closes #N` when the pull request meets every
  remaining criterion, or when each one it leaves unmet has been explicitly dropped or moved to a linked
  issue, as recorded in the issue body. Otherwise write `Part of #N` and list the criteria that are still
  open.
- **Whoever opened the pull request updates the issue on merge.** The developer usually merges in the
  GitHub UI, so the agent session that opened the pull request updates the issue body when the merge reaches
  `dev` (for a stacked pull request merged into its base
  branch, only once that branch lands on `dev`), and closes the issue if every criterion is met.
  A session that starts work on an issue or epic first reconciles it with the merged pull requests on `dev`.
  A manual check the developer reports, such as a Windows run, is ticked in the issue body when reported, and
  an epic's current-state note is updated whenever a child issue changes state.
- **Agent sessions report what remains.** When an agent works on an issue and the issue stays open, the
  final report to the developer says so, says why (partial delivery, a blocker, a decision needed), and lists
  what remains. That list matches the issue's unchecked criteria, which the agent will already have updated.

## Branches and pull requests

- **Branch names describe the change**: lowercase and hyphenated, naming the issue or the feature (for example
  `fix-unsaved-changes-banner`), and include the issue number whenever the work has an issue (for example
  `issue-123-fingerprint-coverage`). Avoid generic names such as `fixes`, `wip`, or a tool-generated session name.
  Agents prefix their branches with `claude/` or `codex/` (see `AGENTS.md` > Branches and pull requests).
- **Target `dev` by default.** `main` is for releases. Work that builds on an unmerged branch is the exception:
  it stacks.
- **Stack sequential pull requests.** When one pull request builds on another that hasn't merged yet, branch
  from the earlier branch and target the pull request at it, not at `dev`, so each diff shows only its own
  change. Say in the body which pull request it stacks on.
  - Check before branching and again before opening the pull request: if the branch contains commits from
    another open pull request's branch, target that branch.
  - A dependent pull request opened against `dev` repeats the lower one's diff for as long as both are open,
    which makes each one harder to review.
  - After the lower pull request is squash-merged, merge `dev` into the next branch, then retarget its pull
    request to `dev`. The squash commit does not contain the lower branch's original commits, so retargeting
    alone brings their changes back into the diff. The merge is usually clean, because `dev` now holds the
    same changes.
- **Keep a pull request to one purpose.** A batch of small, unrelated bug fixes can share one pull request
  when each fix is narrow and is named in the commit message. Don't mix refactoring into a bug-fix pull
  request.
- **Titles** are a sentence-case imperative ("Fix …", "Add …", "Share …"). Reference the issue numbers the pull
  request resolves in the title or the body. Pull requests are squash-merged, and the merge appends the
  pull request number.
- **The body follows [pull-request-template.md](pull-request-template.md):** what changed and why; under Testing, what was
  tested and where (for example, "compile-only on Linux" or "Windows test run"), then the steps still needed
  to verify it.
- Never skip, disable or quarantine a test to get CI green.
- Don't touch areas another branch is actively reworking unless that work has been agreed. Note the
  overlap in the pull request instead.

## CI

geoDC has no CI of its own. [`.github/workflows/geodc-ci.yml`](../.github/workflows/geodc-ci.yml) in this harness
runs its checks against a geoDC branch, tag or commit:

- **Format** on `ubuntu-latest`: whitespace, the style rules a build never reports (IDE0003, IDE0009, IDE0049,
  IDE1006) and XAML. **Build and test** on `windows-2022`: the strict Release build and every test project.
- **When it runs:** nightly against `dev`, and by hand. Before asking for a merge, run it on the pull request's
  branch: **Actions > geoDC CI > Run workflow** with `ref` set to the branch, or the `actions_run_trigger` tool
  (`workflow_id: geodc-ci.yml`, `ref: main`, `inputs: {ref: <branch>}`).
- **Result:** the run's jobs, and a `harness/geodc-ci` commit status on the geoDC commit when the token allows it.
  Report the run in the pull request's Testing section.
- **Setup:** the `GEODC_TOKEN` secret, a fine-grained token on the geoDC repository with Contents: read and Commit
  statuses: write. The `GEODC_REPOSITORY` variable names the repository when it is not the default.
- **NuGet cache:** runs restore `~/.nuget/packages` from the Actions cache, keyed on geoDC's `global.json`,
  `dotnet-tools.json`, `nuget.config`, `Directory.Packages.props` and `Directory.Build.props`. Only `dev` runs save
  it, so every other run can read it.
- It can only reach geoDC while geoDC is on GitHub. Once the repository moves, point `GEODC_REPOSITORY` and the
  checkout at the new host or retire the workflow.

The harness's own [`.github/workflows/checks.yml`](../.github/workflows/checks.yml) runs on every harness pull request
and push to `main`. It checks the `.claude/skills` mirror, the hook scripts and `settings.json`. Its **Harness tools**
jobs check out geoDC's `dev` into `geodc/` with the same `GEODC_TOKEN` secret, check that `.editorconfig` matches
geoDC's, and build `geodc-harness.slnx` with strict code style. The Ubuntu job runs `tests/geoDC.UiDriver.Tests`, and
the Windows job runs every harness test, `tests/geoDC.GalleryCorpus.Tests` included. A geoDC change can break these
tests, for example by renaming an AutomationId the driver's convention lists mirror, and they run against `dev` only,
so run them locally on a geoDC branch that touches the UI.
