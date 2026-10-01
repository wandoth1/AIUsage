# AIUsage for Windows

**Your Codex and Claude Code usage, entirely local.** AIUsage 1.4.0 reads existing usage logs on your local disk and shows tokens, estimated API-equivalent costs by model, and historical limits recorded by Codex. Claude Code usage is optional (Settings → Claude Code) and comes only from the transcripts Claude Code already keeps on this PC. Native C# / WPF / .NET 10, with **English and Spanish**, dark/light themes and a system tray icon.

**No account access. No credential reader. No network client.** The optional online integration from 1.0.0 and earlier was removed, not merely disabled. There is no switch, token, app-server integration or undocumented endpoint that can re-enable it. The app does not launch Codex, another AI, a browser or an external editor.

AIUsage is an **independent Windows port of [OpenUsage](https://github.com/robinebers/openusage)** under MIT, not an OpenAI, Anthropic or official OpenUsage product. Our versions and roadmap are independent; selected upstream improvements may be ported after review. See [provenance](docs/UPSTREAM.md), [versioning](docs/VERSIONING.md), [local-only design](docs/LOCAL-ONLY.md) and [privacy](docs/PRIVACY.md). No affiliation, provider approval, legal certification or account-enforcement guarantee is implied.

[Download for Windows](https://github.com/wandoth1/AIUsage/releases/latest) · [Builds and tests](https://github.com/wandoth1/AIUsage/actions/workflows/windows.yml) · [Security](SECURITY.md)

## What's new in 1.4.0

- **Claude plan limits through Claude Code's documented status line (optional).** Make AIUsage Claude Code's status line and the 5-hour and weekly limits of your Pro/Max plan (or a gateway spend limit) appear in AIUsage, with reset times. Claude Code itself passes them to AIUsage on this PC through its documented status line feature: no credentials, no sign-in, no requests to Anthropic. See [Claude plan limits](#claude-plan-limits-optional).
- **Clearer limits.** Codex limits and Claude plan limits are now separate cards.

## New in 1.3.0

- **Claude Code usage (optional, off by default).** Turn it on in **Settings → Claude Code → Include Claude Code usage**. AIUsage then reads the token counters from the transcripts Claude Code already keeps on this PC, including subagents.
- **All / Codex / Claude Code selector** on the dashboard. The tray total covers both sources.
- **Official Claude API prices** for every current Claude model, including 5-minute and 1-hour cache writes, cache reads, fast mode and US-only inference.
- **No Claude credentials, ever.** See [Claude Code](#claude-code-optional).

Release notes: [docs/RELEASE-NOTES.md](docs/RELEASE-NOTES.md). Previous: 1.2.0 added automatic Codex folder detection.

## Download and upgrade

| Package | Platform |
|---|---|
| `AIUsage-1.4.0-win-x64.zip` | Windows on Intel / AMD |
| `AIUsage-1.4.0-win-arm64.zip` | Windows on ARM |

**Exit the older version from its tray menu first.** Extract **every file** into a new resident folder on a local disk, then run **AIUsage.exe**. Keep the DLLs, native dependencies and `es` subfolder beside the executable: 1.4.0 uses an inspectable self-contained folder, not a single-file bundle. .NET is included; no installer, administrator permissions, Python or Node installation is required.

Closing a window only hides it. Starting 1.4.0 while a legacy instance holds the former mutex shows a warning and exits with code 2; it does not bring the older app forward or terminate it. Supported settings are preserved. The obsolete OnlineQuota setting cannot restore deleted functionality. If the migration cannot be written, the selected folder remains in use with a warning. **Unreadable/corrupt settings pause scanning until you explicitly enter and save a local folder.** Never delete `.codex` to upgrade. Retire old executables and update shortcuts to avoid accidentally running an online-capable historical version.

**Unsigned binaries:** Windows may warn about an unknown publisher. Do not disable antivirus or SmartScreen. Check the repository and the ZIP's SHA-256 against `SHA256SUMS.txt`, or build from source. `PAYLOAD-SHA256.json` additionally lists files inside each extracted package. These hashes verify integrity, not publisher identity.

```powershell
Get-FileHash .\AIUsage-1.4.0-win-x64.zip -Algorithm SHA256
```

Target: 64-bit Windows 10/11 on a version supported by .NET 10. CI executes the published x64 Windows app. ARM64 is cross-compiled and its application metadata inspected, not executed on ARM hardware. No services or startup registration are installed.

## Local operation

The app opens selected JSONL rollouts (and, when enabled, Claude Code transcripts) with **read-only access**, interprets accounting/session metadata and discards conversation messages. It does not open authentication, browser cookies or credential stores. It does not modify Codex's files, installation, configuration or credentials, call a model, spend resets or alter subscriptions. No provider connection, analytics, crash uploads, updater or live price download is implemented.

AIUsage's settings, prices and accounting cache live under `%LOCALAPPDATA%\AIUsage`. A user-triggered CSV export creates a unique file in its `exports` subfolder, displays the path and does not open or upload it. Custom prices use a built-in editor. There are no browser/file-shell buttons or shell-based file dialogs. Metadata and user notes remain private even without conversations: local does not mean anonymous.

Only **resident local-disk paths** are supported. UNC paths (including WSL shares), mapped network drives, device paths, symlinks/junctions and remote-recall placeholders are rejected or skipped. Old WSL/network settings must be changed. For WSL history, use a separately created local copy of the needed rollouts; do not copy credentials. AIUsage does not perform that transfer.

This is an application boundary, not a network sandbox for Windows. Operating-system services, security tools, storage drivers or cloud-sync agents may communicate independently. Keep app data outside synchronized/network-backed storage. Attribute checks cannot defeat malicious filesystem races or every third-party filesystem. Build/restore/release tooling uses the internet and is not executed by the installed app.

## Codex folder

On first use with no settings file, AIUsage uses `CODEX_HOME`, otherwise `%USERPROFILE%\.codex`. **Leave Settings → Codex folder empty for automatic detection**; enter a folder only when your logs live elsewhere. Settings shows the detected folder and how many recent sessions it holds, with **Use detected folder** to apply it. If a selected folder contains no Codex logs, saving it offers the detected folder instead, and the dashboard shows the same one-click suggestion; AIUsage never switches folders silently. A folder where the Codex *application* is installed (for example under `C:\Program Files\WindowsApps`) never contains logs: it is rejected when saved, and an existing setting pointing there is cleared on startup so automatic detection applies, with a notice. Detection only lists rollout file names and modification dates; it never opens log contents, and it does not run while scanning is paused after a settings error unless you request it. The footer identifies the current reading folder. It reads `sessions/**/*.jsonl` and `archived_sessions/**/*.jsonl`; `history.jsonl` is always excluded. For a directly selected folder without those session directories, only `rollout-*.jsonl` candidates are considered. Arbitrary JSONL/history files are not a fallback input.

## Claude Code (optional)

Claude Code support is **off by default**. Enable **Settings → Claude Code → Include Claude Code usage** and press **Save and refresh** (Spanish: **Ajustes → Claude Code → Incluir el uso de Claude Code → Guardar y actualizar**). A row of buttons then switches the dashboard between **All**, **Codex** and **Claude Code**, and the footer shows the Claude folder being read.

**What is read.** Only `*.jsonl` transcripts under `projects\` of `CLAUDE_CONFIG_DIR`, otherwise `%USERPROFILE%\.claude` (subagent transcripts included). Lines without a usage block, such as prompts and tool output, are skipped before parsing. From assistant records AIUsage keeps only the timestamp, model, token counters, `speed` and `inference_geo`; message, request and session ids are kept only as SHA-256 digests to avoid double counting. The cache lives in `%LOCALAPPDATA%\AIUsage\cache-claude`.

**What is never done.** AIUsage does not open `.credentials.json`, `history.jsonl`, settings or any other file outside `projects\`. It does not sign in to Claude, use Claude credentials or OAuth, call Anthropic services, or launch or automate Claude Code. Claude Code's own files are never modified.

**Counting.** Requests that Claude Code logs more than once (resumed sessions, subagent sidechains) are counted once, following the same rules as ccusage: a sidechain copy is matched only within its own session, so reused ids never merge different sessions. Transcripts are read incrementally; before continuing a file AIUsage checks that sampled ranges and the last 64 KiB already counted are unchanged, and rereads the file otherwise. An edit elsewhere inside an old transcript can go unnoticed: use **Settings → Rebuild reading cache** after editing transcripts by hand. Advisor iterations are priced under their own model. Records generated locally by Claude Code (`<synthetic>`) are not API requests and are ignored.

**Prices.** Estimates use Anthropic's published API list prices (platform.claude.com, checked 2026-10-01): uncached input, 5-minute and 1-hour cache writes, cache reads and output, fast mode on Opus 5.5 / 5 / 4.8, and the 1.1x US-only inference multiplier. **With a Pro or Max subscription this is not what you pay**; it shows what the same usage would cost on the API. Web-search charges and private discounts are not included.

**Retention.** Claude Code deletes old transcripts after its retention period (30 days by default), so older usage also disappears from AIUsage.

### Claude plan limits (optional)

AIUsage never reads your Claude credentials and never asks Anthropic for your limits; third-party apps must not use Claude account credentials. Instead, Claude Code has a documented [status line](https://code.claude.com/docs/en/statusline) feature: it runs a command you choose and passes it session data on stdin, which for Pro and Max plans includes `rate_limits` (5-hour and weekly percentages and reset times; behind a Claude apps gateway with spend limits, a `spend_limit` window, which needs Claude Code 2.1.251 or later). Make AIUsage that command and the limits appear in the **Claude plan limits** card.

1. Open **Settings → Claude Code → Claude plan limits (optional)** and copy the entry shown there. It contains the path of the AIUsage you are running, with forward slashes, for example (the path must contain only letters, digits, `.`, `_` and `-`, because Claude Code runs it through Git Bash or PowerShell; otherwise Settings asks you to move AIUsage, for example to `C:\AIUsage`):

   ```json
   {
     "statusLine": {
       "type": "command",
       "command": "C:/Users/you/Documents/AIUsage-1.4.0-win-x64/AIUsage.exe --claude-statusline"
     }
   }
   ```
2. Add it to Claude Code's user settings file, `%USERPROFILE%\.claude\settings.json`, or `settings.json` inside `CLAUDE_CONFIG_DIR` if you set that variable (Settings shows the right file). Merge it with any settings already there and restart Claude Code.

Claude Code then shows the model and your limits at the bottom (for example `Opus 5.5 · 5h 24% · 7d 41%`), and AIUsage shows the same values with reset times after its next refresh. `AIUsage.exe --claude-statusline` opens no window, keeps only the limit percentages and reset times in `%LOCALAPPDATA%\AIUsage\claude-limits.json` and discards everything else Claude Code sends. It replaces any status line you already have. AIUsage never edits Claude Code's settings itself. Limits appear only for Pro and Max plans (or a gateway spend limit), after Claude Code's first response in a session, and only while you use Claude Code; the card shows when they were last received. If you move AIUsage to a new folder, update the path. You can always check limits inside Claude Code with `/usage`.

## Dashboard and Codex limits

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

To uninstall, exit, delete the extracted app folder and optionally `%LOCALAPPDATA%\AIUsage`. Original Codex (`.codex`) and Claude Code (`.claude`) data must not be deleted. See [privacy](docs/PRIVACY.md).

## Build and evidence

Build on Windows with the .NET 10 SDK using `scripts/build.ps1`. Compilation, dependency restore and license collection need internet access; the installed application does not run those tools. Tests are executable runners invoked with `dotnet run`, not VSTest suites. See [verification](docs/BUILD-VERIFICATION.md).

CI validates accounting, local audit, localization, follow-up, security/stress and offline boundaries, plus actual-entry-point x64 UI/timer/mutex/hook tests and bilingual rendering. It inspects the actual published own DLLs, records build provenance and payload hashes, and blocks release on failure. Exact results accompany each release. No new external network trace or proof of zero failed file-open attempts is claimed. The 1.1.0 audit's trace results remain historical evidence.

[First audit response](docs/AUDIT-REMEDIATION.md) · [Second audit](docs/AUDIT-FOLLOWUP.md) · [Local-security remediation](docs/AUDIT-SECURITY-REMEDIATION.md)

Codex and Claude Code (local transcripts only) are the implemented sources. No installer, updater, publisher signature, comprehensive multimonitor/RDP/accessibility validation or legal certification is included. Only process logs you are authorized to use.
