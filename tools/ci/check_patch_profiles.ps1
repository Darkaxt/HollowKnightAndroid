[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InputContract,
    [Parameter(Mandatory = $true)][string]$OutputRoot,
    [Parameter(Mandatory = $true)][string]$RunToken
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
# This step never provisions inputs or reaches signing. Missing mounts fail closed.
& python -B (Join-Path $PSScriptRoot 'compile_receipt.py') check-profiles `
    --repo $repo --contract $InputContract --directory $OutputRoot --run-token $RunToken
if ($LASTEXITCODE -ne 0) { throw "Both-profile prerequisite failed with exit code $LASTEXITCODE" }
