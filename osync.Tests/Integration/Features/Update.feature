Feature: Update errors (update)
  Updating models that do not exist is an error. (Updating real registry models is covered by
  PullUpdate.feature, which needs internet access.)

  @local
  Scenario: Updating a missing model fails
    When I run osync "update {missing}"
    Then the command fails
    And the output contains "No models found matching pattern"

  @remote1
  Scenario: Updating a missing model on a remote server fails
    Given a test model "bystander" on remote1
    When I run osync "update {missing} -d {remote1}"
    Then the command fails
    And the model "{bystander}" exists on remote1
