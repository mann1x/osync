Feature: Setup (preferences in the settings file)
  osync setup changes the settings file of the scenario (OSYNC_CONFIG_DIR); server aliases work wherever a
  server is expected.

  @cli
  Scenario: Add and remove a server alias
    When I run osync "setup alias add gpu 127.0.0.1:1"
    Then the command succeeds
    And the settings file has "aliases.gpu" set to "http://127.0.0.1:1"
    When I run osync "setup alias remove gpu"
    Then the command succeeds
    And the settings file has no "aliases.gpu"

  @cli
  Scenario Outline: Invalid alias names are refused
    When I run osync "setup alias add <name> 127.0.0.1:1"
    Then the command fails
    And the settings file does not exist

    Examples:
      | name      |
      | 2gpu      |
      | my.server |
      | localhost |

  @cli
  Scenario: Removing an unknown alias fails
    When I run osync "setup alias remove nothere"
    Then the command fails

  @cli
  Scenario: Aliases are listed
    Given the alias "box1" for the local server
    When I run osync "setup alias list"
    Then the command succeeds
    And the output contains "box1"

  @cli
  Scenario: Manage theme and default sort order
    When I run osync "setup manage theme tokyo-night"
    Then the command succeeds
    And the settings file has "manage.theme" set to "Tokyo Night"
    When I run osync "setup manage sort size-"
    Then the command succeeds
    And the settings file has "manage.sort" set to "size-"

  @cli
  Scenario: Unknown themes and sort orders are refused
    When I run osync "setup manage theme no-such-theme"
    Then the command fails
    When I run osync "setup manage sort sideways"
    Then the command fails
    And the settings file does not exist

  @cli
  Scenario: Shell theme and color mode
    When I run osync "setup shell theme Dracula"
    Then the command succeeds
    And the settings file has "shell.theme" set to "Dracula"
    When I run osync "setup shell theme plain"
    Then the command succeeds
    And the settings file has "shell.theme" set to "plain"
    When I run osync "setup shell colors 256"
    Then the command succeeds
    And the settings file has "colorMode" set to "256"

  @cli
  Scenario: Theme lists show every theme
    When I run osync "setup shell themes"
    Then the command succeeds
    And the output contains "Catppuccin Latte"
    And the output contains "plain"
    When I run osync "setup manage themes"
    Then the command succeeds
    And the output contains "Rose Pine Dawn"

  @cli
  Scenario: A single local server
    When I run osync "setup server xollama 127.0.0.1:1"
    Then the command succeeds
    And the settings file has "server.flavor" set to "xollama"
    And the settings file has "server.host" set to "127.0.0.1"
    And the settings file has "server.port" set to "1"

  @cli
  Scenario: Ollama and xOllama side by side get an alias each
    When I run osync "setup server both 127.0.0.1 xollama"
    Then the command succeeds
    And the settings file has "server.both" set to "true"
    And the settings file has "server.flavor" set to "xollama"
    And the settings file has "aliases.ollama" set to "http://127.0.0.1:11434"
    And the settings file has "aliases.xollama" set to "http://127.0.0.1:22434"
    When I run osync "setup server auto"
    Then the command succeeds
    And the settings file has "server.flavor" set to "auto"
    And the settings file has no "aliases.xollama"

  @cli
  Scenario: The summary shows the settings
    Given the alias "box1" for the local server
    When I run osync "setup show"
    Then the command succeeds
    And the output contains "Local server"
    And the output contains "box1"

  @cli
  Scenario: Unknown setup sections fail
    When I run osync "setup nonsense"
    Then the command fails

  @cli
  Scenario: Choose the servers manage switches between
    Given the alias "box1" for the local server
    When I run osync "setup manage servers box1"
    Then the command succeeds
    And the settings file has "manage.servers" set to '["box1"]'
    When I run osync "setup alias remove box1"
    Then the command succeeds
    And the settings file has "manage.servers" set to '[]'

  @cli
  Scenario: Unknown aliases cannot be servers of manage
    When I run osync "setup manage servers nothere"
    Then the command fails

  @cli
  Scenario: The settings can take precedence over XOLLAMA_HOST / OLLAMA_HOST
    When I run osync "setup server env ignore"
    Then the command fails
    When I run osync "setup server ollama 127.0.0.1:1"
    And I run osync "setup server env ignore"
    Then the command succeeds
    And the settings file has "server.ignoreEnvironment" set to "true"
    When I run osync "setup server env use"
    Then the command succeeds
    And the settings file has no "server.ignoreEnvironment"

  # Aliases in real commands: "r1" stands for the remote1 server
  @local @remote1
  Scenario: Upload to a server alias
    Given a test model "src" on local
    And the alias "r1" for the remote1 server
    When I run osync "cp {src} r1/{dst}"
    Then the command succeeds
    And the model "{dst}" on remote1 is identical to "{src}" on local

  @remote1
  Scenario: List a server through its alias
    Given a test model "src" on remote1
    And the alias "r1" for the remote1 server
    When I run osync "ls -d r1"
    Then the command succeeds
    And the output contains "{src}:latest"

  @local @remote1
  Scenario: Download from a server alias
    Given a test model "src" on remote1
    And the alias "r1" for the remote1 server
    When I run osync "cp r1/{src} {dst}"
    Then the command succeeds
    And the model "{dst}" on local is identical to "{src}" on remote1
