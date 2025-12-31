# Testing Guide for osync

This document provides comprehensive testing procedures for all osync commands and features.

## Prerequisites

- Ollama installed and running on local machine (default: http://localhost:11434)
- At least one model installed locally for testing
- (Optional) Remote Ollama server for testing remote operations
- (Optional) Multiple test models of varying sizes

## Testing Checklist

### Core Commands

#### 1. List Command (`ls`)

**Local Listing:**
```bash
# Test basic listing
osync ls
# Expected: Display all local models with ID, size, and modified date

# Test pattern matching
osync ls "llama*"
osync ls "*:7b"
osync ls "*test*"
# Expected: Display only matching models

# Test sorting options
osync ls --size          # Largest first
osync ls --sizeasc       # Smallest first
osync ls --time          # Newest first
osync ls --timeasc       # Oldest first
# Expected: Models sorted according to option
```

**Remote Listing:**
```bash
# Test remote server listing
osync ls http://192.168.0.100:11434
osync ls "llama*" http://192.168.0.100:11434
# Expected: Display models from remote server
```

**Edge Cases:**
- Empty model list
- Models with special characters in names
- Models with registry paths (hf.co/user/model)

---

#### 2. Copy Command (`cp`)

**Local Copy:**
```bash
# Test local copy (backup)
osync cp llama3 llama3-backup
osync cp qwen2:7b qwen2:backup-v1
# Expected: Create copy of model locally

# Test destination exists
osync cp llama3 <existing-model>
# Expected: Error message preventing overwrite
```

**Local to Remote:**
```bash
# Test upload to remote server
osync cp llama3 http://192.168.0.100:11434
osync cp qwen2:7b http://192.168.0.100:11434

# Test with bandwidth throttling
osync cp llama3 http://192.168.0.100:11434 -bt 50MB
# Expected: Upload with progress bar, speed limited to 50MB/s

# Test incremental upload (run same command twice)
osync cp llama3 http://192.168.0.100:11434
# Expected: Second run skips already uploaded layers
```

**Remote to Remote:**
```bash
# Test remote-to-remote transfer (requires registry model)
osync cp http://server1:11434/llama3 http://server2:11434/llama3

# Test with custom buffer size
osync cp http://server1:11434/llama3 http://server2:11434/llama3 -BufferSize 1GB
# Expected: Transfer with memory buffering, progress display

# Test with locally created model
osync cp http://server1:11434/custom-model http://server2:11434/custom-model
# Expected: Error indicating model must be from registry
```

**Edge Cases:**
- Very large models (>10GB)
- Network interruptions
- Invalid destination URLs
- Models without `:latest` tag (should auto-append)

---

#### 3. Rename Command (`rename`, `mv`, `ren`)

```bash
# Test basic rename
osync rename llama3 my-llama3
osync mv qwen2:7b qwen2:backup

# Test rename to existing model
osync ren llama3 <existing-model>
# Expected: Error preventing overwrite

# Test rename with verification
osync rename test-model test-model-v2
# Expected: Copy → Verify → Delete original
```

**Edge Cases:**
- Source model doesn't exist
- Destination already exists
- Models with special characters

---

#### 4. Remove Command (`rm`, `delete`, `del`)

```bash
# Test single model deletion
osync rm test-model
osync rm llama3:backup

# Test pattern deletion
osync rm "test-*"
osync rm "*:backup"
# Expected: Confirmation prompt, then delete matching models

# Test remote deletion
osync rm "old-*" http://192.168.0.100:11434
```

**Edge Cases:**
- Model doesn't exist
- Empty pattern match
- Attempting to delete all models (`*`)

---

#### 5. Update Command (`update`)

```bash
# Test update single model
osync update llama3
# Expected: Update if new version available, or "already up to date"

# Test update all models
osync update
osync update "*"
# Expected: Update all outdated models

# Test pattern update
osync update "llama*"
osync update "*:7b"

# Test remote update
osync update llama3 http://192.168.0.100:11434
osync update "*" http://192.168.0.100:11434
```

**Edge Cases:**
- Model already up to date
- Model not in registry
- Network failures during update

---

#### 6. Show Command (`show`)

```bash
# Test local model info
osync show llama3
osync show qwen2:7b
# Expected: Display model metadata, parameters, configuration

# Test remote model info
osync show llama3 http://192.168.0.100:11434
```

**Edge Cases:**
- Model doesn't exist
- Model without extended info

---

#### 7. Pull Command (`pull`)

```bash
# Test pull from registry
osync pull llama3
osync pull qwen2:7b
osync pull hf.co/unsloth/llama3
# Expected: Download model with progress

# Test pull to remote server
osync pull llama3 http://192.168.0.100:11434

# Test pull non-existent model
osync pull fake-model-123
# Expected: Error message indicating model not found
```

**Edge Cases:**
- Model already exists locally
- Network failures
- Invalid model names
- Registry unavailable

---

#### 8. Run/Chat Command (`run`, `chat`)

```bash
# Test local chat
osync run llama3
osync chat qwen2:7b
# Expected: Preload model, enter chat mode

# Test remote chat
osync run llama3 http://192.168.0.100:11434

# Test exit methods
# - Type "/bye"
# - Press Ctrl+D
# Expected: Both methods exit cleanly
```

**Edge Cases:**
- Model doesn't exist
- Model fails to load
- Very long conversations

---

#### 9. Process Status Command (`ps`)

```bash
# Test local ps
osync ps
# Expected: Show loaded models with VRAM usage, percentage if partially loaded

# Test remote ps
osync ps http://192.168.0.100:11434

# Test with no loaded models
# Expected: "No models currently loaded in memory"
```

**Verification:**
- VRAM percentage shows when usage < model size
- Table format matches specification
- Context length and expiration time displayed

---

#### 10. Load Command (`load`)

```bash
# Test load model
osync load llama3
osync load qwen2:7b

# Test with custom keep-alive
osync load llama3 --keepalive 30m
osync load llama3 --keepalive 1h

# Test remote load
osync load llama3 http://192.168.0.100:11434

# Verify with ps command
osync ps
# Expected: Model appears in loaded list
```

**Edge Cases:**
- Model doesn't exist
- Insufficient VRAM
- Already loaded model

---

#### 11. Unload Command (`unload`)

```bash
# Test unload model
osync unload llama3
osync unload qwen2:7b

# Test remote unload
osync unload llama3 http://192.168.0.100:11434

# Verify with ps command
osync ps
# Expected: Model no longer in loaded list
```

**Edge Cases:**
- Model not loaded
- Model doesn't exist

---

#### 12. Quantization Comparison Command (`qc`)

**Prerequisites:**
- Multiple quantizations of same model family (e.g., llama3.2:f16, llama3.2:q4_k_m, llama3.2:q8_0)
- Ollama v0.12.11+ (logprobs support required)
- Sufficient time for 50 questions per quantization (~5-10 minutes each)

**Basic Testing:**
```bash
# Test with minimum required arguments
osync qc -M llama3.2 -Q q4_k_m,q5_k_m
# Expected:
#   - Use f16 as base (default)
#   - Create llama3.2.qc.json results file
#   - Test f16, q4_k_m, q5_k_m (3 quantizations)
#   - 50 questions × 3 = 150 total tests
#   - Progress bars for each quantization
#   - Save after each quantization completes

# Verify results file created
ls llama3.2.qc.json
# Expected: File exists, JSON format
```

**Custom Base Quantization:**
```bash
# Test with custom base
osync qc -M llama3.2 -Q q4_k_m,q5_k_m -B fp16
# Expected: Use fp16 as base instead of f16
```

**Custom Output File:**
```bash
# Test custom output filename
osync qc -M llama3.2 -Q q4_k_m,q5_k_m -O my-test-results.json
# Expected: Create my-test-results.json instead of default
```

**Remote Server Testing:**
```bash
# Test on remote server
osync qc -M llama3.2 -Q q4_k_m,q5_k_m -D http://192.168.0.100:11434
# Expected: Connect to remote, test models there
```

**Incremental Testing:**
```bash
# Initial test with 2 quantizations
osync qc -M llama3.2 -Q q4_k_m,q5_k_m

# Add more quantizations later
osync qc -M llama3.2 -Q q8_0,q6_k
# Expected:
#   - Load existing llama3.2.qc.json
#   - Skip f16, q4_k_m, q5_k_m (already tested)
#   - Test only q8_0, q6_k (new quantizations)
#   - Append results to same file
```

**Custom Test Parameters:**
```bash
# Test with adjusted parameters
osync qc -M llama3.2 -Q q4_k_m -Te 0.1 -S 42 -To 0.9 -Top 40
# Expected: Run with temperature=0.1, seed=42, top_p=0.9, top_k=40

# Test with penalties
osync qc -M llama3.2 -Q q4_k_m -R 1.1 -F 0.5
# Expected: Apply repeat_penalty=1.1, frequency_penalty=0.5
```

**Progress and Status:**
```bash
# Monitor during execution
osync qc -M llama3.2 -Q q4_k_m,q5_k_m,q8_0
# Expected output:
#   - "Using test suite: v1base (50 questions)"
#   - "Creating new results file" or "Loaded existing results file (N quantizations tested)"
#   - For each quantization:
#     - "Testing quantization: llama3.2:q4_k_m"
#     - "Preloading model..."
#     - Progress bar: "Testing q4_k_m  Reasoning 5/10"
#     - "✓ q4_k_m complete"
#   - "Testing complete!"
#   - "Results saved to: llama3.2.qc.json"
#   - "View results with: osync qcview llama3.2.qc.json"
```

**Edge Cases:**
```bash
# Model doesn't exist
osync qc -M nonexistent -Q q4,q8
# Expected: Error message for each missing model variant

# Quantization mismatch (different families)
osync qc -M llama3.2 -Q q4_k_m
# Then try: osync qc -M qwen2.5 -Q q5_k_m -O llama3.2.qc.json
# Expected: Error - model family mismatch

# Different parameter sizes
osync qc -M llama3.2 -Q q4_k_m  # 3B model
# Then: osync qc -M llama3.2 -Q q8_0 -O llama3.2.qc.json  # If 7B variant
# Expected: Error - parameter size mismatch

# Invalid temperature/seed
osync qc -M llama3.2 -Q q4_k_m -Te 2.0
# Expected: Accepts (Ollama will handle validation)

# Empty quants list
osync qc -M llama3.2 -Q ""
# Expected: Error - no quantization tags specified
```

**Validation Tests:**
```bash
# Test model metadata validation
# Ensure all quantizations are same family and parameter size
osync qc -M llama3.2:3b -Q f16,q4_k_m,q8_0
# Expected: All pass (same 3B family)

osync qc -M llama3.2:3b -Q f16
# Then manually edit JSON to change family
# Then: osync qc -M llama3.2:3b -Q q4_k_m
# Expected: Error detecting family mismatch
```

**Interruption Handling:**
```bash
# Start test, press Ctrl+C during execution
osync qc -M llama3.2 -Q q4_k_m,q5_k_m,q8_0
# Press Ctrl+C after q4_k_m finishes
# Expected:
#   - Results file contains completed quantizations (q4_k_m)
#   - Can resume later: osync qc -M llama3.2 -Q q5_k_m,q8_0
```

**Tag Format Flexibility (v1.1.7):**
```bash
# Test with tags without colons (tag portion only)
osync qc -M qwen2 -Q 0.5b-instruct-q8_0,0.5b-instruct-q6_k -B 0.5b-instruct-fp16
# Expected:
#   - Constructs full model names: qwen2:0.5b-instruct-fp16, qwen2:0.5b-instruct-q8_0, etc.
#   - Uses tag portion for tracking in results file
#   - Retrieves actual quantization type from API (details.quantization_level)

# Test with full model names (including colons)
osync qc -M qwen2 -Q qwen2:0.5b-instruct-q8_0,qwen2:0.5b-instruct-q6_k -B qwen2:0.5b-instruct-fp16
# Expected:
#   - Uses provided model names as-is
#   - Extracts tag portion (after last ":") for tracking
#   - Retrieves quantization type from API, not from tag name

# Test mixed format (some with ":", some without)
osync qc -M qwen2 -Q 0.5b-instruct-q8_0,qwen2:0.5b-instruct-q6_k -B 0.5b-instruct-fp16
# Expected: Both formats work correctly in same command
```

**Output Token Limiting (v1.1.7):**
```bash
# Test that verbose question responses don't hang
osync qc -M llama3.2 -Q q4_k_m
# Expected:
#   - Each question limited to 4096 output tokens
#   - No hanging on question 8 or other verbose responses
#   - All 50 questions complete successfully
#   - Responses truncated at token limit if needed

# Monitor progress through all question categories
# Expected categories in order:
#   1. Reasoning (10 questions)
#   2. Math (10 questions)
#   3. Finance (10 questions)
#   4. Technology (10 questions)
#   5. Science (10 questions)
```

**Error Handling and Exit Codes (v1.1.7):**
```bash
# Test when all models fail to test
osync qc -M nonexistent -Q q4_k_m,q8_0
echo $?
# Expected:
#   - Error messages for each model not found
#   - Message: "No quantizations were successfully tested - no results file created"
#   - Exit code: 1 (failure)
#   - No results file created

# Test when some models succeed, some fail
osync qc -M llama3.2 -Q q4_k_m,nonexistent-tag,q8_0
echo $?
# Expected:
#   - Success for q4_k_m and q8_0
#   - Error for nonexistent-tag
#   - Message: "Results saved to: llama3.2.qc.json"
#   - Message: "Successfully tested 2 quantization(s)"
#   - Exit code: 0 (success, since some results exist)
#   - Results file contains only successful tests
```

**Model Size Retrieval (v1.1.7):**
```bash
# Verify model size is correctly retrieved from /api/tags
osync qc -M llama3.2 -Q q4_k_m
osync qcview -F llama3.2.qc.json
# Expected:
#   - Size column shows correct disk size (e.g., "2.7 GB")
#   - Size matches what 'osync ls' shows for same model
#   - No errors about missing 'size' field during testing
```

**Understanding Scoring Algorithm:**

The 4-component scoring system provides comprehensive quality assessment:

**1. Logprobs Divergence (70% weight) - Confidence Test:**
```
Purpose: Measures how confident the model is in its token choices
Calculation: 100 × exp(-confidence_difference × 2)
Uses: Sequence-level average confidence (mean logprob)
Range: 0-100%

How it works:
  - Calculates average confidence (mean logprob) for each sequence
  - Compares base vs quantization confidence levels
  - Higher weight because model confidence is the strongest quality indicator

Example:
  Base: average logprob -0.5 (good confidence)
  Quant: average logprob -0.6 (slightly lower confidence)
  → Small divergence, high score

  Base: average logprob -0.5
  Quant: average logprob -2.0 (much lower confidence)
  → Large divergence, low score

Interpretation:
  95-100%: Quantization maintains very similar confidence levels
  85-95%:  Slightly less confident in predictions
  70-85%:  Noticeably different confidence patterns
  <70%:    Major confidence degradation or uncertainty
```

**2. Perplexity (20% weight) - Overall Confidence Test:**
```
Purpose: Measures model's overall confidence (lower perplexity = more confident)
Calculation: 100 × exp(-0.5 × |1 - perplexity_ratio|)
Perplexity: exp(-average_logprob)
Range: 0-100%

How perplexity works:
  - Lower perplexity = model is more confident/certain
  - Higher perplexity = model is confused/uncertain
  - Ratio compares quant perplexity to base perplexity

Example:
  Base perplexity: 1.5 (confident)
  Quant perplexity: 1.5 → Ratio: 1.0 → Score: 100%

  Base perplexity: 1.5
  Quant perplexity: 2.0 → Ratio: 1.33 → Score: 84.6%

  Base perplexity: 1.5
  Quant perplexity: 5.0 → Ratio: 3.33 → Score: 31.2%

Interpretation:
  95-100%: Quantization maintains similar overall confidence
  85-95%:  Slightly less confident overall
  70-85%:  Noticeably higher uncertainty
  <70%:    Major confidence degradation
```

**3. Token Similarity (5% weight) - Sequence Match Test:**
```
Purpose: Measures if quantization produces similar token sequences
Calculation: Uses Longest Common Subsequence (LCS)
Formula: (LCS_length / max_length) × 100
Range: 0-100%

Example:
  Base output:    "The capital of France is Paris."
  Quant output:   "The capital of France is Paris."
  LCS match:      100% (all tokens in common subsequence)

  Base output:    "The capital of France is Paris."
  Quant output:   "The capital of France is Lyon."
  LCS match:      ~85% (most tokens match in sequence)

Interpretation:
  95-100%: Quantization produces virtually identical outputs
  85-95%:  Minor word choice differences
  70-85%:  Noticeable differences in phrasing
  <70%:    Significant divergence in responses
```

**4. Length Consistency (5% weight) - Verbosity Test:**
```
Purpose: Checks if quantization produces similar-length responses
Calculation: 100 × exp(-2 × |1 - length_ratio|)
Range: 0-100%

Example:
  Base: 50 tokens
  Quant: 50 tokens → Ratio: 1.0 → Score: 100%

  Base: 50 tokens
  Quant: 45 tokens → Ratio: 0.9 → Score: 81.9%

  Base: 50 tokens
  Quant: 30 tokens → Ratio: 0.6 → Score: 44.9%

Interpretation:
  95-100%: Nearly identical answer lengths
  85-95%:  10-20% length variation (usually acceptable)
  70-85%:  Significant length differences
  <70%:    Major verbosity changes (much shorter/longer)
```

**Overall Confidence Score:**
```
Weighted average of all four components:
  = (Logprobs Divergence × 0.70) +
    (Perplexity × 0.20) +
    (Token Similarity × 0.05) +
    (Length Consistency × 0.05)

Example calculation:
  Logprobs Divergence:  88.0% × 0.70 = 61.6%
  Perplexity:           90.0% × 0.20 = 18.0%
  Token Similarity:     92.5% × 0.05 =  4.6%
  Length Consistency:   95.0% × 0.05 =  4.8%
  ─────────────────────────────────────────
  Overall Score:                      89.0%

Quality interpretation (color coding):
  90-100% (Green):    Excellent - minimal quality loss
  80-90%  (Lime):     Very good - acceptable for most uses
  70-80%  (Yellow):   Good - noticeable but manageable degradation
  50-70%  (Orange):   Moderate - quality loss may affect performance
  <50%    (Red):      Poor - significant degradation
```

**Practical Testing Example:**
```bash
# Test llama3.2 quantizations
osync qc -M llama3.2 -Q q4_k_m,q5_k_m,q8_0 -B f16

# Expected typical results (example):
#   f16 (base):    100% overall (by definition)
#   q8_0:          95-98% (excellent, minimal loss)
#   q5_k_m:        90-94% (very good balance)
#   q4_k_m:        85-90% (good, some quality trade-off)
#   q2_k:          70-80% (moderate loss, much smaller)

# View results
osync qcview llama3.2.qc.json

# Analyze component scores to understand where quality is lost:
#   - High token similarity but low logprobs → same words, less confident
#   - Low token similarity but high logprobs → different words, but confident
#   - Low length consistency → responses much shorter/longer
#   - Low perplexity score → overall uncertainty increased
```

---

#### 12b. Judge Model Scoring Tests

**Serial Judge Mode (Default):**
```bash
# Local judge model - runs after each quantization
osync qc -M llama3.2 -Q q4_k_m,q8_0 --judge mistral
# Expected:
#   - Testing q4_k_m...
#   - ✓ Completed q4_k_m
#   - Running judgment for: q4_k_m
#   - Judging q4_k_m  [===       ] 30%
#   - ✓ Judging q4_k_m complete
#   - Testing q8_0...
#   - (continues...)
```

**Parallel Judge Mode:**
```bash
# Parallel mode - judging happens after all testing completes
osync qc -M llama3.2 -Q q4_k_m,q8_0 --judge mistral --mode parallel
# Expected:
#   - Testing q4_k_m...
#   - Testing q8_0...
#   - Running parallel judgment for 2 quantization(s)...
#   - Judging all quantizations [=====     ] 50%
#   - ✓ All judgments complete
```

**Remote Judge Model:**
```bash
# Judge on different server
osync qc -M llama3.2 -Q q4_k_m --judge http://192.168.1.100:11434/mistral
# Expected:
#   - Judge model: mistral:latest @ http://192.168.1.100:11434
#   - Tests run normally, judgment calls go to remote server
```

**Remote Test Models + Local Judge:**
```bash
# Test models on remote, judge locally
osync qc -d http://192.168.1.100:11434/ -M llama3.2 -Q q4_k_m --judge mistral
# Expected:
#   - Model testing uses remote server
#   - Judgment uses local server (localhost:11434)
```

**Judge Skip Logic:**
```bash
# First run with judge
osync qc -M llama3.2 -Q q4_k_m --judge mistral
# Second run - should skip existing judgments
osync qc -M llama3.2 -Q q8_0 --judge mistral
# Expected: q4_k_m judgment skipped (already exists), q8_0 judged

# Force re-judgment
osync qc -M llama3.2 -Q q4_k_m --judge mistral --force
# Expected: q4_k_m re-judged even though judgment exists

# Different judge model - should re-judge
osync qc -M llama3.2 -Q q4_k_m --judge llama3.2
# Expected: All questions re-judged because different model
```

**Judge Results Verification:**
```bash
# Run with judge
osync qc -M llama3.2 -Q q4_k_m,q8_0 --judge mistral

# View results with judgment
osync qcview llama3.2.qc.json
# Expected output:
#   - Header shows "Judge Model: mistral:latest (50% metrics + 50% judgment)"
#   - Table shows columns: Final Score, Metrics Score, Judge Score
#   - Results sorted by Final Score (not just Metrics)

# JSON output should include judgment data
osync qcview llama3.2.qc.json -Fo json
# Verify JSON contains:
#   - "HasJudgmentScoring": true
#   - "JudgeModel": "mistral:latest"
#   - "AverageJudgmentScore" per quantization
#   - "JudgmentScore" per question
```

**Partial Judgment Handling:**
```bash
# If only some quants have judgment, metrics-only mode is used
# Create results with one judged quant
osync qc -M llama3.2 -Q q4_k_m --judge mistral
# Add another quant without judgment
osync qc -M llama3.2 -Q q8_0
# View results
osync qcview llama3.2.qc.json
# Expected: Uses metrics-only display (no Judge Score column)
#   - Because not ALL quants have judgment scoring
```

**Judge Model Edge Cases:**
```bash
# Invalid judge URL format
osync qc -M llama3.2 -Q q4_k_m --judge http://192.168.1.100
# Expected: Warning about invalid format

# Non-existent judge model
osync qc -M llama3.2 -Q q4_k_m --judge nonexistent-model
# Expected: Warning about failed to preload judge model, judgment skipped

# Judge model without tag (should add :latest)
osync qc -M llama3.2 -Q q4_k_m --judge mistral
# Expected: Uses mistral:latest
```

**Judge Scoring Verification:**
```bash
# Run tests and check scoring
osync qc -M llama3.2 -Q q4_k_m,q8_0 --judge mistral
osync qcview llama3.2.qc.json

# Verify scoring behavior:
#   - Judge scores: 1-100 per question
#   - AverageJudgmentScore: average of all question judge scores
#   - FinalScore = (MetricsScore × 0.5) + (JudgeScore × 0.5)
#   - Higher quality quants should have higher both metrics AND judge scores

# Check JSON for detailed per-question judgment
osync qcview llama3.2.qc.json -Fo json -O results.json
# Each QuestionResult should have Judgment: { JudgeModel, Score, JudgedAt }
```

---

#### 13. View Quantization Results Command (`qcview`)

**Basic Table View (Console):**
```bash
# View results in table format (file as positional argument)
osync qcview llama3.2.qc.json
# Expected output:
#   - Header panel with model info, test suite, options
#   - Base model info panel
#   - Main results table with columns:
#     - Tag, Quant, Size, Overall Score
#     - Token Similarity, Logprobs Divergence, Length Consistency, Perplexity
#     - Eval Speed, Eval vs Base, Prompt Speed, Prompt vs Base
#   - Category breakdown table
#   - Color coding: green ≥90%, lime ≥80%, yellow ≥70%, orange ≥50%, red <50%
```

**Table Export to File (v1.1.9):**
```bash
# Export table to text file
osync qcview llama3.2.qc.json -O report.txt
# Expected output:
#   - Message: "Table results saved to: report.txt (X.XX KB)"
#   - File contains plain text table (no ANSI colors)
#   - All columns properly aligned with correct values
#   - Eval/Prompt speeds formatted as numbers (not "F1-12" bug)
```

**JSON Output:**
```bash
# View as JSON in console
osync qcview llama3.2.qc.json -Fo json
# Expected:
#   - "[yellow]JSON Results:[/]" header
#   - JSON output without markup parsing errors (fixed in v1.1.9)
#   - Contains: BaseModelName, BaseTag, BaseFamily, etc.
#   - QuantScores array with all quantizations
#   - CategoryScores for each quantization
#   - QuestionScores array (detailed per-question breakdown)

# Export to JSON file
osync qcview llama3.2.qc.json -Fo json -O report.json
# Expected:
#   - Message: "JSON results saved to: report.json (X.XX KB)"
#   - File contains valid, indented JSON
```

**Results Validation:**
```bash
# Test with valid results file
osync qcview llama3.2.qc.json
# Verify:
#   - Overall scores are 0-100%
#   - Category scores are 0-100%
#   - Performance percentages make sense (faster quants > 100%)
#   - Disk sizes match model sizes
#   - Quantization types are correct
#   - Eval/Prompt speeds show actual numbers (not format strings)

# Test score color coding (v1.1.9 updated thresholds)
# Green (90-100%): Excellent preservation
# Lime (80-90%): Very good
# Yellow (70-80%): Good
# Orange (50-70%): Moderate loss
# Red (<50%): Significant degradation
```

**Edge Cases:**
```bash
# File doesn't exist
osync qcview nonexistent.json
# Expected: "Error: Results file not found: nonexistent.json"

# Invalid JSON file
echo "invalid" > test.json
osync qcview test.json
# Expected: "Error: Failed to parse results file"

# Empty results (no quantizations tested)
# Create results file with empty Results array
osync qcview empty-results.json
# Expected: "No results found in file"

# Missing base quantization
# Edit JSON to remove IsBase flag from all entries
osync qcview no-base.json
# Expected: "Error: No base quantization found in results"

# Invalid format parameter
osync qcview llama3.2.qc.json -Fo invalid
# Expected: Fallback to table format

# JSON to console with special characters (v1.1.9 fix)
osync qcview llama3.2.qc.json -Fo json
# Expected: No "Malformed markup tag" error
# JSON [ and ] characters not interpreted as Spectre.Console markup
```

**Verification Tests:**
```bash
# Compare table vs JSON output
osync qcview llama3.2.qc.json -O table.txt
osync qcview llama3.2.qc.json -Fo json -O output.json
# Manually verify:
#   - Scores match between table and JSON
#   - All quantizations appear in both
#   - Category breakdowns are consistent
#   - File sizes displayed correctly in output messages
```

**Performance Metrics Validation:**
```bash
# Verify performance percentages
osync qcview llama3.2.qc.json
# Check:
#   - Smaller quants (q4_k_m) should have > 100% speed (faster)
#   - Larger quants (q8_0) might have < 100% speed (slower)
#   - f16/fp16 base should show 100% (baseline)
#   - Prompt and eval speeds show actual tok/s values
#   - Arrows: ↑ for faster than base, ↓ for slower, = for same
```

**Category Breakdown Tests:**
```bash
# Verify category scores
osync qcview llama3.2.qc.json
# Check category breakdown table shows:
#   - All 5 categories: Reasoning, Math, Finance, Technology, Science
#   - Scores per category for each quantization
#   - Consistent with overall confidence score
#   - Category scores average to overall score (weighted)
```

**Table Formatting (v1.1.9 fixes):**
```bash
# Test table rendering to console
osync qcview llama3.2.qc.json
# Verify:
#   - Columns align properly
#   - Numbers formatted correctly (1 decimal place for %)
#   - Color codes work in terminal
#   - Unicode arrows (↑↓) display for performance
#   - Sizes shown in human-readable format (GB, MB)

# Test table export to file
osync qcview llama3.2.qc.json -O report.txt
# Verify:
#   - Eval speed shows numbers like "122.8" (not "F1-12")
#   - Prompt speed shows numbers like "1609.7" (not "F1-12")
#   - All columns correctly formatted with values
```

**Output File Tests:**
```bash
# Table export to file (v1.1.9)
osync qcview llama3.2.qc.json -O report.txt
# Expected: "Table results saved to: report.txt (X.XX KB)"
# Verify file exists and contains properly formatted table

# JSON export to file
osync qcview llama3.2.qc.json -Fo json -O report.json
# Expected: "JSON results saved to: report.json (X.XX KB)"
# Verify file exists and contains valid JSON

# Overwrite existing file
osync qcview llama3.2.qc.json -Fo json -O report.json
# Expected: Overwrites without warning, shows new size

# Invalid output path
osync qcview llama3.2.qc.json -Fo json -O /invalid/path/file.json
# Expected: Error about invalid path or permissions
```

---

### Manage TUI Command

#### Launch and Navigation

```bash
# Test local manage
osync manage

# Test remote manage
osync manage http://192.168.0.100:11434
```

**Basic Navigation:**
- **Up/Down arrows** - Navigate model list
- **Page Up/Down** - Scroll page
- **Home/End** - Jump to start/end
- Expected: Smooth navigation, selected row highlighted

---

#### Filtering

**Test Cases:**
1. Press `/` to start filtering
2. Type "llama" - Expected: Only llama models shown
3. Type more characters - Expected: List updates in real-time
4. Press `Esc` - Expected: Filter cleared, all models shown
5. Check top bar shows "Filter: <text>"

---

#### Sorting

**Test Cases:**
1. Press `Ctrl+O` repeatedly - Expected: Cycle through sort modes
2. Verify top bar updates: Name+, Name-, Size-, Size+, Created-, Created+
3. Verify models reorder correctly for each mode
4. Verify selected model stays selected during sort

---

#### Theme Switching

**Test Cases:**
1. Press `Ctrl+T` repeatedly - Expected: Cycle through themes
2. Verify themes: Default, Dark, Blue, Solarized, Gruvbox, Nord, Dracula
3. Verify colors change for: top bar, list rows, selected row, bottom bar

---

#### Copy Operation (Ctrl+C)

**Single Model Copy:**
1. Select a model
2. Press `Ctrl+C`
3. Enter destination name (local copy)
4. Expected: Dialog with text field, Enter key works
5. Verify copy succeeds

**Batch Copy:**
1. Press `Space` to select multiple models
2. Press `Ctrl+C`
3. Enter remote server URL (required for batch)
4. Expected: Copy all selected models with progress
5. Verify models copied incrementally
6. Expected: Return to same model position

**Edge Cases:**
- Empty destination
- Destination exists
- Network failures (remote)

---

#### Rename Operation (Ctrl+M)

**Test Cases:**
1. Select a model
2. Press `Ctrl+M`
3. Enter new name
4. Press `Enter`
5. Expected: Model renamed, cursor stays on renamed model

**Edge Cases:**
- Empty new name
- Destination exists
- Source doesn't exist (deleted meanwhile)

---

#### Run/Chat Operation (Ctrl+R)

**Test Cases:**
1. Select a model
2. Press `Ctrl+R`
3. Expected: Exit TUI, show console, preload model, enter chat
4. Type message, verify response
5. Type `/bye` or Ctrl+D
6. Expected: Return to manage TUI, same model selected

---

#### Show Operation (Ctrl+S)

**Test Cases:**
1. Select a model
2. Press `Ctrl+S`
3. Expected: Exit TUI, show model info in console
4. Press any key
5. Expected: Return to manage TUI, same model selected

---

#### Delete Operation (Ctrl+D)

**Single Delete:**
1. Select a model
2. Press `Ctrl+D`
3. Confirm deletion
4. Expected: Model deleted, cursor moves to next/previous model

**Batch Delete:**
1. Press `Space` to select multiple models
2. Press `Ctrl+D`
3. Confirm deletion
4. Expected: All selected models deleted

**Edge Cases:**
- Delete last model in list
- Delete first model in list
- Cancel confirmation

---

#### Update Operation (Ctrl+U)

**Single Update:**
1. Select a model
2. Press `Ctrl+U`
3. Expected: Exit TUI, show console with update progress
4. Verify "updated successfully" or "already up to date"
5. Press any key
6. Expected: Return to manage TUI, same model selected

**Batch Update:**
1. Press `Space` to select multiple models
2. Press `Ctrl+U`
3. Expected: Update all selected models sequentially
4. Verify each shows correct status

---

#### Pull Operation (Ctrl+P)

**Test Cases:**
1. Press `Ctrl+P`
2. Enter model name (e.g., "llama3")
3. Press `Enter`
4. Expected: Validate model exists on ollama.com
5. If valid: Exit TUI, show console with pull progress
6. If invalid: Error dialog
7. Press any key to return

**Edge Cases:**
- Non-existent model
- Model already exists
- Network failures

---

#### Load Operation (Ctrl+L)

**Test Cases:**
1. Select a model
2. Press `Ctrl+L`
3. Expected: Load model into memory
4. Verify with `Ctrl+X` (ps)

---

#### Unload Operation (Ctrl+K)

**Test Cases:**
1. Select a loaded model
2. Press `Ctrl+K`
3. Expected: Unload model from memory
4. Verify with `Ctrl+X` (ps)

---

#### Process Status (Ctrl+X)

**Test Cases:**
1. Load a model (Ctrl+L)
2. Press `Ctrl+X`
3. Expected: Dialog showing loaded models in table format
4. Verify VRAM percentage shows when partially loaded
5. Verify matches CLI `osync ps` format
6. Press Close or Esc

---

#### Multi-selection

**Test Cases:**
1. Press `Space` on multiple models
2. Verify `[X]` checkbox appears
3. Press `Space` again to deselect
4. Verify `[ ]` checkbox appears
5. Test batch operations (copy, delete, update) with selection

---

#### Exit (Ctrl+Q / Esc)

**Test Cases:**
1. Press `Ctrl+Q` - Expected: Confirmation dialog
2. Select "No" - Expected: Stay in manage
3. Press `Ctrl+Q` again, select "Yes" - Expected: Exit cleanly
4. Same tests with `Esc` key

---

### Interactive REPL Mode

```bash
# Launch REPL
osync

# Test tab completion
> <Tab>
# Expected: Show available models

> cp <Tab>
# Expected: Show available models

# Test command history
> ls
> cp llama3 backup
> <Up arrow>
# Expected: Show previous command

# Test exit
> exit
# Expected: Exit cleanly
```

---

## Performance Tests

### Large Model Transfer

```bash
# Test with 10GB+ model
osync cp large-model http://remote:11434
# Monitor: Progress accuracy, speed calculation, memory usage
```

### Batch Operations

```bash
# In manage TUI: Select 10+ models
# Copy to remote server
# Monitor: Progress, memory usage, completion time
```

### Network Resilience

```bash
# Start large transfer
# Disconnect network briefly
# Expected: Error handling, no corruption
```

---

## Regression Tests

Run after any code changes:

1. **Basic Workflow:**
   ```bash
   osync ls
   osync cp test-model test-backup
   osync rename test-backup test-renamed
   osync rm test-renamed
   ```

2. **Manage TUI Workflow:**
   ```bash
   osync manage
   # Navigate, filter, sort, select, copy, delete
   # Cycle themes, check all keyboard shortcuts
   ```

3. **Remote Operations:**
   ```bash
   osync ls http://remote:11434
   osync cp local-model http://remote:11434
   osync update remote-model http://remote:11434
   ```

---

## Error Scenarios

### Expected Errors

1. **Model not found:**
   ```bash
   osync cp non-existent-model backup
   # Expected: Clear error message
   ```

2. **Server unreachable:**
   ```bash
   osync ls http://invalid-server:11434
   # Expected: Connection error message
   ```

3. **Insufficient permissions:**
   ```bash
   osync rm system-model
   # Expected: Permission error if applicable
   ```

4. **Invalid patterns:**
   ```bash
   osync ls "["
   # Expected: Pattern error or no matches
   ```

---

## Platform-Specific Tests

### Windows
- Test with PowerShell and CMD
- Verify path handling with backslashes
- Test special characters in model names

### Linux
- Test with bash and zsh
- Verify file permissions
- Test line endings (CRLF vs LF)

### macOS
- Test on ARM and x64
- Verify terminal compatibility
- Test with iTerm2 and Terminal.app

---

## Automation

### Test Script Template

```bash
#!/bin/bash
# Basic osync test suite

echo "Testing list command..."
osync ls || exit 1

echo "Testing copy command..."
osync cp test-model test-backup || exit 1

echo "Testing rename command..."
osync rename test-backup test-renamed || exit 1

echo "Testing delete command..."
osync rm test-renamed || exit 1

echo "All tests passed!"
```

---

## Bug Reporting

When reporting issues, include:

1. **Command used:** Exact command that failed
2. **Expected behavior:** What should happen
3. **Actual behavior:** What actually happened
4. **Environment:**
   - OS and version
   - .NET version
   - Ollama version
   - osync version
5. **Steps to reproduce:** Minimal test case
6. **Logs/Screenshots:** Any relevant output

---

## Known Limitations

1. **Remote-to-Remote Copy:**
   - Only works with registry models
   - Locally created models cannot be transferred
   - Requires registry.ollama.ai access

2. **TUI Mode:**
   - Requires terminal size ≥ 80x24
   - Colors may vary across terminals
   - SSH sessions may have display issues

3. **Pattern Matching:**
   - Only `*` wildcard supported
   - No regex support
   - Case-sensitive matching

---

## Test Coverage

### Commands
- [x] ls (list)
- [x] cp (copy)
- [x] rename/mv/ren
- [x] rm/delete/del
- [x] update
- [x] show
- [x] pull
- [x] run/chat
- [x] ps (process status)
- [x] load
- [x] unload
- [x] qc (quantization comparison)
- [x] qcview (view comparison results)
- [x] manage

### Features
- [x] Pattern matching
- [x] Sorting (name, size, time)
- [x] Filtering (live search)
- [x] Batch operations
- [x] Theme switching
- [x] Progress tracking
- [x] Bandwidth throttling
- [x] Remote operations
- [x] Memory management
- [x] Quantization quality testing
- [x] Logprobs analysis
- [x] Incremental testing
- [x] Tab completion
- [x] Command history

---

## Continuous Testing

Recommended testing schedule:

- **Before commit:** Basic regression tests
- **Before release:** Full test suite
- **After deployment:** Smoke tests on production
- **Weekly:** Performance benchmarks
- **Monthly:** Security audit

---

## Contributing Tests

When adding new features:

1. Add test cases to this document
2. Test on all supported platforms
3. Document edge cases
4. Update regression test suite
5. Verify backward compatibility
