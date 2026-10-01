# Independent versioning

AIUsage is an independent Windows port of OpenUsage under MIT. The original reference was OpenUsage v0.7.12; that is provenance, not our release number or a continuing dependency. See UPSTREAM.md for attribution and adaptation history.

Our product line started with 0.1.x previews and AIUsage 1.0.0. AIUsage 1.1.0 removed online integration. AIUsage 1.1.1 is local-security maintenance. AIUsage 1.2.0 adds automatic detection of the local Codex data folder. AIUsage 1.3.0 adds optional, local-only Claude Code usage. These numbers are independent of upstream releases and do not assert complete feature/provider parity.

Patch releases fix existing behavior; minor releases introduce reviewed functionality or deliberate product changes; major releases indicate substantial compatibility changes. Where a protective restriction changes accepted input or packaging, release notes and upgrade instructions must call it out even in a maintenance release. The 1.1.1 folder package must be extracted in full, and direct folders now require rollout- filenames.

Future upstream improvements may be selectively ported, independently reimplemented, deferred or omitted. There is no automatic synchronization obligation. Any imported code keeps its license/copyright notices and must pass our local-only, accounting and Windows tests.

VERSION, RELEASE_TAG, assembly metadata, app manifest and package filenames must agree. Existing tags and release files are retained for historical comparison; the pipeline refuses to overwrite them. Earlier online-capable versions are not described as local-only merely because a newer release is offline.

A normal release number is not a legal/security certification, publisher signature, provider endorsement or promise of zero defects. Known limitations and exact validation scope remain documented. Runtime remains local-only unless an explicit future product decision is separately authorized and clearly communicated; no upstream feature should silently restore account access.
