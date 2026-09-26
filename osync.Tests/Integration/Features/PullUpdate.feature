@registry
Feature: Pull and update models from registries (pull, update)
  These scenarios download from registry.ollama.ai / huggingface.co and only run with OSYNC_TEST_REGISTRY=1.

  @local
  Scenario: Pull a model from the Ollama registry
    Given the registry model "smollm2:135m" is not yet on local
    When I run osync "pull smollm2:135m"
    Then the command succeeds
    And the model "smollm2:135m" exists on local

  # xOllama v0.34.2-xollama.1 (upstream base 0.34.2) only follows same-host redirects, and huggingface.co
  # now redirects downloads to cdn.hf.co, so every HuggingFace pull fails with "blocked redirect to a
  # different host". Upstream Ollama (0.34.4) allows redirects within ollama.com / hf.co / huggingface.co.
  @local @xollamabug
  Scenario: Pull a GGUF from HuggingFace
    Given the registry model "hf.co/bartowski/SmolLM2-135M-Instruct-GGUF:Q2_K" is not yet on local
    When I run osync "pull hf.co/bartowski/SmolLM2-135M-Instruct-GGUF:Q2_K"
    Then the command succeeds
    And the model "hf.co/bartowski/SmolLM2-135M-Instruct-GGUF:Q2_K" exists on local

  @local
  Scenario: Pulling a model that does not exist fails
    When I run osync "pull osync-no-such-model-xyz:v1"
    Then the command fails

  @remote1
  Scenario: Pull a model onto a remote server
    Given the registry model "smollm2:135m" is not yet on remote1
    When I run osync "pull smollm2:135m -d {remote1}"
    Then the command succeeds
    And the model "smollm2:135m" exists on remote1

  @local
  Scenario: Update a registry model
    Given the registry model "smollm2:135m" on local
    When I run osync "update smollm2:135m"
    Then the command succeeds
    And the model "smollm2:135m" exists on local

  @remote1
  Scenario: Update a registry model on a remote server
    Given the registry model "smollm2:135m" on remote1
    When I run osync "update smollm2:135m -d {remote1}"
    Then the command succeeds
    And the model "smollm2:135m" exists on remote1
