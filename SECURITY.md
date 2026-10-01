# Security

AIUsage releases are currently **unsigned**. Releases include SHA-256 hashes; verify both origin and integrity. Do not globally disable SmartScreen, antivirus or other protections. A normal 1.0 release does not imply signing or independent security certification.

Never publish `auth.json`, API keys, OAuth tokens, cookies, full session logs or screenshots with personal data. For a defect report, include the AIUsage version, Windows version/architecture, native or WSL use, selected language and a minimal **synthetic** example of the failing format.

Quota endpoints and rollout formats are not stable contracts. The application warns when data is missing, incomplete or cannot be priced. Estimated costs must not be used as billing controls or exact measures of subscription quota.

AIUsage is read-only with respect to Codex. It does not claim resets, refresh credentials or send conversations. Review [privacy](docs/PRIVACY.md) and the code before enabling online access. Settings and cached activity remain private even without conversations or raw account identifiers.

Use GitHub private vulnerability reporting if enabled for sensitive reports. Otherwise open an issue without secrets to arrange a suitable channel; do not publish a proof of concept containing real credentials.
