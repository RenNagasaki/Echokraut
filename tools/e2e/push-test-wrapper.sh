#!/usr/bin/env bash
# Builds an EchokrauTTS wrapper zip from the local echokrautts checkout (working tree, uncommitted
# changes included - same file set as a release: build-release-zip.py) and puts it on the PC where a
# dev load of the plugin looks for its test install (place-test-wrapper.ps1). Adds the checkout's
# engines.json at the zip root: a test install reads its engine catalog from there instead of the
# release tag (TestWrapperInstall.CatalogFileName).
# Then: tools/e2e/start.sh --test-wrapper --stages preconditions,backup,reset,first-time,engine --restore
# Usage: tools/e2e/push-test-wrapper.sh [echokrautts-checkout]   (default: ../echokrautts next to this repo)
source "$(dirname "$0")/lib.sh"

src="${1:-$EKE2E_DIR/../../../echokrautts}"
src="$(cd "$src" && pwd)"
[[ -f "$src/build-release-zip.py" && -f "$src/engines.json" ]] || { echo "$src is no echokrautts checkout" >&2; exit 64; }

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
zip="$tmp/EchokrauTTS.zip"
python3 "$src/build-release-zip.py" -o "$zip" | head -1
python3 - "$zip" "$src/engines.json" <<'PY'
import json, sys, zipfile
zip_path, catalog = sys.argv[1:]
data = open(catalog, "rb").read()
doc = json.loads(data)                      # refuse a broken catalog before it reaches the PC
assert doc.get("version") == 1 and doc.get("engines"), "engines.json: expected version 1 with engines"
with zipfile.ZipFile(zip_path, "a", zipfile.ZIP_DEFLATED) as z:
    if "engines.json" in z.namelist():
        sys.exit("zip already has an engines.json")
    z.writestr(zipfile.ZipInfo("engines.json", (2026, 1, 1, 0, 0, 0)), data)
print("engines:", ", ".join(f"{e['id']}>={e.get('minVersion') or '-'}" for e in doc["engines"]))
PY

head="$(git -C "$src" rev-parse --short HEAD)"
dirty="$(git -C "$src" status --porcelain -- wrapper engines.json | wc -l | tr -d ' ')"
echo "source: echokrautts @ $head, $dirty uncommitted path(s) under wrapper/ + engines.json"
echo "sha256 $(shasum -a 256 "$zip" | cut -d' ' -f1)"

eke2e_upload_tools
scp -q "$zip" "$EKE2E_HOST:$EKE2E_REMOTE_TOOLS/EchokrauTTS-test.zip"
eke2e_ps place-test-wrapper.ps1 -Uploaded "$EKE2E_REMOTE_TOOLS\\EchokrauTTS-test.zip"
