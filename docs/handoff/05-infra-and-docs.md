# 05 — Infrastructure split and documentation audit

Verified @ e9a8766 (origin/dev, `/home/user/geodc`). Read-only investigation for the handoff epic (snt0/geodc#780). Evidence is `path:line`; GitHub facts were read through `gh` on 2026-11-01.

## 0. Headline findings

| # | Finding | Evidence |
|---|---|---|
| 1 | The generated `main` is already red: its only CI run (2026-10-19, run 39255918328) failed in **Agent skills** (`dev/sync-agent-skills.ps1` is stripped by `.publishignore` but `ci.yml:60` still runs it) and in **Build and test** (`geoDC.Probe.Tests.DoInvocationTests.CheckedInScripts_*` reads `dev/probe-scripts/`, which `.publishignore:24` strips). | `.publishignore:23-24`, `.github/workflows/ci.yml:48-60`, `tests/geoDC.Probe.Tests/DoInvocationTests.cs:298-308`, `gh run view 39255918328` |
| 2 | Nothing in the build graph depends on the paths that move except two items that must therefore stay or be re-homed: `dev/probe-scripts` (test data for `geoDC.Probe.Tests`) and `tools/geoDC.GalleryCorpus` (a solution project referencing `geoDC.App.UI.Sample`). `dev/fetch-typst.ps1`, `dev/format-xaml.ps1`, `dev/build-installer.ps1`, `dev/release.ps1`, `dev/build-proj.ps1`+`dev/proj/`, `dev/runners.ps1` are the only `dev/` files build/CI/release need, and none of them is on the move list. | `Directory.Build.targets:21,30`, `.husky/task-runner.json:29,36`, `ci.yml:132`, `installer.yml:109`, `.publishignore:13-15` |
| 3 | CI cannot run "entirely GitHub-hosted" as configured: the reusable selector job itself defaults to `runs-on: ["self-hosted","linux","geodc-selector"]` (`select-runner.yml:41`), so with the VPS offline every run queues up to 24 h (`docs/ci-runners.md:9`). Hosted-only needs that default (or `selector:` input from `ci.yml:31`/`installer.yml:26`) changed to `"ubuntu-latest"`; the rest already falls back to `windows-2022`/`ubuntu-latest` when `RUNNER_STATUS_TOKEN` is absent (`select-runner.yml:152`). | `select-runner.yml:36-41,118-175` |
| 4 | Everything that assumes a repository root is agent/dev tooling: all 12 skills use root-relative `docs/`, `src/`, `dev/`, `tools/` or `tests/` paths (wpf-probe alone has 20 `dev/` references), `.claude/settings.json` allow-lists `dev/probe.ps1`, `dev/sync-agent-skills.ps1`, `tools/geoDC.Probe/bin/*` and the three hooks `cd "$CLAUDE_PROJECT_DIR"` then run `dotnet restore geoDC.slnx` and `dev/fetch-typst.ps1` from there. The probe binary resolves `.probe/` and `dev/fixtures/probe.gdcproj` from its **working directory**, not the shim's location. | `.claude/settings.json:84-94,131-155`, `.claude/hooks/cloud-session-start.sh:6,72-83`, `tools/geoDC.Probe/ProbeTarget.cs:32`, `tools/geoDC.Probe/ProbeStateDirectory.cs:12-14`, `.agents/skills/wpf-probe/SKILL.md:17-27` |
| 5 | Docs are 8,472 lines in 39 files (5,267 in the 26 top-level pages, 2,247 in `docs/ui/`, 958 in `docs/design/`), not ~5k. Only 6 pages are agent-only (`cloud-sessions*.md`, `publishing.md`, and the capture/design-system half of `ui/sample-gallery.md`, plus `ci-runners.md` if the runners go). The rest is human reference that AGENTS.md merely points at; AGENTS.md itself holds ~60 lines of human content that no doc repeats ("Solution boundaries", "Domain rules", build commands). | `wc -l docs/**`, `AGENTS.md:29-83`, grep of AGENTS phrases across docs (section 4.3) |
| 6 | Recommendation on packaging: a **nested, gitignored clone** (harness repo is the parent directory, `geodc/` inside it, bootstrap script clones/pulls `dev`) is simpler than a submodule for this use: no detached HEAD, no pointer bumps, no second PR, identical path changes. Use a submodule only if the owner wants `git clone --recursive` and a recorded harness↔geoDC pairing. Either way the agent's working directory becomes the harness root and every path gains a `geodc/` prefix. | Section 3 |

## 1. File classification — summary (full table in Appendix A)

Counting rules: `.agents/` 31 files (12 skills), `.claude/` 26 files (settings, 3 hooks, 22 mirrored skill files), `.codex/` 1, `.husky/` 8, `dev/` 17 scripts + `proj/` (vcpkg manifest, 3 triplets, 3 overlay ports) + `probe-scripts/` (13 `.probe` files), `docs/` 39, `.github/` 6, `installer/` 2, `tools/` 9 top-level entries, `assets/` 8 SVGs + `fonts/` (15 TTF).

### 1.1 Disagreements with `.publishignore` (the current answer), both directions

| Path | `.publishignore` says | Reality | Call |
|---|---|---|---|
| `dev/probe-scripts` | strip (`:24`) | `tests/geoDC.Probe.Tests/DoInvocationTests.cs:298-308` enumerates `dev/probe-scripts/**/*.probe` as a `TestCaseSource`; `TextRendererTests.cs:21,138` hard-code the paths; `tools/geoDC.Probe/TextRenderer.cs:15` strips the prefix. Stripping it broke `main` CI. | **Stays in geoDC** (it is probe test data), or the test case source moves to the outer repo with the scripts. Lean: stays. |
| `dev/sync-agent-skills.ps1` | strip (`:25`) | `ci.yml:48-60` runs it on every PR and on `main`; stripping without editing `ci.yml` broke `main`. | Moves out **together with** deleting the `agent-skills` job and `ci.yml:42-44` skip-paths (or move the job to the outer repo's CI). |
| `.husky` | strip (`:17`) | README `:15-16` tells every developer to `dotnet husky install`; `dotnet-tools.json:5-11` pins husky; `.husky/task-runner.json:29,36` drives `dev/format-xaml.ps1`, which CI enforces (`ci.yml:128-132`). This is developer tooling for the Windows/VS user, not agent tooling. | **Stays in geoDC** (disagree with `.publishignore`). |
| `tools/geoDC.GalleryCorpus` | published (not listed) | Owner's brief lists it as capture tooling to move. It is a 1-file WPF exe (`tools/geoDC.GalleryCorpus/Program.cs`) in `geoDC.slnx:45`, referencing `$(GeoDCSourceRoot)geoDC.App.UI.Sample` (`geoDC.GalleryCorpus.csproj:4`); only `dev/capture-ds-previews.ps1:44` and `docs/ui/sample-gallery.md:55-56` use it. Moving it out means an outer-repo csproj with a `../geodc/src/...` reference and no `Directory.Build.props`. | **Stays** (cheap, builds with the solution, useful to a human checking one specimen); move only the `.ps1` drivers. Open question 6.4. |
| `tools/geoDC.Probe`, `tests/geoDC.Probe.Tests` | published (`docs/publishing.md:22`) | Agent-first tooling, but a solution project with 270 tests and a `code-vocabulary.md` AutomationId contract (`tools/geoDC.Probe/AutomationConventions.cs`). | Stays. Consistent with `.publishignore`; noted because the brief treats "the probe" as a CLI tool to document. |
| `README.md:42-44,63` | published | Links `AGENTS.md`, `docs/cloud-sessions.md`, `.agents/skills` — all stripped, so `main`'s README has dead links today. | Rewrite README "Documentation" (section 5.4). |
| `docs/ci-runners.md:12,49`, `docs/github-practices.md:4,107`, `docs/installer.md:143-144` | published | Reference `docs/publishing.md`, `AGENTS.md`, the Agent skills job. | Edit when `publishing.md` is deleted and AGENTS.md leaves. |
| `.gitignore:22-24,41-43,51,58` | published | Ignore rules for `.agents/*`, `.codex/*`, `.claude/settings.local.json`, `.claude/worktrees/` — harmless on `main`, but note `.gitignore:37,40,44` (`dev/fixtures/`, `dev/captures/`, `/.probe/`) must **stay** in geoDC's `.gitignore` even after the drivers move, because the probe and captures write inside the geoDC worktree when run with `-WorkingDirectory geodc`. | Keep the three worktree-side rules; drop the agent-config rules. |
| `dev/cloud-setup.sh`, `dev/capture-*.ps1`, `dev/probe.ps1`, `dev/seed-probe-fixture.ps1`, `dev/sync-design-system.ps1`, `.agents`, `.claude`, `.codex`, `docs/cloud-sessions*.md`, `AGENTS.md`, `dev/publish-main.ps1`, `publish-main.yml`, `.publishignore` | strip | Agree. No build, test or workflow reference remains once `ci.yml:39-60` is edited (references are only from each other, AGENTS.md, skills and `docs/cloud-sessions*.md`). | Outer repo (publish machinery: delete). |
| `docs/publishing.md` | published | Describes the machinery being deleted; linked from `AGENTS.md:14`, `docs/ci-runners.md:12`, `docs/installer.md:143`. | **Delete** (not in `.publishignore` today — a disagreement in the other direction). |

## 2. Build and CI dependencies on paths that move

### 2.1 What each build/CI file references

| File | References into `dev/`, `.claude/`, `.agents/`, `docs/` | After the split |
|---|---|---|
| `Directory.Build.props` | none (defines `GeoDCRepositoryRoot` from its own location, `:3-6`; injects `tests/StaTestRunner.cs`, `GlobalUsings.Tests.cs`, `NUnitAssemblySettings.cs`) | Unchanged. Works in a submodule/nested clone because MSBuild walks up from the **project**, not the shell's cwd. |
| `Directory.Build.targets` | `dev\fetch-typst.ps1` (`:21,30`), `tools\typst\<rid>\` (`:20`), `src\geoDC.Proj\runtimes` (`:59`) | Unchanged; all stay. |
| `Directory.Build.rsp` | none | Unchanged. |
| `dotnet-tools.json` | husky 0.9.1, jb, nbgv, xstyler | Unchanged; husky and xstyler imply `.husky/` and `Settings.XamlStyler` stay. |
| `.husky/task-runner.json` | `dev/format-xaml.ps1` (`:29,36`) | Unchanged. `post-checkout:6-9` self-bootstraps husky. |
| `geoDC.slnx` | `/docs/` solution folder lists 10 docs (`:2-13`); `/src/` lists `GlobalUsings.Library.cs`, `GlobalUsings.Wpf.cs` and `/tests/` lists `StaTestDispatcher.cs`, **none of which exist** (`ls` at root: only `GlobalUsings.Tests.cs`, `NUnitAssemblySettings.cs`; `tests/StaTestRunner.cs` is the real file). | Fix the three stale `<File>` entries (VS shows them as missing); add the new reference doc to `/docs/`. |
| `.github/workflows/ci.yml` | `select-runner.yml` (`:31`), skip-paths `^\.agents/ ^\.claude/ ^\.codex/` (`:42-44`), `./dev/sync-agent-skills.ps1` (`:60`), `./dev/format-xaml.ps1` (`:132`) | Delete job `agent-skills` (`:46-60`) and the three skip-paths; keep Format/Test. |
| `.github/workflows/select-runner.yml` | `docs/ci-runners.md` (`:11`); default selector = self-hosted Linux (`:41`); `RUNNER_STATUS_TOKEN`, `vars.CI_HOSTED_FALLBACK` | Keep only if self-hosted runners survive; else set `selector` default to `"ubuntu-latest"` or replace with static `runs-on`. |
| `.github/workflows/nuget-cache.yml` | none moving; seeds the Actions cache for hosted runs because dev pushes normally run self-hosted and never save (`:3-7`, `ci.yml:134-140`) | Hosted-only CI saves its own cache on every `dev` push (`ci.yml:137-145`), so this becomes an eviction-recovery convenience. Keep (67 lines) or delete; lean keep. |
| `.github/workflows/installer.yml` | `select-runner.yml` (`:26`), `./dev/build-installer.ps1` (`:109`), `CHANGELOG.md` (`:271-294`) | Unchanged apart from the selector decision. |
| `.github/workflows/publish-main.yml` | `dev/publish-main.ps1` (`:38`), `geodc-selector` runner (`:21`) | Delete. |
| `dev/build-installer.ps1` | `src/geoDC.App`, `installer/geoDC.Installer`, `dev/build-proj.ps1` message (per `docs/publishing.md:18-19`) | Stays. |
| `dev/release.ps1` | `CHANGELOG.md`, `version.json`, `installer.yml` | Stays; its `main`-only flow (`:21-22`) needs the post-publish branch model (open question 6.2). |
| `dev/runners.ps1` | `docs/ci-runners.md` | Stays only if runners stay. |
| `.claude/settings.json` | hooks `$CLAUDE_PROJECT_DIR/.claude/hooks/*.sh` (`:131,143,155`); allow-list `dev/probe.ps1`, `dev/sync-agent-skills.ps1`, `dev/seed-probe-fixture.ps1`, `tools/geoDC.Probe/bin/*` (`:84-94`); probe pre-approval regex (`:126`) | Moves; every allow-list path gains `geodc/` (or the probe runs via `-WorkingDirectory geodc`, which changes the regex at `:126`). |
| `.claude/hooks/cloud-session-start.sh` | `cd "$CLAUDE_PROJECT_DIR"` (`:6`), `git fetch --unshallow` (`:36`), `git lfs pull` (`:47`), `dotnet husky install` (`:60`), `tools/typst/linux-x64/typst.pin` and `dev/fetch-typst.ps1` (`:72-74`), `dotnet restore geoDC.slnx` (`:83`) | Must `git submodule update --init` (or clone) then `cd geodc` before `:35`; LFS, husky, unshallow and restore all operate on the inner repo. |
| `.claude/hooks/git-push-guard.sh` | branch from `git -C "$repo" symbolic-ref` (`:31`), honours `git -C <dir> push` (`:27-30`) | Works for `git -C geodc push`; on a **detached-HEAD submodule** `branch` is empty and every refspec push is denied (`:60`) — correct behaviour, but the agent must `git switch -c claude/...` inside `geodc/` first (already the rule, `AGENTS.md:136`). |
| `.codex/rules/default.rules` | `pwsh -NoProfile -File dev/probe.ps1` prefix (`:5`) | Moves; prefix becomes `geodc/dev/probe.ps1` or a `-WorkingDirectory` form. |

### 2.2 Can CI run entirely on GitHub-hosted Windows runners?

Yes, with two edits: (a) the selector job's own `runs-on` (`select-runner.yml:41`) — today it is the one hard dependency on self-hosted hardware; (b) delete the `agent-skills` job. Once `RUNNER_STATUS_TOKEN` is absent or the token lookup finds no `geodc-windows` runner, Build-and-test targets `windows-2022` and Format targets `ubuntu-latest` (`select-runner.yml:20-29,152,172`). `docs/ci-runners.md:117-119` states the design goal that hosted fallback "produces the same build": WiX comes from NuGet (`installer/geoDC.Installer/geoDC.Installer.wixproj:1,32`), Typst from `dev/fetch-typst.ps1` (GitHub release download, allowed on hosted runners), PROJ from committed binaries (`.gitattributes:13`), fonts/logos from Git LFS (`ci.yml:166`, `lfs: true`). Costs to flag: a ~30-min Windows job bills 2× minutes; LFS bandwidth is negligible (8 SVGs). `nuget-cache.yml` keeps hosted restores fast after cache eviction.

### 2.3 What the self-hosted runners are for (`docs/ci-runners.md`, `dev/runners.ps1`)

- `geodc-windows` runners on the owner's desktop (`st-desktop-1..3`, `docs/ci-runners.md:143-154`): free, faster Windows builds; persist `~/.nuget/packages` (`ci.yml:89-92`). One job per runner; Format and Test parallelise only with ≥2 free runners (`:136-138`).
- `geodc-selector` on an Ubuntu VPS: runs the 2-minute selector (`select-runner.yml:63-66`) and the Publish-main job (`publish-main.yml:21`) so CI keeps working when hosted minutes are exhausted (`docs/ci-runners.md:3-13`); never checks out code (`:158`).
- `dev/runners.ps1` (465 lines) installs/starts/stops/removes them as services, pre-installs the pinned SDK into `C:\actions-dotnet` (`docs/ci-runners.md:125-131`), and needs `gh auth login` as repo admin (`:99-102`).
- Secrets/vars involved: `RUNNER_STATUS_TOKEN` (fine-grained PAT, Administration: read, `:76-80`), `CI_HOSTED_FALLBACK` (`:29-36`).
- Live state on 2026-11-01: the latest `dev` CI run (41267953932 @ e9a8766) passed all four jobs; the runner inventory API is blocked through this proxy, so which runners are online was not verifiable.

## 3. Submodule mechanics

### 3.1 Proposed outer layout (either option)

```
geodc-harness/                 # the new repo; agent cwd
├── AGENTS.md / CLAUDE.md      # from geodc/AGENTS.md minus the human sections (4.3)
├── .agents/skills/… .claude/{settings.json,hooks/,skills/} .codex/rules/
├── dev/ (cloud-setup.sh, probe.ps1, seed-probe-fixture.ps1, capture-*.ps1, sync-*.ps1, publish? no)
├── docs/ (cloud-sessions*.md, ci-runners "Managing runners" if runners stay, capture/design-system sync)
├── bootstrap.ps1|sh           # clone/pull geodc on dev, restore tools, husky install, fetch typst
└── geodc/                     # submodule (option A) or gitignored nested clone (option B)
```

### 3.2 What assumes repository-root paths today and how it changes

| Item | Today | Change |
|---|---|---|
| Skills (`.agents/skills/**`): `docs/` (29 refs), `src/` (62), `dev/` (23), `tools/` (6), `tests/` (8) | Root-relative, e.g. `.agents/skills/wpf-probe/SKILL.md:13` (`pwsh -NoProfile -File dev/probe.ps1`), `:36` (`dev/fixtures/probe.gdcproj`), `geodc-test-data-builder/SKILL.md:13-15`, `geodc-charting-builder/SKILL.md:37` (`tools/preview`) | Prefix `geodc/` everywhere, or state once at the top of AGENTS.md that "all paths are relative to `geodc/`" and run commands with `-WorkingDirectory geodc`. The second is less churn but fragile for the PowerShell tool's allow-list regexes (`.claude/settings.json:126`). |
| `AGENTS.md:62` "Run PowerShell commands from the repository root"; `:64-72` command block | `dotnet build geoDC.slnx` from root | `dotnet build geodc/geoDC.slnx` works from the harness root (MSBuild locates `Directory.Build.*` from the project); `dotnet test` and `dotnet run --project geodc/...` likewise. |
| Probe (`tools/geoDC.Probe`) | Resolves `.probe/`, `dev/fixtures/probe.gdcproj` (`ProbeTarget.cs:32`), `src/.../bin` from **cwd** (`wpf-probe/SKILL.md:17-27`) | Always `pwsh -NoProfile -WorkingDirectory geodc -File geodc/dev/probe.ps1 …` (the skill already documents this form for other worktrees). `dev/probe.ps1:9-11` finds the project from its own location, so the shim can live in the harness only if it is rewritten to point at `../geodc/tools/geoDC.Probe`. Simpler: leave `dev/probe.ps1` in geoDC (it is 60 lines, harmless) and move only the policy around it. |
| Convention tests scanning `dev` (`tests/geoDC.Infrastructure.Tests/Conventions/CommentConventionTests.cs:45`) | `SourceTree.Files(..., "src","tests","tools","dev")` from the dir containing `geoDC.slnx` (`SourceTree.cs:10-18`) | Fine in both options (root found by walking up to `geoDC.slnx`, which stays in the inner repo). Harness scripts stop being scanned for the comment rule — acceptable. |
| `.claude/hooks/cloud-session-start.sh` | Assumes `$CLAUDE_PROJECT_DIR` **is** the geoDC clone (`:6,72,83`) | Add: ensure `geodc/` exists (submodule init or clone), then `cd geodc` for unshallow/LFS/husky/typst/restore. `git fetch --unshallow` must run in the inner repo (NBGV reads inner history). |
| `.claude/hooks/cloud-stop-guard.sh:8-12` | `git status` / unpushed count in `$CLAUDE_PROJECT_DIR` | Must check **both** repos; in option B the outer `git status` never sees inner changes. |
| `dev/cloud-setup.sh` | Machine-level only (SDK, pwsh, git-lfs, `:11-29`); no cwd assumption | Unchanged. `docs/cloud-sessions-claude.md:7-10` and `-codex.md:18-39` (which runs `dev/fetch-typst.ps1`, `dotnet restore geoDC.slnx` from the clone root) need the `cd geodc`. |
| `.gitignore` worktree rules (`:37,40,44`: `dev/fixtures/`, `dev/captures/`, `/.probe/`) | In geoDC | Stay in geoDC (the tools write into the inner worktree); duplicate `dev/captures/` in the harness if the capture scripts default there (`dev/capture-views.ps1:487` uses the script's repo root). |
| `tools/preview/server.mjs:6-8` | `repoRoot = ../..` from the script | Unaffected (stays). |

### 3.3 Cloud-session assumptions

The claude.ai/code environment is created for one repository; `cloud-session-start.sh` treats that clone as geoDC. With a harness repo as the session repo, geoDC becomes a **second private repository** that the session's git proxy must be authorised for (attach it to the session/environment); the setup script cannot clone it before that. Both options share this; it is the main operational risk of the split for cloud sessions and should be tried once before committing (open question 6.6). Codex (`docs/cloud-sessions-codex.md:6-7`) is already marked unverified.

### 3.4 PR workflow when the agent edits the submodule

1. In `geodc/`: `git switch -c claude/issue-N-…` (mandatory — a fresh submodule checkout is detached), commit, `git -C geodc push -u origin HEAD`, open the PR into `geodc:dev`. Review happens here; nothing changes.
2. In the harness: `git status` shows `modified: geodc (new commits)`. Either never commit the pointer (option B makes this moot) or bump it in a separate harness PR after the geoDC PR merges. A pointer committed to an unmerged geoDC branch breaks every other clone's `git submodule update`.
3. Claude Code `EnterWorktree` (`AGENTS.md:136`) creates a worktree of the **outer** repo; submodules are not populated in a new worktree without `git submodule update --init` (another clone and another restore). With option B the nested clone is not part of the outer repo at all, so worktrees of the harness never carry geoDC — agents must work in the single checkout or use `git -C geodc worktree add`.

### 3.5 Pitfalls specific to a submodule here

- Detached HEAD after every `submodule update`; the push guard then denies every push (correct, but confusing).
- NBGV computes versions from inner history; a shallow or `--depth 1` submodule clone fails every build (`AGENTS.md:130`). CI in the geoDC repo is unaffected (it clones geoDC directly).
- `git lfs pull`, `dotnet husky install` (`core.hooksPath` is per repository) and `git config lfs.<url>.locksverify` (`cloud-session-start.sh:55-58`) all have to target the inner repo.
- `.git` is a file in a submodule; nothing in the tree looks for a `.git` directory (grep over `src tests tools dev` found only `geoDC.slnx`-based root discovery), so no code breaks.
- Two sources of truth for "which geoDC": the pointer and `dev`. Agents want `dev` HEAD, so the pointer is pure maintenance.

### 3.6 Recommendation

Option B — harness-as-parent with a **gitignored nested clone** of geoDC on `dev`, plus a bootstrap script. It gives the same single working directory and the same config discovery (Claude Code reads `CLAUDE.md`/`.claude/` at the cwd; Codex reads `AGENTS.md` at the git root, which is the harness), with none of the pointer/detached-HEAD/worktree costs. Choose the submodule (option A) only if the owner wants `git clone --recursive` ergonomics or a recorded pairing between harness and geoDC commits. In both: prefix paths with `geodc/`, run the probe with `-WorkingDirectory geodc`, and make the two hooks `cd geodc`.

## 4. Docs audit

### 4.1 Page-by-page (size = lines; inbound = files linking to it, from grep; verdicts: keep / fold → codebase reference / outer / delete)

| Page | Lines | Topic | Audience | Inbound links | Overlap | Verdict |
|---|---|---|---|---|---|---|
| `domain-context.md` | 316 | What dimensional control is; VRF, sign convention, cal/ver cycle, GNSS, shore control, glossary | human | README, AGENTS, 1 skill, 5 docs, slnx | AGENTS "Domain rules" `:31-40` (8 bullets; 6 already implied here) | keep; add a short "Rules that cross layers" box from AGENTS `:33-40` |
| `data-model.md` | 711 | Aggregates, value objects, state machines, persistence notes | human | README, AGENTS, 2 skills, 8 docs, slnx | AGENTS `:96-99` | keep |
| `data-analysis.md` | 160 | Dataset import, inspection, alignment, editing popup | human | README, AGENTS, 1 doc | — | keep |
| `coding-style.md` | 218 | C# baseline, type shapes, DI, nullability, async, comments; links hooks/CI | human | README, AGENTS, 2 skills, tests (comment), props | AGENTS `:85-103` (var, NUlid, XML docs, "no history in source") — all already here except `IApplicationMetadata`, zstd, WPF global usings | keep; absorb the 3 missing bullets |
| `code-vocabulary.md` | 202 | Role suffixes, settled vocabulary, AutomationId roles | human | README, AGENTS, 1 skill, `tools/geoDC.Probe/AutomationConventions.cs` | coding-style "Naming" `:199-208` | keep (contract for the probe); could become an appendix of coding-style |
| `copy-style.md` | 115 | Writing user-facing text | human | AGENTS, 1 skill, 2 docs | string-resources | keep |
| `logging.md` | 323 | Where/how to log, event catalogue, properties | human | README, AGENTS, 5 skills, tests (comment) | AGENTS `:87-88` restates `:10-32` | keep |
| `github-practices.md` | 133 | Labels, issues, keeping issues current, branches/PRs | human (+agent branch rules) | README, AGENTS, 1 skill, PR template | AGENTS `:133-150` repeats `:103-133` and adds agent prefixes | keep as human doc; move `claude/`/`codex/` prefix rules and "Keeping issues current" agent duties to outer AGENTS.md |
| `project-packages.md` | 166 | `.gdcproj` container, locks, manifest, schema history, encryption | human | README, AGENTS, 3 skills, 3 docs, slnx | AGENTS `:100-101` | keep |
| `reporting.md` | 275 | Typst pin, request, template, renderer, **Headless reports (ReportCli)** `:210-243`, Reports page | human | README, AGENTS, 3 docs, `geoDC.Infrastructure.csproj:28`, `tools/geoDC.ReportCli/*`, tests | AGENTS `:71-74` (ReportCli one-liner) | keep; reference links here for ReportCli |
| `charting.md` | 290 | Renderer-neutral contracts, D3 modules, WebView2 host, `tools/preview` harness `:226-244`, tests | human | README, 1 skill, reporting.md, slnx | — | keep |
| `string-resources.md` | 230 | YAML strings → generated classes; "English only; generator supports other languages' plural rules" `:100` | human | README, AGENTS, 1 skill, 3 docs, tests (comment) | AGENTS `:113` | keep (this is where localisation stays possible) |
| `test-data-generator.md` | 407 | Presets, signal model, scenarios, **CLI `:272-345`**, WPF front end `:336`, fingerprint | human | README, 1 skill, `tools/geoDC.TestDataGenerator.*` (3 files), `dev/seed-probe-fixture.ps1` | — | keep; `:288` mentions `dev/seed-probe-fixture.ps1` → reword |
| `geobase-import.md` | 124 | GEOBASE→`.gdcproj` migration CLI (usage `:7-30`) | human | project-packages.md, `tools/geoDC.GeobaseImport/Program.cs` | — | keep |
| `coordinate-transforms.md` | 249 | CRS requests, strict/display modes, epochs, provenance | human | AGENTS, 2 docs, slnx | data-model Shore Control | keep |
| `proj-native.md` | 307 | What ships, loader, grids, **rebuilding with `dev/build-proj.ps1`** | human | AGENTS, 3 docs, 3 src files, `dev/build-proj.ps1`, `.gitattributes:11`, slnx | — | keep |
| `work-item-alterations.md` | 223 | Alteration guards, tokens, invalidation contract | human | AGENTS, 2 docs | data-model lifecycles | keep |
| `shell-menus.md` | 166 | Menu placement rules and inventory | human | AGENTS, 1 doc | ui/controls Menu Bar | keep |
| `installer.md` | 212 | WiX MSI, scopes, versioning, **Cutting a release `:141-166`**, bundled Typst/PROJ | human | README, AGENTS, 2 docs, `dev/build-installer.ps1`, `dev/release.ps1`, slnx | `:143-144` cites publishing.md | keep; edit release section for the post-publish branch model |
| `third-party-notices.md` | 66 | Generating the notices file | human | AGENTS, 2 docs, 4 src refs | — | keep |
| `branding-assets.md` | 42 | `geoDC.App.UI.Branding`, changing the app font | human | `geoDC.Infrastructure.csproj:28`, slnx | ui/theming typography | **fold** into reference (App.UI.Branding section) |
| `ci-runners.md` | 171 | Selector, hosted fallback, NuGet cache, token, **runner management `:82-165`**, forks | human (ops) | README, AGENTS, publishing.md, `select-runner.yml:11`, 4 workflow comments, `dev/runners.ps1` | — | if runners retired: fold `:1-80,167-171` into the reference's CI section and delete the rest with `dev/runners.ps1`; if kept: keep as is minus `:12-13` |
| `publishing.md` | 60 | Generated `main` | agent/owner | AGENTS, ci-runners, installer | — | **delete** with the machinery |
| `cloud-sessions.md` | 30 | VM toolchain; **Linux verification-boundary table `:18-29`** | agent | README, AGENTS, 2 docs, hook | — | **outer repo**; copy the table into the reference's testing section (humans on Linux CI care) |
| `cloud-sessions-claude.md` | 22 | Environment settings, hook table | agent | AGENTS, cloud-sessions | — | outer repo |
| `cloud-sessions-codex.md` | 49 | Codex environment (unverified) | agent | cloud-sessions | — | outer repo |
| `ui/README.md` | 39 | Toolkit index, scope, layout | human | README, 9 docs, `src/geoDC.App.UI/UI-DOCS.md` | — | keep (index) |
| `ui/controls.md` | 1055 | Every toolkit control | human | AGENTS, 2 skills, 5 docs, probe | — | keep (largest single page; the reference links, never duplicates) |
| `ui/theming.md` | 185 | Theme consumption, foreground roots, tokens | human | AGENTS, 2 skills, 2 docs, tests | AGENTS `:107-112` | keep; absorb AGENTS UI bullets `:107-109,112` |
| `ui/data-grids.md` | 122 | Converters, columns, copy/menu/footer | human | AGENTS, 2 docs | — | keep |
| `ui/layout.md` | 276 | Containers, wizard, blueprint surface, panels | human | AGENTS, 9 docs, tests | AGENTS `:108` (`snt:` panels) | keep |
| `ui/page-layout.md` | 116 | `UiPage` regions, archetypes, collapse | human | 9 docs | design/page-layout (the dated record) | keep |
| `ui/feedback.md` | 52 | Notifications, feedback, app-state behaviour | human | AGENTS, 1 doc | — | keep |
| `ui/popups.md` | 291 | Popup frame, sizes, close guards, dialogs | human | AGENTS, 3 skills, 2 docs, probe | — | keep |
| `ui/sample-gallery.md` | 111 | Specimens, taxonomy, coverage `:1-50,80-111`; **screenshot corpus + design-system sync `:51-78`** | mixed | AGENTS, 1 skill, 2 docs, tests, 2 dev scripts | — | split: gallery part stays; `:51-78` → outer repo with the capture scripts |
| `design/README.md` | 14 | Index of dated design records | human | README, 9 docs | — | keep |
| `design/angle-position-conventions.md` | 307 | Frames, attitude, roll, C-O hand-off, schema 7 (dated) | human | 2 docs, 1 src | domain-context Sign Convention | keep (record) |
| `design/page-layout.md` | 153 | Page frame design decisions (dated) | human | 10 docs | ui/page-layout | keep (record) |
| `design/shore-control-workbench.md` | 484 | Shore Control page design (dated) | human | 1 doc | data-model Shore Control | keep (record) |

In-tree docs outside `docs/`: `src/geoDC.App.UI/UI-DOCS.md` (3-line pointer; delete or keep), `src/geoDC.App/Workspace/Handlers/IDENTITY_CONVENTIONS.md` (8 lines, cited by `AGENTS.md:102`, `coding-style.md:218`, 3 skills; keep), `tests/geoDC.Infrastructure.Tests/Crs/Grids/README.md` (provenance; keep).

### 4.2 Which docs the CLI tools already have

| Tool | Existing usage doc | Gap |
|---|---|---|
| `tools/geoDC.ReportCli` | `AGENTS.md:71-74`; `docs/reporting.md` > Headless reports `:210-243`; `Program.cs` cites reporting.md | Usage line lives only in AGENTS.md → must move into the reference |
| `tools/geoDC.TestDataGenerator.Cli` (+`.Wpf`, `.Core`) | `docs/test-data-generator.md:272-345` (full option table) | none |
| `tools/geoDC.GeobaseImport` | `docs/geobase-import.md:7-30` | none |
| `tools/geoDC.Probe` (+`dev/probe.ps1`, `dev/probe-scripts`) | only `.agents/skills/wpf-probe/SKILL.md` (358 lines) and `AGENTS.md:116-120` | no human page at all; the reference needs a 15-line "UI probe" entry (verbs, `.probe/`, fixtures) linking to the skill in the outer repo |
| `tools/preview` (chart harness) | `docs/charting.md:226-244` | none |
| `tools/geoDC.GalleryCorpus` | `docs/ui/sample-gallery.md:55-56` | none |

### 4.3 AGENTS.md sections that are human content, and where each lands

| AGENTS.md section (lines) | Nature | Destination |
|---|---|---|
| Orient by task `:5-22` | agent routing table | outer AGENTS.md (paths prefixed) |
| Agent skills `:24-27` | agent | outer |
| Domain rules `:29-40` | human invariants; not stated as a list anywhere in docs (phrases "job-scoped", "diagnostic only", "reproducible" are scattered across data-model/domain-context/reporting) | reference § Domain (verbatim list), link from domain-context.md |
| Solution boundaries `:42-58` | human; only README `:24-37` has a thinner version; "Dependencies point inward" appears nowhere else | reference § Project map (the backbone of the new doc) |
| Build and test `:60-83` | human; README `:12-24` has the short form | reference § Build, test, run (+ CLI usage) |
| Logging and documentation `:85-90` | human | logging.md (already) + coding-style.md "Comments and Documentation" (already) |
| Coding and persistence conventions `:92-103` | human | coding-style.md (`:94-96,103`), data-model.md/project-packages.md (`:97-101`), IDENTITY_CONVENTIONS (`:102`) |
| UI conventions `:105-114` | human | ui/README.md "Rules" (new 10-line section) and string-resources.md (`:113`) |
| UI probe `:116-120` | agent | outer; one human paragraph in the reference |
| Cloud sessions `:122-131` | agent | outer |
| Branches and pull requests `:133-141`, Issue tracking `:143-150` | mixed | github-practices.md keeps the human rules (already there); agent prefixes and issue-body duties → outer |
| Documentation and checks `:152-158` | human | coding-style.md "Checks" `:190-197` + reference § Documentation |

## 5. Codebase reference design

### 5.1 Structure: by project, grouped by boundary (hybrid)

Recommendation: **by project** in dependency order, under five boundary headings, because every file, test project, csproj and `Directory.Build.props` condition (`:26`) is per project, VS Solution Explorer shows projects, and the existing docs already map 1:1 to projects or tools. A pure by-boundary doc would have to invent names for things the solution does not have. The boundary headings keep the "what depends on what" story readable.

File: `docs/codebase.md` (single file, ~500-700 lines). Each project section follows one template: purpose (2-3 lines) · key folders/types · entry points · how it is tested (test project, Linux/Windows) · related detailed doc(s).

| Part / section | Contents | Material comes from |
|---|---|---|
| **0. Orientation** | What geoDC is (5 bullets), solution map diagram (dependency arrows), "dependencies point inward", layering rules, where things live table | `README.md:3-37`, `AGENTS.md:44-58` |
| **1. Build, test, run** | Prerequisites (VS 2026/.NET 10 pinned by `global.json`, git-lfs, pwsh 7), first-run commands, `GeoDCStrictCodeStyle`, format hooks (Husky, XAML Styler), test layout (`Directory.Build.props:53-66`, STA runner), Linux verification-boundary table, CI jobs and what runs where, release/installer flow | `README.md:12-24`, `AGENTS.md:62-83`, `docs/cloud-sessions.md:18-29`, `docs/coding-style.md:6-20`, `docs/ci-runners.md:1-54`, `docs/installer.md:35-55,141-166` |
| **2. Domain** (`geoDC.Measurements`, `geoDC.Domain`, `geoDC.Application`) | Units/CRS primitives; domain rules list; workflow state machines under `Domain/Common/Workflow/`; use-case interfaces | `AGENTS.md:31-40,46-48,97`, → `domain-context.md`, `data-model.md`, `work-item-alterations.md`, `coordinate-transforms.md` |
| **3. Persistence and platform** (`geoDC.Proj`, `geoDC.Infrastructure`, `geoDC.Reporting.Contracts`) | EF Core SQLite, audit interceptor, packages (zstd tar, legacy ZIP), fingerprint, ingestion, PROJ binding, reporting adapters, Typst provisioning | `AGENTS.md:49-51,98-101`, → `project-packages.md`, `proj-native.md`, `reporting.md`, `charting.md`, `third-party-notices.md` |
| **4. UI toolkit** (`geoDC.App.Core`, `geoDC.App.UI`, `geoDC.App.UI.Branding`, `geoDC.App.UI.Generators`, `geoDC.App.UI.Sample`) | Shell/MVVM primitives, `https://schemas.geodc/ui`, theme/layout resources, the source generator (what it generates, why CI builds it first `ci.yml:110-116`), branding assets and fonts (absorbs `branding-assets.md`), the Sample gallery and its coverage test | `AGENTS.md:52-54,107-112`, `docs/ui/README.md`, `branding-assets.md`, `ui/sample-gallery.md:1-50` |
| **5. Application** (`geoDC.App`) | Startup/DI, shell and menus, pages and viewmodels, workspace + save handlers (IDENTITY_CONVENTIONS), popups, strings (`Strings/*.yaml`, generated classes, localisation status), logging sinks, chart assets (`ChartAssets/`, npm scripts) | `AGENTS.md:55,102,113`, → `shell-menus.md`, `string-resources.md`, `copy-style.md`, `logging.md`, `data-analysis.md`, `ui/popups.md` |
| **6. Tools and CLI usage** | One subsection per tool with the exact command line: ReportCli (`AGENTS.md:71`), TestDataGenerator.Cli/.Wpf (`test-data-generator.md:277-336`), GeobaseImport (`geobase-import.md:9-13`), the UI probe (`dev/probe.ps1`, verbs, `.probe/`, fixtures via the generator's `probe` preset), chart preview (`charting.md:230-244`), GalleryCorpus (`sample-gallery.md:56`) | as cited |
| **7. Tests** | Table of the 12 test projects: what each covers, OS, notable convention tests (`tests/geoDC.Infrastructure.Tests/Conventions/*` enforce logging templates, string resources, XAML keys, comments), `geoDC.Testing` helpers | `geoDC.slnx:53-68`, `cloud-sessions.md:22` |
| **8. Conventions index** | 10-line pointer list to coding-style, code-vocabulary, copy-style, logging, github-practices | `README.md:44-50` |
| **9. Repository plumbing** | Root files explained one line each (`Directory.Build.*`, `Directory.Packages.props`, `dotnet-tools.json`, `version.json`, `nuget.config` + private `Snt.*` feed, `.editorconfig`, `Settings.XamlStyler`, `.gitattributes` LFS/binary rules, `assets/`, `installer/`, `dev/` scripts that remain) | Appendix A of this report |

### 5.2 Scope guard

The reference states, links and shows the command; it never re-explains a topic that has a page. Pages that "earn their place" and stay as linked detail: all of §4.1 marked keep. New-developer first month needs §0, §1, §6, §7 fully and §2-5 as maps.

### 5.3 Localisation note for the reference

`string-resources.md:1-5,100`: all app copy is YAML compiled by `Snt.Lib.Wpf.Text.Generators`; the app is English-only but the generator handles other languages' plural rules, so localisation remains a matter of adding YAML files per culture. The reference's §5 says this in two sentences and links the page; nothing in the split affects it.

### 5.4 README.md "Documentation" section after the split

```
## Documentation
- docs/codebase.md — the codebase, project by project, with build/test/CLI usage. Start here.
- docs/domain-context.md — what dimensional control is; docs/data-model.md — aggregates and invariants.
- Conventions: coding-style, code-vocabulary, copy-style, logging, github-practices.
- Topic guides: project packages, reporting, charting, coordinate transforms, PROJ, string resources,
  test data generator, GEOBASE import, installer, UI toolkit (docs/ui/), design records (docs/design/).
```
Remove `README.md:42-44` (AGENTS.md), `:24` and `:63` (cloud-sessions, skills) and `:58` (CI runners) unless runners stay; drop "`main` is for releases" wording at `:65` if the branch model changes (6.2). Also add `docs/codebase.md` to `geoDC.slnx:2-13` and fix the three phantom `<File>` entries (`:29-30,56`).

## 6. Open questions for the owner

| # | Question | Options | Lean |
|---|---|---|---|
| 6.1 | Keep the self-hosted runners? | (a) retire: hosted-only, change `select-runner.yml:41` default, delete `dev/runners.ps1`, fold ci-runners.md; (b) keep desktop runners but move the selector to `ubuntu-latest`; (c) keep all, document in geoDC for the new dev | (b) if hosted Windows minutes are a budget concern, else (a). The VPS exists only to survive exhausted hosted minutes. |
| 6.2 | Branch model once `main` is no longer generated | (a) `main` = release branch, fast-forward from `dev`, `dev/release.ps1` unchanged; (b) release from `dev` tags, delete `main`; (c) rebuild `main` once from `dev` and merge normally | (a): smallest change to `installer.yml:10-14`, `release.ps1`, `installer.md`. Needs a one-time reset of `main` to `dev` (its current history is synthetic publish commits). |
| 6.3 | Does `.husky/` stay in geoDC? | stay / move | stay (developer formatting hook; CI enforces the same rules) |
| 6.4 | `dev/probe-scripts` and `tools/geoDC.GalleryCorpus` | stay in geoDC / move and drop the dependent test + csproj | stay (both are referenced from the solution/tests; only the `.ps1` drivers move) |
| 6.5 | Submodule vs nested clone | A submodule / B gitignored nested clone / C keep one repo and simply delete the publish machinery | B (section 3.6); C is worth naming because it is the least work if the owner's real goal was only to stop publishing `main` |
| 6.6 | Cloud sessions after the split | keep, with the harness as the session repo and geoDC attached as a second repo / drop cloud sessions | keep but verify once; if attaching a second private repo is awkward, cloud sessions may need to stay on the geoDC repo with a minimal `CLAUDE.md` there |
| 6.7 | Where does `docs/ci-runners.md`'s human-ops content go if runners stay? | geoDC (CI is geoDC's) / outer | geoDC |
| 6.8 | Should `tools/geoDC.Probe` + tests stay in geoDC? | stay / move with the harness (would need an outer build) | stay (already decided in `docs/publishing.md:22`; also a UIA audit tool for humans) |
| 6.9 | One reference file or two? | `docs/codebase.md` alone / `codebase.md` + `tools.md` | one file; §6 is ~80 lines |
| 6.10 | Keep `design/` records in geoDC? | yes / outer | yes (dated human decisions; 10 docs link `design/page-layout.md`) |

## Appendix A — File classification

Columns: destination = stays / outer / delete / fold → doc. Evidence = who references it (grep across repo at e9a8766; counts exclude self-references and `.claude/skills` mirror).

### Root

| Path | What | Audience | Destination | Evidence |
|---|---|---|---|---|
| `.editorconfig` (226) | C#/XAML style rules; CI `GeoDCStrictCodeStyle` source | build | stays | `Directory.Build.props:14-21`, `ci.yml:23-25`, `coding-style.md:6` |
| `.git-blame-ignore-revs` | formatting commits to skip | human | stays | `README.md:17` |
| `.gitattributes` | CRLF default, LF for `.sh`/`.husky`, LFS for SVG, binary PROJ | build | stays (keep `.husky/*` line) | `ci.yml:165-171`, `proj-native.md` |
| `.gitignore` | build output, agent dirs, probe/captures/fixtures | both | stays; drop `:22-24,41-43,51,58`, keep `:37,40,44` | section 1.1 |
| `.publishignore` | strip list for generated `main` | agent/owner | delete | `dev/publish-main.ps1:93`, `publish-main.yml:3` |
| `AGENTS.md` (158) | agent working rules + human sections | both | outer (after extracting §4.3 human content) | README `:42`, 21 docs linked from it, 1 skill |
| `CHANGELOG.md` | Keep-a-Changelog, calver | human | stays | `installer.yml:271-294`, `release.ps1`, `publish-main.ps1:72-79` |
| `Directory.Build.props` | roots, strict style, test injection, Linux targeting | build | stays | `ci.yml:100,198`, `nuget-cache.yml:18`, AGENTS `:80` |
| `Directory.Build.rsp` | quiet MSBuild output | build | stays | slnx `:19` |
| `Directory.Build.targets` | Typst provisioning, PROJ runtime trimming | build | stays | `reporting.md`, `proj-native.md` |
| `Directory.Packages.props` | central package versions (incl. private `Snt.*`) | build | stays | `ci.yml:100`, `string-resources.md:3` |
| `GlobalUsings.Tests.cs`, `NUnitAssemblySettings.cs` | injected into every test project | build | stays | `Directory.Build.props:60-61` |
| `README.md` | entry page | human | stays; rewrite Documentation (§5.4) | 9 docs link back |
| `Settings.XamlStyler` | XAML formatting rules | build | stays | `dev/format-xaml.ps1:26`, `ci.yml:127` |
| `dotnet-tools.json` | husky, jb, nbgv, xstyler | build | stays | `ci.yml:131`, `installer.yml:75` |
| `geoDC.runsettings` | quiet NUnit adapter | build | stays | `Directory.Build.props:38` |
| `geoDC.slnx` | solution (12 src, 7 tools, 12 tests, docs folder) | build | stays; fix phantom files `:29-30,56` | 11 tests find root by it |
| `global.json` | SDK 10.0.302, latestFeature | build | stays | all workflows `setup-dotnet` |
| `nuget.config` | nuget.org + `nuget.sthornton.co.uk` for `Snt.*` | build | stays (new dev needs feed access — note for handoff) | `ci.yml:100` |
| `version.json` | NBGV calver 2026.0 | build | stays | `release.ps1`, `installer.yml:81-83` |
| `src/`, `tests/` | 12 projects / 12 test projects (+`StaTestRunner.cs`) | build | stays | slnx |

### `dev/`

| Path | What | Audience | Destination | Evidence |
|---|---|---|---|---|
| `build-installer.ps1` (250) | self-contained publish + WiX | build | stays | `installer.yml:109`, `installer.md:8-33`, wixproj `:17-19` |
| `build-proj.ps1` (341), `proj/` (vcpkg.json, 3 triplets, ports proj/zlib/sqlite3) | rebuild pinned PROJ | human | stays | `proj-native.md:228-307`, `.gitattributes:17` |
| `capture-ds-previews.ps1` (142) | Sample gallery screenshot corpus | agent/owner | outer | `sample-gallery.md:53`, `sync-design-system.ps1:7` |
| `capture-views.ps1` (766) | app view screenshot corpus | agent/owner | outer | `wpf-probe/SKILL.md:350`, `branch-wrap-up/SKILL.md:45`, `.gitignore:39` |
| `cloud-setup.sh` | cloud VM toolchain | agent | outer | `cloud-sessions*.md`, `cloud-session-start.sh:3` |
| `fetch-typst.ps1` (161) | pinned Typst download | build | stays | `Directory.Build.targets:21,30`, hooks `:74`, `reporting.md:7-50` |
| `format-xaml.ps1` | XAML Styler wrapper | build | stays | `ci.yml:132`, `.husky/task-runner.json:29,36` |
| `probe.ps1` | probe build-and-run shim | agent (+human) | stays (resolves project from its own location `:9-11`; 60 lines; harmless) — or outer if rewritten with `../geodc` | `.claude/settings.json:88-93,126`, `.codex/rules:5`, 13 probe scripts, skill |
| `probe-scripts/` (13 `.probe`) | checked-in probe sequences | agent + test data | **stays** (test dependency) | `DoInvocationTests.cs:298-308`, `TextRendererTests.cs:21,138` |
| `publish-main.ps1` | generate `main` | owner | delete | `publish-main.yml:38`, `publishing.md` |
| `release.ps1` (155) | stamp changelog, tag, push | human | stays | `installer.md:141-166`, `publishing.md:48` |
| `runners.ps1` (465) | self-hosted runner service management | ops | stays if runners stay, else outer/delete (6.1) | `ci-runners.md:82-165` |
| `seed-probe-fixture.ps1` | regenerate `dev/fixtures/*.gdcproj` via generator presets | agent | outer (or stays beside probe.ps1 — same call) | AGENTS `:119`, skills, `ProbeRegister.cs:8`, `test-data-generator.md:288` |
| `sync-agent-skills.ps1` | `.agents/skills` → `.claude/skills` mirror check | agent | outer (+ delete `ci.yml:46-60`) | `ci.yml:60`, AGENTS `:27`, settings `:90-91` |
| `sync-design-system.ps1` (431) | corpus → design-system artifact | owner | outer | `sample-gallery.md:64-78` |
| `fixtures/`, `captures/` (gitignored, absent) | regenerable outputs | agent | n/a; keep ignore rules | `.gitignore:37,40` |

### `docs/` — see §4.1 for all 39 pages (destinations: 33 keep, 1 fold, 3 outer, 1 delete, 1 split).

### `.github/`

| Path | What | Audience | Destination | Evidence |
|---|---|---|---|---|
| `pull_request_template.md` | PR body skeleton | both | stays | AGENTS `:139`, `github-practices.md:124`, branch-wrap-up skill |
| `workflows/ci.yml` | Format + Build/test (+ agent-skills) | build | stays; delete `:42-44,46-60` | §2.1 |
| `workflows/select-runner.yml` | runner selector / skip-paths | build | stays; change `:41` default or drop (6.1) | `ci.yml:31`, `installer.yml:26` |
| `workflows/nuget-cache.yml` | seed hosted caches | build | stays (optional) | `ci-runners.md:67-70` |
| `workflows/installer.yml` | MSI, tag verify, GitHub Release | build | stays | `installer.md:35-55` |
| `workflows/publish-main.yml` | dispatch publish | owner | delete | `publishing.md:26` |

### `.agents/` (31 files), `.claude/` (26), `.codex/` (1)

| Path | What | Audience | Destination | Evidence |
|---|---|---|---|---|
| `.agents/skills/<12 skills>/SKILL.md` + `agents/openai.yaml` (12) + `references/*.md` (6: mvvm-page, persistence, popup, project-session, theme-layout, wpf-ui) | canonical skills; 975 lines total, wpf-probe 358 | agent | outer; prefix paths (§3.2) | AGENTS `:26`, README `:63`, `sync-agent-skills.ps1:10` |
| `.claude/settings.json` | permissions, env, 3 hooks | agent | outer; rewrite paths `:84-94,126,131-155` | `cloud-sessions-claude.md:14-20` |
| `.claude/hooks/cloud-session-start.sh`, `cloud-stop-guard.sh`, `git-push-guard.sh` | cloud bootstrap, stop guard, push guard | agent | outer; add `cd geodc` / dual-repo checks | settings `:131-155` |
| `.claude/skills/**` (22) | generated mirror of `.agents/skills` minus `openai.yaml` | agent | outer (regenerate there) | `sync-agent-skills.ps1:11,20` |
| `.codex/rules/default.rules` | Codex sandbox allow-list | agent | outer; prefix `:5` | `.gitignore:41-43` |

### `.husky/` (8) — stays (6.3)

`pre-commit` (husky run), `pre-push`/`post-checkout`/`post-commit`/`post-merge` (git-lfs hooks; `post-checkout:6-9` self-bootstrap), `task-runner.json` (format staged C#/XAML, restage renames), `_/husky.sh`, `_/.gitignore`. Evidence: `README.md:15-16`, `coding-style.md:6-9`, `dotnet-tools.json:5`.

### `installer/`, `tools/`, `assets/`

| Path | What | Audience | Destination | Evidence |
|---|---|---|---|---|
| `installer/geoDC.Installer/geoDC.Installer.wixproj`, `Package.wxs` | WiX v6 MSI, outside the solution | build | stays | `build-installer.ps1`, `installer.md:200-205` |
| `tools/geoDC.GalleryCorpus` | offscreen specimen renderer (1 file) | agent/owner | stays (6.4) | slnx `:45`, `capture-ds-previews.ps1:44` |
| `tools/geoDC.GeobaseImport` | migration CLI | human | stays | `geobase-import.md`, `tests/geoDC.GeobaseImport.Tests` |
| `tools/geoDC.Probe` | UIA probe exe | agent (+human) | stays | slnx `:47`, 26 test refs, `publishing.md:22` |
| `tools/geoDC.ReportCli` | headless report CLI | human | stays | AGENTS `:71-74`, `reporting.md:210` |
| `tools/geoDC.TestDataGenerator.{Cli,Core,Wpf}` | synthetic projects | human | stays | `test-data-generator.md`, `seed-probe-fixture.ps1:45` |
| `tools/preview` | Node chart preview harness + tests | human | stays | `src/geoDC.App/ChartAssets/package.json:6,9`, `charting.md:226-244` |
| `tools/typst/` (gitignored) | provisioned Typst | build | n/a | `.gitignore:31` |
| `assets/*.svg` (8, LFS) | GEOdc/GEOsolutions logos | build | stays | `geoDC.App.UI.Branding.csproj:17`, `geoDC.Infrastructure.csproj:29-40` |
| `assets/fonts/` (15 TTF: Lato, IBM Plex Mono) | UI, chart and report fonts | build | stays | `geoDC.App.UI.csproj:34-35`, `geoDC.App.csproj:38-39`, `geoDC.Infrastructure.csproj:32` |
