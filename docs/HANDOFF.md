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

## In progress: 1.6.0.105 (unreleased, uncommitted working tree)

Two inherited flight-binding bugs, reported by the maintainer from a two-vessel
rendezvous (each vessel running `LOCK STEERING TO TARGET:POSITION`):

1. **`TARGET` across a vessel switch.** KSP 1.12.5 `Vessel.ResumeTarget` (read from the
   disassembled `Assembly-CSharp.dll`) clears the new active vessel's target and restores
   it from a coroutine 1–121 frames later, keeping the target to restore in `Vessel.pTI`
   (nulled on restore). The getter threw "No TARGET is selected" in between, ending the
   program.
   - `src/kOS/Utilities/VesselTargeting.cs`: `GetTarget(vessel)` = live target, else
     `pTI.FindTarget()` for the active vessel within the window, and for a non-active
     vessel with no `targetObject`. `Hook()` notes the frame of `onVesselChange` and of
     `onPartCouple` (docking/grappling call `MakeActive` on the active vessel in the
     frame of `Part.Couple`, with no `onVesselChange`).
   - `src/kOS.Safe/Utilities/TargetResume.cs`: the 125-frame window rule (tested).
   - `src/kOS/Binding/MissionSettings.cs`: `TARGET` and `HASTARGET` getters use it.
   - `VesselTargeting.RestorePending(vessel)`: called by `SASMODE` `"TARGET"`/
     `"ANTITARGET"` and `NAVMODE` `"TARGET"` (`FlightControlManager.cs`) so a script that
     saw `HASTARGET` can set them: sets the pending target now (`VesselUtils.SetTarget`
     for the active vessel, `Vessel.targetObject` for another), never the vessel itself.
2. **`SASMODE`.** `kOSProcessor.SetAutopilotMode` set the *active* vessel's mode; it now
   sets the processor's vessel, and `FlightControlManager` no longer calls it (it already
   sets its own vessel). `FlightControlManager.currentVessel` is now `Shared.Vessel`
   instead of the vessel captured when the bindings were made.

A whole-mod code review before the release (read-only, not all inherited code) gave eight
findings. Fixed: the two above in `VesselTargeting` (docking, `RestorePending`), and

3. **Pilot trim setters** (`src/kOS/Suffixed/FlightControl.cs` `WritePilot`): wrote the
   active vessel's `FlightInputHandler.state` from any vessel; now only from the active one.
4. **`kOSProcessor.OnDestroy`**: no longer calls `SortAllInstances()` (its comparator reads
   `FlightGlobals.ActiveVessel`; a throw skipped the history save/release, telnet detach
   and CPU dispose). `RemoveAll` keeps the order.

**Review findings left open** (next session):

- `src/kOS.Safe/Screen/CommandHistoryList.cs` `EnsureLoaded`: a `history.txt` that exists
  but cannot be read (locked, denied, over 16 MiB) loads as empty and the next command
  saves over it.
- `src/kOS/Screen/TermWindow.cs` Ctrl+C/X/D/A/E test only `e.control`, so AltGr (Ctrl+Alt
  on Windows layouts) triggers them; only paste and search exclude Alt. How Proton reports
  AltGr is unchecked.
- `src/kOS.Safe/Screen/CpuFilePrune.cs`: the legacy-history delete checks only whether
  `kOS/history` itself is a link, not its parents, and the geometry prune has no folder
  link check; `compatibility.md` says anything under a linked folder is kept.
- `VesselTargeting.GetTarget`: a non-active vessel left before the restore runs
  `pTI.FindTarget()` on every read (each physics tick under `LOCK STEERING`).

Also changed: `kOS.props` 1.6.0.105, changelog `(Unreleased)` entry, `compatibility.md`
(new "Flight bindings" section), manual checks in `testing-and-build.md`, test minimum
1,464 (`.ci/known-test-failures.json`, `AGENTS.md`).

Verified: `bash .ci/test.sh` 49 Python OK, NUnit **1,464 discovered/executed/passed**;
`dotnet build kOS.sln -c Release --no-incremental` **0 errors, 44 warnings** (baseline);
`python3 .ci/check-version.py --built` consistent at 1.6.0.105.

Deployed to the dev copy (game closed, uncommitted build): `GameData/kOS` backed up to
`kOS-GameData-backups/kOS-before-v1.6.0.105-dev-20261112-153510`, then `kOS.dll`
(`2d13d039…`), `kOS.Safe.dll` (`b11a0cc9…`) and `kOS.version` copied; hashes match the
repository outputs, PluginData untouched, `launch-dev.sh --check` passed. Rebuild and
redeploy after committing: the DLLs embed the source commit.

In game (2026-11-12, that deployed build): the maintainer reported it working; which of
the listed checks were run was not recorded. The fixes are in Unity-side code the
headless tests do not reach: only the window rule is unit-tested. Known limits: a restore
KSP never makes (target gone, or no orbit driver, e.g. the Sun) still ends in
"No TARGET is selected" once the window is over; `SET TARGET` during the window is still
overridden by KSP's restore.

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
2. Game-check 1.6.0.105 on the dev copy (two-vessel list in
   [testing-and-build.md](testing-and-build.md#game-checks)), then commit.
3. Remaining implementation and manual coverage: roadmap and testing guide.
