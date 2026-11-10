# Codex cloud sessions

Codex cloud tasks run in the VM described in [cloud-sessions.md](cloud-sessions.md). This page covers the
Codex-specific setup.

> **Unverified.** The configuration below mirrors what Claude Code's session-start hook does, but it has not yet been
> exercised in a real Codex task. Issue #380 tracks confirming it; update this page with what you find.

## Environment

- **Network:** allow the usual package managers plus `builds.dotnet.microsoft.com`, which serves the .NET SDK (see
  [cloud-sessions.md](cloud-sessions.md) > Toolchain). The Typst build step also downloads a `typst/typst` GitHub
  release; without that, builds need `GeoDCSkipTypstFetch=true`.
- **Setup script:** run [`dev/cloud-setup.sh`](../dev/cloud-setup.sh) to install the .NET SDK, PowerShell and
  `git-lfs`, then the repository steps below. Claude Code's `.claude/hooks/cloud-session-start.sh` does not run under
  Codex, so the setup script has to do them.

```bash
bash dev/cloud-setup.sh

# geoDC is the nested clone at geodc/: use the attached repository beside the harness, else clone it on dev.
if [ ! -e geodc ]; then
  if [ -d ../geodc/.git ]; then ln -s ../geodc geodc; else git clone --branch dev https://github.com/snt0/geodc geodc; fi
fi
cd geodc

# Nerdbank.GitVersioning needs full history; fall back to building without git height.
if [ "$(git rev-parse --is-shallow-repository)" = "true" ] && ! git fetch -q --unshallow origin; then
  export NBGV_GitEngine=Disabled
  echo 'export NBGV_GitEngine=Disabled' >> ~/.bashrc
fi

git lfs install --local && git lfs pull || true
# Without LFS credentials, the lock check in Husky's `git lfs pre-push` hook fails every push; skip it for origin.
remote="$(git remote get-url origin)"
git config "lfs.${remote%.git}.git/info/lfs.locksverify" false
dotnet tool restore && dotnet husky install

# Provision Typst for report tests, or skip it when the release download is blocked.
if ! dotnet msbuild dev/typst.proj -p:GeoDCTypstRid=linux-x64; then
  export GeoDCSkipTypstFetch=true
  echo 'export GeoDCSkipTypstFetch=true' >> ~/.bashrc
fi

dotnet restore geoDC.slnx
```

The fallbacks are exported for the rest of the setup script and appended to `~/.bashrc`, which Codex documents as the
way to carry variables from the setup script to the agent. If the agent's shell turns out not to read it, set
`NBGV_GitEngine=Disabled` or `GeoDCSkipTypstFetch=true` in the environment's variables instead.

## Working rules

Branch, push and verification rules are in `AGENTS.md` > Cloud sessions and > Branches and pull requests. Codex has
no push guard hook, so they hold by convention only.
