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

### Key Files

- `Program.cs` - Entry point, CLI routing, all command action methods
- `CommandArguments.cs` - All argument classes for commands
- `ChatSession.cs` - Interactive chat session management
- `ManageCommand.cs` - TUI implementation with themes
- `QcCommand.cs` - Quantization comparison implementation
- `OllamaModels.cs` - Ollama API data models
- `ThrottledStream.cs` - Bandwidth limiting for transfers

### Test Structure

BDD tests using SpecFlow + xUnit in `osync.Tests/`:
- Feature files in `Features/` directory
- Step definitions in `StepDefinitions/`
- Test infrastructure in `Infrastructure/` (OsyncRunner, TestConfiguration)

### Key Technical Details

- Static HttpClient with 1-day timeout for large model transfers
- Model names auto-append `:latest` tag when not specified
- Tab completion support via PowerArgs with local models as source
- Cross-platform publish profiles in `Properties/PublishProfiles/`

### Dependencies

Core: PowerArgs (CLI), Spectre.Console (formatting), Terminal.Gui (TUI), TqdmSharp (progress bars)
Test: xUnit, SpecFlow, FluentAssertions

## Important Guidelines

- **Never change the program version** unless explicitly asked by the user. When updating the changelog in README.md, add new entries under the existing version - do not create a new version number.
