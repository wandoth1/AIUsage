# AIUsage for Windows

**Your Codex usage, in the Windows system tray.** Track local tokens, estimated API-equivalent costs by model, and account limits when available. Native C# / WPF / .NET 10, with **English and Spanish** interfaces, dark and light themes, and no telemetry.

AIUsage is an **independent Windows port of [OpenUsage](https://github.com/robinebers/openusage)**, distributed under MIT. **AIUsage 1.0.0 is our own version**, not an OpenUsage version or a claim of complete feature parity. We maintain our own roadmap and may selectively port future upstream changes after review and testing. The initial source reference is recorded in [UPSTREAM.md](docs/UPSTREAM.md); see our [versioning policy](docs/VERSIONING.md).

[Download for Windows](https://github.com/wandoth1/AIUsage/releases/latest) · [Builds and tests](https://github.com/wandoth1/AIUsage/actions/workflows/windows.yml) · [Privacy](docs/PRIVACY.md) · [Security](SECURITY.md)

## Download and run

| Package | Platform |
|---|---|
| `AIUsage-1.0.0-win-x64.zip` | Windows on Intel / AMD |
| `AIUsage-1.0.0-win-arm64.zip` | Windows on ARM |

Download from [Releases](https://github.com/wandoth1/AIUsage/releases/latest), extract **the entire ZIP** into a folder you can write to, and open **`AIUsage.exe`**. The .NET runtime is included. No administrator permissions, installer, Python or Node installation are required.

The window opens near the clock. Its icon stays in the system tray, possibly in the hidden-icons area. `×` and `Esc` hide the window; **Exit** closes the application. AIUsage does not add itself to Windows startup or install services.

To upgrade from 0.1.x, choose **Exit** in the old app, extract the new ZIP into another folder and open the new executable. Settings are preserved. Older reading caches are rebuilt automatically, including sessions incorrectly excluded by the large-message bug in 0.1.1. The first scan may take longer. **Do not delete your `.codex` folder.**

**Unsigned binaries:** Windows may warn about an unknown publisher. Do not disable antivirus, SmartScreen or other system-wide protections. Check the repository of origin and compare the ZIP's SHA-256 with `SHA256SUMS.txt`; alternatively, build from source. A hash verifies integrity, not publisher identity.

```powershell
Get-FileHash .\AIUsage-1.0.0-win-x64.zip -Algorithm SHA256
```

Target: 64-bit Windows 10/11 on a version supported by .NET 10. There is no 32-bit package. CI runs the published x64 executable on Windows; ARM64 is cross-compiled and still needs hardware validation. This does not establish compatibility with every Windows version, DPI configuration or real Codex account.

## Language

Open **Settings → Language / Idioma**, select **English**, **Español**, or **System default**, and choose **Save and refresh**. In Spanish, use **Ajustes → Idioma / Language → Guardar y actualizar**. The choice is saved; no restart is needed.

The initial system choice uses Spanish for Spanish-language Windows and English for other system languages. English uses `en-US` formatting and Spanish uses `es-ES` formatting inside AIUsage; amounts remain **USD**. The Windows time zone determines daily boundaries. Switching language does not change calculations, credentials or session files.

Model/provider names, user-supplied notes and external data remain unchanged. CSV column names, delimiters and numbers stay language-independent; explanatory pricing notes use the selected language. Repository documentation and release notes are maintained in English. System-owned dialogs may follow Windows' own language.

## Features

- **Today, yesterday, last 7 and 30 days:** estimated costs, measured tokens and usage records, plus model breakdowns with input/cache/output tooltips.
- **Session, weekly and Spark limits**, when reported, with source and observation times. Missing or expired values are not invented.
- **Tray integration, dark/light themes, pinning and periodic refresh**, a seven-day activity chart and aggregated CSV export.
- **Incremental local reading**, including open files, archived rollouts, repeated counters and inherited subagent history. Unknown models retain their tokens and show **No price**; incomplete costs show `≥` and a warning.

Version 1.0 includes the first audit corrections and the follow-up fix for oversized conversation events. The [audit response](docs/AUDIT-REMEDIATION.md) and [follow-up report](docs/AUDIT-FOLLOWUP.md) distinguish fixes, conservative mitigations and remaining limitations.

## Connect your local logs

AIUsage uses `CODEX_HOME` when set; otherwise it uses `%USERPROFILE%\.codex`. Select another folder in **Settings → Codex folder**. It reads `sessions/**/*.jsonl` and `archived_sessions/**/*.jsonl`; selecting a rollout folder directly is also supported.

For WSL, select a Windows-accessible path such as `\\wsl.localhost\Ubuntu\home\YOUR_USER\.codex`. The distribution must be available. Automatic WSL discovery and combining multiple home folders are not implemented. Nested links/junctions are skipped to avoid loops and unrelated folders, with a warning for incomplete reading.

Totals represent **accessible logs in the selected folder**, not all ChatGPT use, cloud activity or other devices. Token history from different accounts in that folder is aggregated. Known local quota identities are separated using pseudonymous identifiers; missing identities cannot be reliably separated. Online quotas belong to the credentials observed at query time, not necessarily every historical log in the folder.

### Optional, read-only online limits

Local mode works offline and does not read `auth.json`. To query recent limits, enable **Settings → Query my account limits online** and confirm the consent dialog.

The existing `access_token` and account identifier, when present, are sent **only to `https://chatgpt.com/backend-api/wham/usage`**. Conversations are never sent. Requests have a timeout, run at most once a minute, respect `Retry-After`, back off after transient failures and do not follow redirects. This is an internal endpoint also used by OpenUsage, not a stable public API.

AIUsage **does not refresh or overwrite credentials**, call models, start conversations or spend limit resets. Renew an expired session in Codex. An API key does not substitute for a ChatGPT login. Windows Credential Manager is not read: if Codex stores its login only there, local accounting still works, but online queries are unavailable. Never attach credentials to an issue.

A response is discarded if credentials change during the query, including a normal token renewal. This conservative check can delay updated limits until the next query. A successful online snapshot replaces the local quota view; absent online windows, including Spark, are not filled from potentially unrelated local accounts. Observation times remain visible.

## What the dollar amounts mean

**They are not subscription charges, credits spent or an OpenAI invoice.** They estimate the API-equivalent cost of tokens recorded by Codex:

```text
(non-cached, non-cache-write input × input price
 + cached input × cache-read price
 + cache-write input × applicable price
 + output × output price) / 1,000,000
```

Cached input is already part of input. Reasoning tokens are included in output and are not charged twice. Service tiers come from each log, not the current Codex configuration. Supported catalog entries apply long-context rules above their input threshold and priority multipliers when recorded; unverified inherited rules are explicitly qualified.

The bundled catalog was reviewed on **2026-10-01** and includes `gpt-6.1-sol`, `gpt-6-astra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-5.6-sol`, `gpt-5.6-terra` and `gpt-5.6-luna`. Sources accompany each entry in [pricing.json](src/AIUsage.Core/pricing.json). Version 1.0 does not change numerical prices from 0.1.1; it additionally qualifies Sol 5.6's inherited long-context cache surcharge as unverified, consistently with Terra and Luna.

History is recalculated using that catalog, **not historical prices**, earlier promotions, individual discounts, tool charges or regional surcharges. The Sol 5.6 promotion observed on 2026-10-01 uses 4 / 0.4 / 20 USD per million input / cached input / output tokens, announced until at least 2026-11-21. From 2026-11-22 the app requests a review rather than inventing an expiry or replacement price. `gpt-reserve` and post-2026-07-09 `codex-auto-review` mappings to Luna 5.6 are inherited compatibility assumptions, not promises about future models. Qualified rules and custom prices are explained per model and in CSV.

### Custom prices

Choose **Settings → Edit custom prices**, edit `%LOCALAPPDATA%\AIUsage\price-overrides.json`, then refresh:

```json
{
  "my-model": {
    "Input": 2.0,
    "Cached": 0.1,
    "Output": 10.0,
    "CacheWriteMultiplier": 1.25,
    "LongThreshold": 272000,
    "FastMultiplier": 2,
    "Source": "Verified source and date"
  }
}
```

`Input`, `Cached` and `Output` are required, in USD per million. Invalid values and duplicate, misspelled or unknown fields are rejected. Model keys are case-insensitive and trimmed. `LongThreshold: 0` disables the surcharge; a positive threshold applies 2× input/cache and 1.5× output above it. Custom prices are identified and do not change the model Codex runs.

### Incomplete logs and cache recovery

A complete JSON record without a final newline is included after a second stable observation and rechecked when the file grows. Recognized large conversation records, including `event_msg.user_message` and `event_msg.item_completed`, are skipped without erasing session accounting. Classification reads only the bounded JSON header; pasted text and images are not cached.

Oversized accounting/session metadata or unknown event types can still conservatively exclude a file with a warning rather than bill ambiguous inherited history. A subagent without a reliable live-task start remains excluded when replay cannot be separated safely. These conditions may leave totals incomplete.

Incremental checks target append-only logs: an older rewrite preserving size, modification time and prefix may be missed. After editing historical logs, use **Settings → Rebuild reading cache**. Only AIUsage's cache is removed. Schema 4 automatically rebuilds caches produced before the follow-up fix.

### CSV

CSV uses UTF-8, a comma delimiter and invariant decimal points. Import explicitly with these settings; double-click import is not guaranteed across regional configurations. `unpriced_events`, `qualified_events` and `pricing_notes` preserve qualifications. No conversations, credentials or original file paths are exported. Potential spreadsheet formulas in text fields are escaped.

## Build and test

Use Windows and the .NET 10 SDK for the GUI. The core and console tests also run without WPF on other systems.

```powershell
dotnet run --project tests/AIUsage.Tests -c Release
dotnet run --project tests/AIUsage.AuditTests -c Release
dotnet run --project tests/AIUsage.LocalizationTests -c Release
dotnet run --project tests/AIUsage.FollowupTests -c Release
dotnet build src/AIUsage.Windows -c Release -p:PublishSingleFile=false
.\scripts\build.ps1 -Runtime win-x64
.\artifacts\publish\win-x64\AIUsage.exe --demo --language en
```

These are executable test runners, **not VSTest projects**. `dotnet test` deliberately returns `AIU0001` to prevent a misleading success with no tests. See [build verification](docs/BUILD-VERIFICATION.md) and [localization maintenance](docs/LOCALIZATION.md).

`--demo` uses synthetic data without reading real settings, logs or credentials. `--tray` starts hidden. `--language en|es|auto` overrides the process language; saving Settings persists the choice. `--smoke-test DIRECTORY` tests the language selector and tray menu, both themes, and scroll/focus preservation, then exits. Its `AIUsage-en-*.png` and `AIUsage-es-*.png` screenshots show the real WPF app with **synthetic data**, not a real-account validation.

## Scope and limitations

**Codex is the only supported provider in 1.0.** Claude, Cursor, Copilot, pi, OpenCode, multiple profiles, automatic updates, code signing and an installer are not included. Internal quota endpoints and rollout formats can change. Mixed-DPI/multiple-monitor, Explorer recovery, concurrent RDP and comprehensive accessibility testing remain incomplete. ARM64 has not been run on ARM hardware, and real-account validation is still required.

Version 1.0 marks an independent AIUsage release line, not the removal of those limits. Read [SECURITY.md](SECURITY.md) before reporting a problem, and retain [third-party notices](THIRD-PARTY-NOTICES.md) when redistributing.
