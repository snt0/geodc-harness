#!/usr/bin/env bash
# SessionStart hook: prepares a Claude Code cloud session (CLAUDE_CODE_REMOTE=true) and is a
# no-op locally. The environment's setup script (dev/cloud-setup.sh) installs the machine-level
# toolchain; this makes sure the harness has its geoDC clone at geodc/ and restores what belongs to
# that clone. See docs/cloud-sessions.md.
[[ "$CLAUDE_CODE_REMOTE" == "true" ]] || exit 0
cd "$CLAUDE_PROJECT_DIR" || exit 0

notes=()
report() {
  local text
  text="$(printf -- '- %s\n' "${notes[@]}")"
  jq -n --arg c "geoDC cloud session setup:"$'\n'"$text" \
    '{hookSpecificOutput:{hookEventName:"SessionStart",additionalContext:$c}}'
  exit 0
}

if [[ -n "$CLAUDE_ENV_FILE" ]]; then
  {
    echo 'export DOTNET_NOLOGO=1'
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    [[ -d /opt/dotnet ]] && echo 'export DOTNET_ROOT=/opt/dotnet'
    echo 'export PATH="$PATH:$HOME/.dotnet/tools"'
  } >>"$CLAUDE_ENV_FILE"
fi

# geoDC is attached to the session as a second repository, cloned beside the harness; link it in as geodc/.
# Without it, clone geoDC on dev, which works only when the session's git proxy is authorised for it.
if [[ ! -e geodc ]]; then
  if git -C ../geodc rev-parse --git-dir >/dev/null 2>&1; then
    ln -s ../geodc geodc
    notes+=("geodc/ links to the geoDC repository attached to this session ($(cd geodc && pwd -P)).")
  elif git clone -q --branch dev https://github.com/snt0/geodc geodc >/tmp/geodc-clone.log 2>&1; then
    notes+=("geodc/ was cloned from snt0/geodc on dev.")
  else
    notes+=("geoDC is not attached to this session and could not be cloned (see /tmp/geodc-clone.log), so there is no geodc/ to work on. Attach snt0/geodc to the session; docs/cloud-sessions.md says how.")
    report
  fi
fi
cd geodc || report

if ! command -v dotnet >/dev/null 2>&1; then
  notes+=("The .NET SDK is not installed, so build, test and commit hooks are unavailable. The cloud environment's setup script has not run or failed; docs/cloud-sessions.md has the fix. Tell the user rather than installing a toolchain mid-session.")
  report
fi
notes+=(".NET SDK $(dotnet --version 2>&1 | head -1).")

# Nerdbank.GitVersioning computes the version height from history, so a shallow clone fails every build
# ("Shallow clone lacks the objects required to calculate version height"). Deepen the clone; if that
# fails, turn version calculation off for the session so builds still run (version height reads as 0).
if [[ "$(git rev-parse --is-shallow-repository 2>/dev/null)" == "true" ]]; then
  if git fetch -q --unshallow origin >/tmp/geodc-unshallow.log 2>&1; then
    notes+=("The clone was shallow; fetched full history so Nerdbank.GitVersioning can compute the build version.")
  else
    [[ -n "$CLAUDE_ENV_FILE" ]] && echo 'export NBGV_GitEngine=Disabled' >>"$CLAUDE_ENV_FILE"
    export NBGV_GitEngine=Disabled
    notes+=("The clone is shallow and git fetch --unshallow failed (see /tmp/geodc-unshallow.log); builds set NBGV_GitEngine=Disabled, so assembly versions omit the git height in this session.")
  fi
fi

if command -v git-lfs >/dev/null 2>&1; then
  git lfs install --local >/dev/null 2>&1
  if git lfs pull >/dev/null 2>&1; then
    notes+=("Git LFS files pulled.")
  else
    notes+=("git lfs pull failed; LFS-tracked *.svg files may be pointer stubs.")
  fi
  # The cloud session has no LFS credentials. Husky's pre-push hook runs `git lfs pre-push`, whose lock
  # verification asks GitHub for credentials and fails the push even when no LFS object needs uploading;
  # turning it off for this remote lets pushes of branches without LFS changes go through.
  lfs_remote="$(git remote get-url origin 2>/dev/null)"
  if [[ -n "$lfs_remote" ]]; then
    git config "lfs.${lfs_remote%.git}.git/info/lfs.locksverify" false
  fi
  # Husky's hooks include an LFS pre-push hook, so only install them when git-lfs exists.
  if dotnet tool restore >/dev/null 2>&1 && dotnet husky install >/dev/null 2>&1; then
    notes+=("Local dotnet tools restored; Husky git hooks installed (pre-commit formats staged C#).")
  else
    notes+=("dotnet tool restore or husky install failed; commits will skip the formatting hook.")
  fi
else
  notes+=("git-lfs is missing; Husky hooks were not installed and LFS files are pointer stubs.")
fi

# geoDC.App bundles Typst through dev/typst.proj, which downloads a typst/typst GitHub release.
# The cloud GitHub proxy can refuse release assets from repositories not attached to the session,
# so when the fetch fails, skip it for the rest of the session instead of failing every build.
if [[ ! -f tools/typst/linux-x64/typst.pin ]]; then
  if dotnet msbuild dev/typst.proj -nologo -p:GeoDCTypstRid=linux-x64 >/tmp/geodc-typst.log 2>&1; then
    notes+=("Typst provisioned for linux-x64.")
  else
    [[ -n "$CLAUDE_ENV_FILE" ]] && echo 'export GeoDCSkipTypstFetch=true' >>"$CLAUDE_ENV_FILE"
    export GeoDCSkipTypstFetch=true
    notes+=("Typst could not be fetched (see /tmp/geodc-typst.log); builds skip it via GeoDCSkipTypstFetch=true, so the built app cannot render reports in this session.")
  fi
fi

if dotnet restore geoDC.slnx -v q >/tmp/geodc-restore.log 2>&1; then
  notes+=("NuGet restore of geoDC.slnx succeeded.")
else
  notes+=("NuGet restore of geoDC.slnx failed; see /tmp/geodc-restore.log.")
fi

if command -v pwsh >/dev/null 2>&1; then
  notes+=("pwsh is available for dev/*.ps1 scripts (dev/ui.ps1 still needs Windows).")
else
  notes+=("pwsh is not installed; dev/*.ps1 scripts cannot run.")
fi
notes+=("Linux host: the solution builds; Domain, Application, Infrastructure, App.Core, GeobaseImport and TestDataGenerator tests run; geoDC.App.Tests, geoDC.App.UI.Tests and the UI driver are Windows-only. Work on geoDC happens in geodc/, a repository of its own. See AGENTS.md > Cloud sessions.")
report
