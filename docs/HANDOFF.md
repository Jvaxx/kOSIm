# KOS Improved — session handoff

## Resume here

**Phases 0–3 are complete** (exit criteria met 2026-10-15, 10-19, 10-19, 10-20; status table in `docs/KOSIm-roadmap.md`). Every Phase 0–3 item is headless-tested and passed in game under Proton, except the **STATUS-01** fix (`kOS.Safe.Screen.WaitingDisplay`), headless-tested but **not deployed or checked in game**.

**Phase 4 step 1 (terminal chrome) is implemented** (2026-10-20, user approval "go for the first step"): our own header (`vessel · CX-4181 · tag`, settings and close buttons), flat frame with a green focus border, one-line status strip (state dot and text, chips by priority, copy notice, size, grip: fixes **BAR-01**), settings popover (reverse, visual beep, key click, brightness slider, character size), chrome following KSP's UI scale. Headless gate **1194/1194**, mutations caught, full build clean. Committed; **not deployed, not checked in game**: the user decided (2026-10-20) that the in-game checks of Phase 4 are done **once, at the end of Phase 4**, together with STATUS-01. What changed: `docs/testing-and-build.md`, "Terminal window (Phase 4, step 1)"; plan and as-implemented notes: `docs/KOSIm-roadmap.md`, "Step 1 plan".

The docs were trimmed on 2026-10-20 (user request): `docs/testing-and-build.md` holds the setup, test method, behavior changes by area, limits, open issues and in-game coverage; `docs/KOSIm-roadmap.md` the phase status, binding decisions and Phase 4. Per-step history is in git (both files and this one at `876e1a29`).

**Next actions, in order:**

1. **Step 2 plan** (geometry and scrolling: saved geometry per CPU next to its history, keep on screen also after a resolution or UI-scale change, edge resize with a size overlay, the scroll bar in the gutter `TerminalChromeLayout` reserves): write it in `docs/KOSIm-roadmap.md` for the user's approval; implement nothing before it. Then steps 3–5, each with its plan approved and headless tests.
2. **One Proton pass at the end of Phase 4** (user decision 2026-10-20; needs the user: main menu, loading `kOSIM-dev`, opening the terminal, all mouse steps). Deploy the last committed build with the game closed (backup `kOS-GameData-backups/kOS-before-p4-<date>`). Step 1 items, on CPU 1 at 50×36 and UI scale 1.3 (add the later steps' items when they are done):
   - Player.log has `kOS: chrome font …` (which of `…`/`·` the font has) and no exception; screenshot the window.
   - Header title shortening, settings/close buttons; frame green with the keyboard, grey after a click outside.
   - Popover: each control (reverse, visual beep, key click, brightness, char size −/+ and the window following); closes with its button, Esc, a press outside, closing the terminal; typing still reaches the terminal after clicking a popover control (`hk.sh`); Alt+F12: `kOSTerminal` held, `kOSMouseOver` over the popover.
   - Strip: `kd.py` client attached → `1 telnet client` chip; `p3r/fill.ks`, PageUp, then output → `New output` chip, a click brings the view back; Ctrl+Shift+C → notice beside the size; `set terminal:width to 15.` and `to 20.` → nothing overlaps (BAR-01, G3-UI).
   - Moving by header and strip only; grip resize snaps to cells and stops at 15×3; selection and wheel aligned with the characters.
   - KSP UI scale changed (settings) → chrome resizes, cells kept; another scale back.
   - STATUS-01: over telnet with `kd.py` title logging, `runpath("0:/kosim-check/p3r/slow2.ks").` logs `Running …` then `Waiting in …` once and stays until `Ready`; `main.ks` `Running` → `Waiting in` → `Ready`.
3. Minor, optional: POWER-01 (a CPU powering itself off with `DOEVENT("Toggle Power")` logs `KOSBadJumpException`; compare with an upstream build first); the checks never performed (`docs/testing-and-build.md`, "In-game coverage").

## Workspace state (2026-10-20)

- Branch `kosim/test-baseline`, local only, never pushed (origin is upstream <https://github.com/KSP-KOS/KOS>). Base: upstream `develop` at `8f281a45`.
- Last code commit: **Phase 4 step 1** (terminal chrome): new `src/kOS.Safe/Screen/TerminalChromeLayout.cs`, `StatusStrip.cs`, `src/kOS/Screen/KOSStyle.cs`, `src/kOS.Safe.Test/Screen/TerminalChromeLayoutTest.cs`; changed `TerminalStatus.cs`, `TermWindow.cs`, `KOSManagedWindow.cs`, `TerminalStatusTest.cs`, `.ci/known-test-failures.json` (`minimum_tests` 1194). Its hashes are in the following docs commit. Previous code commit: STATUS-01 `c8ec39f4` (`kOS.dll f3d7df79…`, `kOS.Safe.dll 698a6291…`).
- **Deployed in the dev instance: the `77d858f9` build** (O3; `kOS.dll 8a5f7bb8…`, `kOS.Safe.dll b2fb1bcf…`), checked in game 2026-10-20. Latest backup `kOS-GameData-backups/kOS-before-p3rest-20261020`. Game closed (not started this session); Player.log of the last pass kept as `dev-logs/Player.log.p3rest-pass-20261020`.
- The dev save has history files `saves/kOSIM-dev/kOS/history/80427209.txt` (CPU 1) and `80427999.txt` (CPU 2) and a `quicksave.sfs`. Dev `settings.cfg`: 1024×768 fullscreen, `UI_SCALE = 1.3`.

## Constraints and decisions (still binding)

- Runtime target: Windows Steam **KSP 1.12.5.3190** under **GE-Proton11-7** via UMU 1.4.4, in the dev copy only. Runtime stays `net48`; SDK 8 is tooling only.
- Protect the vanilla install, Steam settings and the original prefix. Never terminate the game without asking; deploy only with the game closed.
- Local tests only; upstream workflows stay inactive under `.github/upstream-workflows/`. No push, no UI redesign without an approved plan, no compiler/VM rewrite.
- Preserve character-grid semantics, KSM/save/craft compatibility, public mod APIs, safe telnet defaults, predictable flight-input ownership. Keep `docs/improvements-overview.md` unchanged.
- User decisions: `docs/KOSIm-roadmap.md`, "Binding decisions" (INPUT-01 policy, terminal model D1–D4, Phase 3 H1–H4/D1/D2/O1–O3, item 5, file I/O, items left as is) and "Phase 4 — Decisions" (IMGUI, chrome, strip, popover, geometry, scroll bar, themes, toolbar tabs, other windows; each step needs its plan approved). Limits are code constants: `docs/testing-and-build.md`, "Limits and constants".

## Implemented state

Behavior per area: `docs/testing-and-build.md`, "Behavior changes from upstream". In game = Proton-checked.

| Item | Entry points | In game |
| --- | --- | --- |
| Local gate | `.ci/test.sh`, `.ci/known-test-failures.json` (empty; minimum 1194) | — |
| BUFFER-01/02/03/04 | `ScreenBuffer`, `ScreenSnapShotExtensions`, `RenderFaultRecovery`, `TermWindow` | 2026-10-12/15 |
| EDIT-01/02 | `EditorDocument`, `KOSTextEditPopup`, `DelegateDialog` | 2026-10-15 |
| IO-01/02/03 | raw I/O (`READRAW`/`WRITERAW`), `ArchiveFileWriter`; policy `docs/file-io-compatibility.md` | 2026-10-15 |
| NET-01/02, review NET-03, NET-03 types | `kOS.Safe/UserIO` telnet parser/worker/queues/decoder, `TelnetSingletonServer` | 2026-10-12 |
| FMT-01, EXEC-01, PROMPT-01 | `SafeSerializationMgr.DumpValue`, `CPU.IsYielding`, `TextEditor.KeepCursorInBounds` | 2026-10-12/15 |
| Prompt Ctrl-C (decision 4), EXEC-02 | `CPU.CancelAllYields`, `YiedFinishedThreadedDetector.Cancel`, `Interpreter` | 2026-10-19 |
| INPUT-01 keyboard, GAMEEVENTS-01 | `InputOwnership` (kOS.Safe), `KOSInputOwnership`, `KOSManagedWindow` | 2026-10-19 |
| INPUT-01 mouse | `MouseBlocking` (kOS.Safe), `KOSMouseBlocking`, driver | 2026-10-19 (with and without CTB) |
| PANEL-02, WIDGET-01, WIDGET-02 | `ConfigIntInput`, `OptionSearch`, `GuiCause`/`DelayedGuiActions` | 2026-10-19 |
| IO-05, PATH-01, MOVE-01 | `VolumeArguments`, `VolumeManager` endpoint overloads, `PathValue.OwnVolume`, `CheckNotMovedOntoItself` | 2026-10-19 |
| IO-04 hardening, Wine link refusal | `DirectoryContainment`, `ArchiveTextureFiles`, `LinkSafeDelete`, `Archive.CheckDeletable` | 2026-10-19 |
| P2-1 input router | `TerminalInputRouter`, `ITerminalInputHost`, `TermWindow` forwarders | 2026-10-19 |
| P2-2 terminal session, VIEW-03 | `TerminalSession`, `ITerminalViewer` (`TelnetSingletonServer`), `TerminalSession.RepaintFull` | 2026-10-19 |
| P2-3 lifecycle | `TerminalSession.Dispose`, `TelnetSingletonServer` (window by reference, `isAttached`), `TermWindow.OnDestroy`, `Utils.GetTextureWithErrorMsg` cache, `Utils.DestroyCreated` | 2026-10-19 |
| P2-4 screen/view, VIEW-04 | `ScreenBuffer` (view offset, `MAX_SCROLLBACK`, `ScrollScreen`, `ReturnViewToScreen`, `SetSize` keeps the bottom), `TextEditor.ScreenCursorRow`, `ScreenSnapShot.TeleportRow` | 2026-10-19 |
| P3-1 history | `CommandHistory` (kOS.Safe), interpreter history keys and line lookup, `TextEditor.ReplaceLine` | 2026-10-19 |
| P3-2 paste core | `TerminalPaste`, `TerminalInputRouter.QueuePaste`, wait-behind-type-ahead in `Type`/`SpecialKey` | 2026-10-19 |
| P3-3 GUI paste | `TerminalGuiPaste` (kOS.Safe), `TermWindow.ProcessKeyWhileConfirmingPaste`, `DrawPasteConfirmation` | 2026-10-19 |
| P3-4 GUI copy, COPY-01 | `TerminalCopy`, `PrintedErrorRecorder` (kOS.Safe), `Logger.LastError`/`KSPLogger.Log`, `TermWindow` copy | 2026-10-19 |
| VIEW-05 (upstream) | `ScreenSnapShot.DiffFrom` (rows compared by content) | 2026-10-19 (telnet) |
| Ctrl+R history search | `CommandLineEditor` (kOS.Safe; base of `Interpreter` and `FixtureInterpreter`), `CommandHistory.TryFindOlder`/`Recall`, `TextEditor.ShownLine`, decoder Ctrl-R pass-through | 2026-10-20 |
| Saved history, leading-space exclusion | `CommandHistoryFile`/`ICommandHistoryStore`, `CommandLineEditor.HistoryStore`/`RecordCommand`, `kOSProcessor.CommandHistoryPath` | 2026-10-20 |
| D2 CPU state | `TerminalStatus`, `CPU.RunningProgramPath`/`IsWaitingFor`, `ProgramContext.MainProgramPath`, `ConnectivityInterpreter.DelaySecondsLeft`, `TermWindow.RefreshStatus` | 2026-10-20 |
| O1 wheel, O2 follow-output | `ScreenBuffer.ScrollScreen`/`HasNewOutputOutOfView`, `WheelScroll`, `TermWindow.ScrollViewWithWheel` | 2026-10-20 |
| O3 GUI mouse selection | `TerminalSelection`, `ScreenBuffer.GetLineAt`, `TerminalCopy.SelectionText`, `TermWindow.SelectWithMouse`/`DrawSelection` | 2026-10-20 |
| STATUS-01 | `WaitingDisplay` (kOS.Safe), `TermWindow.DescribeStatus` | **not yet** |
| Phase 4 step 1: terminal chrome, BAR-01 | `TerminalChromeLayout`, `StatusStrip`, `TerminalStatus.Kind`/`StateText`/`Chips` (kOS.Safe); `KOSStyle`, `TermWindow` header/strip/popover, `KOSManagedWindow.ContainsPoint` | **not yet** (end of Phase 4) |

Test fixtures to keep in step with game code: `docs/testing-and-build.md`, "Test harness".

## Commands

```bash
export PATH="$HOME/.cache/kosim-tools/dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
bash .ci/test.sh                                   # headless gate (Mono 6.12; PYTHON=python if needed)
dotnet build kOS.sln -c Release --no-incremental  # full build against the dev game's assemblies (kOS.props.user)
```

Results this session (2026-10-20, step 1 tree before committing): gate before the work 1175/1175; after it **1194/1194** passed, 0 skipped, exit 0; mutation run (chip priority, ellipsis trim, telnet count in the title, gutter) 6 failures, as expected, sources restored; full build **0 errors, 44 warnings** (unchanged count). No in-game check.

Deploy procedure, launch, logs, the dev save, the remote-testing tools (`dev-tools/kt.py`, `kd.py`, `vw.sh`, `hk.sh`) and what needs the user: `docs/testing-and-build.md`, "Full build and deployment" and "In-game testing".
