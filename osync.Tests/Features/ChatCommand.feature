Feature: Chat Command
  As a user of osync
  I want to chat with models
  So that I can interact with AI models locally or remotely

  Background:
    Given the Ollama server is running
    And the test model "mistral-nemo:latest" is available

  Scenario: Start chat session with model preloading
    When I run "osync run mistral-nemo"
    Then the model should be preloaded into memory
    And the process status table should be displayed
    And the process status table should show the model name
    And the process status table should show the model ID
    And the process status table should show the model size
    And the process status table should show VRAM usage
    And the process status table should show context length
    And the process status table should show expiration time

  Scenario: Chat with local model
    Given I start a chat session with "mistral-nemo"
    When I send the message "What is 2+2?"
    Then I should receive a response
    And the response should be streamed in real-time

  Scenario: Chat with remote model
    Given I have a remote Ollama server at "http://localhost:11434"
    When I run "osync run mistral-nemo -d http://localhost:11434"
    Then the model should be preloaded on the remote server
    And the process status should show models from the remote server
    When I send the message "What is the capital of France?"
    Then I should receive a response
    And the response streaming should be fast without delays

  Scenario: Process status shows correct model information
    Given the model "llama3:latest" is loaded in memory
    When I view the process status
    Then the NAME column should show "llama3:latest"
    And the ID column should show a 12-character digest
    And the SIZE column should show disk size and parameter count
    And the VRAM USAGE column should show memory usage
    And the CONTEXT column should show the context window size
    And the UNTIL column should show human-readable expiration time

  Scenario: Multiple models loaded shows in status table
    Given the models "llama3:latest" and "mistral-nemo:latest" are loaded
    When I view the process status
    Then the status table should show 2 models
    And each model should have complete information displayed

  Scenario: Chat keyboard shortcuts work correctly
    Given I start a chat session with "mistral-nemo"
    When I press "Ctrl+D" on an empty line
    Then the chat session should exit

  Scenario: Multiline input with triple quotes
    Given I start a chat session with "mistral-nemo"
    When I enter '"""' to start multiline mode
    And I enter "This is line 1"
    And I enter "This is line 2"
    And I enter '"""' to end multiline mode
    Then the message should be sent as a single multiline message

  Scenario: Multiline with content on same line as delimiter
    Given I start a chat session with "mistral-nemo"
    When I enter '"""This is the start'
    And I enter "Middle content"
    And I enter 'This is the end"""'
    Then the message should include all three lines

  Scenario: Command history navigation
    Given I start a chat session with "mistral-nemo"
    When I send the message "First message"
    And I send the message "Second message"
    And I press "Up" arrow
    Then the input should show "Second message"
    When I press "Up" arrow again
    Then the input should show "First message"

  Scenario: Session save and load
    Given I start a chat session with "mistral-nemo"
    When I send the message "Remember this conversation"
    And I run the command "/save test-session"
    And I exit the chat session
    And I start a new chat session with "mistral-nemo"
    And I run the command "/load test-session"
    Then the conversation history should be restored

  Scenario: Performance statistics tracking
    Given I start a chat session with "mistral-nemo" with "--verbose" flag
    When I send the message "Test message"
    Then performance statistics should be displayed
    And the statistics should include total duration
    And the statistics should include tokens per second

  Scenario: Set model parameters during chat
    Given I start a chat session with "mistral-nemo"
    When I run the command "/set parameter temperature 0.8"
    Then the temperature parameter should be set to 0.8
    When I send a message
    Then the model should use the updated temperature parameter

  Scenario: Clear conversation history
    Given I start a chat session with "mistral-nemo"
    When I send the message "First message"
    And I send the message "Second message"
    And I run the command "/clear"
    Then the conversation history should be empty
    When I send the message "New conversation"
    Then only the new message should be in the history
