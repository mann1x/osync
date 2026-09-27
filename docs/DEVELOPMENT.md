# osync development, build and release protocol

## Branches

| Branch | Purpose | Who writes to it |
|---|---|---|
| `master` | Released code. Every push that passes CI publishes release `v<AppVersion>` (once per version). | Merge from `dev` (release) or `hotfix/*` only |
| `dev` | Integration branch. Every push that passes CI publishes a pre-release `v<next>-dev.<run>`. | Pull requests from topic branches |
| `feat/*`, `fix/*`, `test/*`, `ci/*`, `claude/*` | One topic each (a feature, one bug/audit item, CI work, a Claude session) | The author |
| `hotfix/*` | Urgent fix for the released version, branched from `master` | The author |

Recommended GitHub settings (Settings → Branches → rules), set by the repository owner:
- `master` and `dev`: require a pull request, require the **CI** checks (`Build + unit tests (*)`, `Integration (Ollama)`, `Integration (xOllama)`) to pass, no force-push, no deletion.
- Merge style: *squash* for topic → `dev`, *merge commit* for `dev` → `master`.

## Development cycle

1. Pick an item (issue or audit finding). Branch from `dev`: `git switch -c fix/rm-exit-code origin/dev`.
2. **Test first** for bugs: write a scenario/test that fails on the current code, fix the code, see it pass.
   If the bug is documented by an existing `@knownbug` scenario, remove the tag in the same change.
3. Run the fast checks locally: `scripts/test.sh unit` (or `scripts\test.ps1 unit`), plus `integration` when the change touches server operations.
4. Open a PR into `dev`. CI must be green on all three OSes and the integration job.
5. Update the **Changelog in README.md** under the section of the next, unreleased version (never under an already-released version; never bump `AppVersion` in a feature PR).
6. Squash-merge. The merge publishes a `dev` pre-release automatically.

### Dependency upgrades

Spectre.Console, Terminal.Gui, PrettyConsole and PowerArgs have caused interactive-UI regressions before. Rules:
- **One package per PR**, never bundled with other changes.
- The PR must include the result of the *manual interactive checklist* (below) on Windows and on one Unix terminal.
- A version that is deliberately held back gets a comment next to its `PackageReference` saying why.
- Changing the target framework follows the same rule (the move to `net10.0` changed no package version).

## Tests

All tests live in `osync.Tests` (xUnit v3 + Reqnroll):

| Tier | Where | Needs | Runs in |
|---|---|---|---|
| Unit | `UnitTests/*.cs` | nothing | everywhere |
| CLI | `Integration/Features/Cli.feature` (`@cli`) | the osync binary | everywhere |
| Integration | `Integration/Features/*.feature` | Ollama servers, see tags | CI (Linux), developer machines |

Scenario tags declare requirements; a scenario whose requirements are missing is **skipped**, never failed:

| Tag | Requirement |
|---|---|
| `@local` | local server reachable (`OSYNC_TEST_LOCAL`, else `OLLAMA_HOST`, else `http://localhost:11434`), `ollama` or `xollama` CLI on PATH, test model |
| `@remote1`, `@remote2` | `OSYNC_TEST_REMOTE1` / `OSYNC_TEST_REMOTE2` set and reachable |
| `@peer` | `OSYNC_TEST_PEER` set and reachable: a server of the other flavor (xOllama when the others are Ollama, and the reverse), for interoperability scenarios |
| `@stores` | the models directory of every server the scenario uses is known (`OSYNC_TEST_MODELS_DIR`, `OSYNC_TEST_REMOTE1_MODELS_DIR`, `OSYNC_TEST_REMOTE2_MODELS_DIR`, `OSYNC_TEST_PEER_MODELS_DIR`): the scenario compares manifests, so a copy is checked byte for byte (same config and layer digests) |
| `@registry` | `OSYNC_TEST_REGISTRY=1` (downloads from registry.ollama.ai / huggingface.co) |
| `@defaultport` | the local server is on `localhost:11434` (Ollama) or `localhost:22434` (xOllama, with nothing on 11434) — tests osync's own server discovery |
| `@exclusive` | `OSYNC_TEST_EXCLUSIVE=1`: the remote servers are dedicated to tests (e.g. "unload all") |
| `@tty` | Runs osync in a pseudo terminal (`script(1)`, Linux only) and types keys once the screen shows the ready text: interactive commands such as `manage` (`Manage.feature`) |
| `@xollamabug` | Documents a bug of the xOllama server itself (not osync); the scenario is skipped when the local server is xOllama. Remove the tag once the pinned xOllama release fixes it. |
| `@knownbug` | Documents a confirmed, not yet fixed osync bug. Excluded from the required CI step and reported separately; remove the tag in the PR that fixes it. |

Other settings: `OSYNC_TEST_MODELS_DIR` (models dir of the local server, passed to osync as `OLLAMA_MODELS`), `OSYNC_TEST_MODEL_GGUF` (test model path), `OSYNC_TEST_OSYNC` (binary under test).

Rules for integration scenarios:
- **Atomic**: every model a scenario uses is created by the scenario under a unique name (`{alias}` in feature text becomes `osync-t-<run>-<n>-alias`), and everything under that prefix is deleted after the scenario, even on failure. Leftovers of interrupted runs (`osync-t-*`) are removed at the start of a run.
- **Arrange and verify through the Ollama HTTP API**, not through osync (osync is the system under test). Existence is checked by exact `name:tag`; copies are checked for identical size, quantization, family, template and parameters.
- **Never touch developer data**: no wildcard updates, no "unload all" unless `@exclusive`, never delete a model the scenario did not create (registry-pull scenarios are skipped if the model is already present).
- Assert exit codes and server state first; assert output text only where the text is the feature (help, `ls`, `ps`).

The base model is SmolLM2-135M-Instruct Q4_0 (~92 MB): `osync.Tests/Assets/test-model.json` pins it, `Assets/Modelfile` turns it into an Ollama model, and the GGUF is an asset of the `test-assets` pre-release of this repository. `scripts/get-test-model.sh` / `.ps1` download and verify it.

### Running tests locally

```bash
scripts/test.sh unit                        # unit + CLI, no Ollama needed
scripts/test.sh integration                 # against your local Ollama
scripts/test.sh integration --servers       # + two throwaway Ollama servers on :11435/:11436 as remote1/remote2
scripts/test.sh integration --knownbug      # only the documented known bugs
OSYNC_TEST_REGISTRY=1 scripts/test.sh all   # include pull/update tests
```

PowerShell: `scripts\test.ps1 unit|integration|all [-Servers] [-KnownBug]`.
To use your LAN servers instead of throwaway ones, set `OSYNC_TEST_REMOTE1` / `OSYNC_TEST_REMOTE2`.

### Cloud development (Claude Code on the web)

Cloud sessions can build and run the unit and CLI tiers. The environment's network policy decides whether Ollama release downloads (github.com) and the Ollama registry are reachable; when they are not, push the branch and use the CI result for the integration tier.

## CI

| Workflow | Trigger | What it does |
|---|---|---|
| `ci.yml` | every push; PRs into `master`/`dev` | build + unit/CLI tests on Ubuntu, Windows, macOS; integration suite; on `dev`/`master` pushes also packaging and publishing (see *Releases*) |
| `integration.yml` | called by `ci.yml` and `server-compat.yml`; manual | one Linux runner with three servers (local with CLI, remote1, remote2) plus a peer of the other flavor on :22437 (xOllama) or :11437 (Ollama), not on PATH — `server: ollama` on :11434-11436 (version in `.github/ollama-version`) or `server: xollama` on :22434-22436 with only the `xollama` CLI (version in `.github/xollama-version`); required step excludes `@knownbug`, a second step reports the known bugs without failing. CI runs both servers. |
| `compat.yml` → `server-compat.yml` | daily check (seconds) + manual | for Ollama and xOllama: compares the latest **stable** release (pre-releases ignored) with `.github/<server>-version`; only when they differ (and no bump PR / failure issue is open) it runs the integration suite against the new release: pass → PR into `dev` bumping the version file, fail → issue |
| `test-assets.yml` | changes to `osync.Tests/Assets/test-model.json` | publishes/verifies the test model asset on the `test-assets` pre-release |

## Releases

Publishing is automatic and gated on all CI jobs (build + unit on three OSes, integration on Ollama and xOllama):

| Push to | Publishes | Tag |
|---|---|---|
| `dev` | **pre-release** | `v<next>-dev.<run number>`: `<next>` is `AppVersion` if that version is not released yet, else the next patch version. The 10 newest dev pre-releases are kept. |
| `master` | **release** (marked latest) | `v<AppVersion>`, only if that release does not exist yet (otherwise nothing is published) |

Assets: `osync.exe` (Windows x64), `osync` (Linux x64), `osync-macos-arm64`, `osync-macos-x64` (built on macOS so they are ad-hoc signed). Release notes are the `v<AppVersion>` section of the README changelog.

Releases (not dev pre-releases) are announced on Discord through the webhook URL stored in the repository secret `TECH_CORNER_DISCOWH` (an embed with the release notes and a link to the release). A missing secret or a Discord error is reported as a warning and never fails the release.

Release steps:
1. On `dev`: all PRs for the release merged, CI green, a dev pre-release exists.
2. Owner bumps `AppVersion` in `osync/Program.cs` and completes the `v<AppVersion>` changelog section in README.md (PR into `dev`).
3. **Manual interactive checklist** with the latest dev pre-release, on Windows (Windows Terminal) and one Linux/macOS terminal:
   - `osync run <model>`: chat, multi-line input, `/set`, `/save`, `/load`, thinking output, Ctrl+C
   - `osync manage`: navigation, filter, theme picker (Ctrl+T) and settings (Ctrl+E), copy/rename/delete dialogs, copy returns to the list, resize, exit restores the terminal; once with true color and once with `OSYNC_COLOR_MODE=16`
   - `osync setup` (menu), `osync setup shell themes` / `manage themes` previews, and colored `ls` / `ps` / errors in a dark and a light terminal
   - `osync psmonitor`: graphs render, resize, exit restores the terminal (colors, cursor)
   - `osync qc` / `osync bench` progress displays; `qcview` / `benchview` PDF/HTML output opens correctly
   - `osync cp` local→remote progress bar and throttling (`-bt`)
4. PR `dev` → `master`, merge commit. CI publishes release `v<AppVersion>`.
5. Hotfix: branch `hotfix/*` from `master`, bump the patch version, PR into `master`, then merge `master` back into `dev`.

## Roadmap items decided during the audit

- ~~Push-relay copy~~ — done: `RegistryRelay.cs` / `RelayCopy.cs`.
- **xOllama features**: use `/api/tokenize` for exact token counts in bench/qc, numeric/extended thinking budgets in chat/bench/qc, engine info from `/api/engine` in ps/psmonitor (gated on server flavor).
