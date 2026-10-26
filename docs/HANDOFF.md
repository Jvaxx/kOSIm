# kOS Improved — current handoff

## Resume point

**Latest release:** **kOS Improved 1.6.0.103** (2026-10-21) at
[v1.6.0.103](https://github.com/Jvaxx/kOSIm/releases/tag/v1.6.0.103), tag on `5453bc25`,
regular release marked latest. NetKAN submission
[KSP-CKAN/NetKAN#11747](https://github.com/KSP-CKAN/NetKAN/pull/11747) (fork `Jvaxx/NetKAN`,
branch `add-kOSImproved`) waits for first-contributor approval; not yet in the CKAN index.

**Unreleased 1.6.0.104 on `master`** (`kOS.props`, `(Unreleased)` changelog entry), committed as
two commits (shared history, then right-click), **not pushed, not released** (maintainer: no release
yet). Two user-requested changes (2026-10-26), headless-tested, full-built, deployed to the dev copy
and reported working in game by the maintainer:

1. **Shared command history per save** (replaces per-CPU history; user chose per-save scope,
   no option, delete old files without merging).
   - `src/kOS.Safe/Screen/CommandHistoryList.cs` (new): the commands, bound 500, one line-number
     counter, never-reused entry ids, store with lazy load and 2 s save throttle (moved from
     `CommandLineEditor`).
   - `CommandHistory.cs`: now a per-prompt view over a list (own draft, position by id,
     unrecorded leading-space command). Public API kept; new `CommandHistory(CommandHistoryList)`.
   - `CommandLineEditor.cs`: `HistoryList` setter to share a list; search match kept by id;
     `HistoryStore`/`HistoryClock`/`SavePendingHistory` delegate to the list.
   - `SharedCommandHistory.cs` (new): `Saves.Acquire(path)`/`Release(list)`, one list per file,
     reference-counted; last release saves and forgets (next flight re-reads the file).
   - `kOSProcessor.cs`: in flight, acquires `saves/<save>/kOS/history.txt` in `InitObjects`,
     releases in `OnDestroy`; outside flight a CPU keeps a private in-memory history.
   - `CpuFilePrune.cs`/`CpuFilePruner.cs`: geometry-only save-based pruning; old
     `kOS/history/<persistentId>[-n].txt` deleted unconditionally (not links, not through a
     linked folder, unknown names kept, folder removed when empty); temp leftovers beside
     `history.txt` swept. `HISTORY_FOLDER` became `LEGACY_HISTORY_FOLDER` (fork-only constant).
2. **Toolbar right-click in flight** (`KOSToolbarWindow.cs`): stock `onRightClick` (field
   confirmed in the KSP 1.12.5 assembly) and Blizzy `MouseButton == 1`. If any active-vessel
   terminal is closed, open all without focus; else close all. Rules in
   `src/kOS.Safe/Screen/VesselTerminals.cs`; `TermWindow.AtDefaultPosition` /
   `StepFromDefaultPosition` step windows still at (50, 60) by one header height each.

Docs updated: `CHANGELOG.md`, `docs/compatibility.md` (prompt, toolbar, persisted state, prune),
`docs/testing-and-build.md` (matrix + pending game checks), roadmap table, test minimum 1,460
(`AGENTS.md`, testing guide, `.ci/known-test-failures.json`).

### Verification of 1.6.0.104

With `PATH="$HOME/.cache/kosim-tools/dotnet:$PATH"`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`:

- `bash .ci/test.sh`: 49 Python tests OK; NUnit **1,460 discovered/executed/passed** (25 new:
  `SharedHistoryTest`, `VesselTerminalsTest`, legacy-history prune tests), 0 errors/failures/skips.
- Mutation check: index-based search matching made `ASearchKeepsItsMatchWhenTheOtherPromptAddsCommands`
  and `ADroppedMatchIsNoMatch` fail; restored.
- `dotnet build kOS.sln -c Release --no-incremental`: **0 errors, 44 warnings** (baseline; none new);
  `python3 .ci/check-version.py --built` consistent at 1.6.0.104.

- Deployed (game closed) with `rsync -rlt --checksum`, no deletion, `Plugins/PluginData/` excluded;
  installed `kOS.dll` `22be0393…` and `kOS.Safe.dll` `50a4e90d…` match the build. Previous install
  backed up under the dev copy's `kOS-GameData-backups/kOS-before-v1.6.0.104-*`. The deployed build
  predates the commits only by documentation changes.
- In game: maintainer reported the [1.6.0.104 checks](testing-and-build.md#remaining-gaps-from-prior-passes)
  as working (not individually instrumented).

**Not performed:** Blizzy's toolbar (not installed), Sphinx build (no `.rst` changed), release
packaging, push.

## Release record (1.6.0.103)

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

## Release verification (tagged commit `5453bc25`)

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

1. Release 1.6.0.104 only when the maintainer asks (`RELEASECHECKLIST.md`); push likewise.
2. Watch NetKAN#11747: respond to reviewer questions. After merge, check `ckan update` +
   `ckan show kOSImproved`, then update README, packaged `Readme`, `docs/ckan.md` and the
   manual downloads page to say it is indexed.
3. Remaining implementation and manual coverage: roadmap and testing guide.
