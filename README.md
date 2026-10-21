# kOS Improved (kOSIM)

**kOSIM is an independent fork of [kOS](https://github.com/KSP-KOS/KOS),
the scriptable autopilot for Kerbal Space Program.** It focuses on a safer,
more comfortable terminal and scripting workflow. It is not an official kOS release.

Write scripts in an external editor (optionally with
[kos-ls](https://github.com/Jvaxx/kos-ls), a standalone kerboscript language
server), then run and debug them in the game terminal or over telnet.

This fork was developed with Claude Opus 5.5 (AI-assisted coding), but was
tested heavily in game by its maintainer.

## What this fork improves

- Terminal paste confirmation, selection/copy, saved command history and reverse search.
- Separate output and scrollback views, bounded scrollback, wheel scrolling and a scrollbar.
- Scaled, simpler terminal and settings UI; saved window geometry, three shared presets,
  and per-window background opacity.
- Safer editor save/switch transactions, atomic archive saves and byte-preserving raw I/O.
- More predictable keyboard focus and mouse blocking; incremental telnet parsing,
  bounded queues and cooperative shutdown.
- Local regression tests using real terminal models, plus reduced idle and rendering allocations.

See [changes from upstream and compatibility](docs/compatibility.md),
[release notes](CHANGELOG.md), and [known issues / remaining work](docs/KOSIm-roadmap.md).

## Install

Download a packaged build from **[this fork's releases](https://github.com/Jvaxx/kOSIm/releases)**
and extract its `GameData/kOS` into your KSP installation's `GameData`.
Install ModuleManager separately if needed.
Back up your saves, scripts and existing mod configuration first.

**This replaces upstream kOS; do not install both.** Assembly names, part names and
`GameData/kOS` are intentionally retained for compatibility. Preserve
`GameData/kOS/Plugins/PluginData/` when updating. Upstream's CKAN, SpaceDock and
CurseForge listings distribute upstream kOS, not this fork. There is no dedicated
CKAN package or validated CKAN upgrade path; see [installation cautions](docs/compatibility.md#installation-and-versioning).

Runtime validation is limited to **Windows KSP 1.12.5.3190 under GE-Proton11-7 on Linux**.
Other platforms and KSP versions are not validated. This is a development fork;
review the known issues before using it in a valued save.

## Documentation and development

- [User manual source and local HTML build](doc/README.md) — inherited kerboscript reference, updated for the fork.
- [Upstream online manual](https://ksp-kos.github.io/KOS_DOC/) — useful language reference, **not fork-specific UI or release documentation**.
- [Build and test locally](docs/testing-and-build.md) · [Contribute](CONTRIBUTING.md)
- [Developer documentation index](docs/README.md) · [Release checklist](RELEASECHECKLIST.md)
- [Report a fork issue](https://github.com/Jvaxx/kOSIm/issues) — include the version and reproduction steps; do not send fork-only bugs to upstream.

## Version and provenance

The source baseline is **upstream kOS 1.6.0.1**: fork point
`8f281a459b6ea0ba917c91bbbc117e68bb948199`, whose tracked tree matches that
release except for `CHANGELOG.md`. Exact provenance and the current development
version are recorded in [`kOS.props`](kOS.props).

The first three version fields follow upstream; the fourth is a continuous
**kOSIM build counter**, not upstream's fourth field. The corrected development
line starts at **1.6.0.103 (unreleased)**. Historical `1.5.1.100`–`1.5.1.102`
releases already contained upstream 1.6.0.1; their incorrect prefixes are retained
as historical identifiers, not rewritten.

## License and credits

[GNU GPL v3](LICENSE.md). kOS was originally created by **Nivekk** and developed
by the **[kOS team and contributors](https://github.com/KSP-KOS/KOS/graphs/contributors)**.
kOSIM builds on that work; original notices, asset credits and upstream release
history are retained. See [third-party notices](THIRD_PARTY_NOTICES.md) and the
[manual's license](doc/LICENSE.md).
