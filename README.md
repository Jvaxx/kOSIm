# kOS Improved (kOSIM)

A fork of [kOS](https://github.com/KSP-KOS/KOS) with terminal, workflow
and safety improvements. The source baseline is **upstream 1.6.0.1**:
`develop` commit `8f281a459b6ea0ba917c91bbbc117e68bb948199`, whose tree
matches that release except for its changelog heading.

The fork's current development version is in [`kOS.props`](kOS.props).
Its first three fields follow upstream; the fourth is a continuous
**kOSIM build number**, not upstream's fourth field. The corrected line
starts at **1.6.0.103**. Earlier published `1.5.1.100`–`1.5.1.102` names
are preserved, but their prefix was incorrect: they already included
upstream 1.6.0.1 functionality.

- [Fork changelog](CHANGELOG.md)
- [Local build, tests and version/release policy](docs/testing-and-build.md)
- [Roadmap](docs/KOSIm-roadmap.md) and [current development handoff](docs/HANDOFF.md)

Runtime validation targets Windows KSP 1.12.5 under Proton on Linux.
Fork releases are published manually to the private `Jvaxx/kOSIm`
repository, not to the upstream download pages below.

## Upstream kOS

kOS is a scriptable autopilot Mod for Kerbal Space Program. It allows you write small programs that automate specific tasks.

Documentation : https://ksp-kos.github.io/KOS_DOC/

Addon release thread: https://forum.kerbalspaceprogram.com/index.php?/topic/165628-13-kos-v1130-kos-scriptable-autopilot-system/

Curse Forge page: http://kerbal.curseforge.com/projects/kos-scriptable-autopilot-system?gameCategorySlug=ksp-mods&projectID=220265

Spacedock page: http://spacedock.info/mod/60/kOS:%20Scriptable%20Autopilot%20System

Contribution : If you have contributed to the development of the mod, and wish to be named a certain way in the next release notes, then add your edit to the ``### Contributors`` section of the CHANGELOG.md file in your pull request.
