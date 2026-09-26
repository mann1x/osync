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
