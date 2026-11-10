# AGENTS.md

Shared guidance for coding agents working on geoDC from this harness. Keep this file focused on rules that affect decisions across the codebase; put detailed or task-specific procedures in the linked docs and repository skills.

## Layout

- This repository is the agent harness: these instructions, the skills (`.agents/`, `.claude/skills`), the Claude Code and Codex settings and hooks, the cloud-session docs and setup, and the capture and design-system scripts under `dev/`.
- geoDC itself is `geodc/`: a separate clone of `snt0/geodc` on `dev`, gitignored here. `bootstrap.ps1` or `bootstrap.sh` creates or updates it; in a cloud session the session-start hook links the attached geoDC repository there.
- Paths in this file and in the skills are relative to the harness root, so geoDC's are written `geodc/...` (`geodc/docs/codebase.md`, `dotnet build geodc/geoDC.slnx`). A path without the prefix is the harness's own.
- Commit and push geoDC changes inside `geodc/` (`git -C geodc ...`), and harness changes at the root. They are separate repositories with separate branches and pull requests.

## Orient by task

- Read `geodc/docs/codebase.md` first: what each project is for, how the solution is built and tested, and where the detailed pages are.
- Read `geodc/docs/finding-things.md` to get from text, a popup, a menu item or a grid column on screen to the files behind it.
- Read `geodc/docs/domain-context.md` and `geodc/docs/data-model.md` before changing domain names, workflows, calculations, or persisted domain state; read `geodc/docs/data-analysis.md` before changing dataset inspection, alignment, or editing.
- Read `geodc/docs/coding-style.md` before introducing a type shape, interface, DI registration, null-forgiving operator, or cancellable async API, and before writing XML documentation or comments.
- Read `geodc/docs/copy-style.md` before writing or changing user-facing text, and `docs/writing-style.md` before writing or changing comments, XML documentation or pages under `geodc/docs/`.
- Read `geodc/docs/app-layout.md` before adding, moving or renaming files in `geoDC.App` or `geoDC.App.Tests`, or adding a feature area.
- Read `geodc/docs/shell-menus.md` before adding or changing a main menu entry, an application-wide shortcut, or when a shell command is available.
- Read `geodc/docs/logging.md` before adding or changing log calls, and when a change adds I/O, an external process, a user-initiated operation, or a `catch`.
- Read `geodc/docs/project-packages.md` before changing `.gdcproj` behavior, manifests, compatibility, integrity, encryption, or package metadata.
- Read `geodc/docs/reporting.md` before changing report requests, templates, the Typst renderer, or its provisioning.
- Read `geodc/docs/work-item-alterations.md` before changing alteration guards, tokens, invalidation, or what Calculate, Finalise and Reopen do to dependent work.
- Read `geodc/docs/coordinate-transforms.md` before transforming positions between CRSs or changing `ICoordinateTransformService`; read `geodc/docs/proj-native.md` before changing the PROJ binding or its binaries.
- Read `geodc/docs/third-party-notices.md` before vendoring a component (JavaScript, fonts, native binaries) or adding a package whose licence metadata is missing.
- Read `docs/cloud-sessions.md` before changing the cloud environment setup script; read `docs/cloud-sessions-claude.md` before changing the `.claude/hooks` scripts. Both are in this harness.
- Read the relevant page of `geodc/docs/ui/` before changing shared controls (`controls.md`), themes, typed resources or foreground behavior (`theming.md`), data grids (`data-grids.md`), containers and panels (`layout.md`), notifications, feedback or application-state behaviour (`feedback.md`), popup infrastructure (`popups.md`), or the Sample gallery (`sample-gallery.md`).
- Review `geodc/docs/code-vocabulary.md` before introducing or broadly renaming role suffixes.
- Read `docs/github-practices.md` before filing issues, applying labels, or opening pull requests.

## Agent skills

- `.agents/skills` is the canonical, repository-tracked source for geoDC skills. Keep `agents/openai.yaml` there for Codex metadata.
- `.claude/skills` is a generated mirror of the runtime-neutral skill content. Do not edit mirrored skills directly. After changing a canonical skill, run `pwsh -NoProfile -File dev/sync-agent-skills.ps1 -Write`, then run it without `-Write` to confirm parity. The harness's CI runs the parity check on every pull request.

## Rules for every change

The rules that apply to people and agents alike live in the human docs. Follow them:

- `geodc/docs/codebase.md`: the solution map and layering (dependencies point inward; the domain stays free of EF, DI, logging and UI), build and test commands, the domain rules, and an index of every convention page.
- `geodc/docs/domain-context.md` > Rules That Hold Everywhere: the domain rules, with the reasoning behind them.
- `geodc/docs/coding-style.md`: C# conventions, XML documentation and comments, documentation pages, and what a change checks (Making a Change).
- `geodc/docs/data-model.md` > Changing the Model: IDs, value objects, lifecycles, `ProjectFingerprint` coverage and audit.
- `geodc/docs/ui/README.md` > Rules: controls, panels, theme resources, foreground roots, copy and sign wording.
- `geodc/docs/logging.md`: where logging is expected, levels, and the event catalogue.

## Build and test in agent sessions

- geoDC has no CI of its own. Before asking for a merge, run the checks in `geodc/docs/codebase.md` > Checks before merging and releases that your platform allows, and run the harness's `geoDC CI` workflow on the branch (`docs/github-practices.md` > CI); report both under Testing.
- The commands are in `geodc/docs/codebase.md` > Build, Test and Run. Run them inside `geodc/`, or from the harness root with the paths prefixed (`dotnet build geodc/geoDC.slnx`, `dotnet test geodc/tests/geoDC.Domain.Tests`).
- On Linux, `dotnet test geodc/geoDC.slnx` aborts on the Windows-only projects (no `Microsoft.WindowsDesktop.App` runtime). Test the affected projects one by one; `docs/cloud-sessions.md` lists which run.
- `geodc/tools/geoDC.ReportCli` runs on Linux, so use it to check report, template or reporting-data changes end to end (`geodc/docs/reporting.md` > Headless reports).
- If restore fails because of feeds, credentials, SSL, or network access, report it and ask the developer to run restore rather than changing package sources or credentials.
- The pre-commit hook formats staged C# files. Do not run a repository-wide formatting pass.

## UI driver

- The developer guide is `geodc/docs/ui-driver.md`.
- Ask before invoking the UI driver, then follow the `wpf-ui-driver` skill.
- When a change has UI or behaviour the developer should see, especially from a cloud session, hand them a script to run in their worktree with the `geodc-ui-driver-handoff` skill.
- The UI driver resolves its lease, fixtures and builds from its working directory, so always run it from inside geoDC: `pwsh -NoProfile -WorkingDirectory geodc -File geodc/dev/ui.ps1 <verb> ...`.
- Full-app launch may open only `dev/fixtures/probe.gdcproj`, or `dev/fixtures/probe-lifecycle.gdcproj` when a check needs work items in every workflow state, unless the user names another package (both relative to `geodc/`, as the driver sees them). Both are gitignored; create them with `pwsh -NoProfile -WorkingDirectory geodc -File geodc/dev/ui.ps1 fixture seed` (add `--lifecycle` for the second).
- Passing tests or compilation do not prove live layout, focus, keyboard, popup, or rendering behavior. State the verification boundary.

## Cloud sessions

Cloud agent sessions (Claude Code, Codex) run in a disposable Ubuntu VM holding fresh clones of this harness and of geoDC; see `docs/cloud-sessions.md`. Start the session on the harness with `snt0/geodc` attached as a second repository; the setup script and session-start hook link it in as `geodc/` and make the harness's settings and hooks load from the folder above both clones, where such a session starts.

- Creating, moving, and deleting files inside the clones is routine there; nothing outside them matters once the session ends.
- Never push `dev` or `main` (geoDC's or the harness's) from the cloud, and never force push or delete remote branches. Claude Code enforces this with a push guard hook; other agents must follow it by convention.
- Commit and push before stopping. The VM and any uncommitted work are discarded when the session expires.
- The whole solution builds on Linux, but `geoDC.App.Tests`, `geoDC.App.UI.Tests`, the app, the Sample gallery and the UI driver run only on Windows (`docs/cloud-sessions.md` > Verification boundary, `geodc/docs/codebase.md` > Linux and Windows). Changes covered only by those are compile-verified; say so.
- A shallow clone fails every build (Nerdbank.GitVersioning), and a blocked Typst download fails the build unless `GeoDCSkipTypstFetch=true`. Claude Code's session-start hook handles both; otherwise follow `docs/cloud-sessions.md`.
- If the session-start report (Claude Code) or the environment says the .NET SDK is missing, tell the user the environment setup needs fixing rather than installing a toolchain mid-session.

## Branches and pull requests

- Agent branches are named for the agent and the change: `claude/<issue-or-feature>` for Claude Code and `codex/<issue-or-feature>` for Codex (lowercase, hyphenated). Include the issue number whenever the work has an issue, e.g. `claude/issue-123-fingerprint-coverage`.
- This naming overrides any branch the harness or session instructions assign. Cloud sessions start on a generated branch (e.g. `ccr-cb9479d3-9qhhg6`); run `git switch -c claude/<issue-or-feature>` (or `codex/...`) before the first push. The push guard checks the branch before the command runs, so switch and push in separate commands. In a Claude Code worktree, pass the name as `EnterWorktree`'s `name`.
- Open geoDC pull requests into geoDC's `dev`, unless they stack, and harness pull requests into the harness's `main`.
- Stack dependent work: when work builds on an unmerged branch (yours or another session's), branch from it and target its pull request. Check before branching and before opening; `docs/github-practices.md` > Branches and pull requests covers retargeting after the lower one merges.
- Keep each pull request to one purpose and fill in `docs/pull-request-template.md` for geoDC.
- Issues live in `snt0/geodc`, for harness work too. Search open issues before filing; extend or link an existing issue rather than duplicating it. Use the existing labels; do not create, rename, or delete them.
- `docs/github-practices.md` has the full issue, label, and pull request conventions.

## Issue tracking

The issue body is the record of what is asked and what is done; `docs/github-practices.md` > Keeping issues current has the full rules. In short:

- Note an open pull request in the issue body as pending. When it reaches `dev`, the session that opened it ticks the criteria it met, rewords partly met ones, and closes the issue if all are met; the developer usually merges, so don't leave this to them.
- Before starting on an issue or epic, reconcile it with what has merged on `dev`. Tick a manual check the developer reports in the same turn, and keep an epic's current-state note in step with its children.
- Record remaining work as an unchecked criterion or a sub-issue, never only in a comment or pull request body. Group epic children as native sub-issues.
- Use `Part of #N` unless every unmet criterion was dropped or moved; when an issue stays open, the final report says why and lists its unchecked criteria.