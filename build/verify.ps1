#!/usr/bin/env pwsh
# Single per-iteration Verify gate: build (warnings-as-errors via Directory.Build.props),
# format check, then tests. Non-interactive; exits non-zero on the first failure.
$ErrorActionPreference = 'Stop'

Write-Host '== build (Release) =='
dotnet build PodBridge.slnx -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '== format --verify-no-changes =='
dotnet format PodBridge.slnx --verify-no-changes
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '== test =='
dotnet test PodBridge.slnx -c Release --no-build
exit $LASTEXITCODE
