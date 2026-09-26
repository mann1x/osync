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
