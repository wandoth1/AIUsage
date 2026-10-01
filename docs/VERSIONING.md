# Independent versioning and upstream policy

AIUsage maintains its **own versions, releases and roadmap**. The independent Windows release line starts at **AIUsage 1.0.0**, following our own 0.1.x development previews. No version number is copied from OpenUsage.

`VERSION` and `Directory.Build.props` define the application version. `RELEASE_TAG` defines the GitHub tag. The executable manifest, About text and ZIP names must agree with that application version. CI validates this before release. Upstream tags belong in provenance documentation and source attribution, not AIUsage's display version.

## Release conventions

Use `MAJOR.MINOR.PATCH`: patch releases correct defects; minor product releases introduce significant capabilities or explicitly documented retirements with migration steps; major releases identify deliberate breaking changes or a new compatibility baseline. Record changes and migration steps in English release notes. Version numbers do not imply support for every upstream provider or hardware configuration.

Version 1.0.0 is a normal Windows release, not an upstream prerelease. Existing AIUsage preview tags remain available for historical comparison and are not renamed or overwritten. Released assets are not silently replaced. Future development previews require an explicitly labelled prerelease process; the current verified workflow publishes normal releases from `main` only.

## Relationship with OpenUsage

OpenUsage supplied part of the initial design and code under MIT. The initial source reference is v0.7.12, documented in [UPSTREAM.md](UPSTREAM.md). This records provenance; it is not an automatically updated dependency.

We may port selected improvements from a newer OpenUsage version, postpone them, or develop independently. There is no commitment to mirror all upstream releases or achieve feature parity. Review licensing and source changes, add regression tests, then record the selected upstream tag/commit and affected components. Publish under the next appropriate **AIUsage** version whether or not upstream has released anything.

Original copyright and license notices remain required for adapted code. Independent versioning does not erase attribution or imply affiliation with OpenUsage or OpenAI.

Version 1.1.0 intentionally retires the optional online integration for a local-only design. Existing supported local preferences migrate; local data formats and accounting are preserved. It is not an upstream version and not a provider certification.
