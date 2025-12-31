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
- 📋 **Smart Model Management** - List, copy, rename, and delete models with pattern matching support
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
- 📊 **Quantization Comparison** - Compare quality and performance across model quantizations with detailed scoring

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
# Interactive TUI for model management (recommended)
osync manage

# Copy local model to remote server
osync cp llama3 http://192.168.100.100:11434

# List all local models
osync ls

# Chat with a model
osync run llama3

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

#### Show (`show`)

Display detailed information about a model.

```bash
# Show information about a local model
osync show llama3
osync show qwen2:7b

# Show information about a remote model
osync show llama3 http://192.168.0.100:11434
```

**Features:**
- Displays model metadata and configuration
- Shows model file details, parameters, and system information
- Works on both local and remote servers

#### Pull (`pull`)

Pull a model from the Ollama registry.

```bash
# Pull a model from the registry
osync pull llama3
osync pull qwen2:7b
osync pull hf.co/unsloth/llama3

# Pull to a remote server
osync pull llama3 http://192.168.0.100:11434
```

**Features:**
- Downloads models from Ollama registry
- Shows real-time progress during download
- Supports library models and user models
- Works on both local and remote servers

#### Run (`run`, `chat`)

Interactive chat with a model.

```bash
# Chat with a local model
osync run llama3
osync chat qwen2:7b

# Chat with a model on remote server
osync run llama3 http://192.168.0.100:11434
```

**Features:**
- Interactive conversation mode
- Preloads model into memory before chat
- Shows model loading status
- Type `/bye` or press Ctrl+D to exit
- Works on both local and remote servers

#### Process Status (`ps`)

Show models currently loaded in memory.

```bash
# Show loaded models on local server
osync ps

# Show loaded models on remote server
osync ps http://192.168.0.100:11434
```

**Features:**
- Displays models loaded in VRAM
- Shows VRAM usage with percentage when partially loaded
- Displays model size, context length, and expiration time
- Formatted table output

**Output:**
```
Loaded Models:
---------------------------------------------------------------------------------------------------------------------------------------
NAME                           ID              SIZE                      VRAM USAGE      CONTEXT    UNTIL
---------------------------------------------------------------------------------------------------------------------------------------
tinyllama:1.1b-chat-v1-fp16    71c2f9b69b52    2.11 GB (1B)              1.33 GB (63%)   4096       4 minutes from now
---------------------------------------------------------------------------------------------------------------------------------------
```

#### Load (`load`)

Preload a model into memory.

```bash
# Load a model on local server
osync load llama3
osync load qwen2:7b

# Load a model on remote server
osync load llama3 http://192.168.0.100:11434

# Load with custom keep-alive duration
osync load llama3 --keepalive 30m
```

**Features:**
- Preloads model into VRAM for faster inference
- Configurable keep-alive duration
- Works on both local and remote servers

#### Unload (`unload`)

Unload a model from memory.

```bash
# Unload a model on local server
osync unload llama3
osync unload qwen2:7b

# Unload a model on remote server
osync unload llama3 http://192.168.0.100:11434
```

**Features:**
- Frees VRAM by unloading model
- Immediate unloading (keep-alive set to 0)
- Works on both local and remote servers

#### Quantization Comparison (`qc`)

Run comprehensive tests comparing quantization quality and performance across model variants.

```bash
# Compare quantizations of a model (f16 as base)
osync qc -M llama3.2 -Q q4_k_m,q5_k_m,q8_0

# Specify custom base quantization
osync qc -M llama3.2 -Q q4_k_m,q5_k_m -B fp16

# Test on remote server
osync qc -M llama3.2 -Q q4_k_m,q5_k_m -D http://192.168.1.100:11434

# Custom output file
osync qc -M llama3.2 -Q q4_k_m,q5_k_m -O my-results.json

# Adjust test parameters
osync qc -M llama3.2 -Q q4_k_m,q5_k_m -Te 0.1 -S 42 -To 0.9
```

**How It Works:**

The `qc` command runs a comprehensive test suite (50 questions across 5 categories: Reasoning, Math, Finance, Technology, Science) on each quantization variant and captures detailed metrics using Ollama's logprobs API.

**Scoring Algorithm:**

Each quantization is compared against the base model using a 4-component weighted scoring system:

1. **Token Sequence Similarity (5% weight)**
   - Uses Longest Common Subsequence (LCS) to measure token sequence matching
   - More forgiving of small variations while detecting major differences
   - Higher score = quantization produces similar token sequences as base

2. **Logprobs Divergence (70% weight)**
   - Compares sequence-level confidence between base and quantization
   - Calculates average confidence (mean logprob) for each sequence
   - Formula: `100 × exp(-confidence_difference × 2)`
   - Higher score = quantization has similar confidence in its predictions

3. **Answer Length Consistency (5% weight)**
   - Compares total token count between base and quantization answers
   - Formula: `100 × exp(-2 × |1 - length_ratio|)`
   - Higher score = quantization produces similar-length answers

4. **Perplexity Score (20% weight)**
   - Compares model confidence via perplexity (lower = more confident)
   - Perplexity: `exp(-average_logprob)`
   - Formula: `100 × exp(-0.5 × |1 - perplexity_ratio|)`
   - Higher score = quantization maintains similar confidence levels

**Overall Confidence Score:** Weighted sum of all four components (0-100%)

**Color Coding:**
- Green (90-100%): Excellent quality preservation
- Lime (80-90%): Very good quality
- Yellow (70-80%): Good quality
- Orange (50-70%): Moderate quality loss
- Red (below 50%): Significant quality degradation

**Performance Metrics:**
- Evaluation tokens per second (generation speed)
- Prompt tokens per second (encoding speed)
- Performance percentages vs base model

**Results File:**

Results are saved as JSON (`modelname.qc.json` by default) with:
- Test suite name and version
- Test options (temperature, seed, top_p, top_k, etc.)
- Per-quantization results with all question answers and tokens
- Model metadata (family, parameter size, quantization type, disk size)

**Incremental Testing:**

You can add new quantizations to existing results without re-testing:
```bash
# Initial test
osync qc -M llama3.2 -Q q4_k_m,q5_k_m

# Later add more quantizations (f16 and q8_0 will be skipped if already tested)
osync qc -M llama3.2 -Q q8_0,q6_k

# Force re-run testing for quantizations already in results file
osync qc -M llama3.2 -Q q4_k_m,q5_k_m --force
```

**Options:**

- `-M <name>` - Model name without tag (required)
- `-Q <tags>` - Comma-separated quantization tags to compare (required)
- `-B <tag>` - Base quantization tag for comparison (default: fp16, or uses existing base from results file)
- `-D <url>` - Remote server URL (default: local)
- `-O <file>` - Output results file (default: modelname.qc.json)
- `-Te <value>` - Temperature (default: 0.0 for deterministic results)
- `-S <value>` - Random seed (default: 365)
- `-To <value>` - top_p parameter (default: 0.001)
- `-Top <value>` - top_k parameter (default: -1)
- `-R <value>` - repeat_penalty parameter
- `-Fr <value>` - frequency_penalty parameter
- `-T <file>` - External test suite JSON file (default: internal v1base)
- `--force` - Force re-run testing for quantizations already present in results file
- `--judge <model>` - Use a judge model for similarity scoring (see Judge Scoring below)
- `--mode <mode>` - Judge execution mode: `serial` (default) or `parallel`

**Judge Scoring:**

Use a second LLM to evaluate similarity between base and quantized responses:

```bash
# Local judge model
osync qc -M llama3.2 -Q 1b-instruct-q8_0,1b-instruct-q6_k --judge mistral

# Remote test models, local judge
osync qc -d http://192.168.1.100:11434/ -M llama3.2 -Q q4_k_m --judge mistral

# Remote judge model (different server)
osync qc -M llama3.2 -Q q4_k_m --judge http://192.168.1.200:11434/mistral

# Parallel mode (judge runs concurrently with testing)
osync qc -M llama3.2 -Q q4_k_m --judge mistral --mode parallel
```

When judgment scoring is enabled:
- The judge model evaluates each quantized response against the base response
- Returns a similarity score from 1-100 for each question with reasoning
- Final score is calculated as: **50% metrics + 50% judgment**
- Results display shows separate columns for Final Score, Metrics Score, and Judge Score
- Existing judgments are skipped unless `--force` is used or a different judge model is specified

**Judge Execution Modes:**

- **Serial mode (default):** After testing each quantization, all questions are judged sequentially before moving to the next quantization. Simple and predictable execution.

- **Parallel mode:** Testing and judgment run concurrently at the question level. As each question is tested, it is immediately passed to the judge model in a background task. This allows the test model to continue generating answers while the judge evaluates previous questions. Significantly reduces total execution time when using a remote judge or when the judge model is faster than the test model.

**External Test Suite Format:**

Create custom test suites using JSON files with the following structure:
```json
{
  "name": "my-custom-suite",
  "categories": [
    {
      "id": 1,
      "name": "Category Name",
      "questions": [
        {
          "categoryId": 1,
          "questionId": 1,
          "text": "Your question text here"
        }
      ]
    }
  ]
}
```

Use with: `osync qc -M model -Q q4_k_m -T my-custom-suite.json`

A reference `v1base.json` file is included in the osync directory.

#### View Quantization Results (`qcview`)

Display quantization comparison results in formatted tables or export as JSON.

```bash
# View results in table format (console)
osync qcview llama3.2.qc.json

# Export table to text file
osync qcview llama3.2.qc.json -O report.txt

# Export as JSON to file
osync qcview llama3.2.qc.json -Fo json -O report.json

# View in console as JSON
osync qcview llama3.2.qc.json -Fo json
```

**Table Output:**

Displays color-coded results with:
- Overall confidence scores (0-100%) for each quantization
- Component scores: Token Similarity, Logprobs Divergence, Length Consistency, Perplexity
- Category-by-category breakdown
- Performance metrics (eval/prompt tokens per second)
- Model metadata (quantization type, disk size)

**Color Coding:**
- 🟢 Green (90-100%): Excellent quality preservation
- 🟢 Lime (80-90%): Very good quality
- 🟡 Yellow (70-80%): Good quality
- 🟠 Orange (50-70%): Moderate quality loss
- 🔴 Red (<50%): Significant quality degradation

**JSON Output:**

Complete results including:
- Base model information
- Per-quantization overall and category scores
- Detailed per-question scores (with `QuestionScores` array)
- Performance metrics

**Options:**

- `<file>` - Results file to view (required, positional argument)
- `-Fo <format>` - Output format: table or json (default: table)
- `-O <file>` - Output filename (default: console, shows file size on success)

#### Manage (`manage`)

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

v1.1.9
- **Judge Model Scoring** - Use a second LLM to evaluate quantization quality
  - `--judge <model>` - Specify local or remote judge model
  - `--mode serial|parallel` - Serial (after each quant) or parallel (concurrent) execution
  - Judge evaluates similarity between base and quantized responses (score 1-100)
  - Final score: 50% metrics + 50% judgment when enabled
  - Supports local and remote judge models with auto-completion
  - Results display shows separate Final, Metrics, and Judge scores
- **True Parallel Judgment Mode** - Testing and judgment now run concurrently at the question level
  - In `--mode parallel`, each question is immediately judged in the background as soon as testing completes
  - Test model continues generating answers while judge evaluates previous questions
  - Significantly reduces total execution time when using a remote judge or fast judge model
  - Parallel judgment also applies to existing quantizations that need re-judging
- **Improved Judge Response Handling**
  - Changed to `/api/chat` endpoint with structured JSON output schema
  - Added `Reason` field to capture judge's reasoning for each score
  - Robust parsing with fallbacks for different model response formats
  - Treats score 0 as score 1 (minimum) for models that ignore instructions
- **Enhanced Retry Logic**
  - Fixed retry display to show "Attempt 2/5", "Attempt 3/5" etc. after failures
  - Added detailed error messages for judge API failures
  - 5 retry attempts with exponential backoff
- **Improved qcview Output** - Enhanced file export functionality
  - Fixed table file output with correct eval/prompt speed formatting
  - Fixed JSON console output avoiding Spectre.Console markup parsing errors
  - Added file size display on successful file creation
  - Positional argument for results file (e.g., `osync qcview results.json`)
  - Displays judge model info when judgment scoring is available
- **Bug Fixes**
  - Fixed judgment skip logic: existing quantizations without judgments or with different judge model are now properly re-judged
  - Fixed parallel mode not executing judgment concurrently with testing
- **Documentation Updates**
  - Updated color coding documentation to match actual thresholds
  - Clarified qcview command usage and options

v1.1.8
- **External Test Suite Support** - Load custom test suites from JSON files using `-T path/to/suite.json`
  - Bundled `v1base.json` file for reference and portability
  - Create custom test suites with your own categories and questions
  - Full JSON schema support with categories, questions, and metadata
- **Force Re-run Option** - Added `--force` flag to re-run testing for quantizations already in results file
- **Improved Base Model Handling** - Base tag defaults to `fp16` and is automatically detected from existing results
- **Bug Fixes**
  - Fixed case-sensitive model name matching when retrieving disk size from `/api/tags`
  - Fixed command argument positioning (PowerArgs uses 1-based indexing)
  - Fixed `osync ls` to accept model name as positional argument (e.g., `osync ls qw` works like `osync ls qw*`)

v1.1.7
- **Quantization Comparison Testing** - New `qc` and `qcview` commands for comprehensive quality testing
  - 50-question test suite across 5 categories (Reasoning, Math, Finance, Technology, Science)
  - 4-component weighted scoring: Logprobs Divergence (70%), Perplexity (20%), Token Similarity (5%), Length Consistency (5%)
  - Performance metrics: eval/prompt tokens per second with percentage comparison to base model
  - Incremental testing support - add new quantizations without re-testing existing ones
  - JSON results file format with full question/answer history
  - Color-coded table output in `qcview` (90-100% green, 80-90% lime, 70-80% yellow, 50-70% orange, <50% red)
  - JSON export option for programmatic analysis
  - Uses Ollama's logprobs API (requires Ollama v0.12.11+)
  - Configurable test parameters: temperature, seed, top_p, top_k, repeat_penalty, frequency_penalty
  - Works on both local and remote servers
- **Bug Fixes**
  - Fixed PowerArgs duplicate alias errors by removing explicit shortcuts (auto-generated from property names)
  - Fixed model detection issue: `/api/show` endpoint doesn't include `size` field, now fetches from `/api/tags`
  - Fixed misleading "Results saved" message when no quantizations were successfully tested
  - Added proper error handling with exit codes (returns 1 when no results)
  - Set max output tokens to 4096 to prevent tests hanging on overly verbose answers
  - Improved tag handling: accepts any tag format (with or without ":"), extracts quantization from API

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
- **Load/Unload Commands** - VRAM memory management
  - `load` command to preload models into memory with configurable keep-alive
  - `unload` command to free VRAM immediately
  - Works on both local and remote servers
- **Run/Chat Command** - Interactive conversation with models
  - Preloads model into memory before starting chat
  - Shows loading status and model information
  - Type `/bye` or Ctrl+D to exit
  - Works on both local and remote servers
- **Show Command** - Display detailed model information
  - Shows model metadata, configuration, and parameters
  - Works on both local and remote servers
- **Pull Command** - Download models from Ollama registry
  - Validates model existence on ollama.com before pulling
  - Real-time progress display
  - Supports library and user models
  - Works on both local and remote servers

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
