Feature: Show model information (show)

  @local
  Scenario: Show model details
    Given a test model "info" on local
    When I run osync "show {info}"
    Then the command succeeds
    And the output contains "llama"
    And the output contains "Q4_0"

  @local
  Scenario Outline: Show a single section (<flag>)
    Given a test model "info" on local
    When I run osync "show {info} <flag>"
    Then the command succeeds
    And the output contains "<expected>"

    Examples:
      | flag         | expected       |
      | --modelfile  | FROM           |
      | --template   | <\|im_start\|> |
      | --parameters | num_ctx        |

  @local
  Scenario: Show a missing model fails
    When I run osync "show {missing}"
    Then the command fails

  @remote1
  Scenario: Show model details on a remote server
    Given a test model "info" on remote1
    When I run osync "show {info} -d {remote1}"
    Then the command succeeds
    And the output contains "architecture"
    And the output contains "llama"
    And the output contains "Q4_0"
    And the output contains "num_ctx"
    And the output does not contain "FROM "

  @remote1
  Scenario: Show several sections of a remote model at once
    Given a test model "info" on remote1
    When I run osync "show {info} -d {remote1} --template --parameters"
    Then the command succeeds
    And the output contains "<|im_start|>"
    And the output contains "num_ctx"

  @remote1
  Scenario: Show a missing model on a remote server fails
    When I run osync "show {missing} -d {remote1}"
    Then the command fails

  @remote1
  Scenario: Show the template of a remote model
    Given a test model "info" on remote1
    When I run osync "show {info} -d {remote1} --template"
    Then the command succeeds
    And the output contains "<|im_start|>"
