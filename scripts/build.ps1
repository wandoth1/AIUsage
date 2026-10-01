param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet run --project tests/AIUsage.Tests/AIUsage.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed.' }
    $out = Join-Path $root "artifacts/publish/$Runtime"
    dotnet publish src/AIUsage.Windows/AIUsage.Windows.csproj -c Release -r $Runtime --self-contained true -o $out -p:ContinuousIntegrationBuild=true
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    & (Join-Path $PSScriptRoot 'collect-notices.ps1') -Runtime $Runtime -PublishDirectory $out
    Copy-Item README.md, LICENSE, THIRD-PARTY-NOTICES.md $out
    Copy-Item docs (Join-Path $out 'docs') -Recurse -Force
    Write-Host "Ready: $out/AIUsage.exe"
} finally { Pop-Location }
