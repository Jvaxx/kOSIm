# KOS Improved — session handoff

## Resume here

**Phase 1 (correctness and safety) is code-complete and was checked in game on 2026-10-19** (Proton pass; results in `docs/testing-and-build.md`, "Proton pass 2026-10-19"; status in `docs/KOSIm-roadmap.md`, Phase 1 "Exit review"). The pass found and fixed two defects (committed 2026-10-19, one commit each):

- **GAMEEVENTS-01** — `src/kOS/Screen/KOSInputOwnership.cs`, `KOSMouseBlocking.cs`: static handlers added to KSP `GameEvents` throw in KSP's `EvtDelegate` constructor; the first terminal's `TermWindow` broke. Now instance methods on a `GameEventHooks` object. Verified in game (no game-assembly headless coverage).
- **EXEC-02** — `src/kOS.Safe/Execution/YiedFinishedThreadedDetector.cs`, `CPU.cs` (`IsYielding`): a failed `RUNPATH` compile reported an index error instead of its own (detector re-entered through `BreakExecution` → `Cancel()`; stale-index removal). Regression strengthened: `CompileErrorTest.FailedRunpathCompileStillReportsOnce` (fails with either half reverted). Verified in game.

**Next actions, in order:**

1. **IO-04 link deletes under Wine (user decision 2026-10-19: deleting a link must delete the link itself):** under Wine, deleting an archive *file link* deleted its **target** (upstream `File.Delete`; link left dangling), and deleting a *directory link* failed with "access denied" (nothing lost). Wine reports Unix symlinks as reparse points, so `LinkSafeDelete.IsLink` works there. In progress.
2. Install **ModuleManager** and **ClickThroughBlocker** in the dev instance (user-approved 2026-10-19), then the name-tag checks (in-flight lock, #2759 in the VAB) and the CTB checks (#2659; decide INPUT-01 step 2). Minor unperformed checks are listed at the end of the pass section.
3. Then Phase 1 exit, and Phase 2 preparation (roadmap: document screen/viewport coordinates, resize authority, input/thread ownership and lifecycle contracts before structural extraction).

## Workspace state (2026-10-19)

- Branch `kosim/test-baseline` (check `git log`; the two 2026-10-19 fix commits follow `df2c5f0e`); local only, never pushed (origin is upstream <https://github.com/KSP-KOS/KOS>). Base: upstream `develop` at `8f281a45`.
- Last results: `bash .ci/test.sh` exit 0, **992/992**, 0 known failures, 0 skipped (`minimum_tests` 992 unchanged); `dotnet build kOS.sln -c Release --no-incremental` 0 errors, 44 existing warnings.
- **Deployed in the dev instance: the build of the two fix commits** (`kOS.dll c54a276a…`, `kOS.Safe.dll 3f97f5bb…`). The game may still be running; check before redeploying.

## Constraints and decisions (still binding)

- Runtime target: Windows Steam **KSP 1.12.5.3190** under **GE-Proton11-7** via UMU 1.4.4, in the dev copy only. Other platforms outside validation scope; avoid gratuitous breakage. Runtime stays `net48`; SDK 8 is tooling only.
- Protect the vanilla install (`~/.local/share/Steam/steamapps/common/Kerbal Space Program/`), Steam settings and the original prefix. Never terminate the game without asking; deploy only with the game closed.
- Local tests only; upstream workflows stay inactive under `.github/upstream-workflows/`. No push, no UI redesign, no compiler/VM rewrite.
- Preserve character-grid semantics, KSM/save/craft compatibility, public mod APIs, safe telnet defaults, predictable flight-input ownership. Keep `docs/improvements-overview.md` unchanged.
- User decisions: telnet limits are code constants (backlogs 262,144; 4,096 keys and 4 ms per client per update; type-ahead 4,096; 2,048 prompt keys; 100 ms sequence timeout); unrecognized key sequences stay typed as text; no SS3 decoding; prompt Ctrl-C cancels a running compile and waits for its thread; JSON format unchanged; INPUT-01 hover lock `kOSMouseOver` with no mod dependency (CTB integration only if the CTB check shows a conflict); IO-04: links followed to read/write/list, a delete never enters a link, images ≤ 16 MiB and ≤ 4096 px a side; `SWITCH`/`RENAME` attach check dropped; dev `settings.cfg` resolution: leave alone.
- Known edge case, not pursued: telnet two-client **1×1 NAWS runaway** (T4, memory grows by GBs). Upstream issue #3168 (`POPUPMENU:VALUE` from a string fails with an invalid cast) not addressed.

## Implemented state

Details, intentional behavior changes and limits per item: `docs/testing-and-build.md` (one section each). In game = Proton-checked.

| Item | Entry points | In game |
| --- | --- | --- |
| Local gate | `.ci/test.sh`, `.ci/known-test-failures.json` (empty; minimum 992) | — |
| BUFFER-01/02/03/04 | `ScreenBuffer`, `ScreenSnapShotExtensions`, `RenderFaultRecovery`, `TermWindow` | 2026-10-12/15 |
| EDIT-01/02 | `EditorDocument`, `KOSTextEditPopup`, `DelegateDialog` | 2026-10-15 |
| IO-01/02/03 | raw I/O (`READRAW`/`WRITERAW`), `ArchiveFileWriter`; policy `docs/file-io-compatibility.md` | 2026-10-15 |
| NET-01/02, review NET-03, NET-03 types | `kOS.Safe/UserIO` telnet parser/worker/queues/decoder, `TelnetSingletonServer` | 2026-10-12 |
| FMT-01, EXEC-01, PROMPT-01 | `SafeSerializationMgr.DumpValue`, `CPU.IsYielding`, `TextEditor.KeepCursorInBounds` | 2026-10-12/15 |
| Prompt Ctrl-C (decision 4) | `CPU.CancelAllYields`, `YiedFinishedThreadedDetector.Cancel`, `Interpreter` | 2026-10-19 |
| INPUT-01 keyboard | `InputOwnership` (kOS.Safe), `KOSInputOwnership`, `KOSManagedWindow` | 2026-10-19 (no name tag) |
| INPUT-01 mouse step 1 | `MouseBlocking` (kOS.Safe), `KOSMouseBlocking`, driver | 2026-10-19 (no CTB, no name tag) |
| PANEL-02, WIDGET-01, WIDGET-02 | `ConfigIntInput`, `OptionSearch`, `GuiCause`/`DelayedGuiActions` | 2026-10-19 |
| IO-05, PATH-01, MOVE-01 | `VolumeArguments`, `VolumeManager` endpoint overloads, `PathValue.OwnVolume`, `CheckNotMovedOntoItself` | 2026-10-19 |
| IO-04 hardening | `DirectoryContainment`, `ArchiveTextureFiles`, `LinkSafeDelete` | 2026-10-19 (link deletes: finding) |
| GAMEEVENTS-01, EXEC-02 | see "Resume here" | 2026-10-19 |

Test fixtures to keep in step with game code: `FixtureInterpreter` (game `Interpreter`), `FixtureTermWindow` (`TermWindow` input routing), `TelnetServerInputModel` (`TelnetSingletonServer` input sink).

## Commands

```bash
export PATH="$HOME/.cache/kosim-tools/dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
bash .ci/test.sh                                   # headless gate (Mono 6.12; PYTHON=python if needed)
dotnet build kOS.sln -c Release --no-incremental  # full build against the dev game's assemblies (kOS.props.user)
```

The gate builds `KOSHeadless`; run the full build last before deploying, and check the hashes in `Resources/GameData/kOS/Plugins/`.

## Dev instance and in-game testing

- Instance `/home/jvz/Games/KSP-kOSIM-dev/` (setup notes in its `setup-notes.md`); launch with `launch-dev.sh` (`--check` for preflight). Live Unity log: `.proton-prefix/pfx/drive_c/users/steamuser/AppData/LocalLow/Squad/Kerbal Space Program/Player.log` (KSP.log is buffered). Save `kOSIM-dev`, vessel `test-vessel` on the pad: **two kOS CPUs** since 2026-10-19 (the second added to `persistent.sfs`; backup `persistent.sfs.before-2cpu-20261019`), boot file `/boot/kosim_boot.ks` on the first.
- GameData: stock + kOS only (no ModuleManager). kOS `config.xml` (`GameData/kOS/Plugins/PluginData/kOS/`): telnet **enabled**, 127.0.0.1:5410, permanent listen permission.
- Deploy: game closed (`pgrep -af 'KSP_x64|wineserver|umu'`) → `cp -a GameData/kOS kOS-GameData-backups/kOS-before-<commit>-<date>` → `rsync -a Resources/GameData/kOS/ …/GameData/kOS/` (no `--delete`) → verify hashes. Backups so far: `kOS-before-e943ecf4-20261012-194247`, `kOS-before-df2c5f0e-20261019-132530`.
- Remote testing method (2026-10-19): scripted telnet client with a VT screen model (scratch `kt.py`; recreate: answer `DO TTYPE`/`SB TTYPE SEND` with `XTERM`, `DO NAWS` with a size, acknowledge each `WILL` once; Enter = CR, Ctrl-C = 0x03, Ctrl-D detaches; match output lines anchored, not the echoed command). Screenshots with `grim -g "2400,270 1920x1080"` (the game window on HDMI-A-2; check `hyprctl clients`), reading KSP's Alt+F12 *Input Locks* window for lock state. Compositor pointer warps do not reach the game, so mouse checks need the user. In test scripts avoid names of built-ins (`v`, `q`, …: `CLOBBERBUILTINS`).
- Disposable archive content: `Ships/Script/kosim-check/` (`p2/` holds the 2026-10-19 scripts and fixtures, incl. symlinks into `kosim-link-target/` beside the game), `Ships/Script/boot/kosim_boot.ks`, `Ships/kosim-outside.png`.
- Logs kept in `dev-logs/` (`*.checkpoint-*`, `*.gameevents-nre-20261019`, `*.pass-20261019-run2`).
- Recorded minor findings (not pursued): telnet full repaint sends empty cells as NUL; GUI controls overlap at 20 columns; welcome header `INITIAL_UNSET` for an 80×24 client; editor saves archive text with CRLF under Proton (by design); kOS answers every `DO ECHO`/`DO SGA` with `WILL` (non-RFC 1143 clients loop).
