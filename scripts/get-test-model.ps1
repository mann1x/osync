# Downloads the integration-test base model (see osync.Tests/Assets/test-model.json) from the
# 'test-assets' release of this repository into osync.Tests/Assets/ and verifies its checksum.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$lock = Get-Content (Join-Path $root 'osync.Tests/Assets/test-model.json') -Raw | ConvertFrom-Json
$repo = if ($env:OSYNC_REPO) { $env:OSYNC_REPO } else { 'mann1x/osync' }
$dest = Join-Path $root "osync.Tests/Assets/$($lock.file)"

function Test-Model { (Test-Path $dest) -and ((Get-FileHash $dest -Algorithm SHA256).Hash.ToLower() -eq $lock.sha256) }

if (Test-Model) { Write-Host "Test model already present: $dest"; exit 0 }
Write-Host "Downloading $($lock.file) ..."
Invoke-WebRequest -Uri "https://github.com/$repo/releases/download/$($lock.releaseTag)/$($lock.file)" -OutFile "$dest.part"
Move-Item -Force "$dest.part" $dest
if (-not (Test-Model)) { Remove-Item $dest; throw "Checksum mismatch for $dest" }
Write-Host "OK: $dest"
