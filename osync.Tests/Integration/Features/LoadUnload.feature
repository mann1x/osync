Feature: Load, unload and list running models (load, unload, ps)

  @local
  Scenario: Load a model into memory
    Given a test model "runner" on local
    When I run osync "load {runner}"
    Then the command succeeds
    And the model "{runner}" is loaded on local

  @local
  Scenario: Unload a model from memory
    Given a test model "runner" on local
    And the model "{runner}" is loaded on local
    When I run osync "unload {runner}"
    Then the command succeeds
    And the model "{runner}" is not loaded on local

  # Regression: ps used to cut names to 20 characters when output is redirected.
  @local
  Scenario: ps lists a loaded model with its full name
    Given a test model "runner" on local
    And the model "{runner}" is loaded on local
    When I run osync "ps"
    Then the command succeeds
    And the output contains "{runner}:latest"

  @local
  Scenario: Loading a missing model fails
    When I run osync "load {missing}"
    Then the command fails

  @remote1
  Scenario Outline: Load a model on a remote server (<order>)
    Given a test model "runner" on remote1
    When I run osync "<arguments>"
    Then the command succeeds
    And the model "{runner}" is loaded on remote1

    Examples:
      | order                  | arguments                    |
      | destination last       | load {runner} -d {remote1}   |
      | destination first      | load -d {remote1} {runner}   |

  @remote1
  Scenario: ps on a remote server lists a loaded model
    Given a test model "runner" on remote1
    And the model "{runner}" is loaded on remote1
    When I run osync "ps {remote1}"
    Then the command succeeds
    And the output contains "{runner}:latest"

  # A second -d also lists the models loaded on another server (e.g. a plain Ollama next to xOllama)
  @remote1 @peer
  Scenario: ps with a second server lists the models loaded on both
    Given a test model "first" on remote1
    And the model "{first}" is loaded on remote1
    And a test model "second" on peer
    And the model "{second}" is loaded on peer
    When I run osync "ps -d {remote1} -d {peer}"
    Then the command succeeds
    And the output contains "{first}:latest"
    And the output contains "Secondary server:"
    And the output contains "{second}:latest"

  # The secondary is listed even when the first server does not answer, and the command still fails
  @peer
  Scenario: ps lists the secondary server when the first one is down
    Given a test model "second" on peer
    And the model "{second}" is loaded on peer
    When I run osync "ps -d http://127.0.0.1:9 -d {peer}"
    Then the command fails
    And the output contains "Could not connect"
    And the output contains "{second}:latest"

  @remote1
  Scenario: Unload a model on a remote server
    Given a test model "runner" on remote1
    And the model "{runner}" is loaded on remote1
    When I run osync "unload {runner} -d {remote1}"
    Then the command succeeds
    And the model "{runner}" is not loaded on remote1

  @remote1 @exclusive
  Scenario: Unload all models on a remote server
    Given a test model "runner-1" on remote1
    And a test model "runner-2" on remote1
    And the model "{runner-1}" is loaded on remote1
    And the model "{runner-2}" is loaded on remote1
    When I run osync "unload -d {remote1}"
    Then the command succeeds
    And no model is loaded on remote1
