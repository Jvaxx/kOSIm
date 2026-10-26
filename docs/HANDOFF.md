# kOS Improved — current handoff

## Resume point

**Latest release:** **kOS Improved 1.6.0.104** (2026-10-26) at
[v1.6.0.104](https://github.com/Jvaxx/kOSIm/releases/tag/v1.6.0.104), annotated tag on
`422be1eb`, regular release marked latest. `master` and the tag are pushed to the fork
(`origin`) only. NetKAN submission
[KSP-CKAN/NetKAN#11747](https://github.com/KSP-CKAN/NetKAN/pull/11747) (fork `Jvaxx/NetKAN`,
branch `add-kOSImproved`) is still open, awaiting first-contributor approval; the fork is not
yet in the CKAN index, so CKAN users update by removing `kOSImproved` and installing the new
`.ckan`.

1.6.0.104 carries two user-requested changes (commits `6cab0abd`, `9d7aab7c`):

1. **Shared command history per save** (replaces per-CPU history; per-save scope, no option,
   old files deleted without merging).
   - `src/kOS.Safe/Screen/CommandHistoryList.cs`: the commands, bound 500, one line-number
     counter, never-reused entry ids, store with lazy load and 2 s save throttle.
   - `CommandHistory.cs`: a per-prompt view over a list (own draft, position by id,
     unrecorded leading-space command). Public API kept; new `CommandHistory(CommandHistoryList)`.
   - `CommandLineEditor.cs`: `HistoryList` setter shares a list; search match kept by id.
   - `SharedCommandHistory.cs`: `Saves.Acquire(path)`/`Release(list)`, one list per file,
     reference-counted; last release saves and forgets.
   - `kOSProcessor.cs`: in flight, acquires `saves/<save>/kOS/history.txt` in `InitObjects`,
     releases in `OnDestroy`; outside flight a CPU keeps a private in-memory history.
   - `CpuFilePrune.cs`/`CpuFilePruner.cs`: old `kOS/history/<persistentId>[-n].txt` deleted
     on main-menu load (not links, not through a linked folder, unknown names kept, folder
     removed when empty); temp leftovers beside `history.txt` swept.
2. **Toolbar right-click in flight** (`KOSToolbarWindow.cs`): stock `onRightClick` and Blizzy
   `MouseButton == 1`. If any active-vessel terminal is closed, open all without focus; else
   close all. Rules in `src/kOS.Safe/Screen/VesselTerminals.cs`; `TermWindow` steps windows
   still at the default position by one header height each.

## Release record (1.6.0.104)

| Asset | Size | SHA-256 |
| --- | --- | --- |
| `kOS-v1.6.0.104.zip` | 3,224,072 | `07bd639bd2a34b086af4e6a5871c4fafa977b1703e07167d463d8e4c148898c3` |
| `kOSImproved-1.6.0.104.ckan` | 1,504 | `12cb8433d5c7a1716236779fb4dd3f73a7739af82ba52bda4898ab98a10992ca` |
| `SharpZipLib-0.86.0.518-source.zip` | 754,332 | `586c8bc1ae3ff432dccbe11c2bde2a82dabbdaeafb896d92bf1682932bc7dde8` |

GitHub asset digests match. DLLs built from the tagged commit: `kOS.dll`
`4aa61e65…5fcc32`, `kOS.Safe.dll` `bcc09162…48efd53`; both 1.6.0.104+`422be1eb…`,
`KSPAssembly("kOS", 1, 6, 0)`, file version 1.6.0.104 (read with `monodis`). Generated
`kOS.version` is 1.6.0.104, KSP 1.12 only, no `URL`/`DOWNLOAD`. The ZIP holds 68 files, the
same list as 1.6.0.103; `SOURCE.txt` names `v1.6.0.104` and the commit. The SharpZipLib
archive is the one attached to 1.6.0.103 (same hash). Release notes: ignored
`artifacts/release-1.6.0.104-notes.md`.

## Release verification (tagged commit `422be1eb`)

With `PATH="$HOME/.cache/kosim-tools/dotnet:$PATH"`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`:

- `bash .ci/test.sh`: 49 Python tests OK; NUnit **1,460 discovered/executed/passed**,
  0 errors/failures/skips (`artifacts/tests/release-1.6.0.104-headless.log`).
- `dotnet build kOS.sln -c Release --no-incremental`: **0 errors, 44 warnings** (baseline);
  `python3 .ci/check-version.py --built` consistent at 1.6.0.104.
- `python3 .ci/package.py`, `python3 .ci/ckan.py artifacts/kOS-v1.6.0.104.zip`.
- `python3 .ci/ckan-solver-check.py artifacts/kOS-v1.6.0.104.zip --work artifacts/tests/ckan-solver/release-1.6.0.104`:
  CKAN 1.36.4, **19/19 passed**.
- After publishing: the downloaded release ZIP matches the `.ckan` `download_hash`; installing
  the published `.ckan` URL into an isolated fake 1.12.5 instance (HOME/XDG inside
  `artifacts/tests/ckan-solver/published-1.6.0.104/`) installed kOSImproved 1.6.0.104 plus
  ModuleManager 4.2.3 with the expected DLL hashes.
- In game: the maintainer reported both changes working on the dev copy (Windows KSP
  1.12.5.3190, GE-Proton11-7) with a pre-release build of the same runtime code; the release
  commit changes only the changelog date and roadmap status.

**Not performed:** deploying the released DLLs to the dev copy; Blizzy's toolbar (not
installed); Sphinx build (no `.rst` changed); CKAN GUI; older CKAN clients.

## Next steps

1. Watch NetKAN#11747: respond to reviewer questions. After merge, check `ckan update` +
   `ckan show kOSImproved`, then update README, packaged `Readme`, `docs/ckan.md` and the
   manual downloads page to say it is indexed.
2. Next development version: bump `kOS.props` to 1.6.0.105 with a new `(Unreleased)`
   changelog entry only when new changes start.
3. Remaining implementation and manual coverage: roadmap and testing guide.
