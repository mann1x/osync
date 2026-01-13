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

### DeepWiki

An AI generated DeepWiki is available here:

https://deepwiki.com/mann1x/osync

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
- Model metadata (family, parameter size, quantization type, enhanced quantization with tensor analysis, disk size)
- Model digest (SHA256 and short digest for model identification/verification)

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

**Resume Support:**

Testing can be interrupted with Ctrl+C and resumed later:
```bash
# Start testing (press Ctrl+C, then 'y' to confirm save and exit)
osync qc -M llama3.2 -Q q4_k_m,q5_k_m,q8_0

# Resume from where you left off
osync qc -M llama3.2 -Q q4_k_m,q5_k_m,q8_0
```

When resuming:
- Already-completed quantizations are skipped
- Partially-tested quantizations continue from the last saved question
- Missing judgments are automatically detected and run
- Missing model digests are automatically retrieved and backfilled
- Progress bars show resumed position

**Cancellation & Timeout Handling:**

- First Ctrl+C prompts for confirmation (y=cancel and save, n=continue)
- Second Ctrl+C force exits immediately
- Timeouts are retried automatically with exponential backoff
- After retry exhaustion, prompts: y=cancel, n=double timeout and retry
- On-demand models with incomplete results are preserved for resume

**Wildcard Tag Selection:**

Use wildcards (`*`) to select multiple quantizations from HuggingFace or Ollama registries:

```bash
# Test all quantizations from a HuggingFace repository
osync qc -M LFM2.5-1.2B-Instruct -b F16 -Q hf.co/LiquidAI/LFM2.5-1.2B-Instruct-GGUF:*

# Test only IQ quantizations (case-insensitive)
osync qc -M hf.co/LiquidAI/LFM2.5-1.2B-Instruct-GGUF -b F16 -Q IQ*

# Test Q4 and Q5 quantizations
osync qc -M llama3.2 -b fp16 -Q Q4*,Q5*

# Mix HuggingFace base with Ollama quants
osync qc -M LFM2.5-1.2B-Instruct -b hf.co/LiquidAI/LFM2.5-1.2B-Instruct-GGUF:F16 -Q IQ*,Q4*,Q5*

# Multiple HuggingFace patterns
osync qc -M LFM2.5-1.2B-Instruct -b hf.co/LiquidAI/LFM2.5-1.2B-Instruct-GGUF:F16 -Q hf.co/LiquidAI/LFM2.5-1.2B-Instruct-GGUF:IQ*,hf.co/LiquidAI/LFM2.5-1.2B-Instruct-GGUF:Q5*
```

Wildcard behavior:
- `*` matches any characters (case-insensitive)
- Tags are fetched from HuggingFace API for `hf.co/...` models
- Tags are scraped from Ollama website for Ollama registry models
- Patterns without model prefix use the `-M` model as source

**Options:**

- `-M <name>` - Model name without tag (required)
- `-Q <tags>` - Comma-separated quantization tags to compare, supports wildcards (e.g., `Q4*,IQ*`) (required)
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
- `--rejudge` - Re-run judgment process for existing test results (without re-testing)
- `--judge <model>` - Use a judge model for similarity scoring (see Judge Scoring below)
- `--judge-ctxsize <value>` - Context length for judge model (0 = auto, default: 0). Auto mode calculates: test_ctx × 2 + 2048
- `--mode <mode>` - Judge execution mode: `serial` (default) or `parallel`
- `--timeout <seconds>` - API timeout in seconds for testing and judgment calls (default: 600)
- `--verbose` - Show judgment details (question ID, score, reason) for each judged question
- `--ondemand` - Pull models on-demand if not available, then remove after testing (see On-Demand Mode below)
- `--repo <url>` - Repository URL for the model source (saved in results file for qcview)

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

# Re-run judgment only (use existing test results)
osync qc -M llama3.2 -Q q4_k_m --judge mistral --rejudge
```

When judgment scoring is enabled:
- The judge model evaluates each quantized response against the base response
- Returns a similarity score from 1-100 for each question with reasoning
- Final score is calculated as: **50% metrics + 50% judgment**
- Results display shows separate columns for Final Score, Metrics Score, and Judge Score
- Existing judgments are skipped unless `--force`, `--rejudge` is used, or a different judge model is specified

**Separate Best Answer Judge Model:**

Use `--judgebest` to specify a different model for best answer determination (A/B/tie judgment):

```bash
# Same model for similarity and best answer (when using --judge alone)
osync qc -M llama3.2 -Q q4_k_m --judge mistral

# Different model for best answer judgment
osync qc -M llama3.2 -Q q4_k_m --judge mistral --judgebest llama3.3

# Best answer judgment only (no similarity scoring)
osync qc -M llama3.2 -Q q4_k_m --judgebest llama3.3

# Remote best answer judge
osync qc -M llama3.2 -Q q4_k_m --judge mistral --judgebest http://192.168.1.200:11434/llama3.3

# Re-run only best answer judgment with new model
osync qc -M llama3.2 -Q q4_k_m --judgebest llama3.3 --rejudge
```

When `--judgebest` is specified:
- Per quantization: similarity judgment runs first (if `--judge` is used), then best answer judgment
- Best answer model uses a quality-focused prompt (no similarity scoring)
- Results store `JudgeModelBestAnswer`, `ReasonBestAnswer`, and `JudgedBestAnswerAt` timestamps
- QcView outputs show both judge models when different

**Cloud Provider Support:**

Use cloud AI providers (Anthropic Claude, OpenAI, etc.) as judge models with the `@provider/model` syntax:

```bash
# Using environment variable for API key
osync qc -M llama3.2 -Q q4_k_m --judge @claude/claude-sonnet-4-20250514
osync qc -M llama3.2 -Q q4_k_m --judge @openai/gpt-4o

# Using explicit API key
osync qc -M llama3.2 -Q q4_k_m --judge @claude:sk-ant-xxx/claude-sonnet-4

# Azure OpenAI (key@endpoint format)
osync qc -M llama3.2 -Q q4_k_m --judge @azure:mykey@myendpoint.openai.azure.com/gpt4-deployment

# Mixed: cloud judge with ollama best answer judge
osync qc -M llama3.2 -Q q4_k_m --judge @claude/claude-sonnet-4 --judgebest llama3.2:latest

# Both judges from cloud providers
osync qc -M llama3.2 -Q q4_k_m --judge @openai/gpt-4o --judgebest @claude/claude-opus-4
```

Supported cloud providers and their environment variables:

| Provider | Syntax | Environment Variable |
|----------|--------|---------------------|
| Anthropic Claude | `@claude/model` | `ANTHROPIC_API_KEY` |
| OpenAI | `@openai/model` | `OPENAI_API_KEY` |
| Google Gemini | `@gemini/model` | `GEMINI_API_KEY` or `GOOGLE_API_KEY` |
| Azure OpenAI | `@azure/deployment` | `AZURE_OPENAI_API_KEY` + `AZURE_OPENAI_ENDPOINT` |
| Mistral AI | `@mistral/model` | `MISTRAL_API_KEY` |
| Cohere | `@cohere/model` | `CO_API_KEY` or `COHERE_API_KEY` |
| Together AI | `@together/model` | `TOGETHER_API_KEY` |
| HuggingFace | `@huggingface/model` | `HF_TOKEN` or `HUGGINGFACE_TOKEN` |
| Replicate | `@replicate/model` | `REPLICATE_API_TOKEN` |

Use `osync qc --help-cloud` for detailed provider documentation and examples.

When using cloud providers:
- API keys can be provided via environment variables (default) or explicitly in the command line
- Connection and model validation is performed before testing starts
- Cloud provider info (name and API version) is recorded in results for traceability
- QcView displays cloud provider badges in HTML/PDF output (only for cloud providers, not for Ollama)

**Judge Execution Modes:**

- **Serial mode (default):** After testing each quantization, all questions are judged sequentially before moving to the next quantization. Simple and predictable execution.

- **Parallel mode:** Testing and judgment run concurrently at the question level. As each question is tested, it is immediately passed to the judge model in a background task. This allows the test model to continue generating answers while the judge evaluates previous questions. Significantly reduces total execution time when using a remote judge or when the judge model is faster than the test model.

**On-Demand Mode:**

Use `--ondemand` to automatically pull models that aren't available and remove them after testing. This is ideal for testing large models or many quantizations without consuming permanent storage:

```bash
# Test many quantizations without keeping them all
osync qc -M llama3.2 -Q q2_k,q3_k_s,q3_k_m,q4_k_s,q4_k_m,q5_k_s,q5_k_m,q6_k,q8_0 --ondemand

# Test on remote server with on-demand
osync qc -M llama3.2 -Q q4_k_m,q8_0 -D http://192.168.1.100:11434 --ondemand
```

When on-demand mode is enabled:
- Before testing each quantization, osync checks if the model exists on the Ollama instance
- If the model is missing, it is automatically pulled from the registry
- Models that were already present are NOT removed after testing (only on-demand pulled models)
- After testing and judgment complete, on-demand pulled models are removed to free disk space
- The on-demand state is saved in the results file, so interrupted tests can properly clean up on resume
- Works with both local and remote Ollama servers

**Built-in Test Suites:**
- `v1base` (default) - 50 questions across Reasoning, Math, Finance, Technology, Science
- `v1quick` - 10 questions (subset of v1base for quick testing)
- `v1code` - 50 coding questions across Python, C++, C#, TypeScript, Rust (8192 max tokens)

**External Test Suite Format:**

Create custom test suites using JSON files with the following structure:
```json
{
  "name": "my-custom-suite",
  "numPredict": 4096,
  "contextLength": 4096,
  "categories": [
    {
      "id": 1,
      "name": "Category Name",
      "contextLength": 8192,
      "questions": [
        {
          "categoryId": 1,
          "questionId": 1,
          "text": "Your question text here",
          "contextLength": 16384
        }
      ]
    }
  ]
}
```

- `numPredict` - Maximum tokens generated per response (default: 4096). Use higher values for coding or detailed answers.
- `contextLength` - Context length (num_ctx) for the model during testing. Can be specified at:
  - **Suite level** (default: 4096) - applies to all questions
  - **Category level** - overrides suite default for all questions in that category
  - **Question level** - overrides both suite and category settings for that specific question

Use with: `osync qc -M model -Q q4_k_m -T my-custom-suite.json`

Reference files `v1base.json` and `v1code.json` are included in the osync directory.

The recommended model to use as a judge is `gemma3:12b`, it is advisable to use a large context size (6k for `v1base` and 12k for `v1code`).
It's also recommended to run the `qc` command with the `--verbose` argument to verify the scoring and reason given corresponds to the expected beahviour:
```
Judging UD-IQ3_XXS Q1-1 Score: 92% (1/50 2%)
    A and B match: Both responses provide a complete, thread-safe LRU cache implementation in Python using `OrderedDict` and `threading.RLock`. They both include comprehensive docstrings, error
    handling, and a full set of methods (get, put, delete, clear, size, etc.). The core logic for managing the cache, including eviction and thread safety, is nearly identical. The primary differences
    are in the formatting of the docstrings and some minor wording variations in the explanations. Both responses also include example usage and testing code. The code itself is very similar, with
    only minor differences in variable names and comments. Overall, the responses demonstrate a very high degree of similarity in terms of content, approach, and functionality.
Judging UD-IQ3_XXS Q1-2 Score: 75% (2/50 4%)
    A and B match: Both responses provide a Python async web scraper using aiohttp, incorporating concurrent crawling, rate limiting, retries, and CSS selector-based data extraction. They both utilize
    asyncio, aiohttp, logging, and dataclasses. Both include comprehensive error handling and logging. However, they differ in their implementation details. Response A uses a semaphore for concurrency
    control and a class-based structure for the scraper, while Response B introduces a separate RateLimiter class and a more streamlined approach to data extraction. Response A's retry logic is more
    detailed, including random jitter, while Response B's is simpler. Response B also includes an advanced scraper with custom selectors.
Judging UD-IQ3_XXS Q1-3 Score: 75% (3/50 6%)
    A and B differ: Both responses implement a retry decorator factory with similar functionality (configurable max attempts, delay strategies, exception filtering, and support for both sync and async
    functions). However, they differ significantly in their implementation details and structure. Response A uses a class-based approach with a `RetryError` exception and separate `_calculate_delay`
    function. It also includes convenience decorators for common retry patterns. Response B uses a dataclass for configuration and an Enum for delay strategies, making it more structured. It also has
    separate functions for sync and async decorators. While both achieve the same goal, the code organization and specific techniques used are quite different, leading to a noticeable difference in
```

#### View Quantization Results (`qcview`)

Display quantization comparison results in formatted tables or export to various formats.

```bash
# View results in table format (console)
osync qcview llama3.2.qc.json

# Export table to text file
osync qcview llama3.2.qc.json -O report.txt

# Export as JSON to file
osync qcview llama3.2.qc.json -Fo json -O report.json

# Export as Markdown
osync qcview llama3.2.qc.json -Fo md

# Export as HTML (interactive with theme toggle)
osync qcview llama3.2.qc.json -Fo html

# Export as PDF (includes full Q&A pages)
osync qcview llama3.2.qc.json -Fo pdf

# Add or override repository URL in output
osync qcview llama3.2.qc.json -Fo html --repo https://github.com/user/model
```

**Table Output:**

Displays color-coded results with:
- Overall confidence scores (0-100%) for each quantization
- Component scores: Token Similarity, Logprobs Divergence, Length Consistency, Perplexity
- Category-by-category breakdown
- Performance metrics (eval/prompt tokens per second)
- Model metadata (quantization type with tensor analysis, disk size)
  - Shows dominant tensor quantization with percentage (e.g., `Q4_K (87%)`)
  - Mixed quantization shown as `Q6_K (81% Q8_0)` when dominant differs from tag
  - Unknown tensor types indicated with `?` suffix (e.g., `Q3_K?`)

**Color Coding:**
- 🟢 Green (90-100%): Excellent quality preservation
- 🟢 Lime (80-90%): Very good quality
- 🟡 Yellow (70-80%): Good quality
- 🟠 Orange (50-70%): Moderate quality loss
- 🔴 Red (<50%): Significant quality degradation

**Output Formats:**

- **table** (default) - Color-coded console output or plain text file
- **json** - Complete results with all scores and metadata
- **md** - Markdown format with tables (for GitHub, documentation)
- **html** - Interactive HTML with dark/light theme toggle, collapsible Q&A sections
- **pdf** - Professional PDF report with:
  - Summary tables with color-coded scores
  - Category breakdown table
  - Rankings by score, speed, perplexity, and best answers
  - Full Q&A pages for each quantization (questions, answers, judgment details)

**Options:**

- `<file>` - Results file to view (required, positional argument)
- `-Fo <format>` - Output format: table, json, md, html, pdf (default: table)
- `-O <file>` - Output filename (default: auto-generated based on format)
- `--repo <url>` - Repository URL (displayed in output, overrides value from results file)
- `--metricsonly` - Ignore judgment data and show only metrics-based scores (useful for comparing pure model output quality without judge influence)

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
- **Ctrl+C** - Copy model(s) (local or to remote server, supports batch selection)
- **Ctrl+M** - Rename model
- **Ctrl+R** - Run/chat with model
- **Ctrl+S** - Show model information
- **Ctrl+D** - Delete model(s) (supports batch selection with Space to select multiple)
- **Ctrl+U** - Update model(s) (supports batch selection)
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

#### Version (`showversion`, `-v`)

Display osync version and environment information.

```bash
# Show version only
osync -v
osync showversion

# Show detailed environment information
osync showversion --verbose
```

**Basic Output:**
```
osync v1.2.6 (b20260110-1117)
```

**Verbose Output:**
```
osync v1.2.6 (b20260110-1117)
Binary path: C:\Users\user\.osync\osync.exe
Installed: Yes (C:\Users\user\.osync)
Shell: PowerShell Core 7.5.4
Tab completion: Installed (C:\Users\user\Documents\PowerShell\Microsoft.PowerShell_profile.ps1)
```

**Verbose Information:**
- **Binary path** - Location of the osync executable
- **Installed** - Whether osync is installed in the standard install directory
- **Shell** - Detected shell type and version (bash, zsh, PowerShell Core/Desktop, cmd)
- **Tab completion** - Status of tab completion for the current shell

#### Install (`install`)

Install osync to user directory and configure shell completion.

```bash
# Run the installer
osync install
```

**Installation Directory:**
- **Windows:** `~/.osync`
- **Linux/macOS:** `~/.local/bin`

**Features:**
- Copies osync executable to install directory
- Adds install directory to PATH
- Offers to configure tab completion for detected shell
- Supports bash, zsh, and PowerShell

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

v1.2.8
- **Cloud Provider Support for Judge Models** - Use cloud AI providers for `--judge` and `--judgebest`
  - Support for 9 providers: Anthropic Claude, OpenAI, Google Gemini, Azure OpenAI, Mistral AI, Cohere, Together AI, HuggingFace, and Replicate
  - Syntax: `@provider[:token]/model` (e.g., `@claude/claude-sonnet-4-20250514`, `@openai/gpt-4o`)
  - API keys loaded from environment variables by default, can be specified explicitly
  - Connection and model validation before testing starts
  - Cloud provider info (name, API version) recorded in results for traceability
  - QcView displays cloud provider badges in HTML/PDF output (only for cloud, not Ollama)
  - New `--help-cloud` option for detailed provider documentation
- **PDF Text Rendering Fix** - Fixed text corruption in Q&A answers for PDF output
  - Resolved character scrambling issue with certain text patterns (e.g., Python format strings)
  - Uses line-by-line Text elements to prevent text reordering
  - Added Courier monospace font for code content for better readability
  - Added text sanitization to handle problematic Unicode characters
- **QcView File Access Check** - Moved file overwrite confirmation before progress bar
  - Prevents concurrent display errors when output file already exists
  - Applies to all output formats (JSON, Markdown, HTML, PDF)

v1.2.7
- **Separate Best Answer Judge Model (--judgebest)** - New command-line argument for best answer determination
  - Use a different model for best answer judgment vs similarity scoring (--judge)
  - `--judgebest` can be used alone or combined with `--judge` for different models
  - Same configuration options as `--judge`: local model name or `http://host:port/model` for remote
  - New system prompt focused purely on qualitative best answer determination
  - Supports both serial and parallel execution modes
  - Works with `--rejudge` to re-run only best answer judgment with new model
- **Version Tracking in QC Results** - Record osync and Ollama versions in test results
  - `OsyncVersion` - Version of osync used for testing
  - `OllamaVersion` - Ollama server version for test quantizations
  - `OllamaJudgeVersion` - Ollama version for judge server (similarity scoring)
  - `OllamaJudgeBestAnswerVersion` - Ollama version for best answer judge server
  - Versions captured automatically from Ollama `/api/version` endpoint
- **QCView Output Updates** - All output formats updated with new information
  - Table output shows Best Answer Judge model (when different from Judge) and versions
  - JSON output includes all version fields and JudgeModelBestAnswer
  - Markdown output includes Best Answer Judge and versions in header
  - HTML output shows Best Answer Judge in info grid and versions row
  - PDF output includes Best Answer Judge and versions in header tables
- **Manage TUI Multi-Select Delete Fix** - Fixed batch delete for multiple selected models
  - Multi-selection delete now works correctly (previously only deleted single model)
  - Added batch confirmation dialog showing count and list of models to be deleted
- **Judge Retry Output Improvements** - Better visibility into retry attempts during judgment
  - Both judge and judgebest operations now show retry warnings with error codes at each attempt
  - Displays retry delay countdown before each retry attempt
- **Fixed Copy to Remote Server** - Resolved HTTP 500 errors when loading copied models
  - Fixed `stop` parameter serialization (now correctly sent as array instead of string)
  - Fixed numeric/boolean parameter type conversion (top_k, temperature, seed, etc.)
  - New `ConvertParameterValue` helper ensures correct JSON types for all Ollama model parameters
- **Fixed HuggingFace Model Copy** - Correct path resolution for `hf.co/...` models
  - HuggingFace models now use correct manifest path (not under registry.ollama.ai)
  - Fixed cross-platform path separator handling for model paths with forward slashes
- **Load/Unload URL Format Support** - Both commands now accept URL format with embedded model name
  - Supports `osync load http://host:port/modelname` in addition to `osync load modelname -d host`
  - Same URL parsing for unload command
- **Fixed --rejudge Model Pulling** - Rejudge mode no longer attempts to download test models
  - When using `--rejudge` with existing results, only the judge model is needed
  - Wildcard expansion now filters to only tags present in the results file
  - Skips model verification for all existing results in rejudge mode
  - Properly queues partial results for re-judgment without resuming tests

v1.2.6
- **QcView Multiple Output Formats** - Export results to various formats
  - **Markdown (.md)** - Tables formatted for GitHub and documentation
  - **HTML (.html)** - Interactive report with dark/light theme toggle, collapsible Q&A sections, color-coded scores
  - **PDF (.pdf)** - Professional report using QuestPDF library with:
    - Summary tables with color-coded scores for all metrics
    - Scores by category table
    - Rankings tables (by score, eval speed, perplexity, best answers)
    - Full Q&A pages for each quantization with judgment details
  - Use `-Fo md`, `-Fo html`, or `-Fo pdf` to select format
- **QcView Repository URL** - New `--repo` argument to specify model source repository
  - URL is displayed in output headers and included in JSON export
  - Can be saved during `qc` testing and overridden in `qcview`
- **Headless/Background Mode Fix** - Fixed console errors when running qc command in background
  - Console.WindowWidth and Console.ReadKey now properly handled in headless environments
  - Prevents "The handle is invalid" errors when running without a terminal
- **New Version Command** - Added `osync version` (alias `-v`) to display version info
  - Shows osync version number and build timestamp
  - `--verbose` flag displays detailed info: binary path, installation status, shell type/version, tab completion status
  - Detects bash, zsh, PowerShell (Core/Desktop), and cmd shells
  - Smart installation detection: when running a different binary, compares version AND build timestamp with installed version
  - Reports if installed version is older/newer (e.g., `installed v1.2.6 (b20260110-1156) is older`)
  - Fixed tab completion detection to match actual script markers in profiles
- **Model Digest Tracking** - QC results now include SHA256 digest for each tested model
  - Full digest (`Digest`) and short digest (`ShortDigest`, first 12 chars) stored in results JSON
  - Automatically populated from local Ollama or HuggingFace registry
  - Backfill: missing digests are automatically retrieved when loading existing results files
- **Fixed Model ID Display** - IDs now show first 12 chars of manifest SHA256 (matches `ollama ls`)
  - `osync ls` and manage TUI compute SHA256 of manifest file content
  - ID column width increased from 8 to 12 characters
  - Consistent with `ollama ls` output for easy cross-reference
- **Improved osync ps Output** - Dynamic console width and better model name display
  - Detects console width and adjusts column sizes dynamically
  - Model names now truncated from beginning to preserve full tag (e.g., `...0B-A3B-Instruct-GGUF:Q4_K_S`)
  - Better visibility of quantization tags for HuggingFace models with long paths
- **Load Command Timing** - Shows elapsed time and API-reported load duration
  - Displays total elapsed time and Ollama's `load_duration` from response
  - Example: `✓ Model 'model:tag' loaded successfully (2m 15s) (API: 2m 5s)`
- **QCView Table Alignment Fix** - Tag and Quant columns now left-aligned instead of centered
- **Timeout Handling Improvements** - Better handling of HTTP timeouts during testing
  - Timeouts are now properly distinguished from user cancellation (no longer shows "Operation cancelled by user")
  - Timeouts trigger retry with exponential backoff instead of immediate failure
  - After retry attempts exhausted, prompts user: y=cancel, n=double timeout and retry
  - Allows recovery from slow model responses without losing progress
- **Improved On-Demand Model Cleanup** - Fixed critical bug where models were deleted during testing
  - Models with incomplete test results are NEVER cleaned up (preserves for resume)
  - Fixed cleanup to protect incomplete models regardless of error type (timeout, cancellation, etc.)
  - On-demand status tracking is now consistent when resuming interrupted tests
- **Fixed HuggingFace Wildcard Tag Detection** - Now detects all quantization formats
  - Added support for XL variants (Q2_K_XL, Q3_K_XL, Q4_K_XL, Q5_K_XL, Q6_K_XL, Q8_K_XL)
  - Added support for TQ ternary quantization (TQ1_0)
- **Fixed HuggingFace Model Quant Column** - QC results now correctly show quantization type
  - Ollama returns `"quantization_level": "unknown"` for HuggingFace models
  - Now extracts quantization type from model name/tag when API returns "unknown"
- **Enhanced Quantization Display with Tensor Analysis** - Quant column now shows dominant tensor quantization
  - Analyzes transformer block weight tensors only (excludes embeddings, output, and norms)
  - Calculates weighted percentage by tensor size (elements × bits per weight)
  - Displays format like `Q4_0 (87%)` or `Q6_K (81% Q8_0)` showing actual tensor distribution
  - Uses Ollama API `verbose=true` to fetch tensor metadata
  - Fixed: Extract quant type from model name before tensor analysis for correct formatting
  - Fixed: Filter to transformer weights only (Q8_0 embeddings/output were skewing results)
  - Fixed: Unknown tensor types shown with "?" suffix (e.g., `Q3_K?`) to indicate uncertainty
  - Supports all quantization types: Q*_K variants, IQ (importance matrix), and TQ (ternary)
- **Fixed QC Model Validation** - Relaxed overly strict parameter size comparison
  - Parameter size formatting varies between models (e.g., "999.89M" vs "1,000M" for same model)
  - Now only warns on family mismatch instead of blocking testing
  - Testing continues even with warnings
- **Improved Judge API Retry Strategy** - More resilient handling of judge server errors
  - Increased retry attempts from 5 to 25 for judge API calls
  - Delay ramps from 5 seconds to 30 seconds progressively
  - Shows warning and skips judgment only after all retries exhausted (instead of failing)
  - Better handles overloaded or slow judge servers (HTTP 500 errors)
- **Fixed Base Model Re-Pull When Adding Quants** - Skip base model if results already exist
  - When adding new quants to existing test results without `-b`, no longer tries to pull the base model
  - If results file contains any base model results (even partial), the base is skipped entirely
  - Improved base model detection: automatically identifies base by common patterns (fp16, f16, bf16, etc.)
  - Use `--force` to re-run the base model if needed
- **Improved osync ls Wildcard Handling** - Better shell expansion handling on Linux/macOS
  - Default behavior: `osync ls code` matches models starting with "code" (prefix match, same as `code*`)
  - Suffix match: `osync ls *q4_k_m` finds all models ending with "q4_k_m" (useful for finding by quantization)
  - Contains match: `osync ls *code*` finds models containing "code" anywhere in the name
  - Shell expansion handling: detects when shell expanded unquoted wildcards and shows helpful warning
  - Suggests using quotes to prevent expansion: `osync ls 'gemma*'`
- **Wildcard Tag Expansion for osync pull** - Pull multiple models with tag patterns
  - Supports wildcards in tags: `osync pull gemma3:1b-it-q*` pulls all matching tags
  - Works with HuggingFace: `osync pull hf.co/unsloth/gemma-3-1b-it-GGUF:IQ2*`
  - Works with remote servers: `osync pull -d http://server:11434 gemma3:1b-it-q*`
  - Automatically resolves available tags from Ollama registry or HuggingFace API
  - Shows list of matching tags before pulling
- **Judge Best Answer Tracking** - QC judge now evaluates which response is qualitatively better
  - Judge model returns `bestanswer`: A (base better), B (quant better), or AB (tie)
  - Verbose output shows best answer for each judgment: `Score: 75% (27/50 54%) Best: AB`
  - Handles edge cases: normalizes various formats (ResponseA, Response_A, Tie, identical, etc.)
  - Results automatically re-judged if `--judge` is active and `bestanswer` is missing
- **QcView Judge Best Column** - New column showing quant win statistics
  - Format: `67% (B:10 A:5 =:3)` showing quant won 67% of non-tie comparisons
  - B = quant better, A = base better, = = tie
  - Best percentage excludes ties (only counts decisive wins/losses)
  - Color-coded: green (>=60%), yellow (40-60%), red (<40%)
- **Enhanced JSON Output** - Additional statistics in JSON export
  - Per-question `BestAnswer` field (A/B/AB)
  - Per-quantization: `BestCount`, `WorstCount`, `TieCount`, `BestPercentage`, `WorstPercentage`, `TiePercentage`
  - Per-category: `CategoryBestStats` with counts and percentages
- **QcView Metrics-Only Mode** - New `--metricsonly` argument to ignore judgment data
  - Shows only metrics-based scores (token similarity, logprobs divergence, perplexity, length consistency)
  - Useful for comparing pure model output quality without judge influence
  - Works with all output formats (table, json, md, html, pdf)
- **Automatic Judge Context Length** - Judge model context is now auto-calculated by default
  - When `--judge-ctxsize` is 0 (new default), calculates: test_ctx × 2 + 2048
  - Ensures judge has enough context for both base and quantized responses plus prompt
  - Can still be manually overridden with explicit value
- **PDF Generation Progress Bar** - Visual progress indicator when generating PDF reports
  - Shows progress through Q&A pages for each quantization
  - Useful for large test results files with many questions
- **PDF Layout Improvements** - Better page break handling in PDF reports
  - Ranking tables use ShowEntire() to prevent splitting across pages
  - Speed columns simplified to show only percentage (removed tok/s to prevent wrapping)
  - Category scores section moves entirely to next page if it won't fit
  - Rankings organized into paired rows (Final Score + Eval Speed, Perplexity + Prompt Speed, Best Answers)
  - Added Prompt Speed ranking table with vs Base percentage column
- **Manage TUI Batch Delete Fix** - Fixed multi-selection delete not working
  - Delete now properly handles multiple selected models (Ctrl+D with checkmarks)
  - Confirmation dialog shows count and lists all models to be deleted
  - Dialog title shows model count (e.g., "Confirm Delete (3 models)")
  - Success message shows count of deleted models
  - Partial success handling when some deletions fail

v1.2.5
- **QC Resume Bug Fixes** - Fixed critical issues with resuming from saved results files
  - Fixed model name parsing when resuming: full model paths (e.g., `hf.co/namespace/repo:tag`) are now preserved correctly instead of being incorrectly derived from the `-M` argument
  - Fixed base model handling when resuming: the stored full model name is now used instead of just the tag portion
  - Fixed verification loop to use stored model names from results file
- **Cancellation Confirmation Prompt** - Added y/n confirmation before cancelling QC tests
  - First Ctrl+C now prompts "Cancel testing? (y/n)" instead of immediately cancelling
  - Prevents accidental cancellation of long-running tests
  - Second Ctrl+C still force exits immediately

v1.2.4
- **New --rejudge Argument for QC Command** - Re-run judgment without re-testing
  - New `--rejudge` argument to re-run judgment process for existing test results
  - Unlike `--force` which re-runs both testing and judgment, `--rejudge` only re-runs judgment
  - Useful for re-evaluating results with a different judge model or updated prompts
- **Improved Judge Response Parsing** - More robust handling of judge model responses
  - Case-insensitive JSON property matching for Reason/reason/REASON fields
  - Multiple regex patterns with increasing leniency for fallback parsing
  - Truncated JSON repair to handle incomplete responses from models
  - Increased num_predict from 200 to 800 to reduce truncated responses
  - Full raw JSON output displayed when reason parsing fails (for debugging)
- **Improved Judge Scoring Accuracy** - Fixed score interpretation issues
  - Changed JSON schema score type from "integer" to "number" for better model compatibility
  - Added explicit prompt instructions for 1-100 integer scoring (not 0.0-1.0 decimal)
  - Score normalization to handle both 0.0-1.0 and 1-100 ranges from different models
- **Fixed HuggingFace Model Verification in On-Demand Mode** - Registry check now supports HuggingFace models
  - On-demand mode (`--ondemand`) now properly verifies HuggingFace models (hf.co/...)
  - Checks HuggingFace API to verify repository and GGUF file existence
  - Supports various GGUF filename patterns for tag matching
- **Fixed Base Model Name Handling** - Full model names now preserved for base tag
  - Base model specified as full model name (e.g., `-b qwen3-coder:30b-a3b-fp16` or `-b hf.co/namespace/repo:tag`) is now used as-is
  - Previously, only the tag portion was extracted and combined with `-M` model name
- **Wildcard Tag Selection for QC Command** - Dynamically select quantizations with patterns
  - Support for wildcard patterns (`*`) in `-Q` argument (e.g., `Q4*`, `IQ*`, `*`)
  - Fetches available tags from HuggingFace API for `hf.co/...` models
  - Scrapes available tags from Ollama website for Ollama registry models
  - Case-insensitive pattern matching
  - New `ModelTagResolver` class for reusable tag resolution across commands
- **Improved On-Demand Cleanup** - Models pulled on-demand are now properly cleaned up on failure
  - On-demand models are removed when testing fails or is cancelled
  - Cleanup happens in exception handlers to ensure no orphaned models remain
  - Models tracked at class level for reliable cleanup across failures
  - Preload failures now also trigger cleanup of on-demand models
- **Improved Model Preload** - Better error handling and retry logic for model loading
  - Added retry logic (3 attempts with exponential backoff) for transient failures
  - Shows actual error message when preload fails (HTTP status, error details)
  - Uses configurable timeout (`--timeout`) for model loading
  - Handles timeout, connection errors, and server errors gracefully
- **Fixed Model Name Case Sensitivity** - Handle Ollama storing HuggingFace tags with different case
  - After pulling, resolves actual model name stored by Ollama (case-insensitive lookup)
  - Fixes issue where `Q4_0` is stored as `q4_0` causing preload to fail

v1.2.3
- **On-Demand Mode for QC Command** - Pull models automatically and remove after testing
  - New `--ondemand` argument to enable on-demand model management
  - Models missing from the Ollama instance are automatically pulled from the registry
  - Models that were already present are NOT removed (only on-demand pulled models)
  - After testing and judgment complete, on-demand models are removed to free disk space
  - State is persisted in results file for proper cleanup on resume
  - Works with both local and remote Ollama servers
  - Ideal for testing large models or many quantizations without consuming permanent storage
- **Context Length Support for QC Command** - Configure context length (num_ctx) for testing and judgment
  - Default test context length: 4096, default judge context length: 12288
  - Suite-level `contextLength` property in built-in test suites (v1base, v1quick, v1code)
  - External JSON test suites support `contextLength` at suite, category, and question levels
  - Hierarchical override system: question > category > suite
  - Console output displays context length at startup and when overridden
  - New `--judge-ctxsize` argument to configure judge model context length
- **Improved Console Output** - Context length settings displayed during test execution
  - Shows test and judge context lengths at the beginning of testing
  - Displays override notifications when context length changes (e.g., "Context length changed to 8192 (from category Code)")
- **Fixed Linux Terminal Display Issues** - Resolved ANSI color rendering problems
  - Fixed white box display issue in interactive REPL mode on Linux terminals
  - Downgraded PrettyConsole (3.1.0 → 2.0.0) and Spectre.Console (0.54.0 → 0.49.1) for compatibility
- **Improved Installer** - Streamlined installation process
  - Installer now only copies the main executable (no longer copies all directory files)
  - Added mechanism for platform-specific optional dependencies
  - Removed unnecessary libuv.dylib dependency for macOS (not needed in .NET 8+)
- **Fixed Bash Completion on Linux**
  - Fixed tab completion for model names containing colons (e.g., `osync ls qwen2:`)
  - Fixed file tab completion for qcview command

v1.2.2
- **Improved Judgment Prompt Format** - Better compatibility with more models
  - Instructions now in both system prompt and user message for redundancy
  - Clear text markers for RESPONSE A and RESPONSE B instead of JSON encoding
  - Question included for context with clear delimiters
  - Explicit rules to prevent models from judging quality/correctness instead of similarity
- **Verbose Judgment Output** - New `--verbose` flag to show judgment details
  - Displays question ID, score (color-coded), and first 4 lines of reason
  - Works with both serial and parallel judgment modes
  - Helps debug and understand judge model scoring
- **Fixed Parallel Verbose Output** - Verbose output now displays during parallel judgment execution
  - Previously showed all results after completion; now shows each result as it completes
- **Fixed Serial Verbose Progress** - Progress bar now displays alongside verbose output in serial mode
- **Improved Cancellation Handling** - Ctrl+C now immediately stops judgment without retrying
  - Cancellation exceptions are no longer retried 5 times
  - Judgment loop checks for cancellation before each question
- **Missing Reason Retry** - Judge API retries up to 5 times when response contains score but no reason
  - Warning displayed if reason still missing after all retries

v1.2.1
- **Bug Fix: Base Model Detection** - Fixed issue where base model wasn't correctly identified when using full model names
  - Base tag is now properly normalized when specified with full path (e.g., `user/model:tag`)
  - Existing results files with missing `IsBase` flag are automatically repaired on load
  - Judgment now correctly runs for quantizations that need it
- **Bug Fix: Output Filename Sanitization** - Model names with `/` or `\` are now converted to `-` in default output filename
  - Prevents file path issues when model name contains directory separators
- **Improved Startup Output** - Output file path is now displayed early in the execution
  - Shows right after loading test suite, before judge model verification
- **Cancellation Improvements** - Better handling of Ctrl+C during API calls
  - Cancellation token now passed to HTTP requests for immediate cancellation
  - Wrapped cancellation exceptions are properly detected

v1.2.0
- **Coding Test Suite** - New `v1code` test suite for evaluating code generation quality
  - 50 challenging coding questions across 5 languages: Python, C++, C#, TypeScript, Rust
  - Double token output limit (8192) for longer code responses
  - Questions include instruction to limit response size
  - Available as `-T v1code` or via external `v1code.json` file
- **Configurable Token Output** - Test suites now support custom `numPredict` values
  - Each test suite can specify its own maximum token output
  - External JSON test suites support `numPredict` property (default: 4096)
  - Displayed in test suite info when non-default value is used
- **Improved Model Existence Check** - Pull command now uses Ollama registry API for faster, more reliable model validation
  - Uses `registry.ollama.ai/v2/` manifest endpoint instead of HTML scraping
  - Properly handles both library models and user models
  - Faster response times and more accurate error messages
- **True Independent Parallel Judgment** - Testing continues to next quantization while judgment runs in background
  - Testing no longer waits for judgment to complete before moving to next quantization
  - Background judgment tasks tracked and awaited at the end with progress display
  - Progress bars show real-time status for both testing and judgment
- **Improved Progress Display** - Better visibility into parallel operations
  - Dual progress bars during testing (Testing + Judging) in parallel mode
  - Background judgment status shown after each quantization completes
  - Final wait screen shows progress for all pending judgment tasks
- **Configurable API Timeout** - Added `--timeout` argument for testing and judgment API calls
  - Default increased from 300 to 600 seconds for longer code generation
  - Configurable via `--timeout <seconds>` argument
  - Applies to both test model and judge model API calls
- **Resume Support** - Gracefully handle interruptions and resume from where you left off
  - Press Ctrl+C to save partial results and exit cleanly
  - Re-run the same command to resume testing from the last saved question
  - Partial quantization results are preserved in the JSON file
  - Progress bar shows resumed position when continuing
  - Missing judgments are automatically detected and re-run on resume
- **UI Improvements**
  - Unified color scheme: lime for good scores (80%+) and performance above 100%
  - Orange color for performance below 100%

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
