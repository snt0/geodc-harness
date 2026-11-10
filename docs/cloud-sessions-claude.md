# Claude Code cloud sessions

Cloud sessions (claude.ai/code, the Claude app, `claude --cloud`) run Claude Code in the VM described in [cloud-sessions.md](cloud-sessions.md). This page covers the Claude-specific setup and repository configuration.

## One-time environment setup

Configure the cloud environment at claude.ai/code > environment selector > edit:

1. **Network access**: choose **Custom**, tick **Also include default list of common package managers**, and add `builds.dotnet.microsoft.com`.
2. **Setup script**: paste the contents of [`dev/cloud-setup.sh`](../dev/cloud-setup.sh). The result is cached, so later sessions start with the toolchain on disk; editing the script or the allowed domains rebuilds the cache.
3. **Repositories**: start sessions on `snt0/geodc-harness` and attach `snt0/geodc` as a second repository, so the session's git access covers both. The harness must be the session's own repository: its `.claude/` settings, hooks and skills only load from there. The two are cloned side by side; the session-start hook links `geodc/` to the geoDC clone.

## What the repository configures

`.claude/settings.json` registers these hooks; each script is a no-op outside the cloud (`CLAUDE_CODE_REMOTE` is `true` only there):

| Hook | Script | Cloud behaviour |
| --- | --- | --- |
| `SessionStart` | `.claude/hooks/cloud-session-start.sh` | Makes `geodc/` exist: links it to the geoDC repository attached beside the harness, or clones `snt0/geodc` on `dev` when none is attached (which needs the session's git access to cover it), and reports when neither works. Then, inside `geodc/`, it deepens a shallow clone so Nerdbank.GitVersioning can compute the build version (falling back to `NBGV_GitEngine=Disabled` if the fetch fails), pulls LFS files, turns off LFS lock verification for `origin` so the Husky pre-push hook does not demand LFS credentials the cloud session lacks, restores local dotnet tools, installs the Husky git hooks, restores NuGet packages, sets `GeoDCSkipTypstFetch=true` if the Typst fetch fails, and reports what is available to Claude. |
| `PreToolUse` | `.claude/hooks/git-push-guard.sh` | Denies force pushes, remote deletions, `--all`/`--mirror`/`--prune`, and pushes to branches other than the checked-out one, everywhere. In the cloud it also denies pushing `dev` or `main`, so cloud work lands through a pull request. |
| `Stop` | `.claude/hooks/cloud-stop-guard.sh` | Asks Claude once to commit and push before stopping when the harness or `geodc/` holds uncommitted or unpushed work; each is checked on its own. |

The auto-mode classifier does not read `autoMode` rules from repository settings, but it does read the agent instructions, so the shared cloud working rules live in `AGENTS.md` > Cloud sessions.
