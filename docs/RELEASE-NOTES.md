# AIUsage 1.1.0 — local-only Windows release

AIUsage is now exclusively local. **The optional authenticated integration is removed from the code, not just disabled.** No HTTP client, credential reader, account API/app-server call, browser launch, external editor or telemetry remains in application-owned code.

## Downloads

- `AIUsage-1.1.0-win-x64.zip` — Intel/AMD Windows.
- `AIUsage-1.1.0-win-arm64.zip` — Windows ARM; cross-compiled, not hardware-tested.
- `AIUsage-1.1.0-source.zip` — source snapshot.

Portable and self-contained. Extract the complete ZIP on a local disk and open `AIUsage.exe`. **Exit the old version from its tray menu first.** Closing its window is not enough. Settings migrate automatically, discarding the former online option. Do not delete `.codex`. Keep older releases only for intentional historical comparison, not accidental daily use.

## Retained and changed

English/Spanish, themes, tray integration, tokens, bundled/custom API estimates, per-model details, history, incremental parsing and both accounting-audit fixes are retained. Limits are now **only historical values already in logs**, with observation time and stale-reset labels. No current account balance is queried.

Network/UNC/WSL shares, mapped network drives, device paths, links/junctions and remote-storage placeholders are rejected or skipped. Old network paths need a separate local rollout copy. CSV now saves to `%LOCALAPPDATA%\AIUsage\exports`; its path is displayed without launching another program. Custom prices use an internal JSON editor with validation.

## Verification

Publication is gated on the accounting, local audit, localization, follow-up and new offline suites; executable-runner guards; compiled Core/x64/ARM64 API checks; normal x64 workflow with inaccessible synthetic credentials, unchanged source hashes and migration from online=true; and bilingual dark/light demo rendering. Test outputs, BUILD-INFO and SHA256SUMS accompany this release. Retired tests for the deleted online client are explicitly documented, not counted as passes.

No real credentials or private conversations were used. ARM64 is not executed. This is not a packet-capture result, legal opinion, provider approval or zero-ban guarantee. Windows/security/sync services may communicate independently of AIUsage. Use resident non-synchronized local storage for offline operation. Build/release tooling, unlike the app, uses the internet.

Unsigned binaries; hashes are integrity checks, not publisher signatures. No installer/updater, automatic startup, other AI providers or claim of complete OpenUsage parity. MIT and runtime notices are retained. See README, docs/PRIVACY.md and docs/LOCAL-ONLY.md for scope and remaining limitations.
