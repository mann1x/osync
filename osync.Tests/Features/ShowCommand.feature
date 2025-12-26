Feature: Show Command
  As an osync user
  I want to view model information
  So that I can understand model details

  @basic @show
  Scenario: Show model information
    Given the model "{model}" exists locally
    When I run osync with arguments "show {model}"
    Then the command should succeed
    And the output should contain "architecture"
    And the output should contain "parameters"
    And the output should contain "quantization"

  @basic @show @license
  Scenario: Show model license
    Given the model "{model}" exists locally
    When I run osync with arguments "show {model} --license"
    Then the command should succeed
    And the output should contain "license"

  @basic @show @modelfile
  Scenario: Show model Modelfile
    Given the model "{model}" exists locally
    When I run osync with arguments "show {model} --modelfile"
    Then the command should succeed
    And the output should contain "FROM"

  @basic @show @parameters
  Scenario: Show model parameters
    Given the model "{model}" exists locally
    When I run osync with arguments "show {model} --parameters"
    Then the command should succeed
    And the output should contain "stop"

  @basic @show @template
  Scenario: Show model template
    Given the model "{model}" exists locally
    When I run osync with arguments "show {model} --template"
    Then the command should succeed

  @basic @show @system
  Scenario: Show model system message
    Given the model "{model}" exists locally
    When I run osync with arguments "show {model} --system"
    Then the command should succeed

  @basic @show @verbose
  Scenario: Show verbose model information
    Given the model "{model}" exists locally
    When I run osync with arguments "show {model} --verbose"
    Then the command should succeed
    And the output should contain "architecture"
    And the output should contain "parameters"
    And the output should contain "license"

  @basic @show @verbose @short
  Scenario: Show verbose model information with -v flag
    Given the model "{model}" exists locally
    When I run osync with arguments "show {model} -v"
    Then the command should succeed
    And the output should contain "architecture"

  @remote @show
  Scenario: Show model on remote server
    Given the model "{model}" exists on "{remote1}"
    When I run osync with arguments "show {model} -d {remote1}"
    Then the command should succeed
    And the output should contain "architecture"

  @remote @show @license
  Scenario: Show remote model license
    Given the model "{model}" exists on "{remote1}"
    When I run osync with arguments "show {model} -d {remote1} --license"
    Then the command should succeed
    And the output should contain "license"

  @basic @show @error
  Scenario: Show nonexistent model
    When I run osync with arguments "show nonexistent-model:v1"
    Then the command should fail
    And the output should contain "not found"

  @basic @show @multiple-flags
  Scenario: Show model with multiple flags
    Given the model "{model}" exists locally
    When I run osync with arguments "show {model} --license --parameters --template"
    Then the command should succeed
    And the output should contain "license"
    And the output should contain "stop"

  @basic @show @consistency
  Scenario: Verify show output format consistency
    Given the model "{model}" exists locally
    When I run osync with arguments "show {model}"
    Then the command should succeed
    And the output should be properly formatted
    And the output should contain valid JSON or structured text
