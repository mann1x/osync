Feature: Update Command
  As an osync user
  I want to update models to their latest versions
  So that I can keep my models current

  @basic @update @destructive
  Scenario: Update single local model
    Given the model "{model}" exists locally
    When I run osync with arguments "update {model}"
    Then the command should succeed
    And the output should contain "Updating model"

  @basic @update @pattern @destructive
  Scenario: Update models matching pattern
    Given the model "{model}" exists locally
    When I run osync with arguments "update llama*"
    Then the command should succeed
    And the output should contain "Updating"

  @remote @update @destructive
  Scenario: Update model on remote server
    Given the model "{model}" exists on "{remote1}"
    When I run osync with arguments "update {model} -d {remote1}"
    Then the command should succeed
    And the output should contain "Updating"

  @basic @update @error
  Scenario: Update nonexistent model
    When I run osync with arguments "update nonexistent-model:v1"
    Then the command should fail
    And the output should contain "not found"

  @basic @update @all
  Scenario: Update all models with wildcard
    Given the model "{model}" exists locally
    When I run osync with arguments "update *"
    Then the command should succeed
