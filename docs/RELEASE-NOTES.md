# AIUsage 1.1.1 for Windows — local security maintenance

Independent AIUsage versioning; MIT attribution to OpenUsage is preserved. The exclusively local design remains: no account client, credential reader, provider requests, model calls, updater or subprocess integration.

## Corrections

- Separate local-only instance/event names. A legacy instance triggers a warning instead of being silently activated; another version is never terminated automatically.
- Keep the configured source when only migration writing fails. Unreadable settings pause scans until an explicit folder is saved. The active source is visible.
- Direct-folder fallback reads only `rollout-*.jsonl`; exclude `history.jsonl` everywhere.
- Stream compact cache JSON and split large histories into immutable 4,096-event pages with a shared 32 MiB per-file read/write limit. Commit the manifest last, reject incomplete generations and rebuild schema-4 caches. Avoid giant quote arrays and per-event scope strings.
- Disable .NET startup hooks. Use an inspectable self-contained folder instead of native-library bundle extraction into TEMP.
- Inspect actual published own DLLs with reviewed API policies, add real-entry-point x64 UI/timer/settings/hook tests and a 500,000-event persistence workload. Missing symlink privileges are reported precisely as a skip.

## Download and upgrade

Use `AIUsage-1.1.1-win-x64.zip` on Intel/AMD or `AIUsage-1.1.1-win-arm64.zip` on Windows ARM. Exit the older app, extract **every file** into a new local folder, and run `AIUsage.exe`. Keep DLLs and the `es` subfolder; the EXE is not independently portable. .NET is included. Never delete `.codex`. See privacy documentation for optional cleanup of older `%TEMP%\.net\AIUsage` extraction remnants.

## Evidence and limits

Test logs, package checks, build information and hashes accompany this release. x64 execution uses synthetic data; ARM64 is compiled and inspected, not executed. The private audit workspace is not published.

Standard .NET local diagnostics IPC remains documented, not disabled. This is not a new ETW/packet-capture result, legal certification, provider approval, ban guarantee, publisher signature or compromised-OS sandbox. Binaries remain unsigned. Large histories still need proportional memory/disk, and conservative accounting exclusions remain visible. See `docs/AUDIT-SECURITY-REMEDIATION.md` for each finding's disposition.
