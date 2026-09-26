#!/usr/bin/env bash
# Downloads the integration-test base model (see osync.Tests/Assets/test-model.json) from the
# 'test-assets' release of this repository into osync.Tests/Assets/ and verifies its checksum.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
lock="$root/osync.Tests/Assets/test-model.json"
field() { python3 -c "import json,sys; print(json.load(open(sys.argv[1]))[sys.argv[2]])" "$lock" "$1"; }
file="$(field file)"; sha="$(field sha256)"; tag="$(field releaseTag)"
repo="${OSYNC_REPO:-mann1x/osync}"
dest="$root/osync.Tests/Assets/$file"

check() { [ -f "$dest" ] && [ "$(sha256sum "$dest" 2>/dev/null | cut -d' ' -f1 || shasum -a 256 "$dest" | cut -d' ' -f1)" = "$sha" ]; }

if check; then echo "Test model already present: $dest"; exit 0; fi
echo "Downloading $file ..."
curl -fL --retry 5 -o "$dest.part" "https://github.com/$repo/releases/download/$tag/$file"
mv "$dest.part" "$dest"
check || { echo "Checksum mismatch for $dest" >&2; rm -f "$dest"; exit 1; }
echo "OK: $dest"
