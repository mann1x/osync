![GitHub all releases](https://img.shields.io/github/downloads/mann1x/osync/total)
![GitHub release (latest by date)](https://img.shields.io/github/v/release/mann1x/osync)
![GitHub contributors](https://img.shields.io/github/contributors/mann1x/osync)
![GitHub Repo stars](https://img.shields.io/github/stars/mann1x/osync?style=social)

<div align="center">
  <h1>osync</h1>
  <br />
  <br />
  <a href="https://github.com/mann1x/osync/issues/new?assignees=&labels=bug&template=01_BUG_REPORT.md&title=bug%3A+">Report a Bug</a>
  ·
  <a href="https://github.com/mann1x/osync/issues/new?assignees=&labels=enhancement&template=02_FEATURE_REQUEST.md&title=feat%3A+">Request a Feature</a>
  .
  <a href="https://github.com/mann1x/osync/issues/new?assignees=&labels=question&template=04_SUPPORT_QUESTION.md&title=support%3A+">Ask a Question</a>
</div>

<div align="center">
<br />

[![Project license](https://img.shields.io/github/license/mann1x/osync.svg?style=flat-square)](LICENSE)

[![Pull Requests welcome](https://img.shields.io/badge/PRs-welcome-ff69b4.svg?style=flat-square)](https://github.com/mann1x/osync/issues?q=is%3Aissue+is%3Aopen+label%3A%22help+wanted%22)
[![code with love by mann1x](https://img.shields.io/badge/%3C%2F%3E%20with%20%E2%99%A5%20by-mann1x-ff1414.svg?style=flat-square)](https://github.com/mann1x)

</div>



---

## About

**osync** is a powerful command-line tool for managing Ollama models across local and remote servers.

### Key Features

- 🖥️ **Interactive TUI** - Full-screen terminal interface with keyboard shortcuts for all operations
- 🚀 **Fast Model Transfer** - Copy models between local and remote Ollama servers with high-speed uploads
- 🔄 **Remote-to-Remote Copy** - Transfer models directly between remote servers with memory-buffered streaming
- 📋 **Smart Model Management** - List, copy, rename, delete, pull, and show models with pattern matching support
- 📥 **Registry Pull** - Download models from Ollama registry and HuggingFace with automatic tag extraction
- 📄 **Model Information** - Display detailed model information including license, parameters, and templates
- 🔄 **Incremental Uploads** - Skips already transferred layers, saving bandwidth and time
- 📊 **Progress Tracking** - Real-time progress bars with transfer speed indicators
- 🏷️ **Auto-tagging** - Automatically applies `:latest` tag when not specified
- 🔍 **Advanced Listing** - Sort models by name, size, or modification time with visual indicators
- 🌐 **Multi-registry Support** - Works with registry.ollama.ai, hf.co, and custom registries
- 💾 **Offline Deployment** - Perfect for air-gapped servers and isolated networks
- 🎯 **Wildcard Patterns** - Use `*` wildcards for batch operations
- ⚡ **Bandwidth Control** - Throttle upload speeds and configure memory buffer size
- 🎨 **Theme Support** - Choose from 7 built-in color themes
- 💬 **Interactive Chat** - Chat with models directly from the CLI
- 🧠 **Memory Management** - Load/unload models from VRAM with process status monitoring

### Built With

> **[C# .NET 8]**

## Getting Started

### Prerequisites

> **[Windows/Linux/MacOS]**

> **[Arm64/x64/Mac]**

### Installation

#### Quick Install (Recommended)

Download the latest binary for your platform, then run:

```bash
# Windows
osync.exe install

# Linux/macOS
./osync install
```

This will:
1. Install osync to your user directory (`~/.osync` on Windows, `~/.local/bin` on Linux/macOS)
2. Add osync to your PATH automatically
3. Optionally configure shell completion (PowerShell 6.0+ on Windows, Bash on Linux/macOS)
4. Restart your terminal to use `osync` from anywhere

#### Manual Installation

**Download Binary:**
- Download the latest release from [GitHub Releases](https://github.com/mann1x/osync/releases)
- Extract to a directory of your choice
- Add the directory to your PATH manually

**Build from Source:**
1. Clone the repository
2. Open with Visual Studio 2022 or use `dotnet build`
3. Publish: `dotnet publish -c Release`
4. Run `osync install` from the published output directory

## Usage

### Quick Start

```bash
# Interactive TUI for model management (recommended)
osync manage

# Pull a model from registry
osync pull llama3

# Show model information
osync show llama3

# Chat with a model
osync run llama3

# Copy local model to remote server
osync cp llama3 http://192.168.100.100:11434

# List all local models
osync ls

# Interactive REPL mode with tab completion
osync
```

### Commands

#### Copy (`cp`)

Copy models locally, to remote servers, or between remote servers.

```bash
# Local copy (create backup)
osync cp llama3 my-backup-llama3
osync cp llama3:70b llama3:backup-v1

# Local to remote (upload to server)
osync cp llama3 http://192.168.0.100:11434
osync cp qwen2:7b http://192.168.0.100:11434

# Remote to remote (transfer between servers)
osync cp http://192.168.0.100:11434/qwen2:7b http://192.168.0.200:11434/qwen2:latest
osync cp http://server1:11434/llama3 http://server2:11434/llama3-copy

# With custom memory buffer size (default: 512MB)
osync cp http://server1:11434/llama3 http://server2:11434/llama3 -BufferSize 256MB
osync cp http://server1:11434/qwen2 http://server2:11434/qwen2 -BufferSize 1GB

# With bandwidth throttling
osync cp llama3 http://192.168.0.100:11434 -bt 50MB
```

**Features:**
- Automatic `:latest` tag when not specified
- Prevents overwriting existing models
- Skips already uploaded layers
- Real-time progress with transfer speed
- Memory-buffered streaming for remote-to-remote transfers
- Simultaneous download and upload with backpressure control

**Remote-to-Remote Limitations:**
- ⚠️ The model must exist in the Ollama registry (registry.ollama.ai)
- ⚠️ The registry must be accessible from the host running osync
- ⚠️ Locally created models cannot be copied between remote servers
- ⚠️ Only models originally pulled from the registry can be transferred remotely

#### List (`ls`)

List models with filtering and sorting options.

```bash
# List all local models
osync ls

# List models matching pattern
osync ls "llama*"
osync ls "*:7b"
osync ls "mannix/*"

# List remote models
osync ls http://192.168.0.100:11434
osync ls "qwen*" http://192.168.0.100:11434

# Sort by size (descending)
osync ls --size

# Sort by size (ascending)
osync ls --sizeasc

# Sort by modified time (newest first)
osync ls --time

# Sort by modified time (oldest first)
osync ls --timeasc
```

**Output:**
```
NAME                          ID              SIZE      MODIFIED
llama3:latest                 365c0bd3c000    5 GB      2 months ago
qwen2:7b                      648f809ced2b    4 GB      1 years ago
mistral:latest                2ae6f6dd7a3d    4 GB      1 years ago
```

#### Rename (`rename`, `mv`, `ren`)

Rename models safely by copying and deleting the original.

```bash
# Rename with implicit :latest tag
osync rename llama3 my-llama3
osync mv llama3 my-llama3

# Rename with explicit tags
osync ren llama3:7b my-custom-llama:v1

# Create versioned backup
osync mv qwen2 qwen2:backup-20241218
```

**Features:**
- Three-step process: copy → verify → delete
- Checks if destination already exists
- Only deletes original after successful verification

#### Remove (`rm`, `delete`, `del`)

Delete models with pattern matching.

```bash
# Delete specific model
osync rm tinyllama
osync rm llama3:7b

# Delete with pattern
osync rm "test-*"
osync rm "*:backup"

# Delete from remote server
osync rm "old-model*" http://192.168.0.100:11434
```

**Features:**
- Automatic `:latest` tag fallback
- Wildcard pattern support
- Confirmation before deletion
- Works on local and remote servers

#### Update (`update`)

Update models to their latest versions locally or on remote servers.

```bash
# Update all local models
osync update
osync update "*"

# Update specific model
osync update llama3
osync update llama3:latest

# Update models matching pattern
osync update "llama*"
osync update "*:7b"
osync update "hf.co/unsloth/*"

# Update all models on remote server
osync update http://192.168.0.100:11434
osync update "*" http://192.168.0.100:11434

# Update specific models on remote server
osync update "llama*" http://192.168.0.100:11434
```

**Features:**
- Updates models to latest available versions
- Shows real-time progress during updates
- Indicates whether each model was updated or already up to date
- Supports wildcard pattern matching
- Works on both local and remote servers
- Default pattern is `*` (all models) when not specified

**Output:**
```
Updating 2 model(s)...

Updating 'llama3:latest'...
pulling manifest
pulling 6a0746a1ec1a... 100% ▕████████████████▏ 4.7 GB
✓ 'llama3:latest' updated successfully

Updating 'qwen2:7b'...
✓ 'qwen2:7b' is already up to date
```

#### Pull (`pull`)

Pull (download) models from the Ollama registry locally or to remote servers. Works like `ollama pull` with additional support for HuggingFace model URLs.

```bash
# Pull model locally (adds :latest if no tag specified)
osync pull llama3
osync pull llama3:7b

# Pull model to remote server
osync pull llama3 http://192.168.0.100:11434
osync pull qwen2:7b http://192.168.0.100:11434

# Pull from HuggingFace using full URL
osync pull https://huggingface.co/bartowski/Qwen2.5.1-Coder-7B-Instruct-GGUF/blob/main/Qwen2.5.1-Coder-7B-Instruct-IQ2_M.gguf

# Pull from HuggingFace using short format
osync pull hf.co/bartowski/Qwen2.5.1-Coder-7B-Instruct-GGUF:IQ2_M
osync pull hf.co/unsloth/Llama-3.2-1B-Instruct-GGUF:Q4_K_M
```

**Features:**
- Pulls models from Ollama registry (registry.ollama.ai)
- Automatic `:latest` tag when not specified (standard models only)
- Supports both local and remote server pulls
- HuggingFace URL conversion and integration
- Extracts quantization tags (IQ2_M, Q4_K_M, etc.) from GGUF filenames
- Real-time progress display during downloads
- Works with standard models, HuggingFace models, and custom registries

**HuggingFace Integration:**
- Converts full HuggingFace URLs to ollama-compatible format
- Automatically extracts quantization identifier from filename
- Supports various GGUF quantization formats (IQ, Q, F, FP, BF series)
- Example conversion:
  - Input: `https://huggingface.co/bartowski/Qwen2.5.1-Coder-7B-Instruct-GGUF/blob/main/Qwen2.5.1-Coder-7B-Instruct-IQ2_M.gguf`
  - Output: `hf.co/bartowski/Qwen2.5.1-Coder-7B-Instruct-GGUF:IQ2_M`

**Output:**
```
Pulling 'llama3:latest' locally...
pulling manifest
pulling 6a0746a1ec1a... 100% ▕████████████████▏ 4.7 GB
verifying sha256 digest
writing manifest
removing any unused layers
✓ Successfully pulled 'llama3:latest'
```

#### Show (`show`)

Show detailed information about a model locally or on a remote server. Works like `ollama show` with support for displaying specific sections.

```bash
# Show model information locally
osync show llama3
osync show llama3:7b

# Show specific sections
osync show llama3 --license       # Show license only
osync show llama3 --modelfile     # Show Modelfile only
osync show llama3 --parameters    # Show parameters only
osync show llama3 --system        # Show system message only
osync show llama3 --template      # Show template only
osync show llama3 --verbose       # Show all details

# Show model info from remote server
osync show llama3 http://192.168.0.100:11434
osync show qwen2:7b http://192.168.0.100:11434 --verbose
osync show llama3 http://192.168.0.100:11434 --modelfile
```

**Features:**
- Display full model information or specific sections
- Automatic `:latest` tag when not specified
- Works on both local and remote servers
- Multiple display options matching ollama's show command
- Verbose mode for comprehensive details

**Available Flags:**
- `--license` - Show the model's license
- `--modelfile` - Show the Modelfile used to create the model
- `--parameters` - Show the model's parameters
- `--system` - Show the system message
- `--template` - Show the prompt template
- `-v`, `--verbose` - Show detailed information including all sections

**Output:**
```
# Default output (shows Modelfile)
Model: llama3:latest
Modelfile:
FROM llama3:latest
TEMPLATE """{{ .System }}
{{ .Prompt }}"""
PARAMETER stop "<|start_header_id|>"
PARAMETER stop "<|end_header_id|>"
PARAMETER stop "<|eot_id|>"
```

#### Run/Chat (`run`, `chat`)

Interactive chat with a model. Automatically preloads the model into memory and displays status information before starting the chat session.

```bash
# Chat with model locally
osync run llama3
osync chat mistral-nemo

# Chat with model on remote server
osync run llama3 -d http://192.168.0.100:11434
osync run mistral-nemo -d http://192.168.0.100:11434

# Chat with additional options
osync run llama3 --verbose                    # Show performance stats
osync run llama3 --no-wordwrap               # Disable word wrapping
osync run llama3 --format json               # Request JSON output
osync run llama3 --think                     # Enable thinking mode
osync run llama3 --keepalive 10m             # Keep model loaded for 10 minutes
```

**Features:**
- **Automatic Model Preloading** - Loads model into memory before first input
- **Process Status Display** - Shows table of all loaded models with details:
  - NAME: Model name
  - ID: First 12 characters of model digest (Docker-style)
  - SIZE: Disk size combined with parameter count (e.g., "4.54 GB (8.0B)")
  - VRAM USAGE: Memory allocated in VRAM
  - CONTEXT: Context window size (e.g., 4096)
  - UNTIL: Human-readable expiration time (e.g., "2 minutes from now")
- **Fast Streaming** - Optimized for both local and remote servers
- **Command History** - Navigate with Up/Down arrows
- **Keyboard Shortcuts**:
  - `Ctrl+D` on empty line - Exit chat
  - `Ctrl+C` - Cancel current generation
  - `Ctrl+A` - Move to beginning of line
  - `Ctrl+E` - Move to end of line
  - `Ctrl+K` - Delete from cursor to end
  - `Ctrl+U` - Delete entire line
  - `Ctrl+W` - Delete previous word
  - `Ctrl+L` - Clear screen
  - `Up/Down` - Navigate command history
- **Multiline Input** - Use triple quotes for multiline messages:
  ```
  >>> """This is a
  ... multiline
  ... message"""
  ```
- **Session Management**:
  - `/save <filename>` - Save chat session
  - `/load <filename>` - Load chat session
  - `/clear` - Clear conversation history
  - `/stats` - Show performance statistics
  - `/bye` or `/exit` - Exit chat
- **Runtime Options**:
  - `/set verbose` - Enable verbose mode
  - `/set wordwrap` - Enable word wrapping
  - `/set format <format>` - Set output format (json, etc.)
  - `/set system` - Set system message
  - `/set parameter <name> <value>` - Set model parameters (temperature, top_p, etc.)
  - `/show` - Display current settings

**Available Flags:**
- `-d`, `--destination` - Remote Ollama server URL
- `-v`, `--verbose` - Show performance statistics after each response
- `--no-wordwrap` - Disable automatic word wrapping
- `--format <format>` - Request specific output format (e.g., json)
- `--keepalive <duration>` - Keep model loaded for specified duration (e.g., 5m, 1h)
- `--think` - Enable thinking mode (can also specify: high, medium, low)
- `--hide-thinking` - Hide thinking process in output
- `--truncate` - Truncate context when it exceeds model limits
- `--dimensions <size>` - Set embedding dimensions

**Example Session:**
```
>>> Connecting to mistral-nemo:latest...
>>> Loading model into memory...

Loaded Models:
---------------------------------------------------------------------------------------------------------------------------------------
NAME                           ID              SIZE                      VRAM USAGE      CONTEXT    UNTIL
---------------------------------------------------------------------------------------------------------------------------------------
mistral-nemo:latest            994f3b8b7801    6.85 GB (12.2B)           0 B             4096       About a minute from now
---------------------------------------------------------------------------------------------------------------------------------------

>>> Type /? for help or /bye to exit

>>> What is the capital of France?
The capital of France is Paris.

>>> /stats
=== Performance Statistics ===
Total requests: 1

Total Duration (ms):
  Min: 1234.56
  Avg: 1234.56
  Max: 1234.56

Tokens/Second:
  Min: 45.32
  Avg: 45.32
  Max: 45.32

>>> /bye
```

#### Process Status (`ps`)

Display information about models currently loaded in memory on local or remote Ollama servers.

```bash
# Show locally loaded models
osync ps

# Show loaded models on remote server
osync ps -d http://192.168.0.100:11434
osync ps http://192.168.0.100:11434
```

**Features:**
- Shows all models currently loaded in memory
- Displays the same information as the run/chat command preload table
- Works with both local and remote servers
- No model preloading - shows current state only

**Output:**
```
Loaded Models:
---------------------------------------------------------------------------------------------------------------------------------------
NAME                           ID              SIZE                      VRAM USAGE      CONTEXT    UNTIL
---------------------------------------------------------------------------------------------------------------------------------------
llama3:latest                  365c0bd3c000    4.54 GB (8.0B)            0 B             4096       2 minutes from now
mistral-nemo:latest            994f3b8b7801    6.85 GB (12.2B)           0 B             4096       About a minute from now
---------------------------------------------------------------------------------------------------------------------------------------
```

**Table Columns:**
- **NAME**: Model name with tag
- **ID**: First 12 characters of model digest (Docker-style hash)
- **SIZE**: Disk size and parameter count (e.g., "4.54 GB (8.0B)")
- **VRAM USAGE**: Memory currently allocated in VRAM
- **CONTEXT**: Context window size (number of tokens)
- **UNTIL**: Human-readable time until model is unloaded from memory
  - Examples: "Less than a minute", "2 minutes from now", "About an hour from now"

**Use Cases:**
- Check which models are currently loaded before starting a chat
- Monitor memory usage across models
- See when models will be automatically unloaded
- Verify model loading on remote servers

#### Load (`load`)

Preload a model into memory on local or remote Ollama servers without starting a chat session.

```bash
# Load model locally
osync load llama3
osync load mistral-nemo

# Load model on remote server
osync load llama3 -d http://192.168.0.100:11434
osync load mistral-nemo -d http://192.168.0.100:11434
```

**Features:**
- Preloads model into memory without starting interactive chat
- Automatic `:latest` tag when not specified
- Works with both local and remote servers
- Useful for warming up models before use
- Model stays loaded based on Ollama's `keep_alive` setting

**Use Cases:**
- Preload models before starting workload
- Warm up models on remote servers
- Ensure models are ready for API calls
- Reduce first-request latency

**Example:**
```bash
# Load model and check status
osync load llama3
osync ps

# Output:
# Loading model 'llama3:latest' into memory...
# ✓ Model 'llama3:latest' loaded successfully
```

#### Unload (`unload`)

Unload a specific model or all models from memory on local or remote Ollama servers.

```bash
# Unload specific model locally
osync unload llama3
osync unload mistral-nemo:latest

# Unload all loaded models locally
osync unload

# Unload specific model on remote server
osync unload llama3 -d http://192.168.0.100:11434

# Unload all models on remote server
osync unload -d http://192.168.0.100:11434
```

**Features:**
- Unload individual models or all models at once
- Automatic `:latest` tag when not specified
- Works with both local and remote servers
- Sets `keep_alive: 0` to immediately unload from memory
- Fetches loaded models list when no model name specified

**Use Cases:**
- Free up VRAM/memory when models are not needed
- Clear memory before loading different models
- Manage memory on servers with limited resources
- Reset model state without restarting Ollama

**Example:**
```bash
# Check loaded models
osync ps
# Output shows: llama3:latest and mistral-nemo:latest

# Unload one model
osync unload llama3
# Output: ✓ Model 'llama3:latest' unloaded successfully

# Unload all remaining models
osync unload
# Output:
# Fetching loaded models...
# Unloading model 'mistral-nemo:latest'...
# ✓ Model 'mistral-nemo:latest' unloaded successfully
#
# Unloaded 1 models
```

####  Manage (`manage`)

Interactive TUI for managing models with keyboard shortcuts.

```bash
# Launch manage interface for local server
osync manage

# Launch manage interface for remote server
osync manage http://192.168.0.100:11434
```

**Features:**
- Full-screen terminal user interface
- Real-time model listing with dynamic column widths
- Multi-selection support for batch operations
- Filtering with live search
- Multiple sort modes (name, size, created date - ascending/descending)
- Theme switching (7 built-in themes)
- Visual status indicators for sorting and filtering

**Keyboard Shortcuts:**
- **Ctrl+C** - Copy model(s) (local or to remote server)
- **Ctrl+M** - Rename model
- **Ctrl+R** - Run/chat with model
- **Ctrl+S** - Show model information
- **Ctrl+D** - Delete model(s)
- **Ctrl+U** - Update model(s)
- **Ctrl+P** - Pull model from registry
- **Ctrl+L** - Load model into memory
- **Ctrl+K** - Unload model from memory
- **Ctrl+X** - Show process status (loaded models)
- **Ctrl+O** - Cycle sort order
- **Ctrl+T** - Cycle theme
- **Ctrl+Q** or **Esc** - Quit (with confirmation)
- **Space** - Toggle model selection
- **/** - Start filtering (type to filter, Esc to clear)
- **Enter** - Execute action in dialogs

**Sort Modes:**
- Name+ (ascending), Name- (descending)
- Size+ (ascending), Size- (descending)
- Created+ (oldest first), Created- (newest first)

**Themes:**
- Default, Dark, Blue, Solarized, Gruvbox, Nord, Dracula

### Options

#### Global Options

- `-h`, `-?` - Show help for any command
- `-bt <value>` - Bandwidth throttling (B, KB, MB, GB per second)
- `-BufferSize <value>` - Memory buffer size for remote-to-remote copy (KB, MB, GB; default: 512MB)

**Examples:**
```bash
# Bandwidth throttling
osync cp llama3 http://server:11434 -bt 75MB    # Limit to 75 MB/s
osync cp qwen2 http://server:11434 -bt 1GB      # Limit to 1 GB/s

# Memory buffer configuration for remote-to-remote
osync cp http://server1:11434/llama3 http://server2:11434/llama3 -BufferSize 256MB
osync cp http://server1:11434/qwen2 http://server2:11434/qwen2 -BufferSize 1GB
```

#### List Options

- `--size` - Sort by size (largest first)
- `--sizeasc` - Sort by size (smallest first)
- `--time` - Sort by modified time (newest first)
- `--timeasc` - Sort by modified time (oldest first)

### Interactive Mode

Run `osync` without arguments to enter interactive mode with:
- Tab completion for model names
- Command history
- Multi-registry support
- Real-time model listing
- Clear screen with `/clear` command

```bash
osync
# Press tab to see available models
# Type command and press enter
> cp llama3 my-backup
> ls "qwen*"
> clear           # Clear the console screen
> exit
```

**Interactive Commands:**
- `clear` - Clear the console screen and reset the display
- `exit` or `quit` - Exit interactive mode
- All regular osync commands (cp, ls, rm, pull, show, update, etc.)

### Shell Completion

osync supports shell completion for commands and model names on all platforms.

**Automatic Installation:**

Shell completion is automatically offered during the `osync install` process. To install it separately or update it:

```bash
# The install command will prompt you to configure shell completion
osync install
```

**Bash (Linux/macOS):**
- Installs completion script to `/etc/bash_completion.d/` or `~/.bashrc`
- Completes commands, model names, and options
- Automatically handles remote server completions via `-d` flag
- Activate with `source ~/.bashrc` or restart terminal
- Unix line endings automatically applied for cross-platform compatibility

**PowerShell (Windows):**
- **Requires PowerShell 6.0 or higher** (PowerShell Core/7+)
- PowerShell Desktop 5.x is not supported (use interactive mode instead)
- Version check performed before installation
- Configures PowerShell profile automatically
- Creates profile if it doesn't exist
- Completes commands, model names, and flags
- May require: `Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser`

**Features:**
- Auto-completes model names from local or remote Ollama installations
- Completes command-specific options and flags
- Works in both interactive and command-line modes
- Supports remote server model completion
- Updates automatically as models change
- osync must be in PATH for completion to work (automatically configured by `install` command)

### Pattern Matching

Use `*` wildcard for flexible pattern matching:

```bash
# Match any model starting with "llama"
osync ls "llama*"

# Match any 7b model
osync ls "*:7b"

# Match models in namespace
osync ls "mannix/*"

# Match HuggingFace models
osync ls "hf.co/*"

# Match any model containing "test"
osync ls "*test*"
```

### Examples

#### Update Models
```bash
# Update all local models to latest versions
osync update

# Update specific models matching pattern
osync update "llama*"

# Update all models on remote server
osync update http://192.168.0.10:11434
```

#### Backup Before Update
```bash
# Create backup before updating
osync cp llama3:latest llama3:backup
osync update llama3
```

#### Deploy to Multiple Servers
```bash
# Upload from local to multiple servers
osync cp llama3 http://192.168.0.10:11434
osync cp llama3 http://192.168.0.11:11434
osync cp llama3 http://192.168.0.12:11434

# Copy between remote servers
osync cp http://192.168.0.10:11434/llama3 http://192.168.0.11:11434/llama3
osync cp http://192.168.0.10:11434/qwen2 http://192.168.0.12:11434/qwen2
```

#### Clean Up Old Models
```bash
# List and remove test models
osync ls "test-*"
osync rm "test-*"

# Remove old backups
osync rm "*:backup"
```

#### Organize Models
```bash
# List all models by size to find space hogs
osync ls --size

# Rename for better organization
osync mv llama3 llama3-8b:prod
osync mv qwen2 qwen2-7b:dev
```

## Known Issues

> None

## Changelog

v1.1.6
- **Manage TUI Command** - Full-screen interactive terminal user interface for model management
  - Real-time model listing with dynamic column widths
  - Multi-selection support for batch operations (copy, update, delete)
  - Live filtering with instant search
  - Six sort modes: Name+/-, Size+/-, Created+/- (with visual status indicator)
  - Seven built-in themes (Default, Dark, Blue, Solarized, Gruvbox, Nord, Dracula)
  - All operations accessible via keyboard shortcuts (Ctrl+C/M/R/S/D/U/P/L/K/X/O/T/Q)
  - Enter key support in all dialogs for faster workflow
  - Smart model selection persistence after operations
  - Batch copy with incremental uploads
  - Console output for long operations (update, pull) with auto-return to TUI
- **Process Status Enhancements** - `ps` command improvements
  - Shows VRAM usage percentage when model is partially loaded (e.g., "1.33 GB (63%)")
  - Consistent tabular format in both CLI and manage TUI
  - Displays model ID, size, context length, and expiration time
- **New `load` Command** - Preload models into memory without starting a chat session
  - Load models locally or on remote servers
  - Automatic `:latest` tag when not specified
  - Useful for warming up models before use
  - Reduces first-request latency by preloading models
- **New `unload` Command** - Unload models from memory to free VRAM
  - Unload specific models or all loaded models at once
  - Works with both local and remote servers
  - Sets `keep_alive: 0` to immediately free memory
  - Automatically fetches and unloads all models when no model name specified
- **Enhanced Tab Completion** - Added shell completion support for load and unload commands
  - PowerShell completion for model names and -d flag
  - Bash completion for model names and -d flag
  - Works in both interactive and command-line modes
- **Cross-Platform Argument Support** - Load and unload commands support flexible argument ordering
  - Arguments work in any order (e.g., `osync load -d http://... model` or `osync load model -d http://...`)
  - Consistent behavior across Windows, Linux, and macOS

v1.1.5
- **New `ps` Command** - Show running models and their status
  - Display all models currently loaded in memory
  - Works with both local and remote Ollama servers
  - Shows NAME, ID, SIZE, VRAM USAGE, CONTEXT, UNTIL in formatted table
  - Same output format as run/chat command preload display
- **Chat Model Preloading** - Models are now automatically loaded into memory before first chat input
  - Sends empty chat request to preload model
  - Displays loaded model status table after preload
  - Shows model name, ID (shortened digest), size, VRAM usage, context length, and expiration time
- **Process Status Display** - New formatted table showing all loaded models via `/api/ps`
  - NAME: Model name with truncation for long names
  - ID: First 12 characters of model digest (Docker-style)
  - SIZE: Disk size combined with parameter count (e.g., "4.54 GB (8.0B)")
  - VRAM USAGE: Memory allocated in VRAM
  - CONTEXT: Context window size (e.g., 4096)
  - UNTIL: Human-readable expiration time (e.g., "2 minutes from now", "About a minute from now")
- **Improved Chat Performance** - Fixed streaming response buffering for remote servers
  - Uses `HttpCompletionOption.ResponseHeadersRead` for immediate streaming
  - Remote chat now responds as fast as local chat
  - No more delays waiting for full response buffering

v1.1.4
- **Fixed Pattern Matching** - Enhanced `:latest` tag handling in list command pattern matching
  - Patterns without tags (e.g., `llama3`) now correctly match models with `:latest` tag
  - Improved wildcard matching consistency across all commands
- **Fixed Linux Argument Order** - Resolved cross-platform argument parsing issue
  - Arguments now work in any order on Linux (e.g., `osync show -d http://... model` and `osync show model -d http://...`)
  - Added automatic argument reordering for PowerArgs compatibility on Linux
  - Maintains backward compatibility with Windows argument handling
- **Fixed REPL Tab Completion** - Enhanced interactive mode model name completion
  - Fixed colon (`:`) handling in model names for tab completion
  - Improved completion when typing model name followed by colon (e.g., `qwen3:` + tab)
  - Better support for model:tag format in interactive mode
- **Fixed Bash Completion** - Improved Bash shell completion for model names with colons
  - Modified `COMP_WORDBREAKS` to handle colon as part of model names
  - Better completion for model:tag format in Bash shell
- **Fixed PowerShell Completion** - Cleaned up PowerShell completion script
  - Removed unnecessary success message when loading completion
  - Improved version line filtering in model listing
- **Improved Error Messages** - Simplified error output for better user experience
  - Errors now show concise hint to use `-h` or `-?` for help
  - Replaced verbose usage output with brief help reference
  - Users can now see actual error messages without clutter

v1.1.3
- **Install Command** - New unified installation command replacing `install-completion`
- **Automatic Installation** - Installs osync to user directory (`~/.osync` on Windows, `~/.local/bin` on Linux/macOS)
- **PATH Management** - Automatically adds osync directory to PATH on all platforms
  - Windows: Updates user PATH environment variable with broadcast notification
  - Linux: Adds export to `~/.bashrc`
  - macOS: Adds export to `~/.zshrc` (or `~/.bash_profile`)
- **Complete Application Copy** - Copies all required files (exe, dlls, dependencies) for proper installation
- **Shell Completion Integration** - Optionally configures shell completion during installation
- **PowerShell Version Check** - Validates PowerShell version before offering completion (requires 6.0+)
- **Improved Completion Scripts** - Assumes osync is in PATH for reliable completion
- **Cross-Platform Line Endings** - Automatic CRLF to LF conversion for bash completion on Unix
- **Executable Permissions** - Automatically sets execute permissions on Linux/macOS
- **Smart Installation** - Detects if already installed and avoids duplicate installations
- **Enhanced Error Handling** - Better PATH update error messages and fallback instructions

v1.1.2
- **Show Command** - New command to display detailed model information
- **Local and Remote Show** - View model info locally or from remote servers
- **Flexible Display Options** - Show specific sections (license, modelfile, parameters, system, template)
- **Verbose Mode** - Display comprehensive model details with `--verbose` flag
- **Section Filtering** - Use flags to show only the information you need
- **Matches ollama show** - Full compatibility with ollama's show command functionality
- **Clear Command** - New `clear` command for interactive mode to clear console screen
- **Fixed Interactive Mode** - Properly enters REPL mode when called without arguments
- **Fixed Tab Completion** - Tab completion now properly clears previous options before displaying new ones
- **Shell Completion** - Auto-completion for Bash (Linux/macOS) and PowerShell (Windows)
- **Install-Completion Command** - Automatically configures shell completion with `osync install-completion`
- **Model Name Completion** - Tab-complete model names from local Ollama installation
- **PowerShell Profile Setup** - Automatically creates and configures PowerShell profile if needed

v1.1.1
- **Pull Command** - New command to download models from Ollama registry
- **Local and Remote Pull** - Pull models locally or directly to remote servers
- **HuggingFace Integration** - Convert HuggingFace URLs to ollama-compatible format
- **Automatic Tag Extraction** - Extracts quantization identifiers (IQ2_M, Q4_K_M, etc.) from GGUF filenames
- **Smart Tag Handling** - Automatically adds `:latest` tag for standard models, preserves explicit tags for HuggingFace models
- **Comprehensive Quantization Support** - Handles IQ, Q K-quants, Q legacy, Float, and BFloat16 formats
- **Real-time Progress** - Displays streaming download progress for both local and remote operations
- **HuggingFace URL Parsing** - Converts full HuggingFace URLs (e.g., `https://huggingface.co/user/repo/blob/main/file.gguf`) to short format (`hf.co/user/repo:tag`)

v1.1.0
- **Remote-to-Remote Copy** - Transfer models directly between remote servers without local storage
- **Memory-Buffered Streaming** - Efficient transfer using configurable memory buffer (default: 512MB)
- **Simultaneous Download/Upload** - Downloads from registry while uploading to destination
- **Backpressure Control** - Automatically throttles download when upload is slower
- **Configurable Buffer Size** - Use `-BufferSize` parameter (e.g., `256MB`, `1GB`)
- **Enhanced Progress Display** - Tqdm progress bar for remote-to-remote transfers matching local copy format
- **Registry-Based Transfer** - Pulls model blobs from Ollama registry for remote-to-remote operations
- **Fixed HttpClient Issues** - Resolved BaseAddress errors in list and copy commands
- **Improved Error Messages** - Clear documentation of remote-to-remote limitations

v1.0.9
- Added update command to update models to their latest versions
- Supports updating all models or specific patterns with wildcard matching
- Works on both local and remote Ollama servers
- Shows real-time progress and indicates whether each model was updated or already up to date
- Default behavior updates all models when no pattern specified

v1.0.8
- Added rename command with aliases `ren` and `mv` for safe model renaming
- Added local copy support - copy models locally, not just to remote servers
- Added sorting options for list command: `--size`, `--sizeasc`, `--time`, `--timeasc`
- Added destination existence checks for copy and rename operations to prevent overwriting
- Comprehensive README documentation with usage examples and patterns

v1.0.7
- Added list command with pattern matching support using `*` wildcard
- Remote listing support - list models on remote Ollama servers
- Multi-registry support - properly scans registry.ollama.ai, hf.co, hub, and custom registries
- Automatic `:latest` tag fallback for copy and remove commands when tag not specified
- Enhanced remove command with pattern matching support
- Fixed Ollama API migration for model creation (modelfile → files format)
- Fixed missing models in list output (models with `/` in names)

v1.0.6
- Changed syntax to support multiple actions: now copying a model needs `copy` (alias `cp`)
- Fixed upload progress bar updates
- Handles automatically `latest` tag if none specified

v1.0.5
- Added local models TabCompletion with interactive prompt if called without arguments
- Fixed 100 seconds timeout
- Added arguments exception handling

v1.0.4
- Check remote ollama version and display its version
- Fixed streaming output from create model

v1.0.3
- Added -bt switch to throttle the bandwidth in B, KB, MB, GB per second, eg. for 75MB/s use `-bt 75MB`

v1.0.2
- Fixed build with single portable file for Linux/MacOS

v1.0.1
- Fixed bug with stdErr redirect
- Remove Linux and MacOS colored output

v1.0.0
- Initial release

## Roadmap

See the [open issues](https://github.com/mann1x/osync/issues) for a list of proposed features (and known issues).

- [Top Feature Requests](https://github.com/mann1x/osync/issues?q=label%3Aenhancement+is%3Aopen+sort%3Areactions-%2B1-desc) (Add your votes using the 👍 reaction)
- [Top Bugs](https://github.com/mann1x/osync/issues?q=is%3Aissue+is%3Aopen+label%3Abug+sort%3Areactions-%2B1-desc) (Add your votes using the 👍 reaction)
- [Newest Bugs](https://github.com/mann1x/osync/issues?q=is%3Aopen+is%3Aissue+label%3Abug)

## Support

- [GitHub issues](https://github.com/mann1x/osync/issues/new?assignees=&labels=question&template=04_SUPPORT_QUESTION.md&title=support%3A+)


## License

This project is licensed under the **MIT license**.

See [LICENSE](LICENSE) for more information.
