#!/usr/bin/env bash
# Prepares this harness to work on geoDC: clones snt0/geodc into geodc/ on dev (or fast-forwards an existing
# clone that is on dev and clean), then, inside it, pulls Git LFS files, restores the local dotnet tools and
# installs the Husky hooks. Run it from the harness root on a new machine, and again to update geodc/.
# In a cloud session with geoDC attached as a second repository, .claude/hooks/cloud-session-start.sh links
# geodc/ to that clone instead.
set -euo pipefail
cd "$(dirname "$0")"
url="${GEODC_REPOSITORY_URL:-https://github.com/snt0/geodc}"

if [[ ! -e geodc ]]; then
  git clone --branch dev "$url" geodc
elif [[ "$(git -C geodc symbolic-ref --quiet --short HEAD || true)" == "dev" && -z "$(git -C geodc status --porcelain)" ]]; then
  git -C geodc pull --ff-only origin dev
else
  echo "geodc/ is not a clean checkout of dev; leaving it as it is." >&2
fi

cd geodc
if command -v git-lfs >/dev/null 2>&1; then
  git lfs install --local --skip-repo
  git lfs pull
else
  echo "git-lfs is missing: install it, then run this again. Without it the logos are blank and reports fail." >&2
  exit 1
fi
dotnet tool restore
dotnet husky install
echo "geodc/ is ready: dotnet build geodc/geoDC.slnx"
