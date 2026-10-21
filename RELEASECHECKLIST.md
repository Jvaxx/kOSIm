# kOS Improved release checklist

Releases are prepared locally and published manually to **this fork**. No hosted
build/release automation, upstream publishing, automatic game deployment or history
rewrites. Publishing, tagging, deployment and game launches each require maintainer
approval; preparing a build does not imply that approval.

## Prepare

- [ ] Review [known issues](docs/KOSIm-roadmap.md) and remaining manual checks.
- [ ] Set the version **only in `kOS.props`**. Follow the [version policy](docs/compatibility.md#installation-and-versioning): upstream major/minor/patch + continuous fork counter.
- [ ] Add/update the top `## kOS Improved <version> (Unreleased)` entry in `CHANGELOG.md`;
  replace `Unreleased` with the release date only when releasing. Keep inherited
  upstream entries and historical release identifiers unchanged.
- [ ] Check README/manual, installation instructions, provenance and compatibility claims.
  Resolve the [binary distribution audit findings](docs/binary-distribution.md): the
  existing `.100`–`.102` ZIPs have missing license materials and stale source directions.
- [ ] Ensure no credentials, proprietary assemblies, local config or generated files
  are staged. Review [publication checks](docs/publication.md) for a first public release.

## Verify the release commit

```bash
bash .ci/test.sh
dotnet build kOS.sln -c Release --no-incremental
python3 .ci/check-version.py --built
```

- [ ] Record actual test counts/results, full-build warnings and unperformed checks.
- [ ] Render the [manual locally](doc/README.md); review warnings and changed pages.
- [ ] Commit the reviewed source/version/docs, then rebuild **that commit** and rerun
  the built-version checker. Informational versions embed the commit ID.
- [ ] Inspect the actual DLL assembly/file/informational versions and KSP assembly
  attribute (e.g. with `monodis`), not only generated source. Hash both DLLs.
- [ ] Verify generated `kOS.version` matches and contains no `URL` or `DOWNLOAD`.
  If stale fields survive from an older build, delete that generated file and rebuild.

## Package and smoke-test

From the repository root (replace `<version>`):

```bash
mkdir -p artifacts
(cd Resources && zip -rX ../artifacts/kOS-v<version>.zip GameData/kOS \
  -x 'GameData/kOS/Plugins/PluginData/*' 'GameData/kOS/Parts/@thumbs/*')
unzip -t artifacts/kOS-v<version>.zip
unzip -l artifacts/kOS-v<version>.zip
sha256sum artifacts/kOS-v<version>.zip
```

- [ ] Start with a **new archive filename**: `zip` updates an existing archive and can
  retain stale entries. Audit `Resources/GameData/kOS` for stale/unintended files too.
- [ ] Confirm layout begins with `GameData/kOS/`, contains both mod DLLs, SharpZipLib,
  required assets and version metadata, but no PluginData, thumbnails, test binaries,
  logs, local settings or proprietary game files. Supply `LICENSE.md`,
  `THIRD_PARTY_NOTICES.md` and applicable dependency licenses/source arrangements with
  distributions; the zip command alone does not assemble those compliance materials.
- [ ] State the ModuleManager dependency; it is not bundled by the command above.
- [ ] With permission and the isolated dev game closed, back up its mod folder and
  deploy the package without replacing user configuration. Compare DLL hashes.
- [ ] Check main-menu version/commit in Player.log and run relevant functional checks
  under the recorded KSP/Proton versions. A version smoke is not a full gameplay pass.

## Publish (only when approved)

- [ ] Tag the verified commit with an annotated `v<version>` tag; push to the fork remote
  only. Never push to upstream or move a published tag.
- [ ] Create a release at `Jvaxx/kOSIm` with the verified zip, checksum, changes,
  validation scope, known issues and installation/CKAN cautions. Do not publish to
  upstream's SpaceDock, CurseForge, documentation site or release pages.
- [ ] Compare the uploaded asset digest with the local hash.
- [ ] Record the release commit/tag and concise validation result; keep detailed local
  receipts/logs outside tracked docs. Do not report the next development version as released.

No dedicated CKAN metadata or hosted fork documentation deployment is configured.
Those require separate design and validation, not guesses copied from upstream.
