@tty
Feature: psmonitor
  For a server on another machine, psmonitor shows only what that server's API reports: its loaded models
  and, on xOllama, its GPUs and engines. Nothing from the machine osync runs on (GPU, CPU, memory) is shown.
  A server on this machine is shown the same way; without system telemetry, its System panel and GPUs are
  read on this machine.

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

  # xOllama with system telemetry (system_telemetry_v1, 0.40.4-xollama.5+) also sends its host, its own
  # processes and its GPUs: the System panel shows them, read from the server, never from this machine
  @remote1 @xollama
  Scenario: A remote xOllama shows its own host and server processes
    Given a test model "runner" on remote1
    And the model "{runner}" is loaded on remote1
    When I open psmonitor on remote1 as a remote server in a terminal and press "wait:Q4_0 wait:procs text:q"
    Then the command succeeds
    And the output contains "System ──"
    And the output contains "from the server"
    And the output contains "procs"
    And the output contains "runner:latest"
    And the output does not contain "GPU Status"
    And the output does not contain "SWP:"

  # The test servers run on this machine: without OSYNC_PSMONITOR_REMOTE they get the local view, which has the
  # same panels as the remote one (System, models table with Quant), from the server's telemetry or this machine
  @remote1
  Scenario: A server on this machine shows the System panel and the models table of the remote view
    Given a test model "runner" on remote1
    And the model "{runner}" is loaded on remote1
    When I open psmonitor on remote1 as a local server in a terminal and press "wait:Q4_0 wait:procs text:q"
    Then the command succeeds
    And the output contains "System ──"
    And the output contains "procs"
    And the output contains "runner:latest"
    And the output contains "Q4_0"
    And the output does not contain "Remote "
    And the output does not contain "GPU Status"
