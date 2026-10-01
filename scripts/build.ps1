param([ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    foreach ($suite in @('AIUsage.Tests','AIUsage.AuditTests','AIUsage.LocalizationTests','AIUsage.FollowupTests','AIUsage.SecurityTests')) {
        dotnet run --project "tests/$suite" -c Release
        if ($LASTEXITCODE -ne 0) { throw "Regression tests failed: $suite" }
    }
    $out = Join-Path $root "artifacts/publish/$Runtime"
    dotnet publish src/AIUsage.Windows/AIUsage.Windows.csproj -c Release -r $Runtime --self-contained true -o $out -p:ContinuousIntegrationBuild=true
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $app = Join-Path $out 'AIUsage.dll'
    $core = Join-Path $out 'AIUsage.Core.dll'
    dotnet run --project tests/AIUsage.OfflineTests -c Release -- --repo-root $root --app-assembly $app --app-assembly $core
    if ($LASTEXITCODE -ne 0) { throw 'Local-only regression guards failed.' }
    & (Join-Path $PSScriptRoot 'collect-notices.ps1') -Runtime $Runtime -PublishDirectory $out
    Copy-Item README.md, LICENSE, THIRD-PARTY-NOTICES.md $out
    Copy-Item docs (Join-Path $out 'docs') -Recurse -Force
    & (Join-Path $PSScriptRoot 'verify-package.ps1') -PublishDirectory $out -Runtime $Runtime
    Write-Host "Ready: $out/AIUsage.exe"
} finally { Pop-Location }
