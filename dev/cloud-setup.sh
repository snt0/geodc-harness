#!/usr/bin/env bash
# Setup script for the geoDC Claude Code cloud environment. Paste this file into the environment's
# "Setup script" field at claude.ai/code (see docs/cloud-sessions.md). It runs as root on
# Ubuntu 24.04 before Claude Code starts, and the resulting filesystem is cached for later
# sessions, so keep it under about five minutes.
#
# Network: the default Trusted list plus builds.dotnet.microsoft.com, which serves the .NET SDK.
#
# It starts in the folder that holds the session's clones. With snt0/geodc attached beside the harness,
# Claude Code also starts there (link_harness below explains why that matters).
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive

install_packages() {
  # Microsoft's feed provides PowerShell; git-lfs fetches the repository's LFS-tracked SVGs.
  curl -fsSL -o /tmp/packages-microsoft-prod.deb \
    https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb
  dpkg -i /tmp/packages-microsoft-prod.deb
  apt-get update -q
  apt-get install -y -q powershell git-lfs libicu74
}

install_dotnet() {
  # Ubuntu's dotnet-sdk-10.0 is a 10.0.1xx SDK, below the global.json pin, so install Microsoft's
  # build. The newest 10.0 SDK satisfies the pin (rollForward: latestFeature) without this script
  # needing the clone, which may not exist yet when it runs.
  curl -fsSL -o /tmp/dotnet-install.sh \
    https://raw.githubusercontent.com/dotnet/install-scripts/main/src/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /opt/dotnet --no-path
  ln -sf /opt/dotnet/dotnet /usr/local/bin/dotnet
  grep -q '^DOTNET_ROOT=' /etc/environment || echo 'DOTNET_ROOT=/opt/dotnet' >>/etc/environment
}

link_harness() {
  # Only when both clones are here: a session on the harness alone starts inside it, and its settings load.
  [[ -d geodc-harness/.claude && -d geodc/.git ]] || return 0
  # The harness works on geoDC through geodc/.
  [[ -e geodc-harness/geodc ]] || ln -s ../geodc geodc-harness/geodc
  # Claude Code reads project settings from its starting folder, which is this one, not the harness. Link
  # the harness's settings and hooks here so its permissions and its session-start, push and stop guard
  # hooks load; the hooks find the harness from their own path.
  mkdir -p .claude
  ln -sfn ../geodc-harness/.claude/settings.json .claude/settings.json
  ln -sfn ../geodc-harness/.claude/hooks .claude/hooks
  # Nerdbank.GitVersioning fails every build in a shallow clone. The session-start hook retries this and
  # falls back to NBGV_GitEngine=Disabled, so a failure here must not stop the session from starting.
  if [[ "$(git -C geodc rev-parse --is-shallow-repository)" == true ]]; then
    git -C geodc fetch -q --unshallow origin || echo 'Could not deepen geodc/; the session-start hook retries.' >&2
  fi
}

install_packages &
packages_pid=$!
install_dotnet &
dotnet_pid=$!
wait "$packages_pid"
wait "$dotnet_pid"

dotnet --version
pwsh -NoProfile -Command '$PSVersionTable.PSVersion.ToString()'
git lfs version
link_harness
