# Build and verification — 1.5.0

## Requirements

Build on Windows using the .NET 10 SDK. Restore, license collection and GitHub distribution need the internet; the installed application does not run those tools. Inspect scripts before executing them. Never supply real credentials or conversations to CI.

## Executable test suites

```powershell
dotnet run --project tests/AIUsage.Tests -c Release
dotnet run --project tests/AIUsage.AuditTests -c Release
dotnet run --project tests/AIUsage.LocalizationTests -c Release
dotnet run --project tests/AIUsage.FollowupTests -c Release
dotnet run --project tests/AIUsage.SecurityTests -c Release -- --stress
```

The first four suites cover accounting, local-audit fixes, localization and the large-message follow-up. Eleven old authenticated-client tests were retired with that deleted feature in 1.1.0; they are not counted as passes. SecurityTests covers safe settings recovery, source discovery, page atomicity/corruption/path validation, and 500,000 synthetic usage events. The stress case requires exact totals and a persistent cache hit, and reports durations, retained managed heap and cache sizes.

After publishing, inspect the **actual payload assemblies**, not only intermediate outputs:

```powershell
dotnet run --project tests/AIUsage.OfflineTests -c Release -- --repo-root . --app-assembly artifacts/publish/win-x64/AIUsage.dll --app-assembly artifacts/publish/win-arm64/AIUsage.dll --app-assembly artifacts/publish/win-x64/AIUsage.Core.dll --app-assembly artifacts/publish/win-arm64/AIUsage.Core.dll
```

Reviewed assembly/type allowlists reject additions until inspected. Member guards prohibit dynamic activation and external loaders. The fixed embedded theme stylesheet has a source-pinned Parse-only exception; no external XAML is accepted. Source guards and local-path/migration/immutable-file tests remain. Framework functionality is not equated with a capability used by our code. The test process observes its own .NET network-start events, not external traffic from WPF or Windows. Symlink privilege limits are explicitly skipped.

`dotnet test` must error with AIU0001 rather than silently run zero tests. CI verifies all seven executable projects. The startup-hook fixture is a separate test-only class library.

## Published executable validation

`scripts/verify-local-runtime.ps1` creates isolated synthetic logs/settings, locks fake credentials/configuration, invokes the existing normal-workflow smoke fixture, checks 1,100 tokens and unchanged source hashes, and generates eight bilingual dark/light dashboard/Settings screenshots. Demo/smoke output is not a full normal-entry-point proof.

`AIUsage.DesktopTests` adds independent UI Automation of the shipped x64 EXE **without smoke/demo flags**. It runs only on a clean ephemeral GitHub Actions Windows host and refuses an existing profile. It checks legacy conflict warning/no old-event signalling, readonly migration, corrupt-settings pause through a timer interval, explicit source confirmation, same-version activation, two automatic updates, and clean exit. A harmless startup-hook marker has a positive control and must not run in AIUsage. Synthetic auth/config/history files are locked. No real profile is used.

These checks are not ETW/packet capture and do not demonstrate absence of every failed file-open attempt. Locked files test behavior when access is unavailable. The supplied 1.1.0 audit's traces are not relabelled as our new measurements. Diagnostics IPC remains available and documented. ARM64 is not executed.

## Package and release gate

`scripts/verify-package.ps1` checks the shipped startup-hook option, own DLLs and native WPF files, compares the built/published application assembly and writes per-file PAYLOAD-SHA256.json. CI packages the entire self-contained folder with English docs and six runtime notices. Assembly/manifest/tag versions must agree. BUILD-INFO identifies commit/run/SDK, and SHA256SUMS identifies package integrity, not a publisher signature.

Any failed test, guard, smoke or package check blocks publication. PRs never publish. Existing releases are not overwritten. Test logs, metrics and synthetic screenshots accompany the final release. Binaries remain unsigned; multimonitor/RDP/accessibility, hostile-OS and legal-certification claims remain outside scope.
