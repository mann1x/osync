Feature: Pull Command
  As an osync user
  I want to pull models from registries
  So that I can download new models

  @basic @pull
  Scenario: Pull model from Ollama registry
    When I run osync with arguments "pull qwen2.5:0.5b"
    Then the command should succeed
    And the output should contain "Pulling"
    And the model "qwen2.5:0.5b" should exist locally

  @remote @pull
  Scenario: Pull model to remote server
    When I run osync with arguments "pull qwen2.5:0.5b -d {remote1}"
    Then the command should succeed
    And the output should contain "Pulling"
    And the model "qwen2.5:0.5b" should exist on "{remote1}"

  @basic @pull @huggingface
  Scenario: Pull model from HuggingFace short format
    When I run osync with arguments "pull hf.co/bartowski/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M"
    Then the command should succeed
    And the output should contain "Pulling"

  @basic @pull @error
  Scenario: Pull nonexistent model
    When I run osync with arguments "pull nonexistent-model-xyz:v1"
    Then the command should fail

  @basic @pull @tag
  Scenario: Pull model without tag (uses :latest)
    When I run osync with arguments "pull qwen2.5:0.5b"
    Then the command should succeed
    And the model "qwen2.5:0.5b" should exist locally
