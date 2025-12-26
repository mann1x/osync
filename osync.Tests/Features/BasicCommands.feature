Feature: Basic Commands
  As an osync user
  I want to use basic commands
  So that I can manage my Ollama models

  @basic @version
  Scenario: Display version
    When I run osync with arguments "-?"
    Then the command should succeed
    And the output should contain "osync v"

  @basic @list
  Scenario: List local models
    When I run osync with arguments "ls"
    Then the command should succeed
    And the output should contain "NAME"
    And the output should contain "ID"
    And the output should contain "SIZE"

  @basic @help
  Scenario: Show help
    When I run osync with arguments "-h"
    Then the command should succeed
    And the output should contain "osync"
