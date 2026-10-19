# KOS Improved — session handoff

## Resume here

**Phase 1 (correctness and safety) is complete: exit criterion met on 2026-10-19** after the Proton pass (results in `docs/testing-and-build.md`, "Proton pass 2026-10-19"; status in `docs/KOSIm-roadmap.md`, Phase 1 "Exit review"). The pass found and fixed three defects (one commit each):

- **GAMEEVENTS-01** — `src/kOS/Screen/KOSInputOwnership.cs`, `KOSMouseBlocking.cs`: static handlers added to KSP `GameEvents` throw in KSP's `EvtDelegate` constructor; the first terminal's `TermWindow` broke. Now instance methods on a `GameEventHooks` object. Verified in game (no game-assembly headless coverage).
- **EXEC-02** — `src/kOS.Safe/Execution/YiedFinishedThreadedDetector.cs`, `CPU.cs` (`IsYielding`): a failed `RUNPATH` compile reported an index error instead of its own. Regression: `CompileErrorTest.FailedRunpathCompileStillReportsOnce`. Verified in game.
- **IO-04 link deletes under Wine** — `src/kOS.Safe/Persistence/LinkSafeDelete.cs`, `Archive.cs` (`CheckDeletable`), `VolumeManager.Move`: under Wine, deleting a link deletes its target (file links) or fails (directory links), and Wine shows a file link as its target. The user decided that a delete must remove the link and never its target. That can't be done under Wine, so there a delete or move of an entry that is or contains a link is **refused** before anything is deleted or copied. File links are detected through `wine_get_unix_file_name`. Windows and Mono behavior is unchanged. Headless tests simulate Wine (4 tests); the real Wine branch was checked with a scratch probe under GE-Proton11-7's Wine and in game.

**Phase 2 (structural boundaries): all four steps implemented and headless-tested (2026-10-19); the end-of-Phase-2 Proton pass is next** (user decision: one in-game pass for P2-2 onward). Contracts, findings and decisions D1–D4: `docs/phase2-contracts.md`; per-step details and tests: `docs/testing-and-build.md` sections "Terminal session", "Full telnet repaint after a resize (VIEW-03)", "Explicit lifecycle (Phase 2, P2-3)", "Screen and view (Phase 2, P2-4)"; headless exit review: roadmap, Phase 2.

- **P2-1** input router (`TerminalInputRouter`, `ITerminalInputHost`): checked in game.
- **P2-2** `TerminalSession<TViewer>` owns telnet viewers, snapshot, refresh/repaint, resize (D3 last wins), title, clear; **VIEW-03** fixed (full repaint takes a fresh snapshot).
- **P2-3** lifecycle: sessions disposable; telnet clients sent back to the menu with a message when their CPU goes (LIFE-02); kOS textures loaded once per path and owned skins/textures destroyed (LIFE-01); event handles closed, overlay notifiers removed (LIFE-03). **LIFE-04** (a disposed script GUI keeps its GameObject) recorded, not fixed.
- **P2-4** screen/view: PageUp/PageDown move only a shared view (D1); output and input go to the screen (VIEW-01 fixed); scrollback bounded at 2,000 lines (D2); out-of-view cursor hidden in the GUI, at the edge for telnet. **VIEW-04** (a shrunk screen keeps its top row; upstream) recorded, not changed.

**Next actions, in order:**

1. **End-of-Phase-2 Proton pass** with the build deployed (see Workspace state). Checklist:
   - GUI terminal unchanged otherwise: prompt, typing, history, `PRINT AT` dashboards, `CLEARSCREEN`, resize by dragging, fonts, beeps, reverse/visual-beep toggles, focus and flight-input release (P2-1 regressions).
   - **P2-2/VIEW-03 (telnet):** attach from the menu (also a client narrower than 50 columns), NAWS resize with two clients (last wins), GUI resize with a client attached (no wrapped rows), Ctrl-L repaint, Ctrl-D detach, title change (name tag), `CLEARSCREEN`, beeps.
   - **P2-3:** with a telnet client attached, (a) delete/explode the vessel's CPU part, (b) switch scene (to the Space Center): client gets "That CPU is no longer available." / is back at the menu, no exceptions in `Player.log`; reattach works. Open/close the terminal and a script `GUI` many times (and the editor, `LIST` picker): no `Player.log` errors, textures display (shared cache), memory steady. Optional: measure `Texture2D`/`GUISkin` counts before/after (needs a temporary diagnostic build).
   - **P2-4:** fill the screen, PageUp/PageDown in the GUI with a telnet client attached (both follow; GUI cursor hidden, telnet cursor at the edge); type a command while scrolled (view returns, command and output below, scrollback intact); a `PRINT AT` dashboard loop while scrolled (view returns, dashboard on the screen); print 3,000 lines then PageUp to the top (about 2,000 lines back kept, no slowdown).
   - Record KSP/Proton versions, results and unperformed checks in `docs/testing-and-build.md`; then close Phase 2 in the roadmap.
2. Decisions for the user (Phase 2 leftovers): LIFE-04 (destroy a disposed script GUI's GameObject?), VIEW-04 (keep the bottom on shrink?).
3. Phase 3 per roadmap (command history bound/draft is TERM-05 there).
4. Optional minor checks from the Phase 1 pass's "Not performed" list (CTB focus modes, flight-results dialog, vessel destruction).

## Workspace state (2026-10-19)

- Branch `kosim/test-baseline` (check `git log`; the 2026-10-19 commits follow `df2c5f0e`: three fixes, Phase 2 docs, P2-1, P2-2, VIEW-03, P2-3, P2-4); local only, never pushed (origin is upstream <https://github.com/KSP-KOS/KOS>). Base: upstream `develop` at `8f281a45`.
- Last results: `bash .ci/test.sh` exit 0, **1057/1057**, 0 known failures, 0 skipped (`minimum_tests` 1057); `dotnet build kOS.sln -c Release --no-incremental` 0 errors, 44 existing warnings; build output `kOS.dll f083d0f3…`, `kOS.Safe.dll a49d6d99…`.
- **Deployed in the dev instance: the P2-4 build** (`kOS.dll f083d0f3…`, `kOS.Safe.dll a49d6d99…`; P2-2 to P2-4), not yet run in game. Backup of the previous (P2-2) deployment: `kOS-before-p2-4-20261019`. The game may be running; check before redeploying.

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
| Local gate | `.ci/test.sh`, `.ci/known-test-failures.json` (empty; minimum 1057) | — |
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
| P2-1 input router (Phase 2) | `TerminalInputRouter`, `ITerminalInputHost`, `TermWindow` forwarders | 2026-10-19 |
| P2-2 terminal session (Phase 2) | `TerminalSession`, `ITerminalViewer` (`TelnetSingletonServer`), `TermWindow` forwarders | pending |
| VIEW-03 (found in P2-2) | `TerminalSession.RepaintFull` | pending |
| P2-3 lifecycle (Phase 2) | `TerminalSession.Dispose`, `TelnetSingletonServer` (window by reference, `isAttached`), `TermWindow.OnDestroy`, `Utils.GetTextureWithErrorMsg` cache, `Utils.DestroyCreated` | pending |
| P2-4 screen/view (Phase 2) | `ScreenBuffer` (view offset, `MAX_SCROLLBACK`, `ScrollScreen`, `ReturnViewToScreen`), `TextEditor.ScreenCursorRow`, `ScreenSnapShot.TeleportRow` | pending |

Test fixtures to keep in step with game code: `FixtureInterpreter` (game `Interpreter`), `FixtureTermWindow` (`TermWindow`'s `ITerminalInputHost` members, session calls and update order; the routing and session themselves are shared), `TelnetServerInputModel` (`TelnetSingletonServer` input sink and `ITerminalViewer`); `FixtureInterpreter` history keys copy the game `Interpreter`'s. `PreSessionTermWindowFanOut` is a frozen copy of the pre-P2-2 fan-out (oracle), not to be kept in step.

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
- Deploy: game closed (`pgrep -af 'KSP_x64|wineserver|umu'`) → `cp -a GameData/kOS kOS-GameData-backups/kOS-before-<commit>-<date>` → `rsync -a Resources/GameData/kOS/ …/GameData/kOS/` (no `--delete`) → verify hashes. Backups so far: `kOS-before-e943ecf4-20261012-194247`, `kOS-before-df2c5f0e-20261019-132530`, `kOS-before-linkdelete-20261019-141034`, `kOS-before-wine-filelink-20261019-143125`, `kOS-before-p2-1-20261019-145401`, `kOS-before-p2-2-20261019-152122` (holds the P2-1 build), `kOS-before-p2-4-20261019` (holds the P2-2 build).
- Remote testing method (2026-10-19): scripted telnet client with a VT screen model (scratch `kt.py`; recreate: answer `DO TTYPE`/`SB TTYPE SEND` with `XTERM`, `DO NAWS` with a size, acknowledge each `WILL` once; Enter = CR, Ctrl-C = 0x03, Ctrl-D detaches; match output lines anchored, not the echoed command). Screenshots with `grim -g "2400,270 1920x1080"` (the game window on HDMI-A-2; check `hyprctl clients`), reading KSP's Alt+F12 *Input Locks* window for lock state. Compositor pointer warps do not reach the game, so mouse checks need the user. In test scripts avoid names of built-ins (`v`, `q`, …: `CLOBBERBUILTINS`).
- Disposable archive content: `Ships/Script/kosim-check/` (`p2/` holds the 2026-10-19 scripts and fixtures, incl. symlinks `flink.txt`, `mvlink.txt`, `lnk`, `holder/…` into `kosim-link-target/` beside the game; the refusal checks keep them), `Ships/Script/boot/kosim_boot.ks`, `Ships/kosim-outside.png`.
- Logs kept in `dev-logs/` (`*.checkpoint-*`, `*.gameevents-nre-20261019`, `*.pass-20261019-run*`). Wine behavior can be probed without the game: build a small `net48` exe and run it with GE-Proton's `files/bin/wine` and a throwaway `WINEPREFIX` in the scratchpad (never the game's prefix).
- Recorded minor findings (not pursued): telnet full repaint sends empty cells as NUL; GUI controls overlap at 20 columns; welcome header `INITIAL_UNSET` for an 80×24 client; editor saves archive text with CRLF under Proton (by design); kOS answers every `DO ECHO`/`DO SGA` with `WILL` (non-RFC 1143 clients loop).
