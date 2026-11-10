#!/usr/bin/env bash
# Setup script for the geoDC Claude Code cloud environment. Paste this file into the environment's
# "Setup script" field at claude.ai/code (see docs/cloud-sessions.md). It runs as root on
# Ubuntu 24.04 before Claude Code starts, and the resulting filesystem is cached for later
# sessions, so keep it under about five minutes.
#
# Network: the default Trusted list plus builds.dotnet.microsoft.com, which serves the .NET SDK.
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

install_packages &
packages_pid=$!
install_dotnet &
dotnet_pid=$!
wait "$packages_pid"
wait "$dotnet_pid"

dotnet --version
pwsh -NoProfile -Command '$PSVersionTable.PSVersion.ToString()'
git lfs version
