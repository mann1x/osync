@tty
Feature: Manage (full-screen TUI)
  osync manage runs in a pseudo terminal: it lists the server's models, its dialogs act on them, and its
  preferences (theme, server) are saved in the settings file. Keys are sent only after the view is drawn.
  Every scenario first types its own model prefix as the filter, so only models it created are listed.

  @local
  Scenario: Manage lists the models of the server and quits with Ctrl+Q
    Given a test model "alpha" on local
    When I open manage in a terminal and press "text:{prefix} Ctrl+Q"
    Then the command succeeds
    And the output contains "{alpha}:latest"
    And the output contains "Q4_0"

  # Enter answers No (manage stays open: the delete after it works); Tab moves to Yes, which leaves.
  # The first Esc of the final pair clears the filter.
  @local
  Scenario: Esc asks before leaving: Enter keeps manage open, Yes leaves
    Given a test model "alpha" on local
    When I open manage in a terminal and press "Esc Enter text:{prefix} Ctrl+D Tab Enter Esc Esc Tab Enter"
    Then the command succeeds
    And the output contains "Are you sure you want to exit manage mode?"
    And the model "{alpha}" does not exist on local

  @local
  Scenario: F1 shows the keys
    Given a test model "alpha" on local
    When I open manage in a terminal and press "F1 Esc Ctrl+Q"
    Then the command succeeds
    And the output contains "Ctrl+ actions"

  @local
  Scenario: Delete asks for confirmation and Enter cancels
    Given a test model "alpha" on local
    When I open manage in a terminal and press "text:{prefix} Ctrl+D Enter Ctrl+Q"
    Then the command succeeds
    And the model "{alpha}" exists on local

  @local
  Scenario: Delete a model after confirming
    Given a test model "alpha" on local
    When I open manage in a terminal and press "text:{prefix} Ctrl+D Tab Enter Ctrl+Q"
    Then the command succeeds
    And the model "{alpha}" does not exist on local

  @local
  Scenario: Rename a model with F2
    Given a test model "alpha" on local
    When I open manage in a terminal and press "text:{prefix} F2 End Backspace Backspace Backspace Backspace Backspace Backspace text:v2 Enter Ctrl+Q"
    Then the command succeeds
    And the model "{alpha}:v2" exists on local
    And the model "{alpha}" does not exist on local

  @local
  Scenario: The chosen theme is saved in the settings file
    Given a test model "alpha" on local
    When I open manage in a terminal and press "Ctrl+T Down Enter Ctrl+Q"
    Then the command succeeds
    And the settings file has "manage.theme" set to "Dracula"

  @local
  Scenario: Cancelling the theme choice keeps the settings file untouched
    Given a test model "alpha" on local
    When I open manage in a terminal and press "Ctrl+T Down Down Esc Ctrl+Q"
    Then the command succeeds
    And the settings file does not exist

  @remote1
  Scenario: The server settings switch manage to another server and are saved
    Given a test model "beta" on remote1
    When I open manage without host settings in a terminal and press "Ctrl+E Tab text:{remote1.host} Tab text:{remote1.port} Enter text:{prefix} Ctrl+Q"
    Then the command succeeds
    And the settings file has "server.host" set to "{remote1.host}"
    And the settings file has "server.port" set to "{remote1.port}"
    And the output contains "{beta}:latest"

  # The theme list is taller than the 30-row terminal: End must scroll it to the last theme
  @local
  Scenario: The theme list scrolls to themes below the visible rows
    Given a test model "alpha" on local
    When I open manage in a terminal and press "Ctrl+T End Enter Ctrl+Q"
    Then the command succeeds
    And the settings file has "manage.theme" set to "Rose Pine Dawn"
    And the output contains "Rose Pine Dawn"

  @local @remote1
  Scenario: Ctrl+Right switches to the next server
    Given a test model "alpha" on local
    And a test model "beta" on remote1
    And the alias "r1" for the remote1 server
    And manage shows the servers "r1"
    When I open manage in a terminal and press "Ctrl+Right text:{prefix} Ctrl+Q"
    Then the command succeeds
    And the output contains "{beta}:latest"
    And the output contains "[2/2] r1:"

  @local @remote1
  Scenario: Ctrl+Left comes back to the local server
    Given a test model "alpha" on local
    And a test model "beta" on remote1
    And the alias "r1" for the remote1 server
    And manage shows the servers "r1"
    When I open manage in a terminal and press "Ctrl+Right Ctrl+Left text:{prefix} Ctrl+Q"
    Then the command succeeds
    And the output contains "[1/2] local:"

  # Ctrl+W runs `xollama tweak model` on the console against the server manage shows. A flag with a value
  # sets that setting without questions. Tab moves from the settings list to the flags field, where Enter
  # presses Tweak; Space returns from the console to manage once it asks, and Ctrl+Q waits for the list.
  @local @xollama
  Scenario: Ctrl+W sets an xOllama setting of the model with xollama tweak
    Given a test model "alpha" on local
    When I open manage in a terminal and press "text:{prefix} Ctrl+W Tab text:--kv-v=q8_0 Enter wait:view... Space wait:Sorting Ctrl+Q"
    Then the command succeeds
    And the output contains "xollama tweak model '{alpha}:latest'"
    And the model "{alpha}" on local has the xOllama setting "kv.v" set to "q8_0"

  @local @xollama
  Scenario: The model details list its xOllama settings
    Given a test model "alpha" on local
    And the model "alpha" on local has the xOllama settings '{"kv":{"k":"f16","v":"q8_0"}}'
    When I open manage in a terminal and press "text:{prefix} Enter Esc Ctrl+Q"
    Then the command succeeds
    And the output contains "xOllama settings (Ctrl+W to change)"
    And the output contains "kv.k  f16"

  # A server on this machine also offers its own settings (defaults for every model, GPUs, environment
  # variables): xOllama changes them only for a client on its own machine.
  @local @xollama
  Scenario: Ctrl+W offers the settings of a server on this machine
    Given a test model "alpha" on local
    When I open manage in a terminal and press "text:{prefix} Ctrl+W Esc Ctrl+Q"
    Then the command succeeds
    And the output contains "Server: GPUs"
    And the output contains "Engine policies"

  # The last option removes the settings; the confirmation keeps Cancel last (Enter), Tab moves to Remove.
  @local @xollama
  Scenario: Ctrl+W removes the xOllama settings of the model after confirming
    Given a test model "alpha" on local
    And the model "alpha" on local has the xOllama settings '{"kv":{"v":"q8_0"}}'
    When I open manage in a terminal and press "text:{prefix} Ctrl+W Down*11 Enter Tab Enter wait:view... Space wait:Sorting Ctrl+Q"
    Then the command succeeds
    And the model "{alpha}" on local has no xOllama settings
