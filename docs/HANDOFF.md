# KOS Improved — session handoff

## Resume here

**Phase 1 (correctness and safety) is complete: exit criterion met on 2026-10-19** after the Proton pass (results in `docs/testing-and-build.md`, "Proton pass 2026-10-19"; status in `docs/KOSIm-roadmap.md`, Phase 1 "Exit review"). The pass found and fixed three defects (one commit each):

- **GAMEEVENTS-01** — `src/kOS/Screen/KOSInputOwnership.cs`, `KOSMouseBlocking.cs`: static handlers added to KSP `GameEvents` throw in KSP's `EvtDelegate` constructor; the first terminal's `TermWindow` broke. Now instance methods on a `GameEventHooks` object. Verified in game (no game-assembly headless coverage).
- **EXEC-02** — `src/kOS.Safe/Execution/YiedFinishedThreadedDetector.cs`, `CPU.cs` (`IsYielding`): a failed `RUNPATH` compile reported an index error instead of its own. Regression: `CompileErrorTest.FailedRunpathCompileStillReportsOnce`. Verified in game.
- **IO-04 link deletes under Wine** — `src/kOS.Safe/Persistence/LinkSafeDelete.cs`, `Archive.cs` (`CheckDeletable`), `VolumeManager.Move`: under Wine, deleting a link deletes its target (file links) or fails (directory links), and Wine shows a file link as its target. The user decided that a delete must remove the link and never its target. That can't be done under Wine, so there a delete or move of an entry that is or contains a link is **refused** before anything is deleted or copied. File links are detected through `wine_get_unix_file_name`. Windows and Mono behavior is unchanged. Headless tests simulate Wine (4 tests); the real Wine branch was checked with a scratch probe under GE-Proton11-7's Wine and in game.

**Next actions, in order:**

1. **Phase 2 preparation done (2026-10-19, documentation only): `docs/phase2-contracts.md`.** It holds the current terminal map, new findings (VIEW-01: PageUp then a command overwrites scrollback, reproduced headlessly; VIEW-02 unbounded scrollback; INPUT-02 GUI Ctrl-D/X send Enter, kept by decision; LIFE-01/02/03 cleanup gaps), proposed contracts and the extraction order P2-1…P2-4. User decisions D1–D4 are recorded there (shared view offset; scrollback 2,000 lines; last resize wins; GUI Ctrl-D/X keep sending Enter). Next: implement **P2-1** (move `TermWindow` input routing into a `kOS.Safe` `TerminalInputRouter`, replacing the duplicated `FixtureTermWindow`; no behavior change).
2. Optional minor checks from the pass's "Not performed" list (e.g. CTB focus modes, flight-results dialog, vessel destruction, two-client telnet).

## Workspace state (2026-10-19)

- Branch `kosim/test-baseline` (check `git log`; the three 2026-10-19 fix commits follow `df2c5f0e`); local only, never pushed (origin is upstream <https://github.com/KSP-KOS/KOS>). Base: upstream `develop` at `8f281a45`.
- Last results: `bash .ci/test.sh` exit 0, **996/996**, 0 known failures, 0 skipped (`minimum_tests` 996); `dotnet build kOS.sln -c Release --no-incremental` 0 errors, 44 existing warnings.
- **Deployed in the dev instance: the build of the link-delete commit** (`kOS.dll b61668f1…`, `kOS.Safe.dll f674a24d…`). The game may still be running; check before redeploying.

## Constraints and decisions (still binding)

- Runtime target: Windows Steam **KSP 1.12.5.3190** under **GE-Proton11-7** via UMU 1.4.4, in the dev copy only. Other platforms outside validation scope; avoid gratuitous breakage. Runtime stays `net48`; SDK 8 is tooling only.
- Protect the vanilla install (`~/.local/share/Steam/steamapps/common/Kerbal Space Program/`), Steam settings and the original prefix. Never terminate the game without asking; deploy only with the game closed.
- Local tests only; upstream workflows stay inactive under `.github/upstream-workflows/`. No push, no UI redesign, no compiler/VM rewrite.
- Preserve character-grid semantics, KSM/save/craft compatibility, public mod APIs, safe telnet defaults, predictable flight-input ownership. Keep `docs/improvements-overview.md` unchanged.
- User decisions: telnet limits are code constants (backlogs 262,144; 4,096 keys and 4 ms per client per update; type-ahead 4,096; 2,048 prompt keys; 100 ms sequence timeout); unrecognized key sequences stay typed as text; no SS3 decoding; prompt Ctrl-C cancels a running compile and waits for its thread; JSON format unchanged; INPUT-01 hover lock `kOSMouseOver` with no mod dependency (CTB check passed 2026-10-19: no integration); IO-04: links followed to read/write/list, a delete never enters a link and removes a link itself, never its target (under Wine: refused), images ≤ 16 MiB and ≤ 4096 px a side; `SWITCH`/`RENAME` attach check dropped; dev `settings.cfg` resolution: leave alone.
- Known edge case, not pursued: telnet two-client **1×1 NAWS runaway** (T4, memory grows by GBs). Upstream issue #3168 (`POPUPMENU:VALUE` from a string fails with an invalid cast) not addressed.

## Implemented state

Details, intentional behavior changes and limits per item: `docs/testing-and-build.md` (one section each). In game = Proton-checked.

| Item | Entry points | In game |
| --- | --- | --- |
| Local gate | `.ci/test.sh`, `.ci/known-test-failures.json` (empty; minimum 996) | — |
| BUFFER-01/02/03/04 | `ScreenBuffer`, `ScreenSnapShotExtensions`, `RenderFaultRecovery`, `TermWindow` | 2026-10-12/15 |
| EDIT-01/02 | `EditorDocument`, `KOSTextEditPopup`, `DelegateDialog` | 2026-10-15 |
| IO-01/02/03 | raw I/O (`READRAW`/`WRITERAW`), `ArchiveFileWriter`; policy `docs/file-io-compatibility.md` | 2026-10-15 |
| NET-01/02, review NET-03, NET-03 types | `kOS.Safe/UserIO` telnet parser/worker/queues/decoder, `TelnetSingletonServer` | 2026-10-12 |
| FMT-01, EXEC-01, PROMPT-01 | `SafeSerializationMgr.DumpValue`, `CPU.IsYielding`, `TextEditor.KeepCursorInBounds` | 2026-10-12/15 |
| Prompt Ctrl-C (decision 4) | `CPU.CancelAllYields`, `YiedFinishedThreadedDetector.Cancel`, `Interpreter` | 2026-10-19 |
| INPUT-01 keyboard | `InputOwnership` (kOS.Safe), `KOSInputOwnership`, `KOSManagedWindow` | 2026-10-19 |
| INPUT-01 mouse step 1 | `MouseBlocking` (kOS.Safe), `KOSMouseBlocking`, driver | 2026-10-19 (with and without CTB) |
| PANEL-02, WIDGET-01, WIDGET-02 | `ConfigIntInput`, `OptionSearch`, `GuiCause`/`DelayedGuiActions` | 2026-10-19 |
| IO-05, PATH-01, MOVE-01 | `VolumeArguments`, `VolumeManager` endpoint overloads, `PathValue.OwnVolume`, `CheckNotMovedOntoItself` | 2026-10-19 |
| IO-04 hardening | `DirectoryContainment`, `ArchiveTextureFiles`, `LinkSafeDelete`, `Archive.CheckDeletable` | 2026-10-19 |
| GAMEEVENTS-01, EXEC-02, Wine link refusal | see "Resume here" | 2026-10-19 |

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
- GameData: stock, kOS, ModuleManager 4.2.3, ClickThroughBlocker 2.1.10.23, ToolbarController 0.1.9.14, HarmonyKSP 2.2.1 (installed 2026-10-19, dev instance only). kOS `config.xml` (`GameData/kOS/Plugins/PluginData/kOS/`): telnet **enabled**, 127.0.0.1:5410, permanent listen permission.
- Deploy: game closed (`pgrep -af 'KSP_x64|wineserver|umu'`) → `cp -a GameData/kOS kOS-GameData-backups/kOS-before-<commit>-<date>` → `rsync -a Resources/GameData/kOS/ …/GameData/kOS/` (no `--delete`) → verify hashes. Backups so far: `kOS-before-e943ecf4-20261012-194247`, `kOS-before-df2c5f0e-20261019-132530`, `kOS-before-linkdelete-20261019-141034`, `kOS-before-wine-filelink-20261019-143125`.
- Remote testing method (2026-10-19): scripted telnet client with a VT screen model (scratch `kt.py`; recreate: answer `DO TTYPE`/`SB TTYPE SEND` with `XTERM`, `DO NAWS` with a size, acknowledge each `WILL` once; Enter = CR, Ctrl-C = 0x03, Ctrl-D detaches; match output lines anchored, not the echoed command). Screenshots with `grim -g "2400,270 1920x1080"` (the game window on HDMI-A-2; check `hyprctl clients`), reading KSP's Alt+F12 *Input Locks* window for lock state. Compositor pointer warps do not reach the game, so mouse checks need the user. In test scripts avoid names of built-ins (`v`, `q`, …: `CLOBBERBUILTINS`).
- Disposable archive content: `Ships/Script/kosim-check/` (`p2/` holds the 2026-10-19 scripts and fixtures, incl. symlinks `flink.txt`, `mvlink.txt`, `lnk`, `holder/…` into `kosim-link-target/` beside the game; the refusal checks keep them), `Ships/Script/boot/kosim_boot.ks`, `Ships/kosim-outside.png`.
- Logs kept in `dev-logs/` (`*.checkpoint-*`, `*.gameevents-nre-20261019`, `*.pass-20261019-run*`). Wine behavior can be probed without the game: build a small `net48` exe and run it with GE-Proton's `files/bin/wine` and a throwaway `WINEPREFIX` in the scratchpad (never the game's prefix).
- Recorded minor findings (not pursued): telnet full repaint sends empty cells as NUL; GUI controls overlap at 20 columns; welcome header `INITIAL_UNSET` for an 80×24 client; editor saves archive text with CRLF under Proton (by design); kOS answers every `DO ECHO`/`DO SGA` with `WILL` (non-RFC 1143 clients loop).
