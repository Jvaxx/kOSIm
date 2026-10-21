# kOS Improved — current handoff

## Resume point

**Current state:** CKAN packaging for identifier **`kOSImproved`** is implemented and
checked locally, **uncommitted** on `master` (new files staged, edits unstaged). Nothing
was released, tagged, pushed, deployed or submitted to NetKAN. No game was launched.
`Jvaxx/kOSIm` is public. Historical `.100`–`.102` ZIPs stay as published (maintainer decision).

Maintainer decisions for this work: identifier `kOSImproved`; ship a standalone
`.ckan` with releases first, then a NetKAN pull request; claim **KSP 1.12 only**.

## Changed in this task

- `ckan/kOSImproved.netkan`: GitHub `Jvaxx/kOSIm` releases, asset filter
  `^kOS-v<4 fields>.zip$`, prereleases excluded, `$vref` KSP-AVC, `v` stripped from tags;
  `provides: kOS`, `conflicts: kOS, kOS-Classic`, `depends: ModuleManager`,
  `suggests: RemoteTech`, install `GameData/kOS` → `GameData`, `GPL-3.0`.
- `.ci/package.py`: deterministic release ZIP from tracked `GameData/kOS` files + built
  DLLs/`kOS.version`, adding `LICENSE.md`, `THIRD_PARTY_NOTICES.md` and generated `SOURCE.txt`.
  Refuses a dirty tree (unless `--allow-dirty`, `-dirty` name), DLLs without
  `<version>+<HEAD>`, stale metadata, an unaudited SharpZipLib and existing output.
- `Resources/GameData/kOS/Licenses/SharpZipLib-0.86.0.518-COPYING.txt`: GPLv2 text
  from SharpZipLib commit `cfc69a68` `doc/COPYING.txt` (SHA-256 `204d8eff…8994`).
- `.ci/ckan.py`: standalone `.ckan` from netkan + ZIP (version/KSP range from
  `kOS.version`, release URL, sizes, SHA-1/SHA-256). `.ci/ckan-solver-check.py`: manual
  real-client check (19 assertions) on isolated fake instances via loopback repository.
- `src/kOS/kOS.csproj`: explicit `KSP_Version`/`_Min`/`_Max` = 1.12 (was KSPBuildTools'
  1.8–1.12). `check-version.py` now requires these and checks generated `kOS.version`.
- Tests: `.ci/test_ckan.py`, `.ci/test_package.py`, extended `test_check_version.py`.
- Docs: new [CKAN packaging](ckan.md); README install section, packaged `Readme`,
  release checklist (package/CKAN/SharpZipLib-source/publish steps), compatibility,
  testing guide, publication, binary-audit follow-up, notices, roadmap, changelog,
  manual `downloads_links.rst`.

## Verification

With `PATH="$HOME/.cache/kosim-tools/dotnet:$PATH"`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`:

- `bash .ci/test.sh`: **49 Python tests passed** (was 29); source versions consistent;
  NUnit **1,435 discovered/executed/passed**, 0 errors/failures/skips.
- `dotnet build kOS.sln -c Release --no-incremental` (after the gate): **0 errors,
  44 warnings**. `python3 .ci/check-version.py --built`: consistent; generated
  `kOS.version` has KSP 1.12/1.12/1.12.
- `artifacts/docs-venv/bin/python -m sphinx -E -a -W --keep-going -b html doc/source artifacts/tests/ckan-manual`:
  passes, no warnings. `git diff --check` passes.
- `python3 .ci/package.py --allow-dirty --output-dir artifacts/tests/package`: 68 files =
  the `.102` file list + `LICENSE.md`, `THIRD_PARTY_NOTICES.md`, `Licenses/…COPYING.txt`,
  `SOURCE.txt`. Rebuilding into another directory gave an identical SHA-256.
- `python3 .ci/ckan-solver-check.py --test artifacts/tests/package/kOS-v1.6.0.103-dirty.zip`
  with CKAN CLI **1.36.4**, fake KSP 1.12.5: **19/19 passed**. Real
  `~/.local/share/CKAN/config.json` hash unchanged. Logs: ignored `artifacts/tests/ckan-solver/`,
  `artifacts/tests/ckan-{headless,full-build,manual}.log`.
- Findings recorded in [ckan.md](ckan.md#client-behavior): add-ons ask which `kOS`
  provider to use (headless CLI installs neither); a plain install over upstream says
  "Nothing to install"; **newer standalone `.ckan` files do not upgrade in place**.

**Not performed:** CKAN GUI; older CKAN clients; replacing a manual (non-CKAN) install;
NetKAN inflation (`netkan.exe` is no longer distributed; NetKAN's PR checks do it);
a clean committed package; download of the SharpZipLib source archive; any game/Proton
check of the 1.6.0.103 build (the KSP-range change is metadata only).

## Next steps

1. Review and commit this work. Then follow [RELEASECHECKLIST.md](../RELEASECHECKLIST.md)
   for 1.6.0.103: committed full build, `package.py`, `ckan.py`, solver check,
   SharpZipLib source asset, approved dev-copy smoke, non-prerelease GitHub release.
2. After the release: NetKAN pull request per [ckan.md](ckan.md#netkan-submission-maintainer-action-after-the-first-release);
   after merge, update README/packaged readme/ckan.md status.
3. Remaining implementation and manual coverage: roadmap and testing guide.
