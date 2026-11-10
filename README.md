# geodc-harness

The agent harness for [geoDC](https://github.com/snt0/geodc): the instructions, skills, settings, hooks and scripts
that Claude Code and Codex use to work on geoDC. geoDC itself is a separate repository, cloned into `geodc/` and
gitignored here, so the product repository holds only the product.

## Getting started

Prerequisites are geoDC's: the .NET SDK its `global.json` pins, Git LFS and PowerShell 7
(`geodc/docs/codebase.md` > Build, Test and Run, once cloned).

```pwsh
git clone https://github.com/snt0/geodc-harness
cd geodc-harness
pwsh -NoProfile -File bootstrap.ps1     # or: bash bootstrap.sh
dotnet build geodc/geoDC.slnx
```

The bootstrap clones `snt0/geodc` into `geodc/` on `dev`, or fast-forwards it when it is already there on a clean
`dev`, then pulls its Git LFS files, restores its local dotnet tools and installs its Husky hooks. Run it again to
update `geodc/`. Start agents from the harness root, so they read `AGENTS.md` (`CLAUDE.md` imports it) and `.claude/`.

`geodc/` is an ordinary clone with its own branches and remote: commit and push geoDC work there
(`git -C geodc ...`), and harness changes here.

## Cloud sessions

Start a cloud session on this repository with `snt0/geodc` attached as a second repository. The environment's setup
script (`dev/cloud-setup.sh`) links `geodc/` to that clone and makes the harness's settings and hooks load, since such
a session starts in the folder above both clones; [`docs/cloud-sessions.md`](docs/cloud-sessions.md) covers the environment.

## Layout

| Path | Contents |
| --- | --- |
| `AGENTS.md`, `CLAUDE.md` | The working rules for agents; the shared rules point into `geodc/docs` |
| `.agents/skills` | The canonical skills; `.claude/skills` is a generated mirror (`dev/sync-agent-skills.ps1 -Write`) |
| `.claude/` | Claude Code settings and hooks: cloud session setup, the push guard, the stop guard |
| `.codex/rules` | Codex command rules |
| `bootstrap.ps1`, `bootstrap.sh` | Clone or update `geodc/` and prepare it |
| `dev/` | `cloud-setup.sh` (cloud VM toolchain), `capture-views.ps1` (app screenshot corpus), `capture-ds-previews.ps1` and `sync-design-system.ps1` ([design-system sync](docs/design-system-sync.md)), `sync-agent-skills.ps1` |
| `docs/` | Cloud sessions for Claude Code and Codex, design-system sync, and `handoff/`: the investigation reports behind the handoff restructure |
| `geodc/` | geoDC, gitignored |