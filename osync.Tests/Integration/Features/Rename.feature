@local
Feature: Rename models (rename, mv, ren)

  Scenario Outline: Rename a local model with <command>
    Given a test model "before" on local
    And a test model "reference" on local
    When I run osync "<command> {before} {after}"
    Then the command succeeds
    And the model "{after}" exists on local
    And the model "{before}" does not exist on local
    And the model "{after}" on local is identical to "{reference}" on local

    Examples:
      | command |
      | rename  |
      | mv      |
      | ren     |

  Scenario: Rename to a new tag
    Given a test model "model:old" on local
    When I run osync "mv {model}:old {model}:new"
    Then the command succeeds
    And the model "{model}:new" exists on local
    And the model "{model}:old" does not exist on local

  Scenario: Rename onto an existing model is refused and changes nothing
    Given a test model "before" on local
    And a test model "taken" on local
    When I run osync "mv {before} {taken}"
    Then the command fails
    And the model "{before}" exists on local
    And the model "{taken}" exists on local

  Scenario: Rename of a missing model fails
    When I run osync "mv {missing} {after}"
    Then the command fails
    And the model "{after}" does not exist on local
