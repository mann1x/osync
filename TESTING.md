# Testing Guide for osync

This document provides comprehensive instructions for running and customizing the automated test suite for osync.

## Table of Contents

- [Overview](#overview)
- [Test Framework](#test-framework)
- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Configuration](#configuration)
- [Running Tests](#running-tests)
- [Test Categories](#test-categories)
- [Writing New Tests](#writing-new-tests)
- [Test Infrastructure](#test-infrastructure)
- [Troubleshooting](#troubleshooting)

## Overview

The osync test suite is built using a combination of:
- **xUnit** - Modern .NET test framework with parallel execution
- **SpecFlow** - BDD (Behavior-Driven Development) for human-readable test scenarios
- **FluentAssertions** - Expressive assertion library

This combination provides:
- Executable specifications in plain English (Gherkin syntax)
- Robust test execution and reporting
- Easy customization and extension
- Support for both developer and CI/CD workflows

## Test Framework

### Technology Stack

- **.NET 8** - Target framework (net8.0-windows10.0.22621.0)
- **xUnit 2.9.2** - Test runner
- **SpecFlow 3.9.74** - BDD framework
- **FluentAssertions 6.12.0** - Assertion library
- **Microsoft.Extensions.Configuration 8.0.0** - Configuration management

### Project Structure

```
osync.Tests/
├── Features/                           # Gherkin feature files
│   ├── BasicCommands.feature          # Basic command scenarios
│   ├── CopyCommands.feature           # Copy operation tests
│   ├── PullCommand.feature            # Pull command tests
│   ├── RemoveCommand.feature          # Remove command tests
│   ├── RenameCommand.feature          # Rename command tests
│   ├── ShowCommand.feature            # Show command tests
│   ├── UpdateCommand.feature          # Update command tests
│   ├── ChatCommand.feature            # Chat/run command tests (NEW in v1.1.5)
│   ├── LoadCommand.feature            # Load command tests (NEW in v1.1.6)
│   ├── UnloadCommand.feature          # Unload command tests (NEW in v1.1.6)
│   ├── ProcessStatusCommand.feature   # Process status (ps) command tests (NEW in v1.1.6)
│   └── ManageCommand.feature          # Manage TUI command tests (NEW in v1.1.6)
├── StepDefinitions/                   # C# step implementations
│   └── CommonSteps.cs                 # Reusable step definitions
├── Infrastructure/                    # Core test infrastructure
│   ├── TestConfiguration.cs           # Configuration loader
│   ├── OsyncRunner.cs                 # Process spawning & execution
│   └── OsyncResult.cs                 # Command result wrapper
├── Support/                           # Test support classes
│   ├── Hooks.cs                       # Test lifecycle hooks
│   └── TestContext.cs                 # Test state management
├── test-config.json                   # Test configuration
├── specflow.json                      # SpecFlow settings
└── osync.Tests.csproj                 # Project file

```

## Prerequisites

### Required Software

1. **.NET 8 SDK** or later
   ```bash
   dotnet --version  # Should show 8.0.x or higher
   ```

2. **Ollama** running locally (for local tests)
   ```bash
   # Windows
   ollama serve

   # Linux/macOS
   ollama serve
   ```

3. **Test Model** (configured in test-config.json)
   ```bash
   ollama pull llama3.2:1b
   ```

### Optional (for remote tests)

- **Remote Ollama Servers** - Two remote Ollama instances for remote/remote-to-remote testing
- Configure URLs in `test-config.json`

## Quick Start

### 1. Build the Projects

```bash
# From repository root
cd C:\Users\ManniX\source\repos\osync

# Build osync
dotnet build osync/osync.csproj

# Build test project
dotnet build osync.Tests/osync.Tests.csproj
```

### 2. Run All Tests

```bash
# Run all enabled tests
dotnet test osync.Tests/osync.Tests.csproj

# Run with detailed output
dotnet test osync.Tests/osync.Tests.csproj --verbosity normal

# Run with minimal output
dotnet test osync.Tests/osync.Tests.csproj --verbosity quiet
```

### 3. View Results

Test output will show:
- Test execution summary
- Pass/fail status for each scenario
- Execution time
- Any error messages

Example output:
```
=== osync Test Suite Starting ===
Test Model: llama3.2:1b
Remote 1: http://localhost:11434
Remote 2: http://localhost:11435
================================

--- Scenario: Display version ---
✓ PASSED: Display version

--- Scenario: Show help ---
✓ PASSED: Show help

--- Scenario: List local models ---
✓ PASSED: List local models

=== osync Test Suite Completed ===

Test Run Successful.
Total tests: 3
     Passed: 3
 Total time: 1.2 Seconds
```

## Configuration

### test-config.json

The test configuration file controls all test parameters:

```json
{
  "TestConfiguration": {
    "RegistryModel": "llama3.2:1b",
    "RemoteDestination1": "http://localhost:11434",
    "RemoteDestination2": "http://localhost:11435",
    "TestTimeout": 300000,
    "CleanupAfterTests": true,
    "OsyncExecutablePath": "..\\..\\..\\..\\osync\\bin\\Debug\\net8.0-windows10.0.22621.0\\osync.exe",
    "VerboseOutput": true,

    "TestCategories": {
      "RunBasic": true,
      "RunRemote": false,
      "RunInteractive": false,
      "RunDestructive": false
    }
  }
}
```

### Configuration Options

| Option | Type | Description | Default |
|--------|------|-------------|---------|
| `RegistryModel` | string | Base model for tests (must be available in registry) | `llama3.2:1b` |
| `RemoteDestination1` | string | First remote Ollama server URL | `http://localhost:11434` |
| `RemoteDestination2` | string | Second remote Ollama server URL | `http://localhost:11435` |
| `TestTimeout` | int | Timeout for each test in milliseconds | `300000` (5 min) |
| `CleanupAfterTests` | bool | Clean up test models after scenarios | `true` |
| `OsyncExecutablePath` | string | Path to osync.exe (auto-detected if null) | Auto-detected |
| `VerboseOutput` | bool | Show detailed command output during tests | `true` |

### Test Categories

Control which test categories run:

| Category | Description | Default |
|----------|-------------|---------|
| `RunBasic` | Basic commands (help, version, list) | `true` |
| `RunRemote` | Remote server operations | `false` |
| `RunInteractive` | Interactive mode tests | `false` |
| `RunDestructive` | Tests that modify/delete models | `false` |

### Overriding Configuration

#### Via Environment Variables

Prefix settings with `OSYNC_TEST_`:

```bash
# Windows PowerShell
$env:OSYNC_TEST_TestConfiguration__VerboseOutput = "false"
$env:OSYNC_TEST_TestConfiguration__TestTimeout = "600000"
dotnet test osync.Tests/osync.Tests.csproj

# Linux/macOS
export OSYNC_TEST_TestConfiguration__VerboseOutput=false
export OSYNC_TEST_TestConfiguration__TestTimeout=600000
dotnet test osync.Tests/osync.Tests.csproj
```

#### Via Command Line

Pass arguments directly:

```bash
dotnet test osync.Tests/osync.Tests.csproj -- TestConfiguration:VerboseOutput=false
```

## Running Tests

### Run Specific Tests by Filter

#### By Category Tag

```bash
# Run only basic tests
dotnet test osync.Tests/osync.Tests.csproj --filter "Category=basic"

# Run version tests
dotnet test osync.Tests/osync.Tests.csproj --filter "Category=version"

# Run list tests
dotnet test osync.Tests/osync.Tests.csproj --filter "Category=list"
```

#### By Test Name

```bash
# Run specific scenario
dotnet test osync.Tests/osync.Tests.csproj --filter "DisplayName~Display version"

# Run all help-related tests
dotnet test osync.Tests/osync.Tests.csproj --filter "DisplayName~help"
```

### Run with Different Verbosity Levels

```bash
# Quiet (minimal output)
dotnet test osync.Tests/osync.Tests.csproj --verbosity quiet

# Minimal
dotnet test osync.Tests/osync.Tests.csproj --verbosity minimal

# Normal (recommended)
dotnet test osync.Tests/osync.Tests.csproj --verbosity normal

# Detailed
dotnet test osync.Tests/osync.Tests.csproj --verbosity detailed

# Diagnostic (maximum detail)
dotnet test osync.Tests/osync.Tests.csproj --verbosity diagnostic
```

### Generate Test Reports

#### Generate Results File

```bash
# Generate TRX report
dotnet test osync.Tests/osync.Tests.csproj --logger "trx;LogFileName=test-results.trx"

# Generate HTML report (requires ReportGenerator)
dotnet test osync.Tests/osync.Tests.csproj --logger "html;LogFileName=test-results.html"
```

#### Code Coverage

```bash
# Run tests with coverage
dotnet test osync.Tests/osync.Tests.csproj --collect:"XPlat Code Coverage"

# Generate coverage report (requires reportgenerator tool)
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report -reporttypes:Html
```

## Test Categories

### @basic Tests

Non-destructive tests that verify basic functionality:
- Version display
- Help output
- List models
- Show model information

**Enable in config:**
```json
"TestCategories": {
  "RunBasic": true
}
```

### @remote Tests

Tests requiring remote Ollama servers:
- Remote list
- Remote upload
- Remote show
- Remote delete

**Enable in config:**
```json
"TestCategories": {
  "RunRemote": true,
  "RemoteDestination1": "http://your-server:11434"
}
```

### @interactive Tests

Tests for interactive mode features:
- Tab completion
- REPL mode
- Command history
- Clear command

**Enable in config:**
```json
"TestCategories": {
  "RunInteractive": true
}
```

### @destructive Tests

Tests that modify or delete models:
- Copy operations
- Rename operations
- Delete operations
- Update operations

**Enable in config:**
```json
"TestCategories": {
  "RunDestructive": true
}
```

### @chat Tests (NEW in v1.1.5)

Tests for interactive chat functionality:
- Model preloading
- Process status display
- Chat streaming performance
- Keyboard shortcuts
- Multiline input
- Command history
- Session save/load
- Runtime parameters

**Key test scenarios:**
- Automatic model preloading before first input
- Process status table showing NAME, ID, SIZE, VRAM, CONTEXT, UNTIL
- Fast streaming on remote servers (no buffering delays)
- Keyboard shortcuts (Ctrl+D, Ctrl+C, Up/Down arrows)
- Multiline input with triple quotes (`"""`)
- Performance statistics tracking
- Session persistence

**Enable in config:**
```json
"TestCategories": {
  "RunChat": true
}
```

## Writing New Tests

### 1. Create a Feature File

Create a new `.feature` file in `osync.Tests/Features/`:

```gherkin
Feature: Copy Commands
  As an osync user
  I want to copy models
  So that I can manage model versions

  @basic @copy
  Scenario: Copy local model to new name
    Given the model "{model}" exists locally
    When I run osync with arguments "copy {model} test-copy:latest"
    Then the command should succeed
    And the output should contain "copied successfully"
    And the model "test-copy:latest" should exist locally

  @remote @copy
  Scenario: Copy model to remote server
    Given the model "{model}" exists locally
    When I run osync with arguments "copy {model} {remote1}/{model}"
    Then the command should succeed
    And the output should contain "uploaded successfully"
```

### 2. Implement Step Definitions

Add step implementations in `osync.Tests/StepDefinitions/`:

```csharp
[Binding]
public class CopySteps
{
    private readonly TestContext _context;
    private readonly OsyncRunner _runner;

    public CopySteps(TestContext context, OsyncRunner runner)
    {
        _context = context;
        _runner = runner;
    }

    [Given(@"the model ""(.*)"" exists locally")]
    public async Task GivenTheModelExistsLocally(string modelName)
    {
        var resolved = _context.ResolveVariables(modelName);
        var result = await _runner.RunAsync($"ls {resolved}");
        result.IsSuccess.Should().BeTrue($"model {resolved} should exist");
    }

    [Then(@"the model ""(.*)"" should exist locally")]
    public async Task ThenTheModelShouldExistLocally(string modelName)
    {
        var resolved = _context.ResolveVariables(modelName);
        var result = await _runner.RunAsync($"ls {resolved}");
        result.IsSuccess.Should().BeTrue();
        result.Output.Should().Contain(resolved);
    }
}
```

### 3. Run Your New Tests

```bash
# Run all copy tests
dotnet test osync.Tests/osync.Tests.csproj --filter "Category=copy"
```

### Common Step Patterns

The test suite includes reusable steps in `CommonSteps.cs`:

```gherkin
When I run osync with arguments "ls"
Then the command should succeed
Then the command should fail
Then the output should contain "text"
Then the output should not contain "text"
Then the execution time should be less than 10 seconds
```

### Variable Substitution

Use variables in feature files that are replaced at runtime:

| Variable | Replaced With | Example |
|----------|---------------|---------|
| `{model}` | RegistryModel | `llama3.2:1b` |
| `{remote1}` | RemoteDestination1 | `http://localhost:11434` |
| `{remote2}` | RemoteDestination2 | `http://localhost:11435` |

## Test Infrastructure

### OsyncRunner

Core class for executing osync commands:

```csharp
public class OsyncRunner
{
    public async Task<OsyncResult> RunAsync(string arguments, int? timeoutMs = null)
    {
        // Spawns osync process
        // Captures stdout and stderr
        // Returns structured result
    }
}
```

### OsyncResult

Wrapper for command execution results:

```csharp
public class OsyncResult
{
    public int ExitCode { get; set; }
    public string Output { get; set; }
    public string Error { get; set; }
    public TimeSpan Duration { get; set; }
    public string Arguments { get; set; }
    public bool IsSuccess => ExitCode == 0;
}
```

### TestContext

Manages test state and variables:

```csharp
public class TestContext
{
    public OsyncResult? LastResult { get; set; }
    public List<string> CreatedModels { get; }

    public string ResolveVariables(string text) { ... }
    public void AddCreatedModel(string modelName) { ... }
}
```

### Hooks

Test lifecycle management:

```csharp
[BeforeTestRun]    // Runs once before all tests
[BeforeScenario]   // Runs before each scenario
[AfterScenario]    // Runs after each scenario (cleanup)
[AfterTestRun]     // Runs once after all tests
```

## Troubleshooting

### Test Discovery Issues

**Problem:** No tests discovered

**Solution:**
```bash
# Rebuild the test project
dotnet clean osync.Tests/osync.Tests.csproj
dotnet build osync.Tests/osync.Tests.csproj

# Check SpecFlow generation
# Feature files should generate .feature.cs files
```

### Executable Not Found

**Problem:** `Cannot start process because a file name has not been provided`

**Solution:**
1. Verify osync is built:
   ```bash
   dotnet build osync/osync.csproj
   ```

2. Check `OsyncExecutablePath` in `test-config.json`
3. Use absolute path if needed:
   ```json
   "OsyncExecutablePath": "C:\\Users\\YourUser\\source\\repos\\osync\\osync\\bin\\Debug\\net8.0-windows10.0.22621.0\\osync.exe"
   ```

### Tests Timeout

**Problem:** Tests timeout after 5 minutes

**Solution:**
```json
{
  "TestConfiguration": {
    "TestTimeout": 600000  // Increase to 10 minutes
  }
}
```

### Target Framework Mismatch

**Problem:** `Project osync is not compatible`

**Solution:** Ensure test project targets same framework as osync:
```xml
<TargetFramework>net8.0-windows10.0.22621.0</TargetFramework>
```

### Remote Tests Failing

**Problem:** Remote tests fail with connection errors

**Solution:**
1. Verify remote Ollama is running:
   ```bash
   curl http://your-server:11434/api/version
   ```

2. Check firewall settings
3. Update remote URLs in `test-config.json`
4. Disable remote tests if not needed:
   ```json
   "RunRemote": false
   ```

### Verbose Output Not Showing

**Problem:** Can't see command output during test runs

**Solution:**
```json
{
  "TestConfiguration": {
    "VerboseOutput": true
  }
}
```

And run with normal verbosity:
```bash
dotnet test osync.Tests/osync.Tests.csproj --verbosity normal
```

### Clean Test Environment

Reset test environment:

```bash
# Remove test models (if any created)
ollama rm test-*

# Clean and rebuild
dotnet clean osync.Tests/osync.Tests.csproj
dotnet build osync.Tests/osync.Tests.csproj

# Run tests
dotnet test osync.Tests/osync.Tests.csproj
```

## CI/CD Integration

### GitHub Actions Example

```yaml
name: Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: windows-latest

    steps:
    - uses: actions/checkout@v3

    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: 8.0.x

    - name: Build osync
      run: dotnet build osync/osync.csproj

    - name: Build tests
      run: dotnet build osync.Tests/osync.Tests.csproj

    - name: Run basic tests
      run: dotnet test osync.Tests/osync.Tests.csproj --filter "Category=basic" --verbosity normal

    - name: Upload test results
      uses: actions/upload-artifact@v3
      if: always()
      with:
        name: test-results
        path: '**/TestResults/*.trx'
```

### Azure DevOps Example

```yaml
trigger:
  - main

pool:
  vmImage: 'windows-latest'

steps:
- task: UseDotNet@2
  inputs:
    version: '8.0.x'

- task: DotNetCoreCLI@2
  displayName: 'Build osync'
  inputs:
    command: 'build'
    projects: 'osync/osync.csproj'

- task: DotNetCoreCLI@2
  displayName: 'Build tests'
  inputs:
    command: 'build'
    projects: 'osync.Tests/osync.Tests.csproj'

- task: DotNetCoreCLI@2
  displayName: 'Run tests'
  inputs:
    command: 'test'
    projects: 'osync.Tests/osync.Tests.csproj'
    arguments: '--filter "Category=basic" --logger trx'

- task: PublishTestResults@2
  condition: always()
  inputs:
    testResultsFormat: 'VSTest'
    testResultsFiles: '**/*.trx'
```

## Best Practices

### 1. Test Organization

- Group related scenarios in feature files
- Use descriptive scenario names
- Tag scenarios appropriately (@basic, @remote, etc.)

### 2. Test Data

- Use small models for faster tests (e.g., llama3.2:1b)
- Clean up created models in AfterScenario hooks
- Use unique names for test models to avoid conflicts

### 3. Assertions

- Use FluentAssertions for readable assertions
- Check both exit code and output content
- Verify error messages for failure scenarios

### 4. Performance

- Run basic tests frequently during development
- Run remote/destructive tests less frequently
- Use parallel execution when tests are independent

### 5. Maintenance

- Keep test-config.json values sensible for local development
- Document any special setup requirements
- Update tests when adding new features

## Manage Command Testing (v1.1.6)

The `osync manage` command provides an interactive Terminal User Interface (TUI) for model management. Testing this command requires a combination of automated and manual testing approaches.

### Automated Testing Limitations

The manage command uses Terminal.Gui which creates a full-screen interactive interface. Automated testing of TUI applications has inherent challenges:
- Keyboard input simulation requires UI automation frameworks
- Screen rendering validation is complex
- Interactive workflows are difficult to assert programmatically

### Recommended Testing Approach

#### 1. Component Testing

Test individual operations that the manage command uses internally:

```gherkin
Feature: Manage Command Components
  Background:
    Given Ollama is running
    And test model "llama3.2:1b" exists

  Scenario: List models for manage view
    When I execute "osync ls"
    Then the output should contain "llama3.2:1b"
    And the exit code should be 0

  Scenario: Copy operation from manage
    When I execute "osync cp llama3.2:1b llama3.2:1b-backup"
    Then the output should contain "successfully"
    And model "llama3.2:1b-backup" should exist

  Scenario: Delete operation from manage
    Given test model "test-model-to-delete" exists
    When I execute "osync rm test-model-to-delete"
    Then the output should contain "deleted"
    And model "test-model-to-delete" should not exist

  Scenario: Update operation from manage
    When I execute "osync update llama3.2:1b"
    Then the output should contain "updated" or "already up to date"
    And the exit code should be 0

  Scenario: Rename operation from manage
    Given test model "rename-source" exists
    When I execute "osync rename rename-source rename-target"
    Then model "rename-target" should exist
    And model "rename-source" should not exist
```

#### 2. Process Status Testing

Test the ps command which is used by manage (Ctrl+X):

```gherkin
Feature: Process Status Command
  Background:
    Given Ollama is running

  Scenario: Show loaded models
    Given model "llama3.2:1b" is loaded in memory
    When I execute "osync ps"
    Then the output should contain "llama3.2:1b"
    And the output should contain "VRAM USAGE"
    And the output should contain a percentage if model is partially loaded
    And the exit code should be 0

  Scenario: Show percentage for partially loaded model
    Given model "large-model" is partially loaded (50% in VRAM)
    When I execute "osync ps"
    Then the VRAM usage should show "50%"
    And the output format should match CLI ps format

  Scenario: No models loaded
    Given no models are loaded in memory
    When I execute "osync ps"
    Then the output should contain "No models currently loaded"
    And the exit code should be 0
```

#### 3. Load/Unload Testing

Test memory management operations used by manage (Ctrl+L, Ctrl+K):

```gherkin
Feature: Load and Unload Commands
  Background:
    Given Ollama is running
    And test model "llama3.2:1b" exists

  Scenario: Load model into memory
    When I execute "osync load llama3.2:1b"
    Then the output should contain "loaded successfully"
    And "osync ps" should show "llama3.2:1b" as loaded

  Scenario: Unload model from memory
    Given model "llama3.2:1b" is loaded in memory
    When I execute "osync unload llama3.2:1b"
    Then the output should contain "unloaded successfully"
    And "osync ps" should not show "llama3.2:1b"

  Scenario: Load with custom keep-alive
    When I execute "osync load llama3.2:1b --keepalive 30m"
    Then the output should contain "loaded successfully"
```

### Manual Testing Procedures

Since the manage command is a full TUI application, comprehensive manual testing is essential:

#### Test Checklist: Basic Navigation

- [ ] Launch `osync manage` successfully
- [ ] Models list displays with proper formatting
- [ ] Column widths adjust dynamically to terminal size
- [ ] Up/Down arrow keys navigate the list
- [ ] Page Up/Down scroll through models
- [ ] Home/End keys jump to first/last model
- [ ] Selected row is highlighted correctly

#### Test Checklist: Filtering (/)

- [ ] Press `/` to activate filter mode
- [ ] Type "llama" - list updates to show only matching models
- [ ] Filter indicator shows in top bar: `Filter: llama`
- [ ] Continue typing - list updates in real-time
- [ ] Press Esc - filter clears, all models shown
- [ ] Top bar updates to remove filter indicator

#### Test Checklist: Sorting (Ctrl+O)

- [ ] Press Ctrl+O - sort changes to Name-
- [ ] Top bar shows: `Sorting: Name-`
- [ ] Models reorder descending by name
- [ ] Press Ctrl+O again - changes to Size-
- [ ] Top bar shows: `Sorting: Size-`
- [ ] Models reorder by size (largest first)
- [ ] Continue cycling through all 6 sort modes:
  - [ ] Name+, Name-, Size-, Size+, Created-, Created+
- [ ] Selected model stays selected during sort changes

#### Test Checklist: Theme Switching (Ctrl+T)

- [ ] Press Ctrl+T - theme changes to Dark
- [ ] Colors update for all UI elements
- [ ] Continue cycling through all 7 themes:
  - [ ] Default, Dark, Blue, Solarized, Gruvbox, Nord, Dracula
- [ ] All themes render correctly
- [ ] Text remains readable in all themes

#### Test Checklist: Multi-Selection (Space)

- [ ] Press Space on a model - checkbox shows `[X]`
- [ ] Press Space again - checkbox shows `[ ]`
- [ ] Select 3 models using Space
- [ ] All selected models show `[X]`
- [ ] Navigate away and back - selections persist

#### Test Checklist: Copy Operation (Ctrl+C)

**Single Model Copy (Local):**
- [ ] Select a model
- [ ] Press Ctrl+C
- [ ] Dialog appears with destination field
- [ ] Enter new model name
- [ ] Press Enter (or click Copy button)
- [ ] Operation completes successfully
- [ ] Returns to manage view
- [ ] Cursor is on the same model

**Single Model Copy (Remote):**
- [ ] Select a model
- [ ] Press Ctrl+C
- [ ] Enter remote server URL
- [ ] Operation shows progress
- [ ] Returns to manage view after completion

**Batch Copy:**
- [ ] Select 3 models with Space
- [ ] Press Ctrl+C
- [ ] Enter remote server URL (required for batch)
- [ ] All 3 models copy with progress
- [ ] Incremental upload skips existing layers
- [ ] Returns to manage view
- [ ] Cursor returns to first selected model

#### Test Checklist: Rename Operation (Ctrl+M)

- [ ] Select a model
- [ ] Press Ctrl+M
- [ ] Dialog appears with new name field
- [ ] Enter new name
- [ ] Press Enter
- [ ] Model renames successfully
- [ ] Cursor stays on renamed model
- [ ] Model appears with new name in list

#### Test Checklist: Run/Chat Operation (Ctrl+R)

- [ ] Select a model
- [ ] Press Ctrl+R
- [ ] TUI exits, console appears
- [ ] Model preloads into memory
- [ ] Shows loading status
- [ ] Chat prompt appears
- [ ] Type a message - model responds
- [ ] Type `/bye` - exits chat
- [ ] Returns to manage TUI
- [ ] Same model is selected

#### Test Checklist: Show Operation (Ctrl+S)

- [ ] Select a model
- [ ] Press Ctrl+S
- [ ] TUI exits, console appears
- [ ] Model information displays
- [ ] Shows metadata, parameters, configuration
- [ ] Press any key
- [ ] Returns to manage TUI
- [ ] Same model is selected

#### Test Checklist: Delete Operation (Ctrl+D)

**Single Delete:**
- [ ] Select a model
- [ ] Press Ctrl+D
- [ ] Confirmation dialog appears
- [ ] Confirm deletion
- [ ] Model deletes successfully
- [ ] Cursor moves to next model (or previous if last)

**Batch Delete:**
- [ ] Select 3 models with Space
- [ ] Press Ctrl+D
- [ ] Confirmation shows count (3 models)
- [ ] Confirm deletion
- [ ] All 3 models delete

#### Test Checklist: Update Operation (Ctrl+U)

**Single Update:**
- [ ] Select a model
- [ ] Press Ctrl+U
- [ ] TUI exits, console appears
- [ ] Update progress displays
- [ ] Shows "updated successfully" or "already up to date"
- [ ] Press any key
- [ ] Returns to manage TUI
- [ ] Same model is selected

**Batch Update:**
- [ ] Select 3 models with Space
- [ ] Press Ctrl+U
- [ ] TUI exits, console appears
- [ ] Updates all 3 models sequentially
- [ ] Shows status for each model
- [ ] Press any key to return
- [ ] Returns to manage TUI

#### Test Checklist: Pull Operation (Ctrl+P)

- [ ] Press Ctrl+P (no model selection needed)
- [ ] Dialog appears with model name field
- [ ] Enter model name (e.g., "qwen2:0.5b")
- [ ] Press Enter
- [ ] Validates model exists on ollama.com
- [ ] If valid: TUI exits, shows pull progress
- [ ] If invalid: Error dialog appears
- [ ] After pull: Returns to manage TUI
- [ ] New model appears in list

#### Test Checklist: Load Operation (Ctrl+L)

- [ ] Select a model
- [ ] Press Ctrl+L
- [ ] Model loads into memory
- [ ] Success message displays
- [ ] Use Ctrl+X to verify model is loaded

#### Test Checklist: Unload Operation (Ctrl+K)

- [ ] Select a loaded model
- [ ] Press Ctrl+K
- [ ] Model unloads from memory
- [ ] Success message displays
- [ ] Use Ctrl+X to verify model is unloaded

#### Test Checklist: Process Status (Ctrl+X)

- [ ] Load a model (Ctrl+L)
- [ ] Press Ctrl+X
- [ ] Dialog shows loaded models in table format
- [ ] Format matches CLI `osync ps` output
- [ ] Shows: NAME, ID, SIZE, VRAM USAGE, CONTEXT, UNTIL
- [ ] VRAM percentage displays for partially loaded models
- [ ] Example: "1.33 GB (63%)"
- [ ] Press Close or Esc to dismiss

#### Test Checklist: Exit (Ctrl+Q / Esc)

- [ ] Press Ctrl+Q
- [ ] Confirmation dialog appears
- [ ] Select No - stays in manage
- [ ] Press Ctrl+Q again
- [ ] Select Yes - exits cleanly
- [ ] Same test with Esc key

### Edge Cases and Error Scenarios

#### Test Checklist: Error Handling

- [ ] **Empty model list** - manage displays appropriate message
- [ ] **Network failure** - remote operations show error, don't crash
- [ ] **Invalid destination** - copy shows error dialog
- [ ] **Model already exists** - prevents overwrite, shows error
- [ ] **Terminal too small** - UI adjusts or shows warning
- [ ] **SSH session** - TUI renders correctly
- [ ] **Special characters in model names** - handles correctly
- [ ] **Very long model names** - truncates with "..."
- [ ] **Large model list (100+ models)** - scrolling works
- [ ] **Rapid key presses** - no crashes or duplicate operations

### Performance Testing

#### Test Checklist: Performance

- [ ] List loads quickly with 100+ models (< 1 second)
- [ ] Filtering updates in real-time (< 100ms)
- [ ] Sorting completes instantly (< 200ms)
- [ ] Theme switching is immediate
- [ ] Navigation is responsive
- [ ] Batch operations don't freeze UI
- [ ] Memory usage is reasonable (< 100MB for TUI)

### Integration Testing

Test manage command with different server configurations:

```gherkin
Feature: Manage Command Integration
  Scenario: Manage local server
    When I execute "osync manage"
    Then the TUI should launch successfully
    And models from localhost:11434 should be listed

  Scenario: Manage remote server
    Given remote Ollama server at "http://192.168.0.100:11434"
    When I execute "osync manage http://192.168.0.100:11434"
    Then the TUI should launch successfully
    And models from remote server should be listed

  Scenario: Manage with OLLAMA_HOST environment variable
    Given OLLAMA_HOST is set to "http://192.168.0.100:11434"
    When I execute "osync manage"
    Then the TUI should launch successfully
    And models from the environment-specified server should be listed
```

### Testing Recommendations

1. **Automated Tests** - Focus on component operations (ls, cp, rm, update, etc.)
2. **Manual Testing** - Required for TUI interactions, use checklist above
3. **Integration Tests** - Test with local and remote servers
4. **Regression Testing** - Run full manual checklist before each release
5. **User Acceptance Testing** - Have users test common workflows

### Known Testing Challenges

1. **Terminal.Gui Testing** - No built-in test framework for UI automation
2. **Keyboard Input** - Difficult to simulate programmatically
3. **Visual Verification** - Screen rendering cannot be easily asserted
4. **Timing Issues** - Async operations may require manual observation

### Future Testing Improvements

- Consider UI automation framework (e.g., Selenium-like for TUI)
- Record/playback for keyboard interactions
- Screenshot comparison for visual regression testing
- Mock Terminal.Gui components for unit testing

## Additional Resources

- [xUnit Documentation](https://xunit.net/)
- [SpecFlow Documentation](https://docs.specflow.org/)
- [FluentAssertions Documentation](https://fluentassertions.com/)
- [Terminal.Gui Documentation](https://gui-cs.github.io/Terminal.Gui/)
- [osync README](README.md)
- [osync GitHub Issues](https://github.com/mann1x/osync/issues)

## Support

For test-related issues:
1. Check this documentation
2. Review existing tests in `osync.Tests/Features/`
3. Open an issue: [GitHub Issues](https://github.com/mann1x/osync/issues/new?labels=testing)
