# Claude Code cloud sessions

Cloud sessions (claude.ai/code, the Claude app, `claude --cloud`) run Claude Code in the VM described in [cloud-sessions.md](cloud-sessions.md). This page covers the Claude-specific setup and repository configuration.

## One-time environment setup

Configure the cloud environment at claude.ai/code > environment selector > edit:

1. **Network access**: choose **Custom**, tick **Also include default list of common package managers**, and add `builds.dotnet.microsoft.com`.
2. **Setup script**: paste the contents of [`dev/cloud-setup.sh`](../dev/cloud-setup.sh). The result is cached, so later sessions start with the toolchain on disk; editing the script or the allowed domains rebuilds the cache.
3. **Repositories**: start sessions on `snt0/geodc-harness` and attach `snt0/geodc` as a second repository, so the session's git access covers both. The two are cloned side by side (`/home/user/geodc-harness` and `/home/user/geodc`).

### Where a two-repository session starts

With two repositories attached, Claude Code starts in the folder that holds both clones (`/home/user`), not in the harness. It reads `CLAUDE.md` and skills from the harness all the same, but project settings come only from the starting folder's `.claude/`, so without help the harness's permissions and its hooks (session start, push guard, stop guard) do not load, and nothing warns about it.

The setup script handles this in `link_harness`: when both clones are present it links `geodc-harness/geodc` to `../geodc`, deepens the geoDC clone, and links `/home/user/.claude/settings.json` and `/home/user/.claude/hooks` to the harness's own, so the same settings and hooks load from the parent. The hooks find the harness from their own real path rather than `CLAUDE_PROJECT_DIR`, which there is the parent. Paths in the settings that start `geodc/` resolve from either folder, because `/home/user/geodc` is the clone itself. The UI driver's entries (`dev/ui.ps1`, `tools/geoDC.UiDriver/bin/`) resolve only from the harness root, which is where a Windows session that can run the driver starts. A session on the harness alone starts inside it, and `link_harness` does nothing.

If the session-start report (the `geoDC cloud session setup:` context) is missing, the hooks did not load: check that the environment's setup script matches `dev/cloud-setup.sh` and that `/home/user/.claude/settings.json` exists.

## What the repository configures

`.claude/settings.json` registers these hooks, which a two-repository session loads through the links described above; each script is a no-op outside the cloud (`CLAUDE_CODE_REMOTE` is `true` only there):

| Hook | Script | Cloud behaviour |
| --- | --- | --- |
| `SessionStart` | `.claude/hooks/cloud-session-start.sh` | Makes `geodc/` exist: links it to the geoDC repository attached beside the harness, or clones `snt0/geodc` on `dev` when none is attached (which needs the session's git access to cover it), and reports when neither works. Then, inside `geodc/`, it deepens a shallow clone so Nerdbank.GitVersioning can compute the build version (falling back to `NBGV_GitEngine=Disabled` if the fetch fails), pulls LFS files, turns off LFS lock verification for `origin` so the Husky pre-push hook does not demand LFS credentials the cloud session lacks, restores local dotnet tools, installs the Husky git hooks, restores NuGet packages, sets `GeoDCSkipTypstFetch=true` if the Typst fetch fails, and reports what is available to Claude. |
| `PreToolUse` | `.claude/hooks/git-push-guard.sh` | Denies force pushes, remote deletions, `--all`/`--mirror`/`--prune`, and pushes to branches other than the checked-out one, everywhere. In the cloud it also denies pushing `dev` or `main`, so cloud work lands through a pull request. |
| `Stop` | `.claude/hooks/cloud-stop-guard.sh` | Asks Claude once to commit and push before stopping when the harness or `geodc/` holds uncommitted or unpushed work; each is checked on its own. |

The auto-mode classifier does not read `autoMode` rules from repository settings, but it does read the agent instructions, so the shared cloud working rules live in `AGENTS.md` > Cloud sessions.
