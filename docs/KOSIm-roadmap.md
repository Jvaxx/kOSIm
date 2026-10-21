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

Base: upstream <https://github.com/KSP-KOS/KOS> **1.6.0.1-equivalent source**, `develop` at `8f281a45` (identical tracked tree to tag `1.6.0.1` except `CHANGELOG.md`). The original review, [improvements-overview.md](improvements-overview.md), is kept unchanged; it reviewed commit `d4388439`, so verify its claims against current code. Commands, behavior changes, limits and open issues: [testing-and-build.md](testing-and-build.md). Current state: [HANDOFF.md](HANDOFF.md). Step plans and exit reviews of every phase are in git history (this file at `5bd2afab`).

## Status

**All agreed phases are complete**, each headless-tested and passed in game under Proton.

| Phase | Done | Content |
| --- | --- | --- |
| 0 — Local baseline | 2026-10-15 | `.ci/test.sh` gate, compatibility fixtures, dev instance and launcher |
| 1 — Correctness and safety | 2026-10-19 | every P0/P1 review finding checked and fixed or decided |
| 2 — Structural boundaries | 2026-10-19 | input router, `TerminalSession` fan-out, explicit lifecycle, screen/view split |
| 3 — Workflow quality of life | 2026-10-20 | history (draft, Ctrl+R, saved), paste, copy, CPU state, wheel, follow-output, selection |
| 4 — Modern, simpler UI | 2026-10-20 | terminal chrome and strip, geometry and scroll bar, one green look, font order, toolbar panel, other windows |

**Published releases (historical names):** 1.5.1.100 (2026-10-20); 1.5.1.101 (2026-10-21: the geometry presets and the post-release review fixes); **1.5.1.102** (2026-10-21: terminal background opacity). All already contain upstream 1.6.0.1 functionality; their `1.5.1` prefix was a versioning mistake. **Current development version: 1.6.0.103**, a version/provenance correction only, not yet published or deployed. Existing release names/tags/artifacts remain unchanged. Private GitHub Releases on `Jvaxx/kOSIm`; procedure in [testing-and-build.md](testing-and-build.md), "Versions and releases".

**Post-release review** (2026-10-20, [review-report.md](review-report.md), of `e280f197`): **R-01** (game-thread telnet sends could block for seconds), **L-01** (the address picker leaked a component per toggle-close), the idle-work items **P-01 to P-06** (per-frame work and allocation of CPUs nobody looks at, the diff, the toolbar panel) with **R-04/R-05**, and (2026-10-21) **P-09** (scrollback trim), **P-10** (history saves at most every 2 s), the doc fixes **K-01 to K-12**, and **P-07/P-08** (an open terminal keeps its strip layout, title and row strings while they show the same) , then **L-02** (per-CPU files pruned once no save has the part, at a load from the main menu), **L-03** (leftover temporary save files swept by a listing), **P-11** (one walk and one Unix mapping per path for deletes under Wine), **L-04** (the install check names the sounds the terminal uses), **D-01/D-03/D-04** (unused code removed, duplicated key tables, part names and clamps shared) and **N-01** (`CpuTextFile.Write` reports success) are done, headless-tested and built, and checked in game on 2026-10-21 under Proton except the P-10 game-exit path, the L-01 component count and a vessel switch with a stalled telnet client ([testing-and-build.md](testing-and-build.md), "Fixes from the post-release review"). Measuring P-09 found the real cause of slower long pastes: the interpreter rebuilds its whole program for every command (**PERF-01**, inherited, compiler/VM: needs a user decision). Still open from the report: P-12, D-02, R-06/R-07.

**Terminal geometry presets** (user feature, 2026-10-21): three header buttons, left-click applies, right-click saves; one set for the game in `PluginData`. Headless-tested, built and passed in game (2026-10-21); released with the review fixes in **1.5.1.101** (2026-10-21).

The review's terminal items TERM-01 to TERM-08 are covered by Phases 2–4. Not done: the rest of EDIT-05 (find/replace, line numbers, highlighting), the deferred external-editor conveniences (below), and the open issues in [testing-and-build.md](testing-and-build.md), "Known issues and open findings". No further phase is planned: ask the user before starting new work.

## Terminal window opacity

**Implemented and passed in game 2026-10-21; included in 1.5.1.102.** The user selected backgrounds-only opacity and a header context menu, confirmed the range/defaults/older-preset behavior, then approved implementation, testing, documentation and a commit. This is a focused addition, not a new UI phase. After the initially headless-only work, `c1701110` was rebuilt, deployed and launched in the approved dev instance; the user performed the supplied seven-point checklist and reported **"It's all ok"**, then requested a release. Behavior, pass record and expanded checks not individually verified: [testing-and-build.md](testing-and-build.md), "Terminal window opacity" and "In-game coverage".

### Agreed behavior and as built

- **Per terminal window**, remembered per CPU with ordinary geometry, and saved/restored by geometry presets. Shared presets retain their existing left-apply/right-save behavior; applying affects only the receiving window, and saving is not a broadcast.
- **All terminal backgrounds** (body, header, status strip and chip backgrounds), not text, cursor, selection, control graphics or focus border. Brightness remains separate. No click-through, script suffix, telnet or other-window change; `config.xml` and the runtime target are unchanged.
- **Right-click unused title/header space** for an opaque menu with a **0–100% slider**, rounded to whole percent, readout and live preview. No new header button or layout-width change. The right press/release is captured so it does not drag, resize or select; preset/size/close buttons are excluded. This deliberate user click takes keyboard focus so Esc works; script opens still do not.
- **Outside press or Esc dismisses**; outside presses retain their normal recipient. Terminal typing is suppressed while the menu is open. Close, focus/power loss, hidden UI (with `ObeyHideUI`), flight results, paste confirmation and render faults dismiss it too; becoming available again does not reopen it. The popup follows UI scale and stays on screen, has its own mouse/raycast-blocking area and counts as part of its terminal for focus. Its mouse area is unregistered on destruction and its own held controls are released when dismissed.
- **Optional `opacity = 75` / `1.opacity = 75`** in the two existing text files, using the same atomic writer, size limits and save timing. Valid values are whole numbers 0–100. Missing or invalid values mean **100%**, never "keep the last window's value". `TerminalGeometry.IsComplete` still requires only the original five fields, so older presets remain valid. No migration or file rewrite until something changes.
- **No opaque underlay on the normal terminal.** Body, header and strip paint once; chip backgrounds get holes in the strip background so alphas do not stack. Reverse screen/visual beep compose their background color at the existing text brightness before applying opacity once. The opacity menu, paste confirmation, broken-install message and render-fault recovery stay opaque. Shared palette/style resources are not changed.

### Implementation and verification

- New `kOS.Safe.Screen.TerminalOpacity` (per-window state, readout, background color composition and reusable strip tiling), `TerminalOpacityMenu`/`TerminalOpacityMenuLayout` (context-click state, hit testing, popup placement and dismissal), and `src/kOS.Safe.Test/Screen/TerminalOpacityTest.cs`.
- `TerminalGeometry` adds optional opacity to equality/hash, copies and shared parsing/formatting; `TerminalPresets` retains its five-field completeness rule. `TermWindow` connects persistence, presets, rendering, the menu and lifecycle; `KOSManagedWindow` only has a comment updated. User help is in `doc/source/structures/misc/terminal.rst`.
- Gate **1435/1435** (34 new cases; `minimum_tests` 1401 → 1435; no baseline failures). **24/24 mutants killed** (compiled, full test assembly each time); final gate rerun after restoring sources. Headless allocation coverage: unchanged new drawing/layout/readout paths allocate 0 bytes after warmup. Full build: **0 errors, 44 existing warnings**, no new ones. `TermWindow`/Unity rendering and input are not headless-tested: **the user reported the Proton functional checks passed** on `c1701110` (KSP 1.12.5.3190 / GE-Proton11-7). Individual mouse steps were not observed by the agent; optional reverse/beep and the expanded technical checklist were not independently confirmed.

## Terminal geometry presets

Proposed by the user 2026-10-20; decisions agreed the same day; step plan approved 2026-10-21 with Q1 and Q2 as proposed. **Implemented 2026-10-21** as planned (differences: "As built" below); headless-tested, full build, **passed in game 2026-10-21** (the user's own test of the "In game" list below); released in **1.5.1.101**. Behavior: [testing-and-build.md](testing-and-build.md), "Terminal geometry presets".

- **Three preset buttons** (`1`, `2`, `3`) in the terminal's header: **left-click applies** the preset, **right-click saves** the window's current geometry into it.
- **Feedback:** a short strip notice on save and on clicking an empty slot (as the copy notices: `Saved preset 2`, `Preset 3 is empty`); empty slots drawn dim, as the size buttons at the end of their range.
- **Narrow windows:** the preset buttons are hidden below some width, so the title, the size buttons and close keep priority.
- **Shared presets:** one set for every CPU, vessel and save, in their own file under `PluginData` (not `config.xml`, not the per-CPU geometry file).
- **A preset holds** position, columns, rows and character height. Applying one is a resize like any other (resize watchers fire, telnet clients are asked to resize: D3) and is brought wholly on screen by the existing keep-on-screen rule.
- **Not included:** the editor window, keyboard shortcuts, anything telnet-side.

### Step plan (approved 2026-10-21)

Choices marked **(Q)** were confirmed by the user as proposed.

**Headless (`kOS.Safe.Screen`), each with tests:**

- **`TerminalPresets`**: three slots, each a `TerminalGeometry` or empty. `Format`/`Parse` for a small text file: a header line, then `1.x = 120`, `1.y = 80`, `1.columns = 50`, `1.rows = 36`, `1.charheight = 14`, the same for `2.` and `3.`, invariant culture. The per-value range checks are shared with `TerminalGeometry.Parse` (moved into one helper, no change to the geometry file). Tolerant as the geometry file: CR LF, blank and `#` lines, unknown keys and slot numbers ignored. **(Q1)** A slot counts only when all five values are valid; otherwise it is empty, so a preset never applies half a geometry.
- **`TerminalPresetFile`**: the store, on the existing `CpuTextFile` (atomic save through `ArchiveFileWriter`, a failed save keeps the old file, failures logged once, files over 4 KiB not read). Read once on first use and kept in memory; each save writes the whole file at once. Its `Save` reports failure so the window can say so: `CpuTextFile.Write` already returns whether it saved (review N-01, done).
- **`TerminalChromeLayout`**: `PresetButton(slot, width)` (three buttons the size of the others, left of the smaller-text button, with a small gap), `ShowsPresets(width)`: shown only when the title keeps at least a minimum room (proposed 60 px at UI scale 1, so about 260 px of window at scale 1; the 15-column minimum window hides them). `HeaderButtons` then starts at the first preset button, so the title shrinks to its left, `TerminalResize.EdgesAt` does not resize under them, and a window pushed off the left keeps them on screen too (DRAG-01's 48 px rule counts from there). Hidden, everything is as today.

**Game side:**

- **Shared presets:** one `TerminalPresetFile` for the game, owned statically by `TermWindow` (or a small holder), so a save from one terminal shows at once in every other one. Path `GameData/kOS/Plugins/PluginData/kOS/terminal-presets.txt`, next to `config.xml`; the release zip leaves `Plugins/PluginData/` out, so an update keeps the presets.
- **`TermWindow.DrawHeader`**: the three buttons labelled `1`, `2`, `3` in the chrome font, dim when empty (`KOSStyle.TextButton`'s dim look, still clickable). A new `KOSStyle` helper reports which mouse button pressed it: the right button is caught at `MouseDown` over the button and the event used, before `GUI.DragWindow`, the selection and the edge resize see it.
- **Left-click** on a filled slot: the character height, the cells and the position are applied together between passes (the path the size buttons already use, `postponedCharPixelHeight`, then `SetSize(rows, columns)`, then the position). The size change makes `WindowKeeper` bring the window wholly on screen. Saved to the CPU's own geometry file as any other change. On an empty slot: the notice `Preset 3 is empty`, nothing else.
- **Right-click:** `CurrentGeometry()` into the slot, saved at once; notice `Saved preset 2`, or `Preset save failed (see KSP.log)` (the slot is then still updated for this session).
- **Notices** reuse the copy notice in the strip (2 s); `ShowCopyNotice` becomes a general notice. **(Q2)** No notice when a preset is applied: the window moving is the feedback.
- User doc: a short paragraph on the presets in `doc/source/structures/misc/terminal.rst` (near `CHARHEIGHT`), and the behavior entry in `docs/testing-and-build.md`.

**Unchanged:** the per-CPU geometry file and its format, script suffixes, telnet (beyond the usual resize request to attached clients, D3), the editor, the toolbar panel, `config.xml`.

**Intentional differences:** three new header buttons on windows wide enough; a new file in `PluginData`; applying a preset is a resize, so running scripts' resize watchers fire and telnet clients are asked to resize.

**Tests and gate:** `TerminalPresetsTest` (round trip, partial and invalid slots empty, unknown keys and slots, CR LF, the size limit, the store's single read, atomic save and failure report with the existing fake file operations), layout cases (button order and tiling, the gap, no overlap with the title or size buttons, hidden below the threshold at UI scales 1, 1.3 and 2, `HeaderButtons` and the resize exclusion with and without presets); a mutation run; `minimum_tests` raised.

**In game (Proton):** left and right clicks reach the buttons under Wine (right-click in IMGUI), without moving the window, starting a selection or reaching a part behind it (raycast blockers); the notices; the dim empty slots; hidden at a narrow width and back when widened; a preset saved by `cpu down` applied on `cpu top`; a preset saved at 2560×1440 applied at a smaller resolution comes back wholly on screen; presets kept after a game restart (the file); a script's resize watcher and a telnet client see one resize on apply; Alt+F12 locks over the buttons.

**Not in this step:** names or more than three slots, per-save or per-vessel presets, the editor, keyboard shortcuts, telnet commands, a preset for the toolbar panel.

### As built (2026-10-21)

- **Threshold:** the buttons show while the title keeps 60 px (scaled), which at UI scale 1 is a window of **198 px** or more, not "about 260 px" as the plan estimated (six 20 px buttons, the gap, 6 px padding either side of the title). The default 50-column window shows them; the 15-column minimum window hides them at UI scales 1 and above (at 0.5, with 8 px cells, it shows them).
- **Gap:** the chrome padding (6 px at scale 1, at least 4). `HeaderButtons` includes the gap, so the gap does not resize either.
- **Clicks:** `KOSStyle.ClickButton`, a control of its own: a left or right press over the button is taken and used at `MouseDown` (hot control), the click counts on release of the same button over it. `GUI.Button` was not relied on for the right button.
- **Apply order:** the left click only records the preset. At the next `OnGUI` the character height is set, the font looked up for it (the cell width), then `SetSize(rows, columns)` (one resize notification for scripts and telnet), then the position; `keeper.FitWhollyNext()` brings the window wholly on screen even when its size did not change. The size-button refit of the cells to the window is skipped for that change, so the preset's cells are kept.
- **Shared parsing:** `TerminalGeometry.Entries`/`SetValue`/`AppendValues` (internal) serve both files; added `TerminalGeometry.IsComplete` and `Copy()` (public). The geometry file's format and parsing are unchanged.
- The strip notice is now general (`ShowNotice`; was `ShowCopyNotice`, private).

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

- **Hover lock** over any drawn kOS window under `kOSMouseOver`: flight `CAMERACONTROLS | MAP_UI | TARGETING`; editor `EDITOR_SOFT_LOCK | CAMERACONTROLS`; space center `CAMERACONTROLS | KSC_UI`; tracking station `CAMERACONTROLS | TRACKINGSTATION_UI | MAP_UI`. Never other keys, flight controls or staging (`CAMERACONTROLS` also pauses the camera keys while hovering).
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
- **Terminal window:** flat header (title, geometry presets `1`–`3` when wide enough, smaller/larger text, close), status strip, edge resizing snapped to cells; geometry saved per CPU next to its history; kept on screen, but movable partly off screen by its header (48 px rule). Visual beep, key click and brightness are mod options; reverse screen has no control. The previous "no settings on the window" rule has one exception: the opacity-only header context menu (above). Script suffixes unchanged.
- **Toolbar panel:** CPUs and Settings tabs; power read-only; settings labelled in words; number fields apply on Enter or leaving them.
- **Other windows:** the editor is a free, resizable window kept for the session only (not saved); confirmations name their actions; Esc cancels in confirmations and the name tag. Script GUIs and KSP's own dialogs keep their look.
- No UI redesign without a plan approved by the user.

### Other

- File I/O: the additive **Preserve text APIs** policy: raw `READRAW`/`WRITERAW` and byte-exact copies, legacy text APIs unchanged (details in [testing-and-build.md](testing-and-build.md), "Files and volumes"). Archive links are followed to read and list, directory links for every write; a file link is written through by appends and `:CLEAR`, but a whole-file save onto it (editor, copy, compile, `WRITEJSON`) replaces the link with a regular file (the atomic save; recorded, not chosen). Links are never entered by a delete; under Wine deletes and moves involving a link are refused.
- Versions (corrected user decision 2026-10-21): upstream major/minor/patch plus a continuous kOSIM build counter, **`1.6.0.103`**, then `.104`, etc. The fourth field is **not** upstream's revision; exact upstream **1.6.0.1** and the actual source commit are recorded separately in `kOS.props`. Published `1.5.1.100`–`.102` identifiers remain as history, not claims of a 1.5.1 source base. `kOS.version` has no update URLs; releases are made by hand as private GitHub Releases. This correction is authorized for a local commit only: no push, publication, deployment or game launch.
- Left as is by the user: LIFE-04 (disposed script GUIs keep their resources until power-off or scene change); the `SWITCH`/`RENAME` attach check (dropped); the dev instance's display resolution.

## Testing discipline

- Add a regression test with each fix (a mutation run for non-trivial ones); use real terminal models where possible.
- Run the local gate before calling a change done; record commands, outcomes, skipped checks and the manual checks left.
- Check game-dependent changes under Proton in the dev instance with disposable saves; record KSP and Proton versions.
- Compare unchanged behavior with upstream where feasible; document intended differences instead of treating upstream as always right.
- Measure before claiming a performance or leak fix. Keep telnet's loopback/opt-in defaults and the in-game runtime (`net48`).
