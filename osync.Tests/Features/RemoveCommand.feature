Feature: Remove Command
  As an osync user
  I want to remove models
  So that I can manage storage space

  @basic @remove @destructive
  Scenario: Remove local model
    Given the model "{model}" exists locally
    When I run osync with arguments "copy {model} test-remove-single:v1"
    And I run osync with arguments "remove test-remove-single:v1"
    Then the command should succeed
    And the output should contain "Deleting model"
    And the model "test-remove-single:v1" should not exist locally

  @remote @remove @destructive
  Scenario: Remove model from remote server
    Given the model "test-remove-remote:v1" exists on "{remote1}"
    When I run osync with arguments "remove test-remove-remote:v1 -d {remote1}"
    Then the command should succeed
    And the model "test-remove-remote:v1" should not exist on "{remote1}"

  @basic @remove @pattern @destructive
  Scenario: Remove models matching pattern
    Given the model "{model}" exists locally
    When I run osync with arguments "copy {model} test-pattern-1:v1"
    And I run osync with arguments "copy {model} test-pattern-2:v1"
    And I run osync with arguments "copy {model} test-pattern-3:v1"
    And I run osync with arguments "remove test-pattern-*"
    Then the command should succeed
    And the model "test-pattern-1:v1" should not exist locally
    And the model "test-pattern-2:v1" should not exist locally
    And the model "test-pattern-3:v1" should not exist locally

  @basic @remove @alias @destructive
  Scenario: Remove using rm alias
    Given the model "{model}" exists locally
    When I run osync with arguments "copy {model} test-rm-alias:v1"
    And I run osync with arguments "rm test-rm-alias:v1"
    Then the command should succeed
    And the model "test-rm-alias:v1" should not exist locally

  @basic @remove @alias @destructive
  Scenario: Remove using delete alias
    Given the model "{model}" exists locally
    When I run osync with arguments "copy {model} test-delete-alias:v1"
    And I run osync with arguments "delete test-delete-alias:v1"
    Then the command should succeed
    And the model "test-delete-alias:v1" should not exist locally

  @basic @remove @alias @destructive
  Scenario: Remove using del alias
    Given the model "{model}" exists locally
    When I run osync with arguments "copy {model} test-del-alias:v1"
    And I run osync with arguments "del test-del-alias:v1"
    Then the command should succeed
    And the model "test-del-alias:v1" should not exist locally

  @basic @remove @error
  Scenario: Remove nonexistent model
    When I run osync with arguments "remove nonexistent-model:v1"
    Then the command should fail
    And the output should contain "not found"
