# Run the osync test suite locally (Windows / PowerShell 7).
#   scripts\test.ps1 unit                 unit + CLI tests (no Ollama needed)
#   scripts\test.ps1 integration          integration tests against your local Ollama (OSYNC_TEST_LOCAL / OLLAMA_HOST / :11434)
#   scripts\test.ps1 all                  both
#   -Servers                              also start two throwaway Ollama servers on :11435 and :11436 as remote1/remote2
#   -KnownBug                             run only the @knownbug scenarios (expected to fail until fixed)
# Registry tests (pull/update from the internet) run only with $env:OSYNC_TEST_REGISTRY = "1".
param(
    [ValidateSet('unit', 'integration', 'all')][string]$Mode = 'all',
    [switch]$Servers,
    [switch]$KnownBug
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$filter = switch ($Mode) {
    'unit' { 'FullyQualifiedName!~osync.Tests.Integration|Category=cli' }
    'integration' { 'FullyQualifiedName~osync.Tests.Integration' }
    'all' { 'FullyQualifiedName~osync' }
}
$filter = if ($KnownBug) { "($filter)&Category=knownbug" } else { "($filter)&Category!=knownbug" }

$procs = @(); $tmp = $null
try {
    if ($Mode -ne 'unit') {
        & (Join-Path $PSScriptRoot 'get-test-model.ps1')
        if ($Servers) {
            $tmp = New-Item -ItemType Directory -Path (Join-Path ([IO.Path]::GetTempPath()) "osync-test-$([guid]::NewGuid().ToString('N').Substring(0,6))")
            foreach ($port in 11435, 11436) {
                $env:OLLAMA_HOST = "127.0.0.1:$port"; $env:OLLAMA_MODELS = Join-Path $tmp $port
                $procs += Start-Process ollama -ArgumentList 'serve' -PassThru -WindowStyle Hidden `
                    -RedirectStandardOutput (Join-Path $tmp "ollama-$port.log") -RedirectStandardError (Join-Path $tmp "ollama-$port.err")
                $deadline = (Get-Date).AddSeconds(60)
                while ((Get-Date) -lt $deadline) {
                    try { Invoke-RestMethod "http://localhost:$port/api/version" | Out-Null; break } catch { Start-Sleep 1 }
                }
            }
            Remove-Item Env:OLLAMA_HOST, Env:OLLAMA_MODELS
            if (-not $env:OSYNC_TEST_REMOTE1) { $env:OSYNC_TEST_REMOTE1 = 'http://localhost:11435' }
            if (-not $env:OSYNC_TEST_REMOTE2) { $env:OSYNC_TEST_REMOTE2 = 'http://localhost:11436' }
            if (-not $env:OSYNC_TEST_EXCLUSIVE) { $env:OSYNC_TEST_EXCLUSIVE = '1' }
        }
    }
    dotnet build (Join-Path $root 'osync.sln')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet test (Join-Path $root 'osync.Tests') --no-build --filter $filter
    exit $LASTEXITCODE
}
finally {
    $procs | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    if ($tmp) { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }
}
