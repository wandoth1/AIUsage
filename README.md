# AIUsage for Windows

**Your Codex usage, entirely local.** AIUsage 1.2.0 reads existing usage logs on your local disk and shows tokens, estimated API-equivalent costs by model, and historical limits recorded by Codex. Native C# / WPF / .NET 10, with **English and Spanish**, dark/light themes and a system tray icon.

**No account access. No credential reader. No network client.** The optional online integration from 1.0.0 and earlier was removed, not merely disabled. There is no switch, token, app-server integration or undocumented endpoint that can re-enable it. The app does not launch Codex, another AI, a browser or an external editor.

AIUsage is an **independent Windows port of [OpenUsage](https://github.com/robinebers/openusage)** under MIT, not an OpenAI or official OpenUsage product. Our versions and roadmap are independent; selected upstream improvements may be ported after review. See [provenance](docs/UPSTREAM.md), [versioning](docs/VERSIONING.md), [local-only design](docs/LOCAL-ONLY.md) and [privacy](docs/PRIVACY.md). No affiliation, provider approval, legal certification or account-enforcement guarantee is implied.

[Download for Windows](https://github.com/wandoth1/AIUsage/releases/latest) · [Builds and tests](https://github.com/wandoth1/AIUsage/actions/workflows/windows.yml) · [Security](SECURITY.md)

## Download and upgrade

| Package | Platform |
|---|---|
| `AIUsage-1.2.0-win-x64.zip` | Windows on Intel / AMD |
| `AIUsage-1.2.0-win-arm64.zip` | Windows on ARM |

**Exit the older version from its tray menu first.** Extract **every file** into a new resident folder on a local disk, then run **AIUsage.exe**. Keep the DLLs, native dependencies and `es` subfolder beside the executable: 1.2.0 uses an inspectable self-contained folder, not a single-file bundle. .NET is included; no installer, administrator permissions, Python or Node installation is required.

Closing a window only hides it. Starting 1.2.0 while a legacy instance holds the former mutex shows a warning and exits with code 2; it does not bring the older app forward or terminate it. Supported settings are preserved. The obsolete OnlineQuota setting cannot restore deleted functionality. If the migration cannot be written, the selected folder remains in use with a warning. **Unreadable/corrupt settings pause scanning until you explicitly enter and save a local folder.** Never delete `.codex` to upgrade. Retire old executables and update shortcuts to avoid accidentally running an online-capable historical version.

**Unsigned binaries:** Windows may warn about an unknown publisher. Do not disable antivirus or SmartScreen. Check the repository and the ZIP's SHA-256 against `SHA256SUMS.txt`, or build from source. `PAYLOAD-SHA256.json` additionally lists files inside each extracted package. These hashes verify integrity, not publisher identity.

```powershell
Get-FileHash .\AIUsage-1.2.0-win-x64.zip -Algorithm SHA256
```

Target: 64-bit Windows 10/11 on a version supported by .NET 10. CI executes the published x64 Windows app. ARM64 is cross-compiled and its application metadata inspected, not executed on ARM hardware. No services or startup registration are installed.

## Local operation

The app opens selected JSONL rollouts with **read-only access**, interprets accounting/session metadata and discards conversation messages. It does not open authentication, browser cookies or credential stores. It does not modify Codex's files, installation, configuration or credentials, call a model, spend resets or alter subscriptions. No provider connection, analytics, crash uploads, updater or live price download is implemented.

AIUsage's settings, prices and accounting cache live under `%LOCALAPPDATA%\AIUsage`. A user-triggered CSV export creates a unique file in its `exports` subfolder, displays the path and does not open or upload it. Custom prices use a built-in editor. There are no browser/file-shell buttons or shell-based file dialogs. Metadata and user notes remain private even without conversations: local does not mean anonymous.

Only **resident local-disk paths** are supported. UNC paths (including WSL shares), mapped network drives, device paths, symlinks/junctions and remote-recall placeholders are rejected or skipped. Old WSL/network settings must be changed. For WSL history, use a separately created local copy of the needed rollouts; do not copy credentials. AIUsage does not perform that transfer.

This is an application boundary, not a network sandbox for Windows. Operating-system services, security tools, storage drivers or cloud-sync agents may communicate independently. Keep app data outside synchronized/network-backed storage. Attribute checks cannot defeat malicious filesystem races or every third-party filesystem. Build/restore/release tooling uses the internet and is not executed by the installed app.

## Select a source and interpret the results

On first use with no settings file, AIUsage uses `CODEX_HOME`, otherwise `%USERPROFILE%\.codex`. **Leave Settings → Codex folder empty for automatic detection**; enter a folder only when your logs live elsewhere. Settings shows the detected folder and how many recent sessions it holds, with **Use detected folder** to apply it. If a selected folder contains no Codex logs, saving it offers the detected folder instead, and the dashboard shows the same one-click suggestion; AIUsage never switches folders silently. A folder where the Codex *application* is installed (for example under `C:\Program Files\WindowsApps`) never contains logs: it is rejected when saved, and an existing setting pointing there is cleared on startup so automatic detection applies, with a notice. Detection only lists rollout file names and modification dates; it never opens log contents, and it does not run while scanning is paused after a settings error unless you request it. The footer identifies the current reading folder. It reads `sessions/**/*.jsonl` and `archived_sessions/**/*.jsonl`; `history.jsonl` is always excluded. For a directly selected folder without those session directories, only `rollout-*.jsonl` candidates are considered. Arbitrary JSONL/history files are not a fallback input.

Today, yesterday, last 7/30 days, model breakdowns, input/cache/output details, estimated costs, a seven-day activity chart, local CSV, tray integration, dark/light themes and periodic disk refresh are retained.

**Limits are historical log snapshots, not live account queries.** They change only when Codex records new values. Observation times are shown; expired resets are not assumed to have refreshed. Missing limits remain missing. Totals cover accessible logs in the selected folder, not all ChatGPT/cloud use or other devices. Token histories from different accounts in that folder are aggregated; recognized quota identities are pseudonymously separated. Ambiguous replay or unreadable accounting stays visibly incomplete rather than guessed.

**Settings → Rebuild reading cache** deletes only AIUsage's cache and rereads accessible originals. Schema 5 automatically rebuilds older caches. Large checkpoints use compact, streamed manifests/pages with a shared 32 MiB per-file read/write limit. Missing/corrupt pages invalidate the whole checkpoint; partial cached totals are not presented as a successful reload. Very large histories still require proportional memory and disk, and appending may rewrite a page generation. Rebuild manually after an unusual historical edit that preserves source size/time/prefix.

## Language

Choose **Settings → Language / Idioma → English / Español / System default → Save and refresh**. In Spanish: **Ajustes → Idioma / Language → Guardar y actualizar**. No restart is needed. Spanish-language Windows defaults to Spanish; other system languages use English. Presentation uses en-US/es-ES, but money remains **USD**. Day boundaries use the Windows time zone.

Model names, user notes and stored identities do not change. CSV headers and numbers remain invariant; pricing explanations follow the chosen language. Repository documentation and release notes are in English. Windows-owned notifications can follow the OS language.

## Estimated prices, not a bill

Dollar amounts are theoretical **API-equivalent estimates**, not subscription charges, credits spent, an invoice or historical-price reconstruction. Cached input is a subset of input; reasoning is already part of output. Service tier comes from recorded events, not current Codex settings.

Prices are bundled in [pricing.json](src/AIUsage.Core/pricing.json) with references and qualifications; URLs are provenance text, not requests. Unknown models retain their tokens and show **No price**; incomplete totals show `≥`. Rates/promotions may become stale and require manual review. Some aliases and tier/long-context rules remain explicitly qualified. There is no guarantee about regional/tool charges or private discounts.

Use **Settings → Edit custom prices** to edit local USD-per-million values. Saving requires `Input`, `Cached` and `Output`, rejects malformed/duplicate/unknown fields, and changes only `price-overrides.json`:

```json
{"my-model":{"Input":2,"Cached":0.1,"Output":10,"LongThreshold":0,"FastMultiplier":2,"Source":"Manually checked source/date"}}
```

A positive LongThreshold uses the inherited 2x input/cache and 1.5x output estimate; it is not independently verified for an arbitrary new model. Language changes do not alter the arithmetic.

## Runtime and uninstall

Startup hooks are disabled in the shipped runtime configuration. Folder deployment removes the native-library self-extraction mechanism. Earlier single-file versions may have left `%TEMP%\.net\AIUsage`; after exiting all AIUsage versions, that specific old extraction directory can be removed separately. Do not delete other applications' `.net` directories.

.NET's standard **local diagnostics IPC remains available** subject to OS permissions. It is documented, not misrepresented as disabled or as an Internet connection. Windows/security software may create independent caches. These controls are not protection against same-user binary/configuration replacement or a compromised OS.

To uninstall, exit, delete the extracted app folder and optionally `%LOCALAPPDATA%\AIUsage`. Original Codex data must not be deleted. See [privacy](docs/PRIVACY.md).

## Build and evidence

Build on Windows with the .NET 10 SDK using `scripts/build.ps1`. Compilation, dependency restore and license collection need internet access; the installed application does not run those tools. Tests are executable runners invoked with `dotnet run`, not VSTest suites. See [verification](docs/BUILD-VERIFICATION.md).

CI validates accounting, local audit, localization, follow-up, security/stress and offline boundaries, plus actual-entry-point x64 UI/timer/mutex/hook tests and bilingual rendering. It inspects the actual published own DLLs, records build provenance and payload hashes, and blocks release on failure. Exact results accompany each release. No new external network trace or proof of zero failed file-open attempts is claimed. The 1.1.0 audit's trace results remain historical evidence.

[First audit response](docs/AUDIT-REMEDIATION.md) · [Second audit](docs/AUDIT-FOLLOWUP.md) · [Local-security remediation](docs/AUDIT-SECURITY-REMEDIATION.md)

Codex remains the only implemented provider. No installer, updater, publisher signature, comprehensive multimonitor/RDP/accessibility validation or legal certification is included. Only process logs you are authorized to use.
