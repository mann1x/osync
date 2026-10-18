@tty
Feature: psmonitor of a remote server
  For a server on another machine, psmonitor shows only what that server's API reports: its loaded models
  and, on xOllama, its GPUs and engines. Nothing from the machine osync runs on (GPU, CPU, memory) is shown.

  @remote1
  Scenario: A remote server shows its loaded models and no local system panels
    Given a test model "runner" on remote1
    And the model "{runner}" is loaded on remote1
    When I open psmonitor on remote1 as a remote server in a terminal and press "wait:Q4_0 text:q"
    Then the command succeeds
    And the output contains "Remote "
    # At 120 columns the generated name is cut from the left ("..1234-001-runner:latest")
    And the output contains "runner:latest"
    And the output contains "Q4_0"
    And the output contains "Loaded models, VRAM"
    And the output does not contain "GPU Status"
    And the output does not contain "SWP:"
    And the output does not contain "Ollama CPU"
