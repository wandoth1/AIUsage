# Security policy

AIUsage 1.1.0 is a local-only desktop utility, not a credential manager or an official OpenAI product. It parses only resident local logs and never contacts an AI service, reads authentication, launches an external program, modifies Codex or disables OS controls. Older releases had an optional authenticated integration; exit those versions and use the new executable for this boundary.

Report a vulnerability through a private maintainer contact where available. Otherwise open an issue stating only that a private security contact is needed. Do not publish exploit-sensitive details, credentials, auth.json, private conversations, identifying paths or unreviewed usage caches. Prefer minimal synthetic reproductions and include the application version/commit.

The files in AIUsage's own local cache/export folders can reveal activity even without prompts. Review before sharing. Local metadata is not anonymous or encrypted by the application. Process only logs you are authorized to access.

Network paths, drive mappings, links and remote-recall attributes are rejected or skipped, but the app is not an operating-system sandbox and does not defend against a compromised machine or every same-user filesystem race. Keep logs and app data outside network/cloud-sync locations for offline use. OS/security/storage services may communicate independently.

Only user-triggered local exports and custom-price edits are written, besides the app's own settings/cache and explicit synthetic-test output. Test/build tooling can use public networks for dependencies and artifact distribution; it is not run by the installed app. Released binaries are unsigned: keep antivirus and SmartScreen enabled and check source and integrity. See [local-only design](docs/LOCAL-ONLY.md) and [privacy](docs/PRIVACY.md). No legal certification or ban-proof promise is made.
