# Developer documentation

kOS Improved (kOSIM) is an independent fork of upstream kOS 1.6.0.1.
The user/language manual is in [`doc/`](../doc/README.md); this directory is for
maintained development guidance, not a running session diary.

| Document | Purpose |
| --- | --- |
| [Build and tests](testing-and-build.md) | Setup, local commands, test harness, game-check procedure |
| [Changes and compatibility](compatibility.md) | Intentional differences, architecture contracts, persisted state and limits |
| [Roadmap and known issues](KOSIm-roadmap.md) | Completed improvements, open findings and prioritized follow-up |
| [Current handoff](HANDOFF.md) | Short resume point and latest verification; replace stale state rather than append logs |
| [Publication checks](publication.md) | Public-repository preparation, licensing, privacy and history caveats |
| [CKAN packaging](ckan.md) | `kOSImproved` metadata, tested client behavior, release and NetKAN steps |
| [Binary distribution audit](binary-distribution.md) | Verified release payloads/SharpZipLib provenance and missing license/source materials |
| [Original review](improvements-overview.md) | **Unchanged historical research**, not current status or instructions |

Also see [contribution guidance](../CONTRIBUTING.md), [release checklist](../RELEASECHECKLIST.md)
and [changelog](../CHANGELOG.md).

## Historical material

The original review examined upstream tag 1.6.0.1 (`d4388439`) before this fork was
initialized. Its proposals, paths, line numbers, external issue status and CI advice
are historical; current code and the maintained documents above take precedence.
It is preserved verbatim under the project's archival constraint, including its
old workstation paths. Do not use it as a current bug list or a setup guide.

The completed post-release review and detailed phase/session logs are available in
git history (before the public-preparation cleanup, commit `9c0c83a4`). Their remaining
findings are consolidated in the roadmap; useful compatibility rules are in
`compatibility.md`. Avoid restoring duplicate logs to the active documentation.
