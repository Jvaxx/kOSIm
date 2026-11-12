# kOS Improved — current handoff

## Resume point

**Latest release:** **kOS Improved 1.6.0.105** (2026-11-12) at
[v1.6.0.105](https://github.com/Jvaxx/kOSIm/releases/tag/v1.6.0.105), annotated tag on
`6137f5ed`, regular release marked latest. `master` and the tag are pushed to the fork
(`origin`) only. NetKAN submission
[KSP-CKAN/NetKAN#11747](https://github.com/KSP-CKAN/NetKAN/pull/11747) (fork `Jvaxx/NetKAN`,
branch `add-kOSImproved`) is still open, awaiting first-contributor approval; the fork is not
yet in the CKAN index, so CKAN users update by removing `kOSImproved` and installing the new
`.ckan`.

1.6.0.105 fixes four inherited bugs (commit `eb61c45c`). The first two were reported by
the maintainer from a two-vessel rendezvous (each vessel running
`LOCK STEERING TO TARGET:POSITION`); the code review below found the docking case,
`RestorePending` and the last two:

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
3. **Pilot trim setters** (`src/kOS/Suffixed/FlightControl.cs` `WritePilot`): wrote the
   active vessel's `FlightInputHandler.state` from any vessel; now only from the active one.
4. **`kOSProcessor.OnDestroy`**: no longer calls `SortAllInstances()` (its comparator reads
   `FlightGlobals.ActiveVessel`; a throw skipped the history save/release, telnet detach
   and CPU dispose). `RemoveAll` keeps the order.

Known limits: a restore KSP never makes (target gone, or no orbit driver, e.g. the Sun)
still ends in "No TARGET is selected" once the window is over; `SET TARGET` during the
window is still overridden by KSP's restore. `RestorePending` assigning
`Vessel.targetObject` on a non-active vessel was not checked on its own in game.

## Open code-review findings

A whole-mod code review before the release (read-only; inherited upstream code only
spot-checked) gave eight findings. Four are fixed above; **four remain open**:

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

## Release record (1.6.0.105)

| Asset | Size | SHA-256 |
| --- | --- | --- |
| `kOS-v1.6.0.105.zip` | 3,224,504 | `73569d11ed260ed977b3dff1a77adbb9a4ce139d4a979e127f8c70e3583c28dc` |
| `kOSImproved-1.6.0.105.ckan` | 1,504 | `f807012ca860ec6c83e65cc3268cf7ec35b64b1e7f113e1b4fc05d2e7eadee43` |
| `SharpZipLib-0.86.0.518-source.zip` | 754,332 | `586c8bc1ae3ff432dccbe11c2bde2a82dabbdaeafb896d92bf1682932bc7dde8` |

GitHub asset digests match. DLLs built from the tagged commit: `kOS.dll`
`aa98c4e7…8423cca`, `kOS.Safe.dll` `94b0eb9f…3a5310a`; both 1.6.0.105+`6137f5ed…`,
`KSPAssembly("kOS", 1, 6, 0)`, assembly version 1.6.0.105 (read with `monodis`). Generated
`kOS.version` is 1.6.0.105, KSP 1.12 only, no `URL`/`DOWNLOAD`. The ZIP holds 68 files, the
same list as 1.6.0.104; `SOURCE.txt` names `v1.6.0.105` and the commit. The SharpZipLib
archive is the one attached to 1.6.0.103 (same hash). Release notes: ignored
`artifacts/release-1.6.0.105-notes.md`.

## Release verification (tagged commit `6137f5ed`)

With `PATH="$HOME/.cache/kosim-tools/dotnet:$PATH"`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`:

- `bash .ci/test.sh`: 49 Python tests OK; NUnit **1,464 discovered/executed/passed**,
  0 errors/failures/skips (`artifacts/tests/release-1.6.0.105-headless.log`).
- `dotnet build kOS.sln -c Release --no-incremental`: **0 errors, 44 warnings** (baseline);
  `python3 .ci/check-version.py --built` consistent at 1.6.0.105.
- `python3 .ci/package.py`, `python3 .ci/ckan.py artifacts/kOS-v1.6.0.105.zip`.
- `python3 .ci/ckan-solver-check.py artifacts/kOS-v1.6.0.105.zip --work artifacts/tests/ckan-solver/release-1.6.0.105`:
  CKAN 1.36.4, **19/19 passed**.
- After publishing: the downloaded release ZIP matches the `.ckan` `download_hash`; installing
  the published `.ckan` URL into an isolated fake 1.12.5 instance (HOME/XDG inside
  `artifacts/tests/ckan-solver/published-1.6.0.105/`) installed kOSImproved 1.6.0.105 plus
  ModuleManager 4.2.3 with the expected DLL hashes.
- In game: the maintainer reported the fixes working on the dev copy (Windows KSP
  1.12.5.3190, GE-Proton11-7) with a pre-commit build of the same runtime code; which of
  the checks listed in [testing-and-build.md](testing-and-build.md#game-checks) were run
  was not recorded. The fixes are in Unity-side code the headless tests do not reach:
  only the window rule is unit-tested.
- Dev copy: the release package was deployed after the build (game closed, mod folder
  backed up first, PluginData untouched); deployed DLL hashes match the release.

**Not performed:** a game launch with the released DLLs (version/commit line in
Player.log); Sphinx build (no `.rst` changed); CKAN GUI; older CKAN clients.

## Next steps

1. Watch NetKAN#11747: respond to reviewer questions. After merge, check `ckan update` +
   `ckan show kOSImproved`, then update README, packaged `Readme`, `docs/ckan.md` and the
   manual downloads page to say it is indexed.
2. Fix the four open code-review findings above (bump `kOS.props` to 1.6.0.106 with a new
   `(Unreleased)` changelog entry when that work starts).
3. Remaining implementation and manual coverage: roadmap and testing guide.
