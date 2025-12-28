Feature: Load Command
  As a user of osync
  I want to preload models into memory
  So that I can reduce first-request latency and warm up models

  Background:
    Given the Ollama server is running
    And the test model "llama3.2:1b" is available

  Scenario: Load model locally
    When I run "osync load llama3.2:1b"
    Then the command should succeed
    And the output should contain "Loading model"
    And the output should contain "loaded successfully"

  Scenario: Load model with auto-tag
    When I run "osync load llama3.2"
    Then the command should succeed
    And the output should contain "llama3.2:latest"
    And the output should contain "loaded successfully"

  Scenario: Load model on remote server
    Given a remote Ollama server is configured
    When I run "osync load llama3.2:1b -d {RemoteServer}"
    Then the command should succeed
    And the output should contain "Loading model"
    And the output should contain "loaded successfully"

  Scenario: Load model with destination before model name
    Given a remote Ollama server is configured
    When I run "osync load -d {RemoteServer} llama3.2:1b"
    Then the command should succeed
    And the output should contain "loaded successfully"

  Scenario: Verify loaded model appears in process status
    When I run "osync load llama3.2:1b"
    And I run "osync ps"
    Then the command should succeed
    And the output should contain "llama3.2:1b"
    And the output should contain "Loaded Models"

  Scenario: Load model reduces first-request latency
    Given the model "llama3.2:1b" is not loaded
    When I run "osync load llama3.2:1b"
    And I check the process status
    Then the model "llama3.2:1b" should be loaded in memory

  Scenario: Load non-existent model fails
    When I run "osync load nonexistent-model"
    Then the command should fail
    And the output should contain error information

  Scenario: Load without model name fails
    When I run "osync load"
    Then the command should fail
    And the output should contain "Model name is required"
