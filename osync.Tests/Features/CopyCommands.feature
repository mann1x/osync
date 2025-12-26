Feature: Copy Commands
  As an osync user
  I want to copy models between local and remote servers
  So that I can distribute models across my infrastructure

  Background:
    Given the model "{model}" exists locally

  @basic @copy @local
  Scenario: Copy local model to new name
    When I run osync with arguments "copy {model} test-copy-local:v1"
    Then the command should succeed
    And the output should contain "Copying model"
    And the model "test-copy-local:v1" should exist locally

  @remote @copy @upload
  Scenario: Copy local model to remote server 1
    When I run osync with arguments "copy {model} {remote1}/test-copy-remote1:v1"
    Then the command should succeed
    And the output should contain "Uploading model"
    And the model "test-copy-remote1:v1" should exist on "{remote1}"

  @remote @copy @upload
  Scenario: Copy local model to remote server 2
    When I run osync with arguments "copy {model} {remote2}/test-copy-remote2:v1"
    Then the command should succeed
    And the output should contain "Uploading model"
    And the model "test-copy-remote2:v1" should exist on "{remote2}"

  @remote @copy @remote-to-remote @destructive
  Scenario: Copy from remote server 1 to remote server 2
    Given the model "test-r2r-source:v1" exists on "{remote1}"
    When I run osync with arguments "copy {remote1}/test-r2r-source:v1 {remote2}/test-r2r-dest:v1"
    Then the command should succeed
    And the output should contain "Remote-to-remote copy"
    And the model "test-r2r-dest:v1" should exist on "{remote2}"

  @basic @copy @incremental
  Scenario: Copy same model twice (incremental upload)
    Given the model "test-incremental:v1" exists on "{remote1}"
    When I run osync with arguments "copy {model} {remote1}/test-incremental:v1"
    Then the command should succeed
    And the output should contain "already exists"

  @basic @copy @error
  Scenario: Copy with invalid source model
    When I run osync with arguments "copy nonexistent-model:v1 test-fail:v1"
    Then the command should fail
    And the output should contain "not found"

  @basic @copy @alias
  Scenario: Copy using cp alias
    When I run osync with arguments "cp {model} test-copy-alias:v1"
    Then the command should succeed
    And the model "test-copy-alias:v1" should exist locally
