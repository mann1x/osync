Feature: Copy models (cp)

  @local
  Scenario: Copy a local model to a new local name
    Given a test model "src" on local
    When I run osync "cp {src} {dst}"
    Then the command succeeds
    And the model "{dst}" exists on local
    And the model "{src}" exists on local
    And the model "{dst}" on local is identical to "{src}" on local

  @local
  Scenario: Copy with an explicit tag
    Given a test model "src" on local
    When I run osync "copy {src} {dst}:v1"
    Then the command succeeds
    And the model "{dst}:v1" exists on local
    And the model "{dst}:latest" does not exist on local

  @local
  Scenario: Copy onto an existing model is refused
    Given a test model "src" on local
    And a test model "existing" on local
    When I run osync "cp {src} {existing}"
    Then the command fails
    And the model "{existing}" exists on local

  @local
  Scenario: Copy of a missing model fails and creates nothing
    When I run osync "cp {missing} {dst}"
    Then the command fails
    And the model "{dst}" does not exist on local

  @local @remote1
  Scenario Outline: Upload a local model to a remote server (<format>)
    Given a test model "src" on local
    When I run osync "cp {src} <destination>"
    Then the command succeeds
    And the model "{dst}" exists on remote1
    And the model "{dst}" on remote1 is identical to "{src}" on local

    Examples:
      | format        | destination                  |
      | full URL      | {remote1}/{dst}              |
      | host and port | {remote1.hostport}/{dst}     |

  @local @remote1
  Scenario: Upload keeps the model name when only the server is given
    Given a test model "src" on local
    When I run osync "cp {src} {remote1}"
    Then the command succeeds
    And the model "{src}" exists on remote1

  @local @remote1
  Scenario: Uploading the same model twice succeeds and reuses layers
    Given a test model "src" on local
    When I run osync "cp {src} {remote1}/{dst}"
    And I run osync "cp {src} {remote1}/{dst}"
    Then the command succeeds
    And the model "{dst}" on remote1 is identical to "{src}" on local

  # Remote copies download the blobs from registry.ollama.ai instead of the source server,
  # so models that only exist on the source server (created, imported) cannot be copied.
  # Fix planned: push-relay copy (see docs/DEVELOPMENT.md roadmap).
  @remote1 @local @knownbug
  Scenario: Download a model from a remote server to local
    Given a test model "src" on remote1
    When I run osync "cp {remote1}/{src} {dst}"
    Then the command succeeds
    And the model "{dst}" on local is identical to "{src}" on remote1

  @remote1 @remote2 @knownbug
  Scenario: Copy a model between two remote servers
    Given a test model "src" on remote1
    When I run osync "cp {remote1}/{src} {remote2}/{dst}"
    Then the command succeeds
    And the model "{dst}" on remote2 is identical to "{src}" on remote1

  @remote1 @remote2 @registry
  Scenario: Copy a registry model between two remote servers
    Given the registry model "smollm2:135m" on remote1
    When I run osync "cp {remote1}/smollm2:135m {remote2}/{dst}"
    Then the command succeeds
    And the model "{dst}" exists on remote2
