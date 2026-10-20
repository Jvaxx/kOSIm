# KOS Improved roadmap

## Purpose

Make kOS a dependable scripting and flight-operations tool, with a comfortable in-game terminal and supported telnet access. Scripts are written in an external editor with a kerboscript LSP; the in-game terminal is for running, inspecting and debugging; the embedded editor stays a safe emergency editor, not an IDE.

Order followed: tests and baseline → correctness and safety → structural extraction → quality of life → UI modernization. Small safety fixes never wait for structural work. Preserve upstream functionality and compatibility; isolate and document intentional behavior changes.

## Scope

- **Local tests only:** no hosted CI, remote runners or hosted release automation.
- **One runtime target:** the Windows Steam build of KSP under Proton on the user's Linux machine (KSP 1.12.5.3190, GE-Proton11-7), tested in the dev instance `/home/jvz/Games/KSP-kOSIM-dev/`. Native Linux, Windows and macOS are not validated; avoid breaking their code paths without claiming support.
- Headless tests, a full build and Proton checks are separate layers; none replaces another.
- Preserve scripts, saves, craft, KSM and public mod APIs.
- Out of scope unless separately justified and designed: compiler/VM optimizations, steering changes, language changes, a full embedded IDE, a structured remote-control interface, a newer Unity runtime.

Base: upstream <https://github.com/KSP-KOS/KOS> `develop` at `8f281a45`. The original review, [improvements-overview.md](improvements-overview.md), is kept unchanged; it reviewed commit `d4388439`, so verify its claims against current code. Commands, behavior changes, limits and open issues: [testing-and-build.md](testing-and-build.md). Current state: [HANDOFF.md](HANDOFF.md). Step plans and exit reviews of every phase are in git history (this file at `5bd2afab`).

## Status

**All agreed phases are complete**, each headless-tested and passed in game under Proton.

| Phase | Done | Content |
| --- | --- | --- |
| 0 — Local baseline | 2026-10-15 | `.ci/test.sh` gate, compatibility fixtures, dev instance and launcher |
| 1 — Correctness and safety | 2026-10-19 | every P0/P1 review finding checked and fixed or decided |
| 2 — Structural boundaries | 2026-10-19 | input router, `TerminalSession` fan-out, explicit lifecycle, screen/view split |
| 3 — Workflow quality of life | 2026-10-20 | history (draft, Ctrl+R, saved), paste, copy, CPU state, wheel, follow-output, selection |
| 4 — Modern, simpler UI | 2026-10-20 | terminal chrome and strip, geometry and scroll bar, one green look, font order, toolbar panel, other windows |

**Release:** 1.5.1.100 (2026-10-20), a private GitHub Release on `Jvaxx/kOSIm`; procedure in [testing-and-build.md](testing-and-build.md), "Versions and releases".

The review's terminal items TERM-01 to TERM-08 are covered by Phases 2–4. Not done: the rest of EDIT-05 (find/replace, line numbers, highlighting), the deferred external-editor conveniences (below), and the open issues in [testing-and-build.md](testing-and-build.md), "Known issues and open findings". No further phase is planned: ask the user before starting new work.

## Binding decisions

Agreed with the user. Later work keeps them unless the user changes them.

### INPUT-01 design

Keyboard and lock policy (2026-10-15; `kOS.Safe.UserIO.InputOwnership`, applied by `kOS.Screen.KOSInputOwnership`):

1. **Camera:** lock everything (`ControlTypes.All`) while a terminal or the editor has keyboard focus (#3012 stays open); a click outside releases it.
2. **Focus on open:** only a user's open takes focus (a part-menu event clicked by the user, the toolbar). Action groups and scripts (`DOEVENT`, `DOACTION`, `GUI:SHOW`) open without focus or locks.
3. **Hide, dialog, power:** F2 (with `ObeyHideUI`) or the flight-results dialog release the locks and give focus back afterwards; power loss releases it for good.
4. **One keyboard owner** among kOS windows; released on close, destroy, vessel loss and scene change on every path; cleanup idempotent.
5. **The editor and the in-flight name tag** take the terminal's lock while focused.
6. **Lock names stay** (`kOSTerminal` is read by `VesselUtils` and possibly other mods). Script GUIs keep their keyboard opt-out (#2568).

A pure policy class in `kOS.Safe` with headless tests; a thin Unity adapter applies the differences through `InputLockManager`.

#### INPUT-01 mouse half

2026-10-15; `kOS.Safe.UserIO.MouseBlocking` (policy) and `kOS.Screen.KOSMouseBlocking` (per-frame KSP sink):

- **Hover lock** over any drawn kOS window under `kOSMouseOver`: flight `CAMERACONTROLS | MAP_UI | TARGETING`; editor `EDITOR_SOFT_LOCK | CAMERACONTROLS`; space center `CAMERACONTROLS | KSC_UI`; tracking station `CAMERACONTROLS | TRACKINGSTATION_UI | MAP_UI`. Never keys, flight controls or staging.
- **Presses:** a press started over a kOS window keeps the lock until released; one started on the scene is not taken over. Released two frames after last wanted (#2759). Scene change releases at once.
- **Raycast blockers** (INPUT-02, 2026-10-20): a transparent Unity UI image over each visible kOS window, because KSP's part clicks and hover check only Unity UI. No ClickThroughBlocker integration (not needed with CTB 2.1.10).

### Terminal model

- **D1:** one view per terminal, shared by the GUI and all telnet clients; reading old output never moves where output is written. **D2:** scrollback bounded at 2,000 lines. **D3:** the last resize wins, applied on the main thread. **D4:** GUI Ctrl-D/Ctrl-X stay Enter.
- One grid size per processor (per-viewer grids would break `PRINT AT`).
- Closing a view never stops a processor; stopping a script, powering off and releasing flight controls stay separate.
- **Threads:** telnet reader/writer threads only parse and map bytes and touch locked queues and their own client state; every session, screen, interpreter and Unity change runs on the main thread.
- **Ownership:** the processor owns the CPU and window; the window owns its `TerminalSession`, which detaches its telnet clients with a message on dispose; textures are shared through a load-once cache.
- **API kept for mods**, same signatures: `TermWindow.ProcessOneInputChar` (three overloads; kOSPropMonitor) and the other public `TermWindow` members; `Interpreter.Type`/`SpecialKey`/`IsWaitingForCommand`/`IsAtStartOfCommand`/`SetInputLock`; `IScreenBuffer` (including `CharInputQueue`, `AddResizeNotifier`); `SharedObjects.Screen`/`Interpreter`/`Window`; `kOSProcessor.GetScreen`/`GetWindow`; `IConfig`. New classes sit behind them; old members forward.

### Workflow

- **Paste:** a GUI paste with a line break asks first; over 65,536 characters it is refused, never truncated; pasted text cannot break, detach or resize. Telnet paste unchanged.
- **Copy:** Ctrl+Shift+C (selection or view), Ctrl+Shift+E (the last error only); plain Ctrl+C stays BREAK.
- **History:** 500 commands; Ctrl+R search on the prompt line in the GUI and over telnet (telnet Ctrl-R no longer repaints; Ctrl-L does); saved per CPU in the save folder, always on, not rolled back, kept on reboot; a leading space excludes a command.
- **CPU state (D2):** in the status strip, the toolbar panel and the telnet title, never printed on the terminal.
- **Output:** wheel 3 lines a notch over the terminal; output does not pull a scrolled-back view down (`New output`); mouse selection of cells. No scrollback search.
- External-editor conveniences: E1 (show archive location), E3 (rerun key), E4 (overwrite warning in the editor) deferred; E2 (file picker) dropped.
- Limits are code constants, not settings. Unrecognized telnet sequences are typed as text; SS3 is not decoded. Never run scripts automatically on file changes; executable input is never dropped silently.

### UI

- **IMGUI**, one shared style (`KOSStyle`, `TerminalPalette`) following KSP's UI scale; testable layout logic in `kOS.Safe`. Another renderer only after a prototype justifies it.
- **One modern green look**, no themes and no theme script API. No bundled font (Unity under Proton cannot load one at run time); the OS fallback order puts Lucida Console before Courier.
- **Terminal window:** flat header (title, smaller/larger text, close), status strip, edge resizing snapped to cells; geometry saved per CPU next to its history; kept on screen, but movable partly off screen by its header (48 px rule). No settings on the window: visual beep, key click and brightness are mod options; reverse screen has no control. Script suffixes unchanged.
- **Toolbar panel:** CPUs and Settings tabs; power read-only; settings labelled in words; number fields apply on Enter or leaving them.
- **Other windows:** the editor is a free, resizable window kept for the session only (not saved); confirmations name their actions; Esc cancels in confirmations and the name tag. Script GUIs and KSP's own dialogs keep their look.
- No UI redesign without a plan approved by the user.

### Other

- File I/O: the additive **Preserve text APIs** policy: raw `READRAW`/`WRITERAW` and byte-exact copies, legacy text APIs unchanged (details in [testing-and-build.md](testing-and-build.md), "Files and volumes"). Archive links are followed to read, write and list, never entered by a delete; under Wine deletes and moves involving a link are refused.
- Versions: upstream base plus a kOSIM build number (`1.5.1.100`, then `.101`, …); `kOS.version` has no update URLs; releases are made by hand as private GitHub Releases.
- Left as is by the user: LIFE-04 (disposed script GUIs keep their resources until power-off or scene change); the `SWITCH`/`RENAME` attach check (dropped); the dev instance's display resolution.

## Testing discipline

- Add a regression test with each fix (a mutation run for non-trivial ones); use real terminal models where possible.
- Run the local gate before calling a change done; record commands, outcomes, skipped checks and the manual checks left.
- Check game-dependent changes under Proton in the dev instance with disposable saves; record KSP and Proton versions.
- Compare unchanged behavior with upstream where feasible; document intended differences instead of treating upstream as always right.
- Measure before claiming a performance or leak fix. Keep telnet's loopback/opt-in defaults and the in-game runtime (`net48`).
