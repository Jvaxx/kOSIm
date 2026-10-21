# kOS Improved — current handoff

## Resume point

**Current state:** public-fork documentation cleanup and binary audit are committed
on `master` and pushed; `Jvaxx/kOSIm` was made **public** at the maintainer's request
(2026-10-21). Maintainer decision: historical release ZIPs `.100`–`.102` stay as
published (license/notice gaps below are known and accepted). No history rewrite,
tag, release/asset replacement, deployment or game action was performed.

**Important publication finding:** inherited `.travis.yml` held a legacy Slack
notification token. Obsolete Travis configuration/scripts are removed from the
working tree, **not history**. No validity test was attempted. Every commit carrying
it is also reachable from upstream's public `KSP-KOS/KOS` branches, so a public fork
exposes nothing new; revocation is upstream's concern, and history sanitization would
not reduce exposure. See
[publication checklist](publication.md), including historical local paths, binary
art-source metadata and GitHub-hosted content outside this checkout. Do not call
this cleanup a credential/history audit or anonymization.

**Publication review (follow-up):** repo is private; no Actions secrets, one branch,
three releases, no issues/PRs. Fork-only commits (`8f281a45..master`) contain no
credentials; old handoff text in them names local dev/Steam paths under the
maintainer's home (not secrets). README now links the maintainer's
[kos-ls](https://github.com/Jvaxx/kos-ls) language server and discloses Claude
Opus 5.5 assistance. `bash .ci/test.sh`: 1,435 passed, 29 Python tests passed.

## Development / deployed state

- Current development **1.6.0.103, unreleased/undeployed**. Versioning implementation
  `2a1d254a`, verification record `ab47fa4b`, CKAN correction `9c0c83a4` were already
  pushed before this task. Exact upstream baseline: **1.6.0.1**, fork point `8f281a45`.
- Last release **1.5.1.102**, tag `v1.5.1.102` on `c1fb95a9`. Historical version prefixes
  `.100`–`.102` were wrong; do not rename tags/assets. The isolated dev copy was last
  left on `.102`, closed after its approved main-menu/version smoke.
- Last gameplay evidence: phases 0–4, review fixes, presets and user-reported opacity
  pass on Windows KSP **1.12.5.3190 / GE-Proton11-7**. The version correction has not
  had a new game check. Remaining coverage gaps: [testing guide](testing-and-build.md#game-checks).

## This cleanup

- Rewrote README, CONTRIBUTING, RELEASECHECKLIST, STYLEGUIDE and AGENTS for the fork;
  updated packaged readme, Sphinx identity/download/help/contribution/license pages.
- Consolidated maintained development docs into index, build/test guide, compatibility
  contracts, roadmap/known issues, this short handoff and publication checklist.
  Removed completed `review-report.md`; retained remaining P-12/D-02/R-06/R-07 work.
  Detailed old phase/release/test logs remain in git at `9c0c83a4`.
- **Original `docs/improvements-overview.md` preserved unchanged**, explicitly historical
  (project constraint). License texts, attribution, inherited changelog and art/assets
  preserved; legacy mesh READMEs labelled historical. Added `THIRD_PARTY_NOTICES.md`
  identifying bundled SharpZipLib 0.86.0.518 and its tagged-source GPL/linking exception
  (binary/source reproducibility and complete distribution compliance not certified).
  No game runtime source/API changes.
- Removed `.travis.yml` and `.ci/travis/`; disabled upstream GitHub references remain
  inert. Tightened output/environment ignores; added local documentation link coverage.
- Simplified Sphinx config, pinned local docs tooling in `doc/requirements.txt`; corrected
  malformed inherited flight-control tables and heading/toctree warnings found by rendering.

## Binary audit follow-up

- [Binary distribution audit](binary-distribution.md): inspected `.100`, `.101`, `.102`
  ZIPs; all match current GitHub asset digests and pass CRC/layout checks. Each has
  64 files, only three executable DLLs, no packaged game/Unity DLLs. DLL informational
  commits match tags; independent source rebuilds were not performed.
- All three bundle the exact stock NuGet SharpZipLib 0.86.0 `lib/20` DLL. GPLv2-or-later
  + linking exception confirmed against pinned source `cfc69a68`; stock identity verified,
  no inherent redistribution ban found in those terms. No security/legal certification.
- **Public binary distribution gaps:** all three omit full licenses/third-party notices;
  packaged readme points to upstream, not fork source. Automatic tag source downloads
  exist; explicit exact-source/dependency-source directions and a compliant delivery
  arrangement still need work. Source/asset rights not exhaustively audited.
- Updated third-party notices, publication/index/checklist and this handoff. Existing
  ZIPs, release notes and tags remain unchanged. Fixing/replacing historical distribution
  materials requires a separate maintainer decision; no repair was authorized by the audit.
- Detailed audit script, manifests, NuGet download/comparison, extracted metadata and
  read-only release API receipt: ignored `artifacts/tests/package-license-audit/`.

## Verification

With `PATH="$HOME/.cache/kosim-tools/dotnet:$PATH"`,
`DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`:

- `bash .ci/test.sh`: **1,435 discovered/executed/passed**, 0 errors/failures/skips;
  **29 Python tests passed**, including maintained-document local links; source versions
  consistent. Initial pre-link-test pass was 1,435 + 28 Python tests.
- `dotnet build kOS.sln -c Release --no-incremental`: **0 errors, 44 warnings**.
  `python3 .ci/check-version.py --built`: source/generated metadata consistent.
- `artifacts/docs-venv/bin/python -m sphinx -E -a -W --keep-going -b html doc/source artifacts/manual`:
  **passes with no warnings/errors**, Sphinx 8.2.3/theme 3.0.2 on Python 3.13.15.
  Isolated pre-cleanup rendering confirmed the inherited table/heading/toctree warnings.
  Changed rendered pages: **1,365 local HTML links checked, none broken**; external
  links were not exhaustively fetched. Tooling tests no longer need a stub theme.
- `git diff --check` passes; original review, both license texts, runtime source,
  project/solution/version inputs and asset binaries unchanged. Detailed session logs
  were consolidated rather than retained in active guidance.
- Targeted current-text credential patterns found no additional matches; filename-only
  reachable-history inspection found old third-party DLLs, not a content audit.
  **No comprehensive secret/history audit, GitHub-side review or license audit.**
- Logs: ignored `artifacts/tests/public-preparation-{headless-final,full-build,built-version,manual-final}.log`.
  No new package or Proton/game test; no deployment. Cleanup build outputs describe
  existing HEAD plus documentation/tooling edits, not a new release commit.
- Audit verification: `python3 artifacts/tests/package-license-audit/audit_packages.py`
  plus assertion checks against saved API metadata/NuGet bytes; all three archives
  pass digest/CRC/layout/tag-metadata checks. Post-audit `bash .ci/test.sh`:
  **1,435 discovered/executed/passed**, 0 errors/failures/skips; **29 Python tests passed**,
  source-version check passes (`artifacts/tests/package-license-audit/headless.log`).
  `git diff --check` passes; all three archive hashes rechecked unchanged. No full build,
  Sphinx or game check rerun for this Markdown-only audit. The gate replaces safe-project
  outputs, so full-build before any future packaging/deployment.

## Next steps

1. Next release (1.6.0.103 or later): package full licenses, third-party notices and
   fork/SharpZipLib source directions (see [binary audit](binary-distribution.md)).
   Historical ZIPs are intentionally left unchanged.
2. Remaining implementation and manual coverage are in the roadmap/testing guide.
   No further feature or CKAN packaging work is selected. Do not restore completed
   review/session logs to active docs.
