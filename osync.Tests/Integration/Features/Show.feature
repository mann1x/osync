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

  # Remote show prints only the Modelfile, without the model details.
  @remote1 @knownbug
  Scenario: Show model details on a remote server
    Given a test model "info" on remote1
    When I run osync "show {info} -d {remote1}"
    Then the command succeeds
    And the output contains "llama"
    And the output contains "Q4_0"

  @remote1
  Scenario: Show the template of a remote model
    Given a test model "info" on remote1
    When I run osync "show {info} -d {remote1} --template"
    Then the command succeeds
    And the output contains "<|im_start|>"
