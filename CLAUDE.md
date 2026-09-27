# OpenWolf

@.wolf/OPENWOLF.md

This project uses OpenWolf for context management. Read and follow .wolf/OPENWOLF.md every session. Check .wolf/cerebrum.md before generating code. Check .wolf/anatomy.md before reading files.


# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

osync is a CLI tool for managing Ollama models across local and remote servers (Ollama and the xOllama fork, github.com/mann1x/xollama). Written in C# targeting .NET 10 (`net10.0`, cross-platform: builds and runs on Windows, Linux and macOS). Windows-only APIs (WMI, NvAPI, D3DKMT) must stay behind `OperatingSystem.IsWindows()` guards.

## Build Commands

```bash
dotnet build                    # Build debug (framework-dependent, any OS)
dotnet build -c Release         # Build release
dotnet publish -c Release -r win-x64      # Self-contained single-file exe (also linux-x64, osx-arm64, ...)
dotnet test osync.Tests --filter "FullyQualifiedName~osync.Tests.UnitTests"  # Unit tests only (no Ollama needed)
dotnet test                     # All tests (integration tests need a running Ollama)
```

## Architecture

### Command Structure

The application uses PowerArgs for CLI parsing. All commands are defined as action methods in `OsyncProgram` class (Program.cs) with corresponding `*Args` classes in CommandArguments.cs:

- **Copy (cp)** - Model transfers with bandwidth throttling support. Local→remote uploads blobs from the local models dir; remote→remote and remote→local use the push relay (`RelayCopy.cs` + `RegistryRelay.cs`: the source server `/api/push`es to a temporary registry endpoint run by osync, which streams blobs into the destination's `/api/blobs`, then the destination `/api/pull`s the manifest); fallback to registry.ollama.ai downloads only when the source cannot reach the relay
- **List (ls)** - Pattern matching with wildcards, multiple sort modes
- **Remove (rm/delete/del)** - Pattern-based deletion
- **Rename (mv/ren)** - Safe rename via copy → verify → delete workflow
- **Update** - Update models to latest versions
- **Pull** - Download from registry (includes HuggingFace support)
- **Show** - Display model metadata
- **Run/Chat** - Interactive chat with extended thinking mode support
- **Ps** - List loaded models in memory
- **Load/Unload** - VRAM management
- **Manage** - Full-screen TUI using Terminal.Gui 2
- **Qc** - Quantization comparison with test suites
- **QcView** - Quantization comparison test results viewer

### Key Files

- `Program.cs` - Entry point, CLI routing, all command action methods
- `CommandArguments.cs` - All argument classes for commands
- `ChatSession.cs` - Interactive chat session management
- `ManageCommand.cs` - TUI implementation (Terminal.Gui 2)
- `Themes.cs` - the 34 themes (24-bit palettes) of `manage` and of the shell output, adapted to 256/16 colors
- `ShellOutput.cs` - `Out`: colored command output (errors, warnings, results, tables) in the shell theme
- `SetupCommand.cs` - `osync setup` (server, alias, manage, shell); `ServerSetup.cs` - server questions of install/setup
- `ServerAliases.cs` - server aliases from the settings file (`gpu` → `http://…:11434`)
- `ColorSupport.cs` - terminal color depth detection; `OsyncSettings.cs` - preferences file
- `QcCommand.cs` - Quantization comparison implementation
- `QcViewCommand.cs` - QC results viewer with PDF/HTML/Markdown output generation
- `QcModels.cs` - Data models for QC results (JudgmentResult, QuantResult, etc.)
- `QcScoring.cs` - Score calculation logic for QC results
- `OllamaModels.cs` - Ollama API data models
- `ThrottledStream.cs` - Bandwidth limiting for transfers
- `CloudProviders/` - Cloud AI provider implementations for judge models

### Test Structure

xUnit v3 + Reqnroll (Gherkin) in `osync.Tests/` — see `docs/DEVELOPMENT.md` for the full protocol:
- `UnitTests/` - plain xUnit unit tests (no Ollama needed)
- `Integration/Features/*.feature` - scenarios; tags declare requirements (`@cli`, `@local`, `@remote1`, `@remote2`, `@registry`, `@exclusive`); `@knownbug` marks documented unfixed bugs
- `Integration/Steps/` - step definitions; `Integration/Infrastructure/` - `OsyncCli` (runs the binary), `OllamaApi` (arrange/verify via HTTP), `ScenarioState` (unique names + cleanup), `TestEnvironment` (`OSYNC_TEST_*` env vars)
- Scenarios must be atomic: use `{alias}` placeholders for model names, create models with `Given a test model "alias" on <server>`, verify via the Ollama API, never touch models the scenario did not create
- Run: `scripts/test.sh unit|integration|all [--servers] [--knownbug]` (or `scripts\test.ps1`)

### Key Technical Details

- Static HttpClient with 1-day timeout for large model transfers
- Model names auto-append `:latest` tag when not specified
- Tab completion support via PowerArgs with local models as source

### Dependencies

Core: PowerArgs (CLI), Spectre.Console (formatting), Terminal.Gui 2 (TUI), TqdmSharp (progress bars)
PDF: iText7 (AGPL-3.0 licensed) - used for PDF report generation in QcView
AI SDKs: Anthropic, OpenAI, Azure.AI.OpenAI - for cloud judge providers
Test: xUnit v3, Reqnroll, FluentAssertions (pinned to 7.x: v8+ license change)

### Test Data

QC test result files for testing qcview output are located in `d:\install\osync\test\`

## Important Guidelines

### Working Directory and File Management

- **Never save generated files in the repository directory** - The repository will be pushed to GitHub, so do not create test files, output files, logs, or any generated content in the osync source directory
- **Use `.\osync\bin` as the default working directory** for:
  - Running/testing the osync executable
  - Saving test output files (JSON results, logs, etc.)
  - Creating temporary files
  - Executing benchmark or QC test runs
- **Example paths:**
  - Run osync: `.\osync\bin\Debug\net10.0\osync.exe` (or `dotnet osync/bin/Debug/net10.0/osync.dll` on any OS)
  - Save test results: `.\osync\bin\test-results.json`
  - Log files: `.\osync\bin\test_log.txt`

### Version and Changelog

- **Never change the program version** unless explicitly asked by the user
- **Changelog is in README.md** - Update the Changelog section in README.md, not a separate CHANGELOG.md file
- **Do not bump version when updating changelog** - Only add entries under the current version section
- **Always check the current version** before updating the changelog - check `Program.cs` for the version constant or recent git tags
- **Check GitHub releases** at https://github.com/mann1x/osync/releases to see what's already been released before adding changelog entries
- When updating changelog, create a new version section if needed - don't add new features under an already-released version

### Spectre.Console Quirks

- **File access checks with confirmation prompts** must happen BEFORE starting a Progress display
- `AnsiConsole.Confirm()` cannot run inside a Progress context - causes "concurrent interactive functions" error
- When adding file output with overwrite confirmation, check access before `AnsiConsole.Progress().StartAsync()`

### iText7 PDF Generation

- Standard Type1 fonts (Helvetica, Courier) have limited Unicode support
- **Text corruption issue**: Long text with certain patterns (e.g., Python format strings with `%`) can cause character scrambling
- **Solution**: Add text line-by-line as separate `Text` elements instead of passing entire string to Paragraph
- Use `CreateCodeParagraph()` helper in QcViewCommand.cs for code/answer content
- Use `SanitizeForPdf()` to replace problematic Unicode characters with ASCII equivalents
- Courier font is better for code content than Helvetica

### Flexible URL Parsing for Remote Destinations

Commands that support remote servers (copy, bench, qc) use flexible URL parsing via `LooksLikeRemoteServer()` and `NormalizeServerUrl()` in Program.cs. Always use these helper methods when parsing remote destinations.

**Supported formats:**
- IP address: `192.168.100.100/model`
- Hostname with port: `myserver:11434/model`
- Trailing slash: `myserver//model`
- Full URL: `http://server:port/model`
- Cloud provider: `@provider[:token]/model`

**Cloud provider syntax for `--judge` and `--judgebest`:**
- `@claude/model-name` - Uses ANTHROPIC_API_KEY env var
- `@openai/model-name` - Uses OPENAI_API_KEY env var
- `@gemini/model-name` - Uses GEMINI_API_KEY env var
- `@provider:explicit-token/model` - Explicit token in command
- Local Ollama: `model-name` (no @ prefix) - Uses the local server (see below) regardless of `-d` setting

**Important:** The `-d` destination flag only affects test models. Judge models always use the local server unless explicitly specified with a remote URL (e.g., `192.168.1.100:11434/model`) or cloud provider prefix (`@provider/model`).

### Local server resolution (Ollama and xOllama)

Always use `OllamaServer` (OllamaServer.cs) — never hardcode `localhost:11434`, read `OLLAMA_HOST` directly, or spawn `ollama`:
- `OllamaServer.ResolveHost(destination)` / `OllamaServer.LocalUrl`: `-d`, else `XOLLAMA_HOST`, `OLLAMA_HOST`, the settings file (first when `server.ignoreEnvironment`), then probe localhost:11434 (Ollama) and localhost:22434 (xOllama); the logic is `OllamaServer.ResolveLocalUrl` (pure, unit-tested)
- `OllamaServer.GetFlavor(url)`: Ollama vs xOllama (xOllama answers `GET /api/xollama`)
- `OllamaServer.CliName` + `OllamaServer.ApplyCliEnvironment(startInfo)` for CLI shell-outs (`ollama` or `xollama`, `OSYNC_OLLAMA_CLI` override)
- `OllamaServer.ModelsDirFromEnvironment()`: `XOLLAMA_MODELS`, then `OLLAMA_MODELS`
- xOllama-only API features (`/api/tokenize`, numeric/extended `think` budgets) must be gated on `GetFlavor(url) == ServerFlavor.XOllama`

### Colored shell output

- Write messages through `Out` (ShellOutput.cs): `Out.Error("...")` prints "Error: ...", `Out.Warning`, `Out.Success` ("✓ ..."), `Out.Failure` ("✗ ..."), `Out.StatusLine` (highlights 'model' names and URLs); `Out.Paint(text, p => p.Size)` for table columns
- Pad table columns before painting (escape codes have no width); colors never change the text, so redirected output (tests, pipes) is identical
- Never hardcode colors: roles come from the shell theme (`ShellPalette`), which is null (plain output) when redirected, with NO_COLOR or the `plain` theme
- New themes go in `Themes.All`; `ThemesTests` checks WCAG contrast for every theme at every color depth

### Server aliases

- Any argument that can be a server must go through `NormalizeServerUrl` / `LooksLikeRemoteServer` / `ParseRemoteSource` or `ServerAliases.IsServerReference`, so aliases (`gpu`, `gpu/model`) work everywhere

### Terminal.Gui 2 (manage)

- Instance API only: `Application.Create().Init()` per TUI session, never the obsolete static `Application.*`
- Console operations (copy, run, update, pull) never run inside the TUI: set a pending action, `RequestStop`, dispose the app, run on the console, then start a new session (`ManageUI.Run` loop)
- Colors come from the theme (`Themes`), registered as the `Base`/`Dialog`/`Error` schemes on every session (so a `~/.tui/config.json` cannot change them); `Driver.Force16Colors` follows `ColorSupport` (no 256-color output in Terminal.Gui)
- Set `HotKeySpecifier = (Rune)0xFFFF` on views showing model names (`_` is a hotkey marker), `KeystrokeNavigator = null` on lists fed by typing, handle `Esc` (app quit key) yourself
- Dialogs: the last `AddButton` is the default (Enter); put the safe choice last for destructive confirmations; validate in `FormDialog.Validate`
- Interactive behavior is covered by `@tty` scenarios (`Manage.feature`, pseudo terminal via `script`)

### MCP Server Notes

- **context7**: If the context7 MCP server requires re-authentication during a session, pause and ask the user to re-authenticate before continuing with documentation queries.
