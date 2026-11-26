#!/usr/bin/env bash
# Release preflight from the Mac (docs/plans/ingame-e2e.md › Coverage, build step B1).
# Read-only: never pushes, tags or triggers anything. Covers the checklist items that need no game:
# section 0, R0 and R6 of docs/pre-release-ingame-checklist.md.
#
# Usage: tools/release/preflight.sh [--no-verify] [--wrapper <echokrautts-checkout>]
#   --no-verify  skip both `node .claude/verify.js` runs (they take minutes)
# Exit: 0 = no FAIL (WARNs are things that are expected before a push/tag, read them), 1 = at least one FAIL.
set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WRAPPER="${EKREL_WRAPPER:-$REPO/../echokrautts}"
VERIFY=1
while [ $# -gt 0 ]; do
  case "$1" in
    --no-verify) VERIFY=0 ;;
    --wrapper) WRAPPER="$2"; shift ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
  shift
done
WRAPPER="$(cd "$WRAPPER" && pwd)" || { echo "wrapper checkout not found" >&2; exit 2; }

GH_PLUGIN="RenNagasaki/Echokraut"
GH_WRAPPER="RenNagasaki/Echokrautts"
REMOTE_URLS="Echokraut/Resources/RemoteUrls.json"
FAILS=0; WARNS=0
pass() { echo "PASS  $*"; }
warn() { echo "WARN  $*"; WARNS=$((WARNS + 1)); }
fail() { echo "FAIL  $*"; FAILS=$((FAILS + 1)); }
info() { echo "      $*"; }
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT

# Highest X.Y.Z.W tag of a GitHub repo (ELI-* and marker tags excluded), optional prefix (e.g. ELI-).
latest_tag() {
  git ls-remote --tags "https://github.com/$1.git" 2>/dev/null | awk -F/ '{print $NF}' | grep -v '\^{}$' \
    | grep -E "^${2:-}[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$" | sed "s/^${2:-}//" | sort -V | tail -1
}
# version_gt a b: a > b
version_gt() { [ "$1" != "$2" ] && [ "$(printf '%s\n%s\n' "$1" "$2" | sort -V | tail -1)" = "$1" ]; }
http_code() { curl -s -o /dev/null -L -I --max-time 20 -w '%{http_code}' "$1"; }
# Known open defects (tracked in the WORKLOG): reported as WARN so they do not hide new FAILs.
KNOWN_BAD=("alltalk_tts-alltalkbeta.zip")

echo "== Versions"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$REPO/Echokraut/Echokraut.csproj" | head -1)"
TAG="$(latest_tag "$GH_PLUGIN")"; ELI="$(latest_tag "$GH_PLUGIN" ELI-)"; WTAG="$(latest_tag "$GH_WRAPPER")"
info "plugin <Version> $VERSION · GitHub plugin tag $TAG · installer tag ELI-$ELI · wrapper tag $WTAG"
if [ -z "$TAG" ]; then warn "GitHub tags not readable (network?)"
elif version_gt "$VERSION" "$TAG"; then pass "<Version> $VERSION > released $TAG"
else fail "<Version> $VERSION is not above the released tag $TAG (bump before the release)"; fi

echo "== Changelog + tester checklist"
for lang in EN DE; do
  f="Echokraut/Resources/Changelogs/v${VERSION}_${lang}.txt"
  if [ -f "$REPO/$f" ]; then pass "$f exists"; else fail "$f missing"; fi
  if grep -q "Resources\\\\Changelogs\\\\v${VERSION}_${lang}.txt" "$REPO/Echokraut/Echokraut.csproj"; then
    pass "$f embedded in the csproj"; else fail "$f not embedded (<EmbeddedResource> in Echokraut.csproj)"; fi
done
CL_VERSION="$(jq -r '.version // empty' "$REPO/Testing/checklist.json" 2>/dev/null)"
if [ "$CL_VERSION" = "$VERSION" ]; then pass "Testing/checklist.json version $CL_VERSION"
else fail "Testing/checklist.json version '$CL_VERSION' != $VERSION"; fi

echo "== RemoteUrls.json: local, GitLab main, GitHub main"
cp "$REPO/$REMOTE_URLS" "$TMP/local.json"
git -C "$REPO" fetch -q origin main 2>/dev/null || warn "git fetch origin failed, GitLab state may be stale"
git -C "$REPO" show "origin/main:$REMOTE_URLS" > "$TMP/gitlab.json" 2>/dev/null || warn "no $REMOTE_URLS on origin/main"
curl -sf --max-time 20 "https://raw.githubusercontent.com/$GH_PLUGIN/main/$REMOTE_URLS" > "$TMP/github.json" \
  || fail "GitHub main $REMOTE_URLS not readable"
same_json() { [ "$(jq -S . "$1" 2>/dev/null)" = "$(jq -S . "$2" 2>/dev/null)" ]; }
if same_json "$TMP/gitlab.json" "$TMP/github.json"; then pass "GitHub mirror shows the GitLab main state"
else fail "GitHub main differs from GitLab main (mirror stale): $(diff <(jq -S . "$TMP/gitlab.json") <(jq -S . "$TMP/github.json") | grep -c '^[<>]') changed lines"; fi
if same_json "$TMP/local.json" "$TMP/github.json"; then pass "local RemoteUrls.json is live on GitHub main"
else
  warn "local RemoteUrls.json is not live yet (expected until the push):"
  diff <(jq -S . "$TMP/github.json") <(jq -S . "$TMP/local.json") | grep '^[<>]' | sed 's/^/        /'
fi

# Every URL in a RemoteUrls file answers; version fields match their URLs.
check_urls() { # <file> <label> <fail|warn>
  local file="$1" label="$2" level="$3" url code iv tv
  iv="$(jq -r '.installerVersion' "$file")"; tv="$(jq -r '.echokrauTtsVersion' "$file")"
  case "$(jq -r '.installerUrl' "$file")" in */"$iv"/*) pass "$label installerUrl matches $iv" ;; *) $level "$label installerUrl does not contain $iv" ;; esac
  case "$(jq -r '.echokrauTtsUrl' "$file")" in */"$tv"/*) pass "$label echokrauTtsUrl matches $tv" ;; *) $level "$label echokrauTtsUrl does not contain $tv" ;; esac
  for key in gameVoiceAliasesUrl gameVoiceActorSplitsUrl; do
    jq -e "has(\"$key\")" "$file" >/dev/null || warn "$label has no $key (R6: add after the plugin release)"
  done
  jq -r '.. | strings | select(test("^https?://"))' "$file" | sed "s/{version}/$tv/" | sort -u > "$TMP/urls"
  local bad=0 base known
  base="$(jq -r '.echolinesUrl // empty' "$file")"   # an API base: any answer below 500 means it is up
  while read -r url; do
    case "$url" in *api.github.com*) continue ;; esac   # rate-limited, checked through the tag below
    code="$(http_code "$url")"
    if [ "$url" = "$base" ]; then
      [ "$code" != 000 ] && [ "$code" -lt 500 ] && continue
    elif [ "${code:0:1}" = 2 ] || [ "${code:0:1}" = 3 ]; then continue; fi
    known=""; for k in "${KNOWN_BAD[@]}"; do case "$url" in *"$k"*) known=1 ;; esac; done
    if [ -n "$known" ]; then warn "$label URL answers $code (known open defect): $url"
    else $level "$label URL answers $code: $url"; bad=1; fi
  done < "$TMP/urls"
  [ $bad = 0 ] && pass "$label: no unexpected failure among $(wc -l < "$TMP/urls" | tr -d ' ') URLs"
}
check_urls "$TMP/github.json" "live" fail
same_json "$TMP/local.json" "$TMP/github.json" || check_urls "$TMP/local.json" "local" warn

echo "== engines.json"
if same_json "$WRAPPER/engines.json" "$REPO/Echokraut/Resources/Engines.json"; then pass "wrapper engines.json == embedded Engines.json"
else fail "wrapper engines.json differs from Echokraut/Resources/Engines.json"; fi
info "order: $(jq -r '[.engines[] | "\(.id)(\(.minVersion // "-"))"] | join(" → ")' "$WRAPPER/engines.json")"
while read -r id min; do
  [ "$min" = null ] && continue
  if [ -n "$WTAG" ] && version_gt "$min" "$WTAG"; then warn "$id minVersion $min is above the released wrapper $WTAG"; fi
done < <(jq -r '.engines[] | "\(.id) \(.minVersion)"' "$WRAPPER/engines.json")
for f in "$TMP/github.json" "$TMP/local.json"; do
  tv="$(jq -r '.echokrauTtsVersion' "$f")"
  url="$(jq -r '.echokrauTtsEnginesUrl' "$f" | sed "s/{version}/$tv/")"
  if curl -sf --max-time 20 "$url" > "$TMP/tag-engines.json"; then
    if same_json "$TMP/tag-engines.json" "$WRAPPER/engines.json"; then pass "engines.json at tag $tv == wrapper checkout"
    else warn "engines.json at tag $tv differs from the wrapper checkout (fine if the checkout is ahead)"; fi
  else warn "engines.json at tag $tv not reachable (tag not pushed yet?)"; fi
  same_json "$TMP/local.json" "$TMP/github.json" && break
done

echo "== Wrapper release zip"
if (cd "$WRAPPER" && python3 build-release-zip.py --list) > "$TMP/zip.txt" 2>"$TMP/zip.err"; then
  missing="$(cd "$WRAPPER/wrapper" && git ls-files 'src/*.py' | grep -vxFf "$TMP/zip.txt")"
  if [ -z "$missing" ]; then pass "zip lists every tracked wrapper/src/*.py ($(wc -l < "$TMP/zip.txt" | tr -d ' ') entries)"
  else fail "zip misses: $(echo $missing)"; fi
  if grep -qiE '(^|/)(CLAUDE\.md|\.claude/|windows-test-out/|__pycache__/)' "$TMP/zip.txt"; then
    fail "zip contains dev files: $(grep -iE '(^|/)(CLAUDE\.md|\.claude/|windows-test-out/|__pycache__/)' "$TMP/zip.txt" | head -3 | tr '\n' ' ')"
  else pass "zip has no CLAUDE.md / .claude / test output / __pycache__"; fi
else fail "build-release-zip.py --list failed: $(head -1 "$TMP/zip.err")"; fi

echo "== GitLab pipelines (main)"
for dir in "$REPO" "$WRAPPER"; do
  remote="$(git -C "$dir" remote get-url origin)"
  host="$(echo "$remote" | sed -E 's#^(https?://|git@)([^/:]+).*#\2#')"
  path="$(echo "$remote" | sed -E 's#^(https?://[^/]+/|git@[^:]+:)##; s#\.git$##')"
  enc="$(printf '%s' "$path" | jq -sRr @uri)"
  name="$(basename "$path")"
  if out="$(glab api --hostname "$host" "projects/$enc/pipelines?ref=main&per_page=1" 2>/dev/null)" && [ "$(echo "$out" | jq length)" -gt 0 ]; then
    status="$(echo "$out" | jq -r '.[0].status')"; sha="$(echo "$out" | jq -r '.[0].sha[0:7]')"
    head="$(git -C "$dir" rev-parse --short=7 origin/main 2>/dev/null)"
    case "$status" in
      success) pass "$name pipeline $sha success" ;;
      running|pending|created) warn "$name pipeline $sha $status" ;;
      *) fail "$name pipeline $sha $status" ;;
    esac
    [ "$sha" = "$head" ] || warn "$name newest pipeline is for $sha, origin/main is $head"
    ahead="$(git -C "$dir" rev-list --count origin/main..HEAD 2>/dev/null)"
    [ "${ahead:-0}" = 0 ] || warn "$name: $ahead local commit(s) not pushed"
  else warn "$name: pipeline status not readable (glab auth?)"; fi
done

if [ $VERIFY = 1 ]; then
  echo "== verify (plugin, wrapper)"
  for dir in "$REPO" "$WRAPPER"; do
    if (cd "$dir" && node .claude/verify.js) > "$TMP/verify.log" 2>&1; then pass "$(basename "$dir") verify.js green"
    else fail "$(basename "$dir") verify.js red: $(grep -m1 -iE 'error|fail' "$TMP/verify.log")"; fi
  done
fi

echo "== Not covered here (manual or in the game): SonarQube scan, Docker images (needs the user's OK to trigger), everything in R1-R5."
echo "preflight: $FAILS FAIL, $WARNS WARN"
[ $FAILS = 0 ]
