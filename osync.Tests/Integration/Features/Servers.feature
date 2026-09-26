Feature: Server discovery (Ollama and xOllama)
  osync works with Ollama and with xOllama (github.com/mann1x/xollama, default port 22434),
  finds the local server by itself and reports which implementation it talks to.

  @local
  Scenario: ps names the local server implementation
    When I run osync "ps"
    Then the command succeeds
    And the output names the server flavor of local

  @remote1
  Scenario: ps names the remote server implementation
    When I run osync "ps -d {remote1}"
    Then the command succeeds
    And the output names the server flavor of remote1

  @local @defaultport
  Scenario: The local server is found on its default port without OLLAMA_HOST / XOLLAMA_HOST
    Given a test model "found" on local
    And the model "{found}" is loaded on local
    When I run osync "ps" without host settings
    Then the command succeeds
    And the output names the server flavor of local
    And the output contains "{found}:latest"

  # remote1 (e.g. :11435) is never found by probing the default ports, so this only passes if the
  # server configured in settings.json is used.
  @remote1
  Scenario: The local server comes from the settings file when no host variable is set
    Given a test model "configured" on remote1
    And the model "{configured}" is loaded on remote1
    And the settings file configures the remote1 server
    When I run osync "ps" without host settings
    Then the command succeeds
    And the output names the server flavor of remote1
    And the output contains "{configured}:latest"
