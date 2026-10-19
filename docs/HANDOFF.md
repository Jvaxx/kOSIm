# KOS Improved — session handoff

## Resume here

**Phase 2 (structural boundaries) is complete: exit criterion met on 2026-10-19** after the end-of-Phase-2 Proton pass (results: `docs/testing-and-build.md`, "Proton pass 2026-10-19 (end of Phase 2)"; status: `docs/KOSIm-roadmap.md`, Phase 2 "Exit review"). Contracts, findings and decisions D1–D4: `docs/phase2-contracts.md`. Per step (details in `docs/testing-and-build.md`):

- **P2-1** input router (`TerminalInputRouter`, `ITerminalInputHost`).
- **P2-2** `TerminalSession<TViewer>` owns telnet viewers, snapshot, refresh/repaint, resize (D3 last wins), title, clear; **VIEW-03** fixed (full repaint takes a fresh snapshot).
- **P2-3** lifecycle: sessions disposable; telnet clients back to the menu when their CPU goes (LIFE-02); kOS textures loaded once per path, owned skins/textures destroyed (LIFE-01; in game the counts stay flat across reloads); event handles closed, overlay notifiers removed (LIFE-03).
- **P2-4** screen/view: PageUp/PageDown move only a shared view (D1); output and input go to the screen (VIEW-01 fixed); scrollback bounded at 2,000 lines (D2); out-of-view cursor hidden in the GUI, at the edge for telnet.
- **VIEW-04** (found in P2-4, seen by the user in game, fixed in the pass): a shorter screen keeps its bottom (the cursor's row), the top rows go into the scrollback, rows below the cursor are dropped (they no longer reappear on growing).

Phase 1 (correctness and safety) was completed earlier the same day ("Proton pass 2026-10-19 (Phase 1 fixes)").

**Phase 3 items 1–2 (P3-1 history, P3-2 paste core, P3-3 GUI paste, P3-4 copy) are done and Proton-checked (2026-10-19)**: plan in `docs/KOSIm-roadmap.md`, Phase 3 "Plan for items 1–2"; per-step details in `docs/testing-and-build.md` ("Command history", "Paste core", "GUI paste", "GUI copy"); pass record "Proton pass 2026-10-19 (Phase 3, P3-1 to P3-4)". The Wine clipboard bridges both ways (Wayland ↔ game, non-ASCII kept). Found in the pass: **COPY-01** (Ctrl+Shift+E copied the chars 26/27 a verbose syntax error uses as token marks) fixed, headless-tested and rechecked in game; **PASTE-UI-01** (the "Paste" box is translucent, its hint tiny) left as is (user decision); **VIEW-05** (telnet, not P3) open.

**Next actions, in order:**

1. **VIEW-05:** when text is printed while a wrapped input line is being typed (paste refusal, a `WHEN … THEN print` trigger), the telnet client is not sent the moved input rows (blank/stale until Ctrl-L); the GUI is right. Suspect `ScreenSnapShot.DiffFrom`'s `LastChangeTick` skip (upstream logic) with rows moved by `ScreenBuffer.GetBuffer` (changed in P2-4). Reproduce headlessly (real screen + `TerminalSession` diff + a VT model), then check whether upstream `8f281a45` behaves the same before fixing.
2. The rest of Phase 3 (reverse history search, optional persistence, items 3–5), to plan with the user.
3. Minor, optional: POWER-01 (a CPU powering itself off with `DOEVENT("Toggle Power")` logs `KOSBadJumpException`; likely upstream, compare with an upstream build first); the GUI's "N telnet clients attached" label overlaps "Keyclicker"; checks not performed: vessel destruction/unload with telnet attached, Phase 1 pass leftovers (CTB focus modes, flight-results dialog).

## Workspace state (2026-10-19)

- Branch `kosim/test-baseline` (check `git log`; the 2026-10-19 commits follow `df2c5f0e`: three fixes, Phase 2 docs, P2-1, P2-2, VIEW-03, P2-3, P2-4, VIEW-04 fix, pass record); local only, never pushed (origin is upstream <https://github.com/KSP-KOS/KOS>). Base: upstream `develop` at `8f281a45`.
- The P3 pass commit follows `1541e353`: COPY-01 fix (`src/kOS.Safe/UserIO/TerminalCopy.cs` `WithoutControlCharacters`, `PrintedErrorRecorder.End`), tests (`src/kOS.Safe.Test/UserIO/TerminalCopyTest.cs`, 2 new), `.ci/known-test-failures.json` (`minimum_tests` 1109), docs (`docs/testing-and-build.md`, `docs/KOSIm-roadmap.md`, this file).
- Last results (COPY-01): `bash .ci/test.sh` exit 0, **1109/1109**, 0 known failures, 0 skipped (`minimum_tests` 1109); `dotnet build kOS.sln -c Release --no-incremental` 0 errors, 44 existing warnings; `kOS.dll d719dbdf…`, `kOS.Safe.dll 6d5ec9fe…`. The build is deterministic (same hashes on rebuild); `1541e353` builds to `kOS.dll 4370aa74…`, `kOS.Safe.dll 356da3b7…` (the `6f6f022a…`/`ce9032c1…` recorded earlier for P3-4 do not match it). Running one test fixture alone with `-run=` fails in `ScriptHost.Boot` for fixtures outside the `Execution` namespace (its `[SetUpFixture]` does not run); use the full gate.
- **Deployed in the dev instance and checked in game: the COPY-01 build** (`kOS.dll d719dbdf…`, `kOS.Safe.dll 6d5ec9fe…`). Game closed by the user at the end of the session.

## Constraints and decisions (still binding)

- Runtime target: Windows Steam **KSP 1.12.5.3190** under **GE-Proton11-7** via UMU 1.4.4, in the dev copy only. Other platforms outside validation scope; avoid gratuitous breakage. Runtime stays `net48`; SDK 8 is tooling only.
- Protect the vanilla install (`~/.local/share/Steam/steamapps/common/Kerbal Space Program/`), Steam settings and the original prefix. Never terminate the game without asking; deploy only with the game closed.
- Local tests only; upstream workflows stay inactive under `.github/upstream-workflows/`. No push, no UI redesign, no compiler/VM rewrite.
- Preserve character-grid semantics, KSM/save/craft compatibility, public mod APIs, safe telnet defaults, predictable flight-input ownership. Keep `docs/improvements-overview.md` unchanged.
- User decisions: telnet limits are code constants (backlogs 262,144; 4,096 keys and 4 ms per client per update; type-ahead 4,096; 2,048 prompt keys; 100 ms sequence timeout); unrecognized key sequences stay typed as text; no SS3 decoding; prompt Ctrl-C cancels a running compile and waits for its thread; JSON format unchanged; INPUT-01 hover lock `kOSMouseOver` with no mod dependency (CTB check passed 2026-10-19: no integration); IO-04: links followed to read/write/list, a delete never enters a link and removes a link itself, never its target (under Wine: refused), images ≤ 16 MiB and ≤ 4096 px a side; `SWITCH`/`RENAME` attach check dropped; dev `settings.cfg` resolution: leave alone.
- LIFE-04 (a disposed script GUI keeps its window, texture and skin until power-off or a scene change): left as is (user decision 2026-10-19).
- Known edge case, not pursued: telnet two-client **1×1 NAWS runaway** (T4, memory grows by GBs). Upstream issue #3168 (`POPUPMENU:VALUE` from a string fails with an invalid cast) not addressed.

## Implemented state

Details, intentional behavior changes and limits per item: `docs/testing-and-build.md` (one section each). In game = Proton-checked.

| Item | Entry points | In game |
| --- | --- | --- |
| Local gate | `.ci/test.sh`, `.ci/known-test-failures.json` (empty; minimum 1107) | — |
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
| P2-2 terminal session (Phase 2) | `TerminalSession`, `ITerminalViewer` (`TelnetSingletonServer`), `TermWindow` forwarders | 2026-10-19 |
| VIEW-03 (found in P2-2) | `TerminalSession.RepaintFull` | 2026-10-19 |
| P2-3 lifecycle (Phase 2) | `TerminalSession.Dispose`, `TelnetSingletonServer` (window by reference, `isAttached`), `TermWindow.OnDestroy`, `Utils.GetTextureWithErrorMsg` cache, `Utils.DestroyCreated` | 2026-10-19 |
| P3-1 history (Phase 3) | `CommandHistory` (kOS.Safe), `Interpreter`/`FixtureInterpreter` history keys and line lookup, `TextEditor.ReplaceLine` | 2026-10-19 |
| P3-2 paste core (Phase 3) | `TerminalPaste`, `TerminalInputRouter.QueuePaste` and wait-behind-type-ahead in `Type`/`SpecialKey`, `FixtureTermWindow.QueuePaste` | 2026-10-19 |
| P3-3 GUI paste (Phase 3) | `TerminalGuiPaste` (kOS.Safe), `TermWindow` keys (`ProcessKeyWhileConfirmingPaste`), `DrawPasteConfirmation`, `FixtureTermWindow.GuiPaste` | 2026-10-19 |
| P3-4 GUI copy (Phase 3) | `TerminalCopy`, `PrintedErrorRecorder` (kOS.Safe), `Logger.LastError`/`KSPLogger.Log`, `TermWindow.CopyVisibleRows`/`CopyLastError`/copy notice | 2026-10-19 |
| COPY-01 (found in the P3 pass) | `TerminalCopy.WithoutControlCharacters`, `PrintedErrorRecorder.End` | 2026-10-19 |
| P2-4 screen/view (Phase 2) | `ScreenBuffer` (view offset, `MAX_SCROLLBACK`, `ScrollScreen`, `ReturnViewToScreen`, `SetSize` keeps the bottom: VIEW-04), `TextEditor.ScreenCursorRow`, `ScreenSnapShot.TeleportRow` | 2026-10-19 |

Test fixtures to keep in step with game code: `FixtureInterpreter` (game `Interpreter`), `FixtureTermWindow` (`TermWindow`'s `ITerminalInputHost` members, session calls and update order; the routing and session themselves are shared), `TelnetServerInputModel` (`TelnetSingletonServer` input sink and `ITerminalViewer`); `FixtureInterpreter` calls the shared `CommandHistory` as the game `Interpreter` does (keep the calls in step). `PreSessionTermWindowFanOut` is a frozen copy of the pre-P2-2 fan-out (oracle), not to be kept in step.

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
- Deploy: game closed (`pgrep -af 'KSP_x64|wineserver|umu'`) → `cp -a GameData/kOS kOS-GameData-backups/kOS-before-<commit>-<date>` → `rsync -a Resources/GameData/kOS/ …/GameData/kOS/` (no `--delete`) → verify hashes. Backups so far: `kOS-before-e943ecf4-20261012-194247`, `kOS-before-df2c5f0e-20261019-132530`, `kOS-before-linkdelete-20261019-141034`, `kOS-before-wine-filelink-20261019-143125`, `kOS-before-p2-1-20261019-145401`, `kOS-before-p2-2-20261019-152122` (holds the P2-1 build), `kOS-before-p2-4-20261019` (holds the P2-2 build), `kOS-before-view04-20261019` (holds `c8582b78`), `kOS-before-p3-20261019` (holds the P2-4 + VIEW-04 build), `kOS-before-copy01-20261019` (holds the `1541e353` build).
- Remote testing method (2026-10-19): scripted telnet client with a VT screen model (scratch `kt.py`; recreate: answer `DO TTYPE`/`SB TTYPE SEND` with `XTERM`, `DO NAWS` with a size, acknowledge each `WILL` once; Enter = CR, Ctrl-C = 0x03, Ctrl-D detaches, PageUp/PageDown = `ESC[5~`/`ESC[6~`, Ctrl-L repaints, a NAWS subnegotiation resizes; the title arrives as OSC 0/2; match output lines anchored, not the echoed command). Screenshots with `grim -g "2400,270 1920x1080"` (the game window on HDMI-A-2; check `hyprctl clients`), reading KSP's Alt+F12 *Input Locks* window for lock state. Compositor pointer warps do not reach the game, so mouse checks need the user. **GUI keys can be sent remotely** (since the P3 pass): Hyprland 0.56 Lua dispatcher `hyprctl dispatch 'hl.dsp.send_key_state({ mods = "CTRL", key = "v", state = "down" })'` then `"up"`, modifiers also held as keys (`Control_L`, `Shift_L`), to the focused game window; keysym names of the user's French Mac (AZERTY) layout, so digits and `period` are not available (type text over telnet). `wtype` does not work (XWayland ignores its keymap). Clipboard: `wl-copy`/`wl-paste` (save and restore the user's clipboard). Tools kept in the dev instance `dev-tools/`: `kt.py` (step client), `kd.py` (persistent client: `python3 kd.py NAME CPU [W H]` → `NAME.screen`, `NAME.log`, commands via FIFO `NAME.in`: `line …`, `send …` with escapes, `naws W H`, `quit`), `hk.sh "MODS" KEY…`. Use the GUI's size (50×36) for a telnet client on the GUI's CPU (last resize wins). A terminal opened by a script event, or after a power cycle, needs a user click before it has the keyboard. Don't `pkill -f` a pattern that the running shell command also contains. Helper scripts: `Ships/Script/kosim-check/p3/` (`fmon.ks` frame monitor, `other.ks` events to the other CPU, `rterr.ks`). In test scripts avoid names of built-ins (`v`, `q`, …: `CLOBBERBUILTINS`).
- Disposable archive content: `Ships/Script/kosim-check/` (`p24/` holds the end-of-Phase-2 scripts) (`p2/` holds the 2026-10-19 scripts and fixtures, incl. symlinks `flink.txt`, `mvlink.txt`, `lnk`, `holder/…` into `kosim-link-target/` beside the game; the refusal checks keep them), `Ships/Script/boot/kosim_boot.ks`, `Ships/kosim-outside.png`.
- Logs kept in `dev-logs/` (`*.checkpoint-*`, `*.gameevents-nre-20261019`, `*.pass-20261019-run*`, `Player.log.p24-pass-20261019-run{1,2}`, `Player.log.p3-pass-20261019-run1`). Wine behavior can be probed without the game: build a small `net48` exe and run it with GE-Proton's `files/bin/wine` and a throwaway `WINEPREFIX` in the scratchpad (never the game's prefix).
- Recorded minor findings (not pursued): PASTE-UI-01 (translucent "Paste" box, tiny hint; user: leave); AltGr+V on the French Mac layout types `?` under Wine (keysym `diamond`, Wine mapping); after a narrowing, the prompt's empty input line covers rows below the prompt with blanks until a key is typed (upstream; see "GUI copy"); telnet full repaint sends empty cells as NUL; GUI controls overlap at 20 columns; welcome header `INITIAL_UNSET` for an 80×24 client; editor saves archive text with CRLF under Proton (by design); kOS answers every `DO ECHO`/`DO SGA` with `WILL` (non-RFC 1143 clients loop).
