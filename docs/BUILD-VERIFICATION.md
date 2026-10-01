# Build and verification — 1.1.0

## Requirements and trust boundary

Build with .NET 10 on Windows. SDK/NuGet restore, license collection and GitHub distribution use public networks. The installed application does not run those tools. Read scripts before executing them. `scripts/build.ps1` validates the suites and API boundary before producing a self-contained local package; CI also runs the published x64 executable and creates screenshots. Never supply real authentication or conversations to CI.

## Suites

Tests are independent executable runners, not VSTest projects:

```powershell
dotnet run --project tests/AIUsage.Tests -c Release
dotnet run --project tests/AIUsage.AuditTests -c Release
dotnet run --project tests/AIUsage.LocalizationTests -c Release
dotnet run --project tests/AIUsage.FollowupTests -c Release
```

The original accounting suite has 48 cases. The local audit suite now has 34: eleven tests specific to the deleted authenticated client/online merging were retired with that feature. Localization retains 22 cases, updated for offline migration and messages. The 17 follow-up accounting regressions remain. These are distinct suites, not historical counts silently summed into the new result.

After publishing both runtime identifiers, run the local-only suite against the actual intermediate application-owned assemblies used by publishing:

```powershell
dotnet run --project tests/AIUsage.OfflineTests -c Release -- --repo-root . --app-assembly src/AIUsage.Windows/bin/Release/net10.0-windows/win-x64/AIUsage.dll --app-assembly src/AIUsage.Windows/bin/Release/net10.0-windows/win-arm64/AIUsage.dll
```

Source/API guards fail if a client, credential reader, subprocess/browser integration, dynamic assembly loading or unapproved native import returns. The bundled .NET/WPF framework may contain networking capability; the inspection targets **our code**, not all framework APIs. The suite also tests local-path rejection, settings migration, immutable sources, locked synthetic credentials, local export and built-in price validation. It observes .NET network-start events in its own synthetic test process; this is not a packet capture. Test output reports skips explicitly, including hosts unable to create a synthetic symbolic link.

`dotnet test` deliberately errors with AIU0001 rather than returning success after zero tests. CI verifies that guard for all five projects.

## Published x64 executable

```powershell
.\scripts\verify-local-runtime.ps1 -Executable artifacts/publish/win-x64/AIUsage.exe -Destination artifacts/screenshots
```

The helper makes a temporary synthetic Codex folder and AIUsage data directory, with legacy online=true settings, a 1,100-token rollout and exclusively locked fake credential/configuration files. It exercises the normal app workflow, requires correct accounting and no error, verifies settings cleanup and unchanged source hashes, and checks that only local Settings are available. It then checks both language selections, invariant demo totals, tray translation, dark/light rendering and scroll/focus preservation. Eight synthetic screenshots and separate local/UI success markers must exist. The fake credential directory is removed before diagnostics are uploaded.

## Packaging and evidence

Windows CI checks application/manifest/tag version agreement, compiles x64 and ARM64, collects six complete runtime license/notice files, and packages the app, English documentation and source snapshot. BUILD-INFO identifies commit/run/SDK. SHA256SUMS checks integrity. None is a publisher signature or cryptographic attestation.

A failing test, guard or packaging step blocks release. A PR never publishes. Every release has its own immutable-in-practice tag/files; the workflow refuses to overwrite an existing release. Previous releases remain historical, not represented as local-only builds.

## Not established

No legal or provider-policy certification, account-enforcement guarantee, real-account comparison, external network trace, hostile-OS sandbox proof, full accessibility test, mixed-DPI/multimonitor matrix, RDP/Explorer matrix or ARM64 execution. Built-in paths/metadata guards reduce unintended remote file access but cannot control Windows, security agents, cloud synchronization or every storage provider. Existing accounting limitations remain documented in README and historical audit notes.
