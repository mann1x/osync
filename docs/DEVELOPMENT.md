# osync development, build and release protocol

> Status: **DRAFT for review**. Sections marked *(planned)* describe workflows that are not in the repo yet.

## Branches

| Branch | Purpose | Who writes to it |
|---|---|---|
| `master` | Released code only. Every release is a tag `vX.Y.Z` on `master`. | Merge from `dev` (release) or `hotfix/*` only |
| `dev` | Integration branch. Always builds, CI always green. | Pull requests from topic branches |
| `feat/*`, `fix/*`, `test/*`, `ci/*`, `claude/*` | One topic each (a feature, one bug/audit item, CI work, a Claude session) | The author |
| `hotfix/*` | Urgent fix for the released version, branched from `master` | The author |

Recommended GitHub settings (Settings → Branches → rules), set by the repository owner:
- `master` and `dev`: require a pull request, require the **CI** status checks to pass, no force-push, no deletion.
- Merge style: *squash* for topic → `dev`, *merge commit* for `dev` → `master` (keeps the release history readable).

## Development cycle

1. Pick an item (issue or audit finding). Branch from `dev`: `git switch -c fix/rm-exit-code origin/dev`.
2. **Test first** for bugs: write a test that fails on the current code, then fix it, then see it pass.
3. Run the fast checks locally (see *Local testing*): build + unit tests at minimum.
4. Open a PR into `dev`. CI must be green on all three OSes, and the integration job must be green once it exists.
5. Update the **Changelog in README.md** under the *unreleased* section (never under an already-released version; never bump the version in a feature PR).
6. Squash-merge.

### Dependency upgrades

Spectre.Console, Terminal.Gui, PrettyConsole and PowerArgs have caused interactive-UI regressions before. Rules:
- **One package per PR**, never bundled with other changes.
- The PR must include the result of the *manual interactive checklist* below on Windows and on one Unix terminal.
- A version that was deliberately held back gets a comment next to its `PackageReference` saying why.
- Changing the target framework follows the same rule (the move to `net10.0` did not change any package version).

## Test tiers

| Tier | Needs | Filter | Runs in |
|---|---|---|---|
| Unit | nothing | `FullyQualifiedName~osync.Tests.UnitTests` | everywhere (Windows/Linux/macOS, CI, cloud sessions) |
| Integration: local *(planned)* | one Ollama server + `ollama` CLI | `Category=Local` | CI (Linux), developer machines |
| Integration: remote *(planned)* | local + two remote Ollama servers | `Category=Remote` | CI (Linux, two Ollama containers), developer machines via docker compose |

Integration-test rules *(planned, applies to the rewritten suite)*:
- **Atomic**: every test creates what it needs under a unique name (`osync-t-<id>`), and deletes it in cleanup even on failure. No test depends on another test or on models the developer has.
- **Arrange/verify through the Ollama HTTP API**, not through osync (osync is the thing under test). Existence is checked by exact `name:tag`.
- **Never touch developer data**: no `update *`, no "unload all" against a server the test did not start, no deleting models the test did not create.
- **Skip, don't fail**, when the required server is not reachable.
- Integration tests run serially (single xUnit collection); unit tests run in parallel.
- Base model: SmolLM2-135M-Instruct GGUF, created in Ollama from a Modelfile by the test fixture (not pulled from the registry). Only tests of `pull`/`update` themselves use the registry.

### Local testing

```bash
dotnet build
dotnet test osync.Tests --filter "FullyQualifiedName~osync.Tests.UnitTests"
```

*(planned)* `scripts/test.ps1` / `scripts/test.sh` with `unit | local | remote | all`, and `docker-compose.test.yml` that starts two Ollama servers on ports 11435 and 11436 so the remote tests run without LAN servers. Remote servers can also be set with `OSYNC_TEST_REMOTE1` / `OSYNC_TEST_REMOTE2`.

### Cloud development (Claude Code on the web)

Cloud sessions can build and run unit tests (.NET SDK installed from Ubuntu packages). They cannot reach `ollama.com` / `registry.ollama.ai` under the current network policy, so integration tests run on the CI runners: push the branch and read the CI result.

## CI

| Workflow | Trigger | What it does |
|---|---|---|
| `ci.yml` | every push, PRs into `master`/`dev` | build + unit tests on Ubuntu, Windows, macOS; smoke-runs the CLI |
| integration *(planned, part of `ci.yml`)* | same | Linux runner: installs the Ollama version recorded in `.github/ollama-version` as the local server, starts two `ollama/ollama` containers as remote servers, runs the local + remote integration tests |
| `ollama-compat.yml` *(planned)* | daily **check only** (seconds) + manual | Looks up the latest **stable** (non-pre-release) Ollama release. If it differs from `.github/ollama-version`, runs the full integration suite against it: green → opens a PR bumping `.github/ollama-version`; red → opens an issue with the failing tests. Nothing heavy runs unless Ollama released something new. |
| `release.yml` *(planned)* | tag `v*` pushed on `master` | builds self-contained single-file binaries (win-x64, linux-x64, linux-arm64, osx-x64, osx-arm64), attaches them to a **draft** GitHub Release with the changelog section; the owner publishes it |

## Release protocol

1. On `dev`: all PRs for the release merged, CI green.
2. Owner bumps `AppVersion` in `osync/Program.cs` and turns the *unreleased* changelog section in README.md into the new version section.
3. **Manual interactive checklist** on Windows (Windows Terminal) and on one Linux/macOS terminal, using the CI-built artifact:
   - `osync run <model>`: chat, multi-line input, `/set`, `/save`, `/load`, thinking output, Ctrl+C
   - `osync manage`: navigation, themes, copy/rename/delete dialogs, resize
   - `osync psmonitor`: graphs render, resize, exit restores the terminal (colors, cursor)
   - `osync qc` / `osync bench` progress displays; `qcview` / `benchview` PDF/HTML output opens correctly
   - `osync cp` local→remote progress bar and throttling (`-bt`)
4. PR `dev` → `master`, merge commit.
5. Tag `vX.Y.Z` on `master` and push the tag. `release.yml` builds the draft release; the owner checks the assets and publishes.
6. Hotfix: branch `hotfix/*` from `master`, PR into `master`, tag, then merge `master` back into `dev`.
