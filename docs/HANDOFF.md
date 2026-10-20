# KOS Improved — session handoff

## Resume here

**Phases 0–3 are complete** (exit criteria met 2026-10-15, 10-19, 10-19, 10-20; status table in `docs/KOSIm-roadmap.md`). Every implemented item is headless-tested and passed in game under Proton, except the **STATUS-01** fix (`kOS.Safe.Screen.WaitingDisplay`: `Waiting in` kept until 1.5 s of game time pass without a held wait), which is headless-tested but **not deployed or checked in game**. **BAR-01** (the "N telnet clients attached" label covers "Keyclicker" and part of the D2 state text) is deferred to Phase 4 (user decision).

The docs were trimmed on 2026-10-20 (user request): `docs/testing-and-build.md` now holds the setup, the test method, the behavior changes from upstream by area, limits, open issues and in-game coverage; `docs/KOSIm-roadmap.md` holds the phase status, binding decisions and Phase 4. Per-step test lists, pass logs, mutation results and hash histories are in git history (both files and this one at `876e1a29`).

**Next actions, in order:**

1. **STATUS-01 in-game check, when the user allows in-game testing** (can be folded into the first Phase 4 pass): deploy the fix build (game closed, backup `kOS-before-status01-<date>`), then over telnet on CPU 1 with `kd.py` title logging: `runpath("0:/kosim-check/p3r/slow2.ks").` should log `Running …` then `Waiting in …` once and stay there until `Ready` (was 177 changes in 90 s); `main.ks` still `Running` → `Waiting in` → `Ready`. Needs the user only to load the save and open the terminal.
2. **Phase 4** (`docs/KOSIm-roadmap.md`, "Phase 4 — Modern, simpler UI"): decisions agreed with the user 2026-10-20 (IMGUI, flat header and frame, status strip, settings popover, saved geometry per CPU, scroll bar, themes with green default, tabbed toolbar panel, other windows restyled with EDIT-03). **Step 1 (terminal chrome) is planned there and awaits the user's approval**; implement nothing before it.
3. Minor, optional: POWER-01 (a CPU powering itself off with `DOEVENT("Toggle Power")` logs `KOSBadJumpException`; compare with an upstream build first); the checks never performed (`docs/testing-and-build.md`, "In-game coverage").

## Workspace state (2026-10-20)

- Branch `kosim/test-baseline`, local only, never pushed (origin is upstream <https://github.com/KSP-KOS/KOS>). Base: upstream `develop` at `8f281a45`.
- Last code commit: **STATUS-01 `c8ec39f4`** (`src/kOS.Safe/Screen/WaitingDisplay.cs`, `src/kOS/Screen/TermWindow.cs` `DescribeStatus`, 4 tests in `src/kOS.Safe.Test/Screen/TerminalStatusTest.cs`, `minimum_tests` 1175). Gate **1175/1175**; Release build 0 errors, 44 warnings; a build of `c8ec39f4` gives `kOS.dll f3d7df79…`, `kOS.Safe.dll 698a6291…` (later commits embed their own ID and hash differently). Since then: docs only (`876e1a29` hashes, the 2026-10-20 doc trim).
- **Deployed in the dev instance: the `77d858f9` build** (O3; `kOS.dll 8a5f7bb8…`, `kOS.Safe.dll b2fb1bcf…`), checked in game 2026-10-20. Latest backup `kOS-GameData-backups/kOS-before-p3rest-20261020` (the VIEW-05 build). Game closed; Player.log of the pass kept as `dev-logs/Player.log.p3rest-pass-20261020`, no exception.
- The dev save has history files `saves/kOSIM-dev/kOS/history/80427209.txt` (CPU 1) and `80427999.txt` (CPU 2) and a `quicksave.sfs` from the pass.

## Constraints and decisions (still binding)

- Runtime target: Windows Steam **KSP 1.12.5.3190** under **GE-Proton11-7** via UMU 1.4.4, in the dev copy only. Runtime stays `net48`; SDK 8 is tooling only.
- Protect the vanilla install, Steam settings and the original prefix. Never terminate the game without asking; deploy only with the game closed.
- Local tests only; upstream workflows stay inactive under `.github/upstream-workflows/`. No push, no UI redesign without an approved plan, no compiler/VM rewrite.
- Preserve character-grid semantics, KSM/save/craft compatibility, public mod APIs, safe telnet defaults, predictable flight-input ownership. Keep `docs/improvements-overview.md` unchanged.
- User decisions: `docs/KOSIm-roadmap.md`, "Binding decisions" (INPUT-01 policy, terminal model D1–D4, Phase 3 H1–H4/D1/D2/O1–O3, item 5, file I/O, items left as is). Limits are code constants: `docs/testing-and-build.md`, "Limits and constants".

## Implemented state

Behavior per area: `docs/testing-and-build.md`, "Behavior changes from upstream". In game = Proton-checked.

| Item | Entry points | In game |
| --- | --- | --- |
| Local gate | `.ci/test.sh`, `.ci/known-test-failures.json` (empty; minimum 1175) | — |
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

Test fixtures to keep in step with game code: `docs/testing-and-build.md`, "Test harness".

## Commands

```bash
export PATH="$HOME/.cache/kosim-tools/dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
bash .ci/test.sh                                   # headless gate (Mono 6.12; PYTHON=python if needed)
dotnet build kOS.sln -c Release --no-incremental  # full build against the dev game's assemblies (kOS.props.user)
```

Deploy procedure, launch, logs, the dev save, the remote-testing tools (`dev-tools/kt.py`, `kd.py`, `vw.sh`, `hk.sh`) and what needs the user: `docs/testing-and-build.md`, "Full build and deployment" and "In-game testing".
