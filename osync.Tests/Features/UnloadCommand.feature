Feature: Unload Command
  As a user of osync
  I want to unload models from memory
  So that I can free VRAM and manage memory usage

  Background:
    Given the Ollama server is running
    And the test model "{model}" is available

  Scenario: Unload specific model locally
    Given the model "{model}" is loaded in memory
    When I run "osync unload {model}"
    Then the command should succeed
    And the output should contain "Unloading model"
    And the output should contain "unloaded successfully"

  Scenario: Unload model with auto-tag
    Given the model "{model}" is loaded in memory
    When I run "osync unload {model}"
    Then the command should succeed
    And the output should contain "unloaded successfully"

  Scenario: Unload all loaded models
    Given the model "{model}" is loaded in memory
    When I run "osync unload"
    Then the command should succeed
    And the output should contain "Fetching loaded models"

  Scenario: Unload single model when only one loaded
    Given the model "{model}" is loaded in memory
    When I run "osync unload"
    Then the command should succeed

  Scenario: Unload when no models loaded
    Given no models are loaded in memory
    When I run "osync unload"
    Then the command should succeed
    And the output should contain "No models currently loaded"

  @remote
  Scenario: Unload specific model on remote server
    Given a remote Ollama server is configured
    And the model "{model}" is loaded on the remote server
    When I run "osync unload {model} -d {RemoteServer}"
    Then the command should succeed
    And the output should contain "unloaded successfully"

  @remote
  Scenario: Unload all models on remote server
    Given a remote Ollama server is configured
    And multiple models are loaded on the remote server
    When I run "osync unload -d {RemoteServer}"
    Then the command should succeed
    And the output should contain "Fetching loaded models"

  @remote
  Scenario: Unload model with destination before model name
    Given a remote Ollama server is configured
    And the model "{model}" is loaded on the remote server
    When I run "osync unload -d {RemoteServer} {model}"
    Then the command should succeed
    And the output should contain "unloaded successfully"

  Scenario: Verify unloaded model not in process status
    Given the model "{model}" is loaded in memory
    When I run "osync unload {model}"
    And I run "osync ps"
    Then the model "{model}" should not appear in the output

  @skip
  Scenario: Unload frees VRAM
    Given the model "{model}" is loaded in memory
    And I check the VRAM usage
    When I run "osync unload {model}"
    And I check the VRAM usage again
    Then VRAM usage should be reduced

  Scenario: Load and unload workflow
    When I run "osync load {model}"
    And I run "osync ps"
    Then the output should contain "{model}"
    When I run "osync unload {model}"
    And I run "osync ps"
    Then the output should not contain "{model}"
