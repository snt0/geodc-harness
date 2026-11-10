#!/usr/bin/env bash
# Stop hook: in a Claude Code cloud session (CLAUDE_CODE_REMOTE=true), asks Claude once to commit
# and push before it stops, because work left on the VM is lost when the session expires.
# A no-op locally and on the second stop attempt (stop_hook_active).
[[ "$CLAUDE_CODE_REMOTE" == "true" ]] || exit 0
input="$(cat)"
[[ "$(jq -r '.stop_hook_active // false' <<<"$input")" == "true" ]] && exit 0
cd "$CLAUDE_PROJECT_DIR" 2>/dev/null || exit 0

# The harness and its geoDC clone (geodc/) are separate repositories, so each is checked on its own.
pending=()
for repo in . geodc; do
  git -C "$repo" rev-parse --git-dir >/dev/null 2>&1 || continue
  changed="$(git -C "$repo" status --porcelain | wc -l | tr -d ' ')"
  unpushed="$(git -C "$repo" rev-list --count HEAD --not --remotes 2>/dev/null || echo 0)"
  name="$([[ "$repo" == . ]] && echo 'the harness' || echo 'geodc/')"
  [[ "$changed" == 0 && "$unpushed" == 0 ]] || pending+=("$name: $changed uncommitted path(s), $unpushed commit(s) on no remote")
done
[[ ${#pending[@]} == 0 ]] && exit 0

jq -n --arg r "This cloud session has unsaved work ($(IFS=';'; echo "${pending[*]}")), and the VM is discarded when the session expires. Unless you are stopping to wait for the user's answer, commit the work in each repository and push it to a feature branch (never dev or main), then stop. If leaving it uncommitted is deliberate, say why and stop." \
  '{decision:"block",reason:$r}'
