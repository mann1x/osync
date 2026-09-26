Feature: Remove models (rm, delete, del)

  @local
  Scenario Outline: Remove a local model with <command>
    Given a test model "victim" on local
    And a test model "bystander" on local
    When I run osync "<command> {victim}"
    Then the command succeeds
    And the model "{victim}" does not exist on local
    And the model "{bystander}" exists on local

    Examples:
      | command |
      | remove  |
      | rm      |
      | delete  |
      | del     |

  @local
  Scenario: Remove models matching a wildcard pattern
    Given a test model "batch-1" on local
    And a test model "batch-2" on local
    And a test model "keep" on local
    When I run osync "rm {prefix}batch-*"
    Then the command succeeds
    And no model starting with "{prefix}batch-" exists on local
    And the model "{keep}" exists on local

  @local
  Scenario: Remove only the given tag
    Given a test model "tagged:v1" on local
    And a test model "tagged:v2" on local
    When I run osync "rm {tagged}:v1"
    Then the command succeeds
    And the model "{tagged}:v1" does not exist on local
    And the model "{tagged}:v2" exists on local

  # rm prints "No models found matching pattern" and exits 0.
  @local @knownbug
  Scenario: Removing a missing model fails
    When I run osync "rm {missing}"
    Then the command fails

  @remote1
  Scenario: Remove a model on a remote server
    Given a test model "victim" on remote1
    And a test model "bystander" on remote1
    When I run osync "rm {victim} -d {remote1}"
    Then the command succeeds
    And the model "{victim}" does not exist on remote1
    And the model "{bystander}" exists on remote1
