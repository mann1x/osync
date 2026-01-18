# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

osync is a CLI tool for managing Ollama models across local and remote servers. Written in C# targeting .NET 8 (net8.0-windows10.0.22621.0).

## Build Commands

```bash
dotnet build                    # Build debug
dotnet build -c Release         # Build release
dotnet publish -c Release       # Create standalone executables
dotnet test                     # Run all tests
dotnet test --filter DisplayName~CopyCommands  # Run specific feature tests
```

## Architecture

### Command Structure

The application uses PowerArgs for CLI parsing. All commands are defined as action methods in `OsyncProgram` class (Program.cs) with corresponding `*Args` classes in CommandArguments.cs:

- **Copy (cp)** - Model transfers with bandwidth throttling support, memory-buffered streaming for remote transfers
- **List (ls)** - Pattern matching with wildcards, multiple sort modes
- **Remove (rm/delete/del)** - Pattern-based deletion
- **Rename (mv/ren)** - Safe rename via copy → verify → delete workflow
- **Update** - Update models to latest versions
- **Pull** - Download from registry (includes HuggingFace support)
- **Show** - Display model metadata
- **Run/Chat** - Interactive chat with extended thinking mode support
- **Ps** - List loaded models in memory
- **Load/Unload** - VRAM management
- **Manage** - Full-screen TUI using Terminal.Gui
- **Qc** - Quantization comparison with test suites
- **QcView** - Quantization comparison test results viewer

### Key Files

- `Program.cs` - Entry point, CLI routing, all command action methods
- `CommandArguments.cs` - All argument classes for commands
- `ChatSession.cs` - Interactive chat session management
- `ManageCommand.cs` - TUI implementation with themes
- `QcCommand.cs` - Quantization comparison implementation
- `QcViewCommand.cs` - QC results viewer with PDF/HTML/Markdown output generation
- `QcModels.cs` - Data models for QC results (JudgmentResult, QuantResult, etc.)
- `QcScoring.cs` - Score calculation logic for QC results
- `OllamaModels.cs` - Ollama API data models
- `ThrottledStream.cs` - Bandwidth limiting for transfers
- `CloudProviders/` - Cloud AI provider implementations for judge models

### Test Structure

BDD tests using SpecFlow + xUnit in `osync.Tests/`:
- Feature files in `Features/` directory
- Step definitions in `StepDefinitions/`
- Test infrastructure in `Infrastructure/` (OsyncRunner, TestConfiguration)

### Key Technical Details

- Static HttpClient with 1-day timeout for large model transfers
- Model names auto-append `:latest` tag when not specified
- Tab completion support via PowerArgs with local models as source

### Dependencies

Core: PowerArgs (CLI), Spectre.Console (formatting), Terminal.Gui (TUI), TqdmSharp (progress bars)
PDF: iText7 (AGPL-3.0 licensed) - used for PDF report generation in QcView
AI SDKs: Anthropic, OpenAI, Azure.AI.OpenAI - for cloud judge providers
Test: xUnit, SpecFlow, FluentAssertions

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
  - Run osync: `.\osync\bin\Debug\net8.0-windows10.0.22621.0\osync.exe`
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
- Local Ollama: `model-name` (no @ prefix) - Uses localhost:11434 regardless of `-d` setting

**Important:** The `-d` destination flag only affects test models. Judge models always use localhost unless explicitly specified with a remote URL (e.g., `192.168.1.100:11434/model`) or cloud provider prefix (`@provider/model`).

### MCP Server Notes

- **context7**: If the context7 MCP server requires re-authentication during a session, pause and ask the user to re-authenticate before continuing with documentation queries.
