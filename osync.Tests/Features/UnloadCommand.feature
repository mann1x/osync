Feature: Unload Command
  As a user of osync
  I want to unload models from memory
  So that I can free VRAM and manage memory usage

  Background:
    Given the Ollama server is running
    And the test model "llama3.2:1b" is available

  Scenario: Unload specific model locally
    Given the model "llama3.2:1b" is loaded in memory
    When I run "osync unload llama3.2:1b"
    Then the command should succeed
    And the output should contain "Unloading model"
    And the output should contain "unloaded successfully"

  Scenario: Unload model with auto-tag
    Given the model "llama3.2:1b" is loaded in memory
    When I run "osync unload llama3.2"
    Then the command should succeed
    And the output should contain "llama3.2:latest"
    And the output should contain "unloaded successfully"

  Scenario: Unload all loaded models
    Given the models "llama3.2:1b" and "mistral-nemo:latest" are loaded
    When I run "osync unload"
    Then the command should succeed
    And the output should contain "Fetching loaded models"
    And the output should contain "Unloaded 2 models"

  Scenario: Unload single model when only one loaded
    Given the model "llama3.2:1b" is loaded in memory
    When I run "osync unload"
    Then the command should succeed
    And the output should contain "Unloaded 1 models"

  Scenario: Unload when no models loaded
    Given no models are loaded in memory
    When I run "osync unload"
    Then the command should succeed
    And the output should contain "No models currently loaded"

  Scenario: Unload specific model on remote server
    Given a remote Ollama server is configured
    And the model "llama3.2:1b" is loaded on the remote server
    When I run "osync unload llama3.2:1b -d {RemoteServer}"
    Then the command should succeed
    And the output should contain "unloaded successfully"

  Scenario: Unload all models on remote server
    Given a remote Ollama server is configured
    And multiple models are loaded on the remote server
    When I run "osync unload -d {RemoteServer}"
    Then the command should succeed
    And the output should contain "Fetching loaded models"
    And the output should contain "Unloaded"

  Scenario: Unload model with destination before model name
    Given a remote Ollama server is configured
    And the model "llama3.2:1b" is loaded on the remote server
    When I run "osync unload -d {RemoteServer} llama3.2:1b"
    Then the command should succeed
    And the output should contain "unloaded successfully"

  Scenario: Verify unloaded model not in process status
    Given the model "llama3.2:1b" is loaded in memory
    When I run "osync unload llama3.2:1b"
    And I run "osync ps"
    Then the model "llama3.2:1b" should not appear in the output

  Scenario: Unload frees VRAM
    Given the model "llama3.2:1b" is loaded in memory
    And I check the VRAM usage
    When I run "osync unload llama3.2:1b"
    And I check the VRAM usage again
    Then VRAM usage should be reduced

  Scenario: Load and unload workflow
    When I run "osync load llama3.2:1b"
    And I run "osync ps"
    Then the output should contain "llama3.2:1b"
    When I run "osync unload llama3.2:1b"
    And I run "osync ps"
    Then the output should not contain "llama3.2:1b"
