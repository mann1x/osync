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

  # Regression: in http://server/model:tag the ':' of the tag was taken for a port, so the URL got port 80.
  # A server URL without a port uses 11434 (or 22434 when only an xOllama answers there).
  @local @remote1 @defaultport
  Scenario: A server URL without a port uses the default port even when the model has a tag
    Given a test model "src:v1" on local
    When I run osync "cp http://localhost/{src}:v1 {remote1}/{dst}"
    Then the command succeeds
    And the model "{dst}" on remote1 is identical to "{src}:v1" on local

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

  # When the local models directory cannot be read (e.g. the server runs as the ollama service user), the
  # upload goes through the local server with the push relay instead of reading the blobs from disk.
  @local @remote1
  Scenario: Upload through the local server when the local models directory is not readable
    Given a test model "src" on local
    When I run osync "cp {src} {remote1}/{dst}" without access to the local models directory
    Then the command succeeds
    And the output contains "copying through the local server"
    And the model "{dst}" on remote1 is identical to "{src}" on local

  # Remote-to-local and remote-to-remote copies go through osync's push relay: the source server pushes
  # the model to a temporary registry endpoint run by osync, which streams every blob into the destination.
  # This works for any model on the source, including created/imported ones that are not in any registry.
  @remote1 @local
  Scenario: Download a model from a remote server to local
    Given a test model "src" on remote1
    When I run osync "cp {remote1}/{src} {dst}"
    Then the command succeeds
    And the model "{dst}" on local is identical to "{src}" on remote1

  @remote1 @remote2
  Scenario: Copy a model between two remote servers
    Given a test model "src" on remote1
    When I run osync "cp {remote1}/{src} {remote2}/{dst}"
    Then the command succeeds
    And the model "{dst}" on remote2 is identical to "{src}" on remote1

  @remote1 @remote2
  Scenario: Copy between remote servers keeps the model name when none is given
    Given a test model "src" on remote1
    When I run osync "cp {remote1}/{src} {remote2}"
    Then the command succeeds
    And the model "{src}" on remote2 is identical to "{src}" on remote1

  @remote1 @remote2
  Scenario: Copy between remote servers skips layers the destination already has
    Given a test model "src" on remote1
    And a test model "existing" on remote2
    When I run osync "cp {remote1}/{src} {remote2}/{dst}"
    Then the command succeeds
    And the output contains "already present on the destination"
    And the model "{dst}" on remote2 is identical to "{src}" on remote1

  # Fallback used when the destination cannot connect to the relay: the blobs are already on the
  # destination and the model is recreated with /api/create from the manifest the source pushed.
  @remote1 @remote2
  Scenario: Copy between remote servers recreates the model when the manifest cannot be installed
    Given a test model "src" on remote1
    When I run osync "cp {remote1}/{src} {remote2}/{dst}" with OSYNC_RELAY_INSTALL set to "create"
    Then the command succeeds
    And the output contains "Recreating the model from its manifest"
    And the model "{dst}" on remote2 is identical to "{src}" on remote1
    And no relay model exists on remote2

  # Regression: the recreate dropped the renderer and parser (and xOllama's settings), yet reported success.
  @remote1 @remote2
  Scenario: Recreating a model keeps its renderer and parser
    Given a test model "src" with renderer "qwen3-coder" on remote1
    When I run osync "cp {remote1}/{src} {remote2}/{dst}" with OSYNC_RELAY_INSTALL set to "create"
    Then the command succeeds
    And the output contains "recreated from its manifest, verified"
    And the model "{dst}" on remote2 is identical to "{src}" on remote1

  # Byte for byte: the recreate sends the source's config and settings layers verbatim, so /api/create writes the
  # same blobs. The second copy finds every blob already on the destination; the relay then asks the source again
  # for the small ones.
  @remote1 @remote2 @stores
  Scenario: A recreated model has the source's layers byte for byte
    Given a test model "src" with renderer "qwen3-coder" on remote1
    When I run osync "cp {remote1}/{src} {remote2}/{first}" with OSYNC_RELAY_INSTALL set to "create"
    Then the command succeeds
    When I run osync "cp {remote1}/{src} {remote2}/{dst}" with OSYNC_RELAY_INSTALL set to "create"
    Then the command succeeds
    And the model "{first}" on remote2 has the same layers as "{src}" on remote1
    And the model "{dst}" on remote2 has the same layers as "{src}" on remote1

  @local @remote1 @stores
  Scenario: Uploading a local model keeps its layers byte for byte
    Given a test model "src" with renderer "qwen3-coder" on local
    When I run osync "cp {src} {remote1}/{dst}"
    Then the command succeeds
    And the output contains "recreated from its manifest, verified"
    And the model "{dst}" on remote1 has the same layers as "{src}" on local

  # Interoperability: remote1/remote2 run one flavor (Ollama or xOllama), peer the other.
  @remote1 @peer @stores
  Scenario: Copy to a server of the other flavor keeps every layer
    Given a test model "src" with renderer "qwen3-coder" on remote1
    When I run osync "cp {remote1}/{src} {peer}/{dst}"
    Then the command succeeds
    And the model "{dst}" on peer has the same layers as "{src}" on remote1

  @peer @remote2 @stores
  Scenario: Copy from a server of the other flavor keeps every layer
    Given a test model "src" with renderer "qwen3-coder" on peer
    When I run osync "cp {peer}/{src} {remote2}/{dst}"
    Then the command succeeds
    And the model "{dst}" on remote2 has the same layers as "{src}" on peer

  @remote1 @peer @stores
  Scenario: Recreating on a server of the other flavor keeps every layer
    Given a test model "src" with renderer "qwen3-coder" on remote1
    When I run osync "cp {remote1}/{src} {peer}/{dst}" with OSYNC_RELAY_INSTALL set to "create"
    Then the command succeeds
    And the output contains "recreated from its manifest, verified"
    And the model "{dst}" on peer has the same layers as "{src}" on remote1

  @remote1 @remote2
  Scenario: Copying a missing remote model fails and creates nothing
    When I run osync "cp {remote1}/{missing} {remote2}/{dst}"
    Then the command fails
    And the model "{dst}" does not exist on remote2

  @remote1 @remote2
  Scenario: The relay leaves no temporary models behind
    Given a test model "src" on remote1
    When I run osync "cp {remote1}/{src} {remote2}/{dst}"
    Then the command succeeds
    And no relay model exists on remote1
    And no relay model exists on remote2

  @remote1 @remote2 @registry
  Scenario: Copy a registry model between two remote servers
    Given the registry model "smollm2:135m" on remote1
    When I run osync "cp {remote1}/smollm2:135m {remote2}/{dst}"
    Then the command succeeds
    And the model "{dst}" exists on remote2
