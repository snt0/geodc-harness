#!/usr/bin/env bash
# PreToolUse guard for git push.
# - Denies pushes that could overwrite or delete anything other than the current branch:
#   force flags, +refspecs, deletes, --all/--mirror/--prune, and refspecs whose destination
#   is not the checked-out branch (tags are allowed; git rejects non-forced tag overwrites).
# - In cloud sessions (CLAUDE_CODE_REMOTE=true) also denies pushing dev or main, so unattended
#   work lands through a pull request.
# - Allows a standalone `git push ...` that passes those checks, so it skips the auto-mode
#   classifier. Compound commands are left to the classifier after the deny checks.
input="$(cat)"
cmd="$(jq -r '.tool_input.command // empty' <<<"$input")"
cwd="$(jq -r '.cwd // empty' <<<"$input")"
case "$cmd" in *push*) ;; *) exit 0 ;; esac

segments="$(printf '%s' "$cmd" | grep -oE 'git( -C [^ ]+)? push[^;&|]*')"
[[ -z "$segments" ]] && exit 0

decide() {
  jq -n --arg d "$1" --arg r "$2" \
    '{hookSpecificOutput:{hookEventName:"PreToolUse",permissionDecision:$d,permissionDecisionReason:$r}}'
  exit 0
}
deny() { decide deny "git-push-guard: $1. Only non-forced pushes of the current branch are permitted; ask the user to run this one."; }

while IFS= read -r seg; do
  repo="${cwd:-.}"
  if [[ "$seg" =~ ^git\ -C\ ([^ ]+) ]]; then
    c="${BASH_REMATCH[1]//[\"\']/}"
    [[ "$c" = /* || "$c" =~ ^[A-Za-z]: ]] && repo="$c" || repo="$repo/$c"
  fi
  branch="$(git -C "$repo" symbolic-ref --quiet --short HEAD 2>/dev/null)"

  read -ra toks <<<"${seg#*push}"
  pos=()
  for t in "${toks[@]}"; do
    t="${t//[\"\']/}"
    [[ "$t" == *[\<\>]* ]] && break # redirections such as 2>&1 end git's arguments
    case "$t" in
      --force* | --mirror | --all | --branches | --delete | --prune) deny "'$t' is not allowed" ;;
      --*) ;;
      -*) [[ "${t:1}" == *[fd]* ]] && deny "'$t' forces or deletes" ;;
      *) pos+=("$t") ;;
    esac
  done

  # With no refspec, git pushes the current branch.
  pushes_branch=$([[ ${#pos[@]} -le 1 ]] && echo 1)
  for ref in "${pos[@]:1}"; do
    [[ "$ref" == +* ]] && deny "'$ref' is a forced refspec"
    if [[ "$ref" == *:* ]]; then
      [[ -z "${ref%%:*}" ]] && deny "'$ref' deletes a remote ref"
      dst="${ref#*:}"
    else
      dst="$ref"
      [[ "$dst" == HEAD ]] && dst="$branch"
    fi
    [[ "$dst" == refs/tags/* ]] && continue
    git -C "$repo" show-ref --verify --quiet "refs/tags/$dst" 2>/dev/null && continue
    dst="${dst#refs/heads/}"
    [[ -n "$branch" && "$dst" == "$branch" ]] || deny "'$ref' targets '$dst', not the current branch '${branch:-<detached>}'"
    pushes_branch=1
  done

  if [[ "$CLAUDE_CODE_REMOTE" == "true" && -n "$pushes_branch" && "$branch" =~ ^(dev|main)$ ]]; then
    decide deny "git-push-guard: cloud sessions don't push '$branch' directly. Move the commits to a feature branch (git switch -c <name>), push that, and open a pull request into dev."
  fi
done <<<"$segments"

if printf '%s' "$cmd" | grep -qE '^git( -C [^ ]+)? push[^;&|`$<>()]*$'; then
  decide allow "git-push-guard: push of the current branch only"
fi
exit 0
