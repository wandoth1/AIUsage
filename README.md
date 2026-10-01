# AIUsage for Windows

**Your Codex usage, entirely local.** AIUsage 1.1.0 reads existing usage logs on your local disk and shows tokens, estimated API-equivalent costs by model, and historical limits recorded by Codex. Native C# / WPF / .NET 10, with **English and Spanish**, dark/light themes and a system tray icon.

**No account access. No credential reader. No network client.** The optional online integration from 1.0.0 and earlier has been removed from the application, not merely disabled. There is no switch, token, app-server integration or undocumented endpoint that can re-enable it. The app does not launch Codex, another AI, a browser or an external editor.

AIUsage is an **independent Windows port of [OpenUsage](https://github.com/robinebers/openusage)** under MIT, not an OpenAI or official OpenUsage product. Our versions and roadmap are independent; selected upstream improvements may be ported after review. See [provenance](docs/UPSTREAM.md), [versioning](docs/VERSIONING.md), [local-only design](docs/LOCAL-ONLY.md) and [privacy](docs/PRIVACY.md). No affiliation, provider approval, legal certification or guarantee about account enforcement is implied.

[Download for Windows](https://github.com/wandoth1/AIUsage/releases/latest) · [Builds and tests](https://github.com/wandoth1/AIUsage/actions/workflows/windows.yml) · [Security](SECURITY.md)

## Download and upgrade

| Package | Platform |
|---|---|
| `AIUsage-1.1.0-win-x64.zip` | Windows on Intel / AMD |
| `AIUsage-1.1.0-win-arm64.zip` | Windows on ARM |

Extract **the whole ZIP** into a resident folder on a local disk, then run **AIUsage.exe**. .NET is included; no installer, administrator permissions, Python or Node installation is required. The app does not register services or start with Windows automatically.

**Exit the older version from its tray menu first.** Closing its window only hides it; opening the new executable while the old one is running can bring the old instance forward. Use a separate folder for 1.1.0. Settings are preserved. The obsolete `OnlineQuota` setting is ignored and removed from AIUsage's settings during migration, even if it was previously true. A failed migration uses safe local-only defaults; it cannot restore the removed client. **Never delete your `.codex` folder to upgrade.** Retire older executables to avoid accidentally running the former online-capable app.

**Unsigned binaries:** Windows may warn about an unknown publisher. Do not disable antivirus or SmartScreen. Check the source repository and the ZIP's SHA-256 against `SHA256SUMS.txt`, or build from source. A hash verifies integrity, not publisher identity.

```powershell
Get-FileHash .\AIUsage-1.1.0-win-x64.zip -Algorithm SHA256
```

Target: 64-bit Windows 10/11 on a version supported by .NET 10. CI executes the x64 Windows app; ARM64 is cross-compiled and its application metadata checked, not executed on ARM hardware.

## What stays local

The app opens selected JSONL rollouts with **read-only access**, interprets accounting/session metadata and discards conversation messages. It never opens `auth.json`, browser cookies or credential stores. It does not modify Codex's files, configuration, installation or credentials, call any AI model, spend resets or alter a subscription. It does not contact OpenAI or any other service, check for updates, send analytics or upload crash reports.

Settings and accounting cache are stored under `%LOCALAPPDATA%\AIUsage`. They still contain private activity metadata; local does not mean anonymous. A user-triggered CSV export creates a new file in `%LOCALAPPDATA%\AIUsage\exports`, displays its path and does not open or upload it. Custom prices are edited in a built-in text editor, not through a subprocess. There are no browser/file-shell buttons or shell-based file dialogs in this version.

Only **resident local-disk paths** are supported. UNC paths (including WSL shares), mapped network drives, device paths, symlinks/junctions and remote-storage placeholders are rejected or skipped before their content is opened. Old WSL/network settings must be changed. For WSL history, use a separately created local copy of the needed rollouts; do not copy credentials. AIUsage does not perform that transfer for you.

This is an application boundary, not a Windows network sandbox. Windows, security software, cloud-sync agents, storage drivers or software chosen separately by the user may access the network independently. Keep the app, logs, settings and exports outside cloud-sync/network-backed locations for an offline workflow. Attribute checks cannot guarantee against malicious filesystem races or every third-party filesystem. Build, dependency restore and GitHub release tooling use the internet; that tooling is not executed by the installed app.

## Features and limits

Today, yesterday, last 7/30 days, model breakdowns, input/cache/output details, estimated costs, a seven-day activity chart, local CSV, tray integration, dark/light themes and periodic disk refresh are retained.

**Limits are log snapshots, not live account queries.** They change only when Codex writes new values to the selected logs. The observation time is shown. An expired reset is labelled as expired, not silently assumed to have refreshed. Missing limits remain missing; the app cannot report a current balance or account-wide consumption by contacting a provider.

Totals cover **accessible logs in the selected folder**, not all ChatGPT/cloud usage or other devices. Token histories from different accounts in that folder are aggregated; recognized local quota identities are pseudonymously separated. Ambiguous subagent replay and unreadable accounting may be excluded with a warning rather than guessed. The two previous accounting audits remain documented as historical records: [first response](docs/AUDIT-REMEDIATION.md), [follow-up](docs/AUDIT-FOLLOWUP.md).

## Select a local folder

By default AIUsage uses `CODEX_HOME`, otherwise `%USERPROFILE%\.codex`. Set another local folder in **Settings → Codex folder**. It reads `sessions/**/*.jsonl` and `archived_sessions/**/*.jsonl`, or a directly selected local rollout folder.

**Settings → Rebuild reading cache** removes only AIUsage's cached metadata and rereads accessible logs. Use this after an unusual historical edit that preserves a source file's size/time/prefix. Original rollouts are never deleted or rewritten.

## Language

Choose **Settings → Language / Idioma → English / Español / System default → Save and refresh**. In Spanish, use **Ajustes → Idioma / Language → Guardar y actualizar**. The choice persists without restarting. Spanish-language Windows defaults to Spanish; other system languages use English. Presentation uses en-US/es-ES, but money remains **USD**, not converted currency. Day boundaries use the Windows time zone.

Model names, user notes and stored identifiers are unchanged. CSV headers/numbers remain invariant; pricing explanations follow the chosen language. Repository documentation and release notes are in English. System-owned notifications may use Windows' language.

## Estimated prices, not a bill

Dollar amounts are theoretical **API-equivalent estimates**, not subscription charges, credits spent, an OpenAI invoice or a reconstruction of historical prices. Cached input is a subset of input; reasoning is already part of output. Service tier comes from the recorded event, not current Codex settings.

Prices are bundled in [pricing.json](src/AIUsage.Core/pricing.json) with source references and qualifications. These URLs are provenance text, not requests. No live pricing updates occur. Unknown models keep their tokens and display **No price**; partial totals show `≥`. Some aliases, promotions and long-context/tier rules require explicit qualifications. Rates may become stale; review them manually. There are no regional, tool-charge or private-discount guarantees.

Use **Settings → Edit custom prices** to edit local USD-per-million values. Saving validates required `Input`, `Cached` and `Output`, rejects malformed/duplicate/unknown fields and atomically updates only your `price-overrides.json`. Example:

```json
{"my-model":{"Input":2,"Cached":0.1,"Output":10,"LongThreshold":0,"FastMultiplier":2,"Source":"Manually checked source/date"}}
```

A positive `LongThreshold` applies the documented inherited 2x input/cache and 1.5x output estimate for that entry; it is not independently verified for an arbitrary new model. Language changes never change the arithmetic.

## Build and verification

Use a Windows host with the .NET 10 SDK. Read the scripts before running them; build-time restore and license downloads require internet access.

```powershell
.\scripts\build.ps1 -Runtime win-x64
.\artifacts\publish\win-x64\AIUsage.exe --demo
```

Tests use executable runners (`dotnet run --project tests/NAME -c Release`), not `dotnet test`. The latter deliberately errors with AIU0001 to avoid silent zero-test success. See [verification](docs/BUILD-VERIFICATION.md) for the five suites, compiled/source API guards and published-x64 test with locked synthetic credentials. Test results and `BUILD-INFO.json` accompany releases. Synthetic tests are not a real-account audit, packet capture, legal opinion or certification.

`--demo` and bilingual `--smoke-test DIRECTORY` use synthetic data. The CI-only `--local-fixture` option, accepted within smoke mode, additionally exercises the normal local workflow in its explicitly supplied synthetic data directory. No real Codex account is needed for testing.

There is no updater, installer, publisher signature, additional AI provider or automatic WSL discovery. Mixed-DPI, multiple-monitor, RDP/Explorer recovery, full accessibility and real ARM64 runtime coverage remain manual-validation items.
