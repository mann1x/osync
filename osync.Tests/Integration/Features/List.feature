@local
Feature: List models (ls)

  Scenario: List a local model by exact name
    Given a test model "alpha" on local
    When I run osync "ls {alpha}"
    Then the command succeeds
    And the output contains "{alpha}:latest"

  Scenario: Wildcard pattern only lists matching models
    Given a test model "match-1" on local
    And a test model "match-2" on local
    And a test model "other" on local
    When I run osync "ls {prefix}match-*"
    Then the command succeeds
    And the output contains "{match-1}"
    And the output contains "{match-2}"
    And the output does not contain "{other}"

  Scenario: Pattern without matches succeeds with an empty result
    When I run osync "ls {prefix}nothing-*"
    Then the command succeeds
    And the output does not contain ":latest"

  @remote1
  Scenario: List models on a remote server
    Given a test model "remote-alpha" on remote1
    When I run osync "ls {prefix}* -d {remote1}"
    Then the command succeeds
    And the output contains "{remote-alpha}:latest"
