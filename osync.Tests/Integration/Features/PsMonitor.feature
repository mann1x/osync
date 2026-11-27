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

  # Up/Down select a loaded model and open its details under the table; Esc closes them, and quits only when
  # nothing is selected. The table splits each model into VRAM and RAM
  @remote1
  Scenario: Selecting a loaded model opens its details
    Given a test model "runner" on remote1
    And the model "{runner}" is loaded on remote1
    When I open psmonitor on remote1 as a remote server in a terminal and press "wait:Q4_0 Down wait:Esc=Close Esc text:q"
    Then the command succeeds
    And the output contains "VRAM"
    And the output contains "RAM"
    And the output contains "Esc=Close"
    And the output contains "Memory"
    And the output does not contain "Size (VRAM)"

  # A second -d lists the models loaded on another server (typically a plain Ollama next to xOllama, or the
  # reverse) in a table of their own. Up/Down move through both tables; U unloads the selected model on the
  # server it is loaded on, and only there
  @remote1 @peer
  Scenario: A secondary server's models are listed and U unloads the selected one there
    Given a test model "first" on remote1
    And the model "{first}" is loaded on remote1
    And a test model "second" on peer
    And the model "{second}" is loaded on peer
    When I open psmonitor on remote1 with peer as secondary in a terminal and press "wait:Secondary wait:second:latest Down Down wait:Esc=Close text:u wait:Unloaded text:q"
    Then the command succeeds
    And the output contains "first:latest"
    And the output contains "second:latest"
    And the output contains "Secondary "
    And the model "{second}" is not loaded on peer
    And the model "{first}" is loaded on remote1

  # U with nothing selected unloads nothing and says how to select a model
  @remote1
  Scenario: U without a selected model unloads nothing
    Given a test model "runner" on remote1
    And the model "{runner}" is loaded on remote1
    When I open psmonitor on remote1 as a remote server in a terminal and press "wait:Q4_0 text:u wait:Select text:q"
    Then the command succeeds
    And the output contains "Select a model with Up/Down first"
    And the model "{runner}" is loaded on remote1
