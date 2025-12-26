Feature: Rename Command
  As an osync user
  I want to rename models
  So that I can organize my model library

  @basic @rename @destructive
  Scenario: Rename local model
    Given the model "{model}" exists locally
    When I run osync with arguments "copy {model} test-rename-source:v1"
    And I run osync with arguments "rename test-rename-source:v1 test-rename-dest:v1"
    Then the command should succeed
    And the output should contain "Renaming model"
    And the model "test-rename-dest:v1" should exist locally
    And the model "test-rename-source:v1" should not exist locally

  @basic @rename @alias
  Scenario: Rename using mv alias
    Given the model "{model}" exists locally
    When I run osync with arguments "copy {model} test-mv-source:v1"
    And I run osync with arguments "mv test-mv-source:v1 test-mv-dest:v1"
    Then the command should succeed
    And the model "test-mv-dest:v1" should exist locally
    And the model "test-mv-source:v1" should not exist locally

  @basic @rename @alias
  Scenario: Rename using ren alias
    Given the model "{model}" exists locally
    When I run osync with arguments "copy {model} test-ren-source:v1"
    And I run osync with arguments "ren test-ren-source:v1 test-ren-dest:v1"
    Then the command should succeed
    And the model "test-ren-dest:v1" should exist locally
    And the model "test-ren-source:v1" should not exist locally

  @basic @rename @error
  Scenario: Rename nonexistent model
    When I run osync with arguments "rename nonexistent-model:v1 test-fail:v1"
    Then the command should fail
    And the output should contain "not found"

  @basic @rename @tag
  Scenario: Rename without explicit tag (uses :latest)
    Given the model "{model}" exists locally
    When I run osync with arguments "copy {model} test-tag-source"
    And I run osync with arguments "rename test-tag-source test-tag-dest"
    Then the command should succeed
    And the model "test-tag-dest:latest" should exist locally
