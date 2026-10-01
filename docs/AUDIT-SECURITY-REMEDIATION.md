# Local-security audit response — AIUsage 1.1.1

Base: 1.1.0, `90ed4c7ad853d41661fb999c733cc8f664cc3c11`. The private independent report was reviewed against that source. Its workspace, personal paths and traces are not published. This records our changes, not an independent certification.

| Finding | Disposition | Evidence and boundary |
|---|---|---|
| F-01: legacy instance activation | Fixed. Stable local-only instance/event namespace; detect the former mutex, warn and exit 2 without signalling or terminating it. | Core mutex control and published normal-entry-point UI test. The old namespace covers both 1.0.x and 1.1.0; the warning does not pretend to identify an exact version. |
| F-02: source fallback after settings failure | Fixed. Separate decode and migration failures. Keep valid preferences after an unsuccessful migration write; otherwise pause scans until an explicit local folder is saved. Display the active source. | Readonly, corrupt/deep/oversized/default settings tests and actual EXE recovery cases. |
| F-03: runtime writes/diagnostics | Documented and partly reduced. Folder deployment removes native bundle extraction. Describe old TEMP remnants, automatic settings migration and standard local .NET diagnostics IPC. | Native dependency/package checks and synthetic extraction-directory control. Diagnostics IPC is NOT disabled; no invented runtime switch or OS policy change. |
| F-04: startup hooks | Hardened: StartupHookSupport=false and shipped System.StartupHookProvider.IsSupported=false. | Actual runtimeconfig check and an innocuous hook positive control; no marker may appear from AIUsage. Same-user binary/config/environment control is not treated as a remote exploit. |
| F-05: cache exceeds its own reader limit | Persistence corrected with compact streaming, immutable pages, symmetric per-file limits, manifest-last commit and complete-generation validation. Reuse strings and remove large quote/scope allocations. | 500,000-event scan, exact totals, page bounds and persistent reload hit. Timings/heap/cache bytes are recorded, not a universal guarantee. Retained history and total disk still scale; appends may rewrite a generation. |
| F-06: unrelated JSONL discovery | Fixed: direct folders accept rollout-*.jsonl; history.jsonl is excluded even inside session directories. | Locked history and source discovery regressions. Candidate names do not authenticate arbitrary user-selected contents. |
| F-07: incomplete/fragile guards | Improved: precise symlink privilege skips, inspection of actual folder-published own DLLs, reviewed type/assembly and member policies, real-entry-point timer/UI tests. | A fixed embedded stylesheet is an explicit source-pinned XamlReader.Parse exception, not general external XAML loading. No new external network trace or claim that locked-file success proves zero open attempts. |
| F-08: informational boundaries | Retained/documented: supported settings, pseudonymous accounts and plain session IDs, path races and extreme input limits. | Streaming summaries use checked arithmetic; the UI reports overflow as an error. No guessed totals, anonymity or universal integrity claims. |

## Validation

Accounting, local-audit, localization, follow-up, security/stress, offline policies, published x64 desktop tests, bilingual rendering and packaging must pass before publication. Exact results and metrics are attached to the release. Existing tests were not relabelled as new independent audit results. Source/payload hashes identify the inspected artifact, not publisher identity.

Normal execution is tested separately from demo/smoke. ARM64, full multimonitor/RDP/accessibility behavior and external tracing remain outside these new execution checks. Historical 1.1.0 audit traces are not new 1.1.1 measurements.

## Primary technical references

- https://learn.microsoft.com/en-us/dotnet/core/deploying/ — self-contained folder deployment.
- https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview — native self-extraction and its path.
- https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trimming-options — StartupHookSupport.
- https://github.com/dotnet/sdk/blob/main/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.Sdk.targets — mapping to System.StartupHookProvider.IsSupported. WPF is NOT trimmed here.
- https://learn.microsoft.com/en-us/dotnet/core/diagnostics/diagnostic-port — standard local diagnostics IPC.

The no-account-access design and existing license/privacy qualifications remain. No legal certification, OpenAI approval, no-ban guarantee or signature is implied.
