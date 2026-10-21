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
  `.ci/package.py` adds the license and source materials the
  [binary distribution audit](docs/binary-distribution.md) found missing from the
  `.100`–`.102` ZIPs; those historical ZIPs remain unchanged.
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

From the repository root, after the full build of the committed release tree:

```bash
python3 .ci/package.py                                    # artifacts/kOS-v<version>.zip
unzip -t artifacts/kOS-v<version>.zip && unzip -l artifacts/kOS-v<version>.zip
python3 .ci/ckan.py artifacts/kOS-v<version>.zip          # artifacts/kOSImproved-<version>.ckan
python3 .ci/ckan-solver-check.py artifacts/kOS-v<version>.zip
curl -fL -o artifacts/SharpZipLib-0.86.0.518-source.zip \
  https://github.com/icsharpcode/SharpZipLib/archive/cfc69a68fefbc5858fe70b35f7b69fc505b8c2d6.zip
sha256sum artifacts/kOS-v<version>.zip artifacts/kOSImproved-<version>.ckan \
  artifacts/SharpZipLib-0.86.0.518-source.zip
```

- [ ] `package.py` refuses a dirty tree, a DLL built from another commit, stale
  `kOS.version` metadata, an unaudited SharpZipLib and an existing output file. It
  packages only **tracked** `GameData/kOS` files plus the built DLLs and `kOS.version`;
  PluginData and part thumbnails are never included. Use `--allow-dirty` only for local
  test packages (`-dirty` name, never published).
- [ ] Confirm the layout begins with `GameData/kOS/` and holds both mod DLLs,
  SharpZipLib, assets, `kOS.version`, `LICENSE.md`, `THIRD_PARTY_NOTICES.md`,
  `Licenses/SharpZipLib-0.86.0.518-COPYING.txt` and `SOURCE.txt` naming the tag and
  commit. Compare the file list with the previous release; explain additions/removals.
- [ ] Run the [CKAN solver check](docs/ckan.md#release-steps) and record its result.
  It needs network access and the CKAN command line; it uses isolated fake instances.
- [ ] The SharpZipLib source archive is offered beside the binaries on the release page
  (see [binary audit](docs/binary-distribution.md)); unpack it once to check it is the
  0.86.0.518 source tree.
- [ ] State the ModuleManager dependency for manual installs; CKAN installs it automatically.
- [ ] With permission and the isolated dev game closed, back up its mod folder and
  deploy the package without replacing user configuration. Compare DLL hashes.
- [ ] Check main-menu version/commit in Player.log and run relevant functional checks
  under the recorded KSP/Proton versions. A version smoke is not a full gameplay pass.

## Publish (only when approved)

- [ ] Tag the verified commit with an annotated `v<version>` tag; push to the fork remote
  only. Never push to upstream or move a published tag.
- [ ] Create a **non-prerelease** release at `Jvaxx/kOSIm` with the verified zip,
  `kOSImproved-<version>.ckan`, `SharpZipLib-0.86.0.518-source.zip`, checksums,
  changes, validation scope, known issues and [CKAN notes](docs/ckan.md#client-behavior).
  Use a prerelease for anything not meant for CKAN users. Do not publish to upstream's
  SpaceDock, CurseForge, documentation site or release pages.
- [ ] Compare uploaded asset digests with the local hashes; download the ZIP and confirm
  its SHA-256 matches the `.ckan` `download_hash`. **Never replace a published asset:**
  CKAN metadata pins its hash. Publish a new version instead.
- [ ] Once the fork is in the CKAN index, a published release is picked up by CKAN's bot
  automatically. Check `ckan show kOSImproved` after indexing. Until then, see the
  [NetKAN submission steps](docs/ckan.md#netkan-submission-maintainer-action-after-the-first-release).
- [ ] Record the release commit/tag and concise validation result; keep detailed local
  receipts/logs outside tracked docs. Do not report the next development version as released.

CKAN metadata and its checks are described in [CKAN packaging](docs/ckan.md).
No hosted fork documentation deployment is configured.
