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

- 🚀 **Fast Model Transfer** - Copy models between local and remote Ollama servers with high-speed uploads
- 📋 **Smart Model Management** - List, copy, rename, and delete models with pattern matching support
- 🔄 **Incremental Uploads** - Skips already transferred layers, saving bandwidth and time
- 📊 **Progress Tracking** - Real-time progress bars with transfer speed indicators
- 🏷️ **Auto-tagging** - Automatically applies `:latest` tag when not specified
- 🔍 **Advanced Listing** - Sort models by name, size, or modification time
- 🌐 **Multi-registry Support** - Works with registry.ollama.ai, hf.co, and custom registries
- 💾 **Offline Deployment** - Perfect for air-gapped servers and isolated networks
- 🎯 **Wildcard Patterns** - Use `*` wildcards for batch operations
- ⚡ **Bandwidth Control** - Throttle upload speeds to manage network usage

### Built With

> **[C# .NET 8]**

## Getting Started

### Prerequisites

> **[Windows/Linux/MacOS]**

> **[Arm64/x64/Mac]**

### Installation

> **[Download latest binary release]**

> **[Build from sources]**

> Clone the repo

> Compile with Visual Studio 2022

## Usage

### Quick Start

```bash
# Copy local model to remote server
osync cp llama3 http://192.168.100.100:11434

# List all local models
osync ls

# Interactive mode with tab completion
osync
```

### Commands

#### Copy (`cp`)

Copy models locally or to remote servers.

```bash
# Local copy (create backup)
osync cp llama3 my-backup-llama3
osync cp llama3:70b llama3:backup-v1

# Remote copy (upload to server)
osync cp llama3 http://192.168.0.100:11434
osync cp qwen2:7b http://192.168.0.100:11434

# With bandwidth throttling
osync cp llama3 http://192.168.0.100:11434 -bt 50MB
```

**Features:**
- Automatic `:latest` tag when not specified
- Prevents overwriting existing models
- Skips already uploaded layers
- Real-time progress with transfer speed

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

### Options

#### Global Options

- `-h`, `-?` - Show help for any command
- `-bt <value>` - Bandwidth throttling (B, KB, MB, GB per second)

**Examples:**
```bash
osync cp llama3 http://server:11434 -bt 75MB    # Limit to 75 MB/s
osync cp qwen2 http://server:11434 -bt 1GB      # Limit to 1 GB/s
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

```bash
osync
# Press tab to see available models
# Type command and press enter
> cp llama3 my-backup
> ls "qwen*"
> exit
```

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
# Upload to multiple servers
osync cp llama3 http://192.168.0.10:11434
osync cp llama3 http://192.168.0.11:11434
osync cp llama3 http://192.168.0.12:11434
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
