# Public-repository preparation

This is a checklist, **not a claim that publication or a security audit is complete**.
Documentation cleanup does not change repository visibility, rewrite git history,
publish a release or grant permission to deploy/launch the game.

## Working-tree preparation

- Fork identity and destinations: README, contribution/release guides, packaged readme
  and manual entry pages must distinguish `Jvaxx/kOSIm` from upstream `KSP-KOS/KOS`.
  Keep upstream links where they are attribution, historical issues or language references.
- Keep licenses, contributor/asset notices, upstream changelog and compatibility assets.
  Old textures and legacy part definitions are not automatically disposable build artifacts.
- `.travis.yml` and obsolete Travis scripts are removed. Upstream GitHub workflow
  reference copies remain inert under `.github/upstream-workflows/`; no active hosted jobs.
- Ignore outputs, local config, virtual environments and reports. The tracked
  `Resources/GameData/kOS/Plugins/ICSharpCode.SharpZipLib.dll` is an intentional build/runtime
  dependency, not an accidentally committed fork build. Its DLL metadata identifies
  **0.86.0.518**, whose tagged source uses GPL-2.0-or-later with a linking exception,
  not the modern project's license. See [third-party notices](../THIRD_PARTY_NOTICES.md).
  The [binary audit](binary-distribution.md) verified its exact NuGet-distribution identity,
  but found all three existing release ZIPs lack required license/notice materials and
  point their packaged source readme at upstream. `.ci/package.py` adds those materials
  to new packages; the historical ZIPs are unchanged. Do not remove notices.
- Active developer docs contain reusable contracts and remaining work, not workstation
  inventories, Steam shortcut IDs, personal save details, deployment hashes or chat transcripts.
  The original review is the explicit archival exception, preserved unchanged and labelled
  in the [documentation index](README.md).

## Specific history finding

The removed upstream `.travis.yml` contains a **legacy Slack notification token**
in git history. It was not tested for validity and is not reproduced here. Every
commit containing it is also reachable from upstream's public branches, so it is
already public there; publishing this fork adds no exposure and rewriting fork
history would not remove it. Revocation is for upstream's owner. Deleting the current
file does not revoke a credential or remove historical copies. Embedded author paths
also remain in some original binary art-source metadata; those files are retained.

## Before changing visibility (maintainer action)

- [ ] Review **all branches, tags and reachable history**, not just the current tree,
  for credentials/tokens, proprietary assemblies, personal paths/data and old logs.
  A file removed in this cleanup remains available through git history.
- [ ] Review GitHub release assets/notes (including earlier private releases), issues,
  PRs, discussions, wiki, Pages settings, Actions secrets/permissions and collaborators.
  Changing visibility may expose material outside this checkout.
- [ ] Decide whether historical workstation paths are acceptable. They still exist in
  older commits and the protected original review. This cleanup is **not anonymization**.
- [ ] If a secret is found, revoke/rotate it first. Agree any history rewrite separately;
  do not silently force-push, replace tags or rewrite historical releases.
- [ ] Confirm rights/provenance for assets and bundled dependencies, retain license texts
  and notices, and ensure binary distributions meet corresponding-source obligations.
  A filename-only scan of reachable local history also found old ModuleManager and
  KSPAPIExtensions binaries; include historical dependency provenance in that review.
  This preparation is not a legal opinion or completed third-party license audit.
- [ ] Decide which releases to make available and their support claims. Current development
  `1.6.0.103` is unreleased; historical `1.5.1.100`–`.102` names are intentionally unchanged.
- [ ] Review installation as a replacement for upstream, ModuleManager dependency,
  [CKAN behavior](ckan.md#client-behavior) and the narrow Proton validation scope.
- [ ] Run local tests and a manual HTML build; document unresolved warnings. For a new
  binary release, follow the full [release checklist](../RELEASECHECKLIST.md), including
  committed rebuild, binary metadata, package inspection and approved game verification.

The source documentation is available under `doc/`; **no fork-hosted documentation
site is configured**. CKAN metadata exists ([CKAN packaging](ckan.md)), but until a
release attaches a `.ckan` and the NetKAN pull request is merged, do not describe the
fork as available in the CKAN index.
