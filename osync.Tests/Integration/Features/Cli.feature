@cli
Feature: CLI basics
  osync starts, reports its version and help, and rejects invalid invocations,
  without needing any Ollama server.

  Scenario: Show version
    When I run osync "-v"
    Then the command succeeds
    And the output contains "osync v"

  Scenario: Show help
    When I run osync "-h"
    Then the command succeeds
    And the output contains "Usage: osync <command>"

  Scenario: Unknown command fails
    When I run osync "no-such-command"
    Then the command fails

  Scenario Outline: Missing required argument fails with an error and the command help
    When I run osync "<command>"
    Then the command fails
    And the output contains "is required"

    Examples:
      | command |
      | load    |
      | show    |
      | pull    |
      | cp      |
      | mv      |

  Scenario: rm without a pattern fails
    When I run osync "rm"
    Then the command fails
    And the output contains "Model pattern is required"

  # Regression: help rendering used to loop forever when the console width is 0
  # (output redirected on Linux/macOS), so "<command> -h" and missing-argument errors hung.
  Scenario Outline: Command help exits (<command>)
    When I run osync "<command> -h"
    Then the command succeeds
    And the output contains "<text>"

    Examples:
      | command | text                     |
      | cp      | osync Copy <Source>      |
      | ls      | osync List [<Pattern>]   |
      | load    | osync Load <ModelName>   |
