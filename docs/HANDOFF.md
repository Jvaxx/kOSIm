# kOS Improved — current handoff

## Resume point

**Current state:** **kOS Improved 1.6.0.103 is released** (2026-10-21) at
[v1.6.0.103](https://github.com/Jvaxx/kOSIm/releases/tag/v1.6.0.103), tag on `5453bc25`,
as a regular (non-pre) release marked latest. It is the first release with a standalone
`kOSImproved` `.ckan` and with license/source materials in the ZIP. The NetKAN file was submitted as
[KSP-CKAN/NetKAN#11747](https://github.com/KSP-CKAN/NetKAN/pull/11747) from fork
`Jvaxx/NetKAN`, branch `add-kOSImproved`. Its "Inflate netkans" run is
**action_required** (first-contributor approval by CKAN maintainers). Not yet in the CKAN index.

No deployment to the dev copy and no game launch were performed for this release.
Historical `.100`–`.102` ZIPs stay as published (maintainer decision).

## Release record

| Asset | Size | SHA-256 |
| --- | --- | --- |
| `kOS-v1.6.0.103.zip` | 3,221,678 | `85cafed00e3662af41a9babaa36a72b31d7545e5cf5dbb06411df00320de8b46` |
| `kOSImproved-1.6.0.103.ckan` | 1,504 | `b8b04601b8a63569b240a109ac8512f4332ebdeb3fbdfc1cffdd6f11910648df` |
| `SharpZipLib-0.86.0.518-source.zip` | 754,332 | `586c8bc1ae3ff432dccbe11c2bde2a82dabbdaeafb896d92bf1682932bc7dde8` |

GitHub asset digests match. DLLs from the tagged commit: `kOS.dll`
`3d95397c…614212c`, `kOS.Safe.dll` `dc5ca4c2…db88b122`. Both are 1.6.0.103+`5453bc25…`;
`KSPAssembly("kOS", 1, 6, 0)`; file version 1.6.0.103 (read with `monodis`).
The ZIP holds 68 files: the `.102` file list plus `LICENSE.md`, `THIRD_PARTY_NOTICES.md`,
`Licenses/SharpZipLib-0.86.0.518-COPYING.txt` and `SOURCE.txt` (tag/commit). The
SharpZipLib archive is the GitHub archive of commit `cfc69a68`: assembly version
0.86.0.518, linking exception in `src/Zip/ZipFile.cs`, identical `doc/COPYING.txt`.
Release notes: ignored `artifacts/release-1.6.0.103-notes.md`.

## Verification (tagged commit `5453bc25`)

With `PATH="$HOME/.cache/kosim-tools/dotnet:$PATH"`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`:

- `bash .ci/test.sh`: 49 Python tests passed; NUnit **1,435 discovered/executed/passed**,
  0 errors/failures/skips (`artifacts/tests/release-1.6.0.103-headless.log`).
- `dotnet build kOS.sln -c Release --no-incremental` after the gate: **0 errors, 44 warnings**;
  `python3 .ci/check-version.py --built` consistent (`…-build.log`).
- `python3 .ci/package.py`, `python3 .ci/ckan.py artifacts/kOS-v1.6.0.103.zip`.
- `python3 .ci/ckan-solver-check.py artifacts/kOS-v1.6.0.103.zip --work artifacts/tests/ckan-solver/release-1.6.0.103`:
  CKAN 1.36.4, **19/19 passed**; real CKAN config hash unchanged.
- After publishing: installing from the published `.ckan` URL into an isolated fake
  instance downloaded the release ZIP, passed CKAN's hash validation and installed
  the expected `kOS.dll` hash (`artifacts/tests/ckan-solver/published-1.6.0.103/`).
- Sphinx `-W` manual build passed before the release commit; only changelog/README/ckan.md
  status text changed afterwards.

**Not performed:** dev-copy deployment and in-game/Proton smoke for 1.6.0.103 (runtime code
equals `.102`; only version metadata changed); CKAN GUI; older CKAN clients; replacing
a manual install; local `netkan.exe` inflation.

## Next steps

1. Watch NetKAN#11747: respond to reviewer questions. After merge, check `ckan update` +
   `ckan show kOSImproved` against the default repository, then update README,
   packaged `Readme`, `docs/ckan.md` and the manual downloads page to say it is indexed.
2. Optional with approval: deploy 1.6.0.103 to the isolated dev copy and check the
   main-menu version/commit in Player.log.
3. Next development version: set `KSPBT_ModVersion` to 1.6.0.104 with a new
   `(Unreleased)` changelog entry when the next change starts.
4. Remaining implementation and manual coverage: roadmap and testing guide.
