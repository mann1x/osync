#!/usr/bin/env bash
# Run the osync test suite locally.
#   scripts/test.sh unit                 unit + CLI tests (no Ollama needed)
#   scripts/test.sh integration          integration tests against your local Ollama (OSYNC_TEST_LOCAL / OLLAMA_HOST / :11434)
#   scripts/test.sh all                  both
#   --servers                            also start two throwaway Ollama servers on :11435 and :11436 as remote1/remote2
#   --knownbug                           run only the @knownbug scenarios (expected to fail until fixed)
# Registry tests (pull/update from the internet) run only with OSYNC_TEST_REGISTRY=1.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
mode="${1:-all}"; shift || true
servers=false; knownbug=false
for a in "$@"; do case "$a" in --servers) servers=true ;; --knownbug) knownbug=true ;; *) echo "unknown option $a" >&2; exit 2 ;; esac; done

case "$mode" in
  unit) filter="FullyQualifiedName!~osync.Tests.Integration|Category=cli" ;;
  integration) filter="FullyQualifiedName~osync.Tests.Integration" ;;
  all) filter="FullyQualifiedName~osync" ;;
  *) echo "usage: $0 unit|integration|all [--servers] [--knownbug]" >&2; exit 2 ;;
esac
if $knownbug; then filter="($filter)&Category=knownbug"; else filter="($filter)&Category!=knownbug"; fi

pids=()
cleanup() { for p in "${pids[@]:-}"; do [ -n "$p" ] && kill "$p" 2>/dev/null || true; done; [ -n "${tmp:-}" ] && rm -rf "$tmp"; }
trap cleanup EXIT

if [ "$mode" != unit ]; then
  "$root/scripts/get-test-model.sh"
  if $servers; then
    command -v ollama >/dev/null || { echo "ollama not on PATH" >&2; exit 1; }
    tmp="$(mktemp -d)"
    for port in 11435 11436; do
      OLLAMA_HOST="127.0.0.1:$port" OLLAMA_MODELS="$tmp/$port" ollama serve > "$tmp/ollama-$port.log" 2>&1 &
      pids+=($!)
      timeout 60 bash -c "until curl -fs localhost:$port/api/version >/dev/null; do sleep 1; done"
    done
    export OSYNC_TEST_REMOTE1="${OSYNC_TEST_REMOTE1:-http://localhost:11435}"
    export OSYNC_TEST_REMOTE2="${OSYNC_TEST_REMOTE2:-http://localhost:11436}"
    export OSYNC_TEST_EXCLUSIVE="${OSYNC_TEST_EXCLUSIVE:-1}"
  fi
fi

dotnet build "$root/osync.sln"
dotnet test "$root/osync.Tests" --no-build --filter "$filter"
