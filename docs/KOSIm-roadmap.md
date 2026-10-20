# KOS Improved roadmap

## Purpose and priorities

Make kOS a dependable scripting and flight-operations tool, with a comfortable in-game terminal and supported telnet access.

The primary workflow is writing scripts in an external editor with a kerboscript LSP. The in-game terminal is primarily for executing commands, inspecting state, and debugging. The embedded editor remains a safe emergency editor, not a competing IDE.

Agreed implementation order:

1. Establish tests and a trustworthy baseline.
2. Fix critical correctness and data-safety defects.
3. Extract structural boundaries incrementally.
4. Add workflow-focused quality-of-life improvements.
5. Modernize and simplify the UI.

Structural improvements precede the UI redesign, but should not delay small safety fixes. Preserve upstream functionality and compatibility; isolate and document intentional behavior changes.

## Scope

- **Local tests only.** No hosted CI, remote runners, or hosted release automation. Run the local suite before completing changes; failure-propagation checks remain required.
- **One runtime target:** the Windows Steam build of KSP under Proton on the user's Linux machine (KSP 1.12.5.3190, GE-Proton11-7). Native Linux, native Windows, and macOS game validation are deferred, not implied by upstream compatibility.
- Game tests use the dev instance `/home/jvz/Games/KSP-kOSIM-dev/` and its isolated Proton prefix; the vanilla install, Steam settings and Steam prefix stay untouched.
- Headless tests, compilation against game references, and Proton gameplay checks are distinct; none substitutes for the others.
- Preserve scripts, saves, craft, KSM and public mod APIs within this target; avoid unnecessary platform-specific breakage elsewhere without promising untested support.
- The original review ([improvements-overview.md](improvements-overview.md)) is kept unchanged; its hosted-CI and cross-platform recommendations are superseded by this scope.

Commands, behavior changes, limits and open issues: [testing-and-build.md](testing-and-build.md). Session progress: [HANDOFF.md](HANDOFF.md).

## Starting point

- Upstream: <https://github.com/KSP-KOS/KOS>; initial clone `develop` at `8f281a459b6ea0ba917c91bbbc117e68bb948199`.
- The review used commit `d4388439c712994e47258ed7514e863f79da632b`, not the clone's starting commit. Revalidate its findings against current code before acting on them.

## Status

| Phase | Status |
| --- | --- |
| 0 — Trustworthy local baseline | Complete (2026-10-15): local gate `.ci/test.sh`, compatibility fixtures, dev instance and launcher. |
| 1 — Correctness and safety | Complete (2026-10-19): every P0/P1 review finding checked; fixes headless-tested and passed in game. |
| 2 — Structural boundaries | Complete (2026-10-19): input router, `TerminalSession` fan-out, explicit lifecycle, screen/view split ([phase2-contracts.md](phase2-contracts.md)). |
| 3 — Workflow quality of life | Complete (2026-10-20): history (draft, Ctrl+R search, saved history), paste, copy, CPU state, wheel, follow-output, mouse selection. STATUS-01 fix awaits its in-game check. |
| 4 — Modern, simpler UI | Decisions agreed 2026-10-20. Step 1 (terminal chrome) approved and implemented 2026-10-20: headless-tested, full build clean, **not yet checked in game**. Step 2 (geometry and scrolling) approved and implemented 2026-10-20: headless-tested, full build clean, **not yet checked in game**. Step 3 re-decided with the user (one modern green look, niche settings to mod options, bundled-font prototype); 3a font prototype run 2026-10-20 (approved "go for step 3a"): no run-time way to load a bundled font, user decision no-go. 3b (look and settings) approved ("go for step 3b") and implemented 2026-10-20: headless-tested, full build clean, **not yet checked in game**. 3c (font fallback order) approved ("let's go do it") and implemented 2026-10-20: headless-tested, full build clean, **not yet checked in game**. Step 4 (toolbar panel) planned with Q1–Q4 chosen, approved ("go for it, implement step 4 thoroughly") and implemented 2026-10-20: headless-tested, full build clean, **not yet checked in game**. Step 5 (other windows) planned and approved 2026-10-20 (Q1–Q4 as recommended) and implemented 2026-10-20: headless-tested, full build clean, **not yet checked in game**. Next: the end-of-Phase-4 Proton pass. |

What each item changed, and what was never checked in game: [testing-and-build.md](testing-and-build.md), "Behavior changes from upstream" and "In-game coverage". Detailed step plans and exit reviews of Phases 0–3 are in git history (this file at `876e1a29`).

Review items reclassified out of Phase 1 and not done: `EDIT-03` (the editor follows the caret on every repaint, `KOSTextEditPopup.cs`) and `TERM-01/02/04/06/07` (terminal and UI work) are candidates for Phase 4. `TEST-01` is resolved by policy (local gate, no hosted CI).

## Binding decisions

Decisions made with the user that later work must keep (or change only with the user).

### INPUT-01 design

Keyboard and lock policy (agreed 2026-10-15; `kOS.Safe.UserIO.InputOwnership`, applied by `kOS.Screen.KOSInputOwnership`):

1. **Camera:** keep locking everything (`ControlTypes.All`) while a terminal or the editor has keyboard focus. #3012 stays open; clicking outside releases.
2. **Focus on open:** only an open by the user takes focus (a part-menu event clicked by the user, the toolbar button). Action groups and scripts (`DOEVENT`, `DOACTION`, `GUI:SHOW`) open the window without focus and without locks; kOS sets a scope flag around `evt.Invoke()`/`act.Invoke()` in `PartModuleFields`.
3. **Hide / dialog / power:** F2 hide (when `ObeyHideUI`) or the flight-results dialog stop reading keys and release the locks; focus comes back with the UI if the window still wanted it. Power loss releases and does not take it back; the user clicks to refocus.
4. **One keyboard owner** among kOS windows; release on close, destroy, vessel loss and scene change on every path, including exceptions; cleanup idempotent.
5. **Editor and in-flight name-tag window** take the same lock as the terminal while focused.
6. **Lock names stay** `kOSTerminal` (read by `VesselUtils` and possibly other mods). Script GUI windows keep their keyboard opt-out (#2568).

Shape: a pure policy class in `kOS.Safe` with headless tests for every transition; a thin Unity adapter applies only the differences through `InputLockManager`.

#### INPUT-01 mouse half

Agreed 2026-10-15; `kOS.Safe.UserIO.MouseBlocking` (policy) and `kOS.Screen.KOSMouseBlocking` (per-frame KSP sink):

- **Hover lock:** while the pointer is over any drawn kOS window (terminals, editor and its dialog, name tag, script GUIs and their popup menus, toolbar panel, list picker, telnet dialogs), lock the scene's mouse controls under `kOSMouseOver`: flight `CAMERACONTROLS | MAP_UI | TARGETING`; editor `EDITOR_SOFT_LOCK | CAMERACONTROLS`; space center `CAMERACONTROLS | KSC_UI`; tracking station `CAMERACONTROLS | TRACKINGSTATION_UI | MAP_UI`. Never keys, flight controls or staging.
- **Presses:** a press started over a kOS window keeps the lock until released (drags, resizes); one started on the scene is not taken over. Release two frames after the lock was last wanted (a click that closes a window does not reach the scene, #2759). Scene change releases at once. `VesselUtils.SetTarget` lifts it like `kOSTerminal`.
- **Modal:** the editor stays frozen under its confirm dialog; the terminal stays usable.
- No ClickThroughBlocker integration (checked in game with CTB 2.1.10: not needed). #2659 (key order with CTB) is not addressed.

### Terminal model (Phase 2)

- **D1:** one view per terminal, shared by the GUI and all telnet clients. Reading old output never redirects writes or breaks `PRINT AT`, `CLEARSCREEN`, cursor access or resize watchers.
- **D2 (Phase 2):** scrollback bounded at 2,000 lines. **D3:** the last resize wins. **D4:** GUI Ctrl-D/Ctrl-X stay Enter.
- Closing a view never stops a processor; stopping a script, powering off and releasing flight controls stay distinct.
- Keep public signatures and shims used by other mods, including terminal input injection (`ProcessOneInputChar`).

### Workflow (Phase 3)

- **Paste:** a GUI paste with a line break asks first; over 65,536 characters it is refused, never truncated. Pasted text cannot break, detach or resize. Telnet paste is unchanged.
- **Copy:** keyboard shortcuts (Ctrl+Shift+C view or selection, Ctrl+Shift+E last error); plain Ctrl+C stays BREAK. **D1 (Phase 3):** keep the last error only.
- **History:** last 500 commands; Ctrl+R reverse search inline on the prompt line in the GUI and over telnet (telnet Ctrl-R no longer repaints; Ctrl-L does) (H1/H2); saved per CPU under the save folder, always on, not rolled back, kept on reboot (H3); a leading space excludes a command (H4).
- **D2 CPU state:** in the GUI's bottom bar and the telnet title, never printed on the terminal. STATUS-01: `Waiting` kept 1.5 s of game time after the last held wait.
- **Output:** the wheel scrolls the shared view 3 lines a notch, over the terminal only (O1); output does not pull a scrolled-back view down, `New output` shows until it is back (O2); mouse selection of grid cells, edge drag scrolls (O3). **No scrollback search.**
- **External-editor conveniences (item 5):** E1 (show archive location), E3 (rerun key), E4 (overwrite warning in the in-game editor) deferred; E2 (file picker) dropped.
- Telnet and paste limits are code constants, not settings. Unrecognized telnet sequences are still typed as text; SS3 is not decoded.
- Never run scripts automatically on file changes. File UI actions use the existing filesystem and execution semantics.
- Queue limits and update budgets keep input order, responsiveness and reliable interrupts; executable input is never dropped silently.

### Other

- File I/O: the additive **Preserve text APIs** policy ([file-io-compatibility.md](file-io-compatibility.md)); archive links followed to read/write/list, never entered by a delete, and deletes/moves involving a link refused under Wine.
- Left as is by the user: LIFE-04 (disposed script GUIs keep their resources until power-off or scene change), PASTE-UI-01, `SWITCH`/`RENAME` attach check (dropped); the dev instance's display resolution.

## Phase 4 — Modern, simpler UI

Redesign the presentation only after the models and adapters are stable.

### Direction

- Output dominates the terminal window.
- A compact status strip exposes operational state.
- Preferences move into a settings panel rather than occupying the working surface.
- Chrome scales independently of terminal cell dimensions.
- Font fallback, contrast, cursor alignment, and control hit targets are dependable.
- Window geometry persists safely and remains recoverable after resolution/UI-scale changes.
- Retro appearance is optional; decorative framing must not compromise usability.
- The embedded editor remains minimal, safe, and usable.

Start with disciplined IMGUI. Evaluate another renderer only if a scoped prototype demonstrates benefits that justify packaging, lifecycle, and compatibility costs. Do not assume KSP supports a newer Unity framework or runtime.

Carried in from Phase 3 (user decision 2026-10-20): **BAR-01**, the bottom bar's "N telnet clients attached" label overlaps the "Keyclicker" toggle and the D2 state text; fix it as part of the status strip, not as a patch to the current bar (done in step 1, to check in game). Also open in the UI: G3-UI (controls overlap at 20 columns), PASTE-UI-01 (translucent paste box), the narrow-width bar (never checked).

No UI redesign without a plan approved by the user; no wholesale terminal rewrite or embedded IDE.

### Decisions (agreed with the user 2026-10-20)

- **Renderer:** IMGUI. One shared style module (palette, metrics, fonts, UI-scale factor) for every kOS window; layout logic that can be tested (strip truncation, layout ↔ cell counts, on-screen clamping, geometry file) lives in `kOS.Safe` with headless tests.
- **Terminal window:** our own flat header (vessel and CPU name, settings button, close button) and frame instead of KSP's window skin and the monitor bezel; a thin accent border shows keyboard focus. The grid fills the window with small even padding.
- **Status strip** (one line, replaces the bottom bar): the D2 state with a colored dot on the left; chips only when they apply (`New output`, which jumps to the bottom when clicked; `Delay`; `No connection`; telnet client count); size and resize grip on the right. When narrow, chips drop lowest priority first, never overlapping (fixes BAR-01). Copy notices show briefly without hiding the size.
- **Settings popover** (from the header button): reverse screen, visual beep, key click, a horizontal brightness slider, character size. Nothing else on the working surface. Script API unchanged (`TERMINAL:REVERSE`, `VISUALBEEP`, `BRIGHTNESS`, `CHARHEIGHT`, …). *Superseded by the step 3 decisions below (popover removed; built in step 1).*
- **Chrome scale:** header, strip and popover follow KSP's UI scale; cells follow `CHARHEIGHT`.
- **Geometry:** each CPU's terminal position and size saved in the save folder next to its history (`saves/<save>/kOS/…`, by `persistentId`); a window that would open off screen is moved back on, also after a resolution or UI-scale change. Resizing by the grip or an edge, snapped to whole cells, with a size overlay while dragging.
- **Scroll bar:** thin, shown only while the view is scrolled back; draggable.
- **Themes:** classic green stays the default; Amber and a modern neutral theme (light grey on near-black) as options. *Superseded by the step 3 decisions below (one modern green look, no theme choice).*
- **Toolbar panel:** two tabs. *CPUs* (default): per vessel, each CPU's D2 state, power and Open/Close; hovering highlights the part; *Suppress autopilot* stays visible in the footer with the telnet status. *Settings*: grouped Terminal defaults, Telnet (live status, "restart to apply"), Boot (reread boot folder); the hovered setting's description inline. Config keys and `config.xml` stay compatible.
- **Other windows:** the paste box (PASTE-UI-01), the editor, the name tag and dialogs adopt the style; **EDIT-03** (the editor follows the caret on every repaint) is fixed with the editor.
- Telnet is unchanged (its title already carries the state).

### Decisions for step 3 (agreed with the user 2026-10-20, later the same day)

The user finds the stock kOS style dated, cluttered and hard to read; the terminal should look modern and simple.

- **One look, no themes:** a single modern green style (green kept, not CRT-style); no theme choice and **no script API for themes**.
- **The terminal window keeps one control: character size** (smaller/larger buttons in the header, saved per CPU with the geometry). The settings popover goes.
- **Niche settings go to mod options** (the toolbar panel's settings, `config.xml`), not the terminal: **visual beep** and **key click** (new keys), **brightness** (the existing key, for new terminals). **Reverse screen** gets no control at all (useless to the user). Their script suffixes (`TERMINAL:REVERSE`, `VISUALBEEP`, `BRIGHTNESS`) keep working for compatibility.
- **Font:** prototype a **bundled, open-licensed monospace font** (it must be monospace); adopt it only if the prototype shows KSP can load it under Proton. **Result (3a, 2026-10-20): no run-time way works; the user chose no-go** (no bundled font, no asset bundle): 3c ships a better OS fallback order only (Lucida Console before Courier New under Proton).

### Steps

Each with headless tests; the Proton checks of all steps are done in one pass at the end of Phase 4 (user decision 2026-10-20):

1. **Terminal chrome:** style module, header, flat frame, status strip, settings popover, chrome UI scale.
2. **Geometry and scrolling:** saved geometry, keep on screen, edge resizing with the size overlay, the scroll bar.
3. **Modern look:** one green style, a better monospace fallback order (the bundled font was dropped after the prototype), the popover replaced by character-size buttons, niche settings moved to mod options.
4. **Toolbar panel** tabs.
5. **Other windows** (paste box, editor with EDIT-03, name tag, dialogs).

### Step 1 plan (approved by the user 2026-10-20; implemented; Proton check at the end of Phase 4)

Headless (`kOS.Safe`):

- `TerminalChromeLayout`: from the window size, UI scale and cell size, the rectangles of the header, character field, strip, grip and the gutter reserved for the step 2 scroll bar; the rows/columns that fit; the window size for given rows/columns. It replaces the fixed offsets in `TermWindow` (`CharacterField`, `HowManyRowsFit/ColumnsFit`, `NotifyOfScreenResize`, the wheel field), so drawing, selection, wheel and resize use one layout. Tests: size ↔ cells round trips at several scales and cell sizes, minimum sizes, no negative field.
- `StatusStrip`: items with priority (state, chips, copy notice, size) and a width measurer; returns what fits and where; the state text is shortened with `…` last. Tests with a fake measurer, including BAR-01's case.
- `TerminalStatus` also gives its parts separately (activity kind for the dot, chips); `Text` (telnet title) unchanged.

Game side (`TermWindow`, new `KOSStyle`):

- The window draws with an empty KSP title and our header; dragging moves the window from the header (and the strip's empty space); the grid keeps mouse selection.
- Icons (gear, close, grip) are small textures generated in code (no new binary assets; Unicode glyphs may be missing from KSP's font under Proton). The old bezel textures stay in `GFX/` unused.
- Removed from the working surface: the bottom toggles, vertical brightness slider, font −/+ buttons, bottom Close button, the "N telnet clients attached" label.
- The popover is drawn after the terminal like the paste box, closes on the gear, Esc, a click outside, or closing the terminal; it is covered by the `kOSMouseOver` lock and must not take the keyboard from the terminal (to verify in game).
- Unchanged: the render-fault fallback and missing-texture screens, the paste box (step 5), the telnet title text, focus and lock behavior, the grid rendering.

Intentional differences: the window's pixel size for a given cell count changes (different chrome); the window moves only by its header/strip, not by the old frame; the in-game title reads `test-vessel · CX-4181 · tag` (telnet keeps its title).

In game (Proton): layout at 50×36 and at the script minimum width, UI scale 1.0 and another, focus border, every popover control, popover close paths and keyboard focus, strip chips (telnet attached, New output click, copy notice), drag and grip resize, selection and wheel still aligned, Alt+F12 locks.

As implemented (2026-10-20; behavior in `testing-and-build.md`, "Terminal window (Phase 4, step 1)"):

- `kOS.Safe.Screen.TerminalChromeLayout` (metrics at UI scale 1: header 20, strip 18, padding 6 (at least 4, for descenders), gutter 8, minimum width 150, chrome font 11; the scale is clamped to 0.5–3), `StatusStrip` (with `Shorten`, also used for the header title), `TerminalStatus.Kind`/`StateText`/`Chips`/`TelnetClients` (`StatusKind`, `StatusChip` with priorities No connection 40, Delay 30, New output 20, telnet 10). Tests: `TerminalChromeLayoutTest`, `StatusStripTest` (in `TerminalChromeLayoutTest.cs`), four new cases in `TerminalStatusTest`; mutations of the chip priority, the ellipsis trim, the telnet count in the title and the gutter each failed tests.
- Game side: `kOS.Screen.KOSStyle` (palette, chrome font with `…`/`·` fallbacks, generated icon textures kept for the game, flat button/toggle/slider), `TermWindow` (header, strip, popover as window `UniqueId + 11`, layout used by drawing, selection, wheel, resize and `NotifyOfScreenResize`), `KOSManagedWindow.ContainsPoint` (virtual; the popover counts as the terminal for focus clicks).
- Choices made while implementing (within the plan): the resize-drag minimum is the script minimum of 15 × 3 cells; a press in the characters' padding also starts a selection; a UI-scale change keeps the cells and resizes the window; the popover's settings are written only when changed (fixes the float-rounded `TERMINAL:BRIGHTNESS` while the window is open); the state under delay at the prompt is `Delay 2.4 s` (as the title); the missing-texture screen still checks the old textures.

### Step 2 plan (approved by the user 2026-10-20 with Q1–Q3 as proposed; implemented; Proton check at the end of Phase 4)

Geometry and scrolling, built on step 1's `TerminalChromeLayout` (the scroll bar goes in its gutter). The choices marked **(Q)** were proposals; the user approved them as written: Q1 the character height is saved, Q2 a window larger than the screen keeps its cells, Q3 a press on the track pages.

Headless (`kOS.Safe.Screen`), each with tests:

- `TerminalGeometry`: a CPU's saved window: position (`x`, `y`, screen pixels), `columns`, `rows`, and **(Q1)** the character height. `Format`/`Parse`: a small `key = value` text file with a header line, invariant culture; tolerant (CR LF, unknown keys, missing keys, garbage): a value that is missing, not a number or outside the script limits (`TerminalStruct` 15–`MAX_COLUMNS`, 3–`MAX_ROWS`, 4–48 px) is dropped and the default for it is used.
- `TerminalGeometryFile`: the store, with `CommandHistoryFile`'s failure policy (path asked once, saves through `ArchiveFileWriter` so a failed save keeps the old file, I/O failures logged once per CPU and never shown on the terminal, a file over a few KiB not read). Path **next to the history**: `saves/<save>/kOS/terminal/<persistentId>[-index].txt`, same key and same flight-only rule as the history (no file in the editor scene or without a `persistentId`: upstream behavior).
- `WindowPlacement.KeepOnScreen(rect, screenWidth, screenHeight, …)`: a window that fits is moved wholly on screen; **(Q2)** a window larger than the screen keeps its cells (a script's `TERMINAL:WIDTH` is never changed by placement) and is put with its header's left part on screen at the top left. While moving by the header, a part of the header (at least the title's start and a strip of buttons, about 48 px) always stays on screen, so a window can always be caught again.
- `TerminalResize`: edge hit-testing and the drag. From the pointer, a band at the frame (4 px at UI scale 1, scaled; 12 px along the edges at the corners) gives the edges grabbed: left, right, top, bottom, the four corners; the header buttons and the grip keep their own clicks. From the start rectangle, the edges and the pointer's movement: the new rectangle **snapped live to whole cells**, the opposite edge staying put (left/top move the window), between the script minimum 15 × 3 and the maximum cells, with the cells it gives for the overlay. The step 1 grip becomes the bottom-right corner of the same code.
- `TerminalScrollBar`: from the scrollback lines, the rows, the view offset and the track height: whether it shows (view scrolled back, or a thumb drag in progress), the thumb's position and size (at least a minimum length); from a thumb position, the view offset; a press on the track above or below the thumb pages (**(Q3)**; otherwise only the thumb moves).

Game side (`TermWindow`, `KOSStyle`):

- **Restore:** in `AttachTo`, after the config defaults, the saved cells, character height (Q1) and position are applied; a boot script that sets the size still wins (D3, last resize). Placement is checked at the first draw (the screen size is known in `OnGUI`).
- **Save:** whenever the geometry differs from what was saved, no mouse button is held and no drag is in progress, at most once every 2 s, and once more on `OnDestroy` (scene change, vessel unload). Every source counts: moving, resizing, the popover's character size, a script's `TERMINAL:WIDTH`/`HEIGHT`/`CHARHEIGHT`. Not rolled back by a quickload or revert (as the history, H3).
- **Keep on screen** on: open, restore, a change of the game's resolution (`Screen.width`/`height` compared in `OnGUI`), a UI-scale change (after step 1's refit), a size change by a script or the popover, and the end of a move.
- **Edge resizing:** the edge under the pointer is drawn in the accent color (no OS cursor change: `Cursor.SetCursor` would replace KSP's cursor for the whole game); the window outline follows the snapped size during the drag; the screen's `SetSize` happens **once, on release** (scripts' resize watchers and telnet clients see one resize, as with the step 1 grip); the characters stay clipped to the body meanwhile.
- **Size overlay:** while a resize drag (edge or grip) is in progress, `columns × rows` centered on the body in the chrome font on a chrome-colored box.
- **Scroll bar:** in the gutter, chrome colors; shown while the view is scrolled back (as decided), so the wheel, PageUp or a selection's edge drag brings it up; dragging the thumb moves the shared view (D1: telnet clients follow, as with the wheel); back at the bottom it hides once the drag ends; `New output` is unchanged.
- Unchanged: the render-fault fallback (its pixel-only resize stays), the selection, the wheel, focus and locks (`kOSMouseOver` covers the edges as part of the window), the telnet title.

Intentional differences: a CPU's terminal reopens where and at the size it was, also after reloading the save (upstream: always at 50,60 with the config's default size; the toolbar's default width/height now apply only to a CPU with no saved geometry); `TERMINAL:WIDTH`/`HEIGHT` (and `CHARHEIGHT`, Q1) read before any script sets them give the saved values; a window cannot be left or moved off screen.

Not in this step: cascading new windows, a scroll bar while the view is at the bottom, saving brightness/reverse or other preferences, the editor's and other windows' geometry (step 5).

In game (added to the end-of-Phase-4 pass): move and resize CPU 1, quit to the main menu and load again (same place, same cells, char size); a file deleted → defaults; resolution changed in settings (smaller) → window back on screen, cells kept; UI scale changed → refit and on screen; each edge and corner, snapping, overlay, minimum 15 × 3, left/top keep the opposite edge; buttons still clickable at the corners; scroll bar appears after PageUp, thumb drag and track clicks, hides at the bottom, telnet view follows; Alt+F12 locks while resizing at an edge.

As implemented (2026-10-20; behavior in `testing-and-build.md`, "Terminal window geometry and scroll bar (Phase 4, step 2)"):

- `kOS.Safe.Screen`: `TerminalGeometry` (with `Format`/`Parse`), `ITerminalGeometryStore`/`TerminalGeometryFile` (4 KiB read limit), `CpuTextFile` (the read/atomic-save/log-once code, now also under `CommandHistoryFile`, whose API and messages are unchanged), `WindowPlacement.KeepOnScreen`, `TerminalResize` (`ResizeEdges`, `EdgesAt`, `Drag` → `ResizeResult`), `TerminalScrollBar` (`IsShown`, `Thumb`, `ViewOffsetFor`, `SideOfThumb`); `TerminalChromeLayout` gained `ResizeBand` (4), `ResizeCorner` (12), `MinimumThumb` (16) and `ScrollTrack`. Tests: `TerminalGeometryTest.cs` (`TerminalGeometryTest`, `WindowPlacementTest`), `TerminalResizeTest.cs` (`TerminalResizeTest` with a seeded randomized drag test, `TerminalScrollBarTest`): 34 new. Mutations of the scroll bar's right-edge rule, the left edge keeping the right, nearest-cell snapping, the screen limit, the button exclusion, the offset inversion, the thumb cap, the columns range check, equality, log-once and the track's height each failed tests; making `KeepOnScreen`'s larger-than-screen line a no-op did not, as it is redundant with the clamp after it (equivalent mutant).
- Game side: `TermWindow` (`AttachTo(shared, store)` restores; `SaveGeometryIfChanged` from `Update` and `OnDestroy`; `BeginResizeAtEdge` in the window callback before the selection, `FollowResizeDrag` in `OnGUI` from the mouse's screen position, `SetSize` once on release; `KeepOnScreen` before and after `GUI.Window`; `DrawScrollBar`, `DrawResizeFeedback`; the character group clipped to the body), `kOSProcessor.CpuFilePath` (history and geometry), `KOSStyle.ScrollTrack`.
- Choices made while implementing (within the plan): the window is kept wholly on screen at every draw, so a move by the header stops at the screen's edges (this covers the plan's 48 px header rule, which was not needed); no file is written for a window whose geometry never changed; the geometry is checked every 0.5 s and saved at most every 2 s; a telnet client's size is saved like any resize (D3); the top-right corner does not resize (the close button keeps its clicks); a press above or below the thumb moves by the screen's rows (PageUp moves 10); edges under the settings popover do not resize; the size box reads `80x24`, as the strip.

### Step 3 plan (2026-10-20; 3a done; 3b and 3c approved and implemented; Proton check at the end of Phase 4)

From the step 3 decisions above. Three parts: **3a** a font prototype whose result decides 3c (done); **3b** the look and the settings move (independent of the font); **3c** the font fallback order.

**3a. Font prototype (done 2026-10-20; throwaway code, not in the repo).**

- Approved by the user ("go for step 3a"); one launch to the main menu, no interaction; the game closed with the user's permission. Probe: the dev instance's `dev-tools/font-probe/FontProbe.cs` (a separate `KSPAddon` assembly deployed as `GameData/kOSIM-FontProbe/`, removed after the run); font **JetBrains Mono 2.304** (OFL 1.1, `JetBrainsMono-2.304.zip` SHA-256 `6f6376c6ed2960ea8a963cd7387ec9d76e3f629125bc33d1fdcd7eb7012f7bbf`, from the GitHub release over HTTPS).
- Ways tried: (1) private GDI registration (`AddFontResourceEx`, `FR_PRIVATE`), early (before kOS's `AssetManager` lists fonts) and late; (B, added while preparing: the game's `UnityEngine.TextRenderingModule.dll` shows that the public `new Font(path)` calls Unity's internal `CreateFontFromPath`) loading the file by path; (2) TextCore's `FontEngine`, checked against the game's assemblies without a launch. (3), an asset bundle built with the Unity 2019.4.18 editor, was not tried.
- **Result: no-go.** Unity 2019.4 lists and finds OS fonts only among the files in `C:/windows/Fonts`; the GDI registration succeeds but the name gives Arial; `new Font(path)` gives an empty non-dynamic font; `FontEngine` cannot produce a `Font`. Details and cell metrics: `testing-and-build.md`, "Fonts under Proton (step 3a probe, 2026-10-20)". Also found: the terminal's font under Proton is **Courier New Bold** today; Lucida Console is the only other Latin monospace font in the prefix.
- **User decision (2026-10-20): no bundled font**, no asset bundle (Unity editor) and no hand-made bundle; 3c ships the better fallback order only.

**3b. The look and the settings (headless where possible; approved "go for step 3b", implemented 2026-10-20).**

- `kOS.Safe.Screen.TerminalPalette` (new): the colors as data (RGBA) for the cells (text, background, unpowered text, selection, cursor) and the chrome (header/strip, panel, chips, text, dim text, frame, accent, state dots, scroll bar). Proposed: text a soft green near `#7EE08F` on a near-black `#0F1311`, unpowered text a neutral grey, chrome dark neutral greys a step lighter than the body, the accent (focus border, edge highlight) the same green. Upstream's pure `#80FF80` on black and the bezel are gone. `TermWindow`'s fixed colors and `KOSStyle`'s palette come from it.
- Tests (WCAG contrast ratios, computed headlessly): text on background ≥ 7:1, and ≥ 4.5:1 at the default brightness 0.7 (text faded into the background); unpowered text ≥ 4.5:1; chrome text ≥ 7:1 and dim chrome text ≥ 4.5:1 on the chrome; the accent ≥ 3:1 against the chrome and the frame color; text under the selection ≥ 4.5:1; every state dot ≥ 3:1 on the strip.
- **Header:** title, then **smaller / larger text** buttons (icons generated in code, as the gear was), then close. `TerminalChromeLayout` gives their rectangles in place of the settings button; `TerminalResize.EdgesAt` keeps excluding every header button (tests updated). Steps of 2 px within `CHARHEIGHT`'s 4–48, applied between passes as now; saved per CPU by step 2's geometry file.
- **Removed:** the settings popover and its code (`DrawSettings`, `settingsRect`, its mouse area and the click-outside close; `KOSManagedWindow.ContainsPoint` stays virtual).
- **Mod options** (`config.xml`, shown by the current toolbar panel, which lists config keys generically; step 4 restyles and groups it): new `TerminalVisualBeep` (bool, false: initial `TERMINAL:VISUALBEEP` of a new terminal, as `TerminalBrightness` is for brightness) and `TerminalKeyClick` (bool, false: GUI terminals click on keys, live; it was a per-window toggle). The existing `TerminalBrightness` (0.7), `TerminalFontDefaultSize`, `TerminalFontName`, default width and height stay. Older `config.xml` files load (missing keys take their defaults); no key is renamed or removed.
- Unchanged: the script API (no theme suffix), the telnet output, the grid rendering method, the paste box (step 5), focus and locks.

As implemented (3b, 2026-10-20; behavior in `testing-and-build.md`, "Terminal look and settings (Phase 4, step 3b)"):

- `kOS.Safe.Screen.TerminalPalette` with `PaletteColor` (`FromHex`, `WithAlpha`, `Over` blended in sRGB, `RelativeLuminance`, `ContrastRatio`); colors as proposed (text `#7EE08F` on `#0F1311`), selection alpha lowered from 0.35 to **0.25** so that selected text keeps 4.5:1. `KOSStyle`'s palette and `TermWindow`'s cell colors come from it (`KOSStyle.ToColor`). Tests: `TerminalPaletteTest` (11, the plan's ratios plus the cursor's character, hovered buttons and chips, the scroll thumb, the attention states differing from Ready, the chrome a step lighter than the body).
- `TerminalChromeLayout`: `SmallerTextButton`, `LargerTextButton`, `HeaderButtons` (replace `SettingsButton`), `CHARACTER_HEIGHT_STEP` (2), `SmallerCharacterHeight`/`LargerCharacterHeight` (clamped to 4–48); `TerminalResize.EdgesAt` excludes `HeaderButtons`. Tests: the button order and tiling, `CharacterHeightStepTest`, the resize exclusion of each button. Gate 1228 → **1241**. Mutations (12: text, unpowered, dim and chrome colors, frame, a state dot, selection alpha, the resize exclusion, the step, the clamp, a button's place, the title's end) each failed tests.
- `Config`: `TerminalVisualBeep` (`VISUALBEEP`) and `TerminalKeyClick` (`KEYCLICK`), bool, false, after `TerminalBrightness`; not added to `IConfig` (public interface other mods may implement). User docs: `doc/source/structures/misc/config.rst`, `terminal.rst`.
- **Deviation from the plan:** the size buttons are a small and a large **bold `A` in the chrome font**, not icons generated in code: a generated `A` was unreadable at the 12 px icon size (UI scale 1). The text is plain ASCII, which KSP's font has under Proton. A button at the end of the range is dim and reports no click.
- Kept: `KOSStyle.Toggle`/`Slider`/`Button` (unused until steps 4–5); `KOSManagedWindow.ContainsPoint` stays virtual.

**3c. The font fallback order (no-go path of 3a; approved "let's go do it", implemented 2026-10-20).**

- `kOS.Safe.Screen.TerminalFontChoice` (new, headless): the ordered names the terminal tries. A font the user picked comes first; not picked (`_not_chosen_yet_` or the first-run placeholder `Choose Font`, `Bootstrapper.CheckForUnpickedFont`) → not tried. Then upstream's fallbacks in upstream's order (`Consolas Bold`, `Consolas`, `Monaco Bold`, `Monaco`, `Liberation Mono Bold`, `Liberation Mono`, then the Courier entries, `Arial` last) with **`Lucida Console` inserted before the Courier entries**, so Proton gets Lucida Console instead of Courier New Bold and native Windows, macOS and Linux keep what they had. Tests for each case. `TermWindow.tryFontNames` uses it.
- Unchanged: the font picker and `TerminalFontName` (a picked font, Courier New Bold included, is kept); the chrome keeps KSP's UI font; the editor (step 5).
- In game: Lucida Console's taller ascent (17 against Courier's 14 at 16 px, line height 21 against 18) puts its baseline lower in each row: check the bottom row's descenders and underscores against the field's padding (at least 4 px) at char sizes 8–24, and `█`/box drawing between rows.

As implemented (3c, 2026-10-20; behavior in `testing-and-build.md`, "Terminal font fallback order (Phase 4, step 3c)"):

- `kOS.Safe.Screen.TerminalFontChoice` (`Fallbacks`, `IsPicked`, `NamesToTry`; constants `NOT_CHOSEN_YET`, `CHOOSE_FONT`, `LUCIDA_CONSOLE`); a blank `TerminalFontName` also counts as not picked. `TermWindow`'s `tryFontNames` array is replaced by `pickedFontName` (the name last looked up) and `NamesToTry`. Tests: `TerminalFontChoiceTest` (6: a pick first and kept, placeholders not tried, upstream's order with Lucida Console just before Courier, Proton gets Lucida Console, Windows/macOS/Linux font sets keep upstream's result, fresh lists). Gate 1241 → **1247**. Mutations (7: Lucida Console removed, first, after Courier New Bold; each placeholder and blank tried; the pick last) each failed tests.
- `Bootstrapper.CheckForUnpickedFont` and `Config` keep their literals (the tests pin the constants to them); the editor's own list (`KOSTextEditPopup`) is unchanged until step 5.

Intentional differences (3b and 3c implemented): the terminal's colors differ from upstream, and its default font under Proton (Lucida Console instead of Courier New Bold); the window has no reverse, visual beep, key click or brightness controls (scripts and mod options keep them; reverse only by script); key click becomes a global mod option instead of a per-window toggle (not saved before either); new `CONFIG:VISUALBEEP`/`CONFIG:KEYCLICK` and their `LIST CONFIG` rows (additive); the selection is fainter (0.25); a font picked in mod options is kept.

In game (end-of-Phase-4 pass; the 3a launch is done): the look at UI scales 1.0 and 1.3 and char sizes 8–24 (readability, cursor and selection alignment with Lucida Console, bottom-row descenders), unpowered colors, the size buttons (readable `A`s, dim at 4 and 48, the window following, and their saving), the new mod options in the toolbar panel (visual beep for a new terminal, key click live), the font picker still working and a picked font kept, an older `config.xml` loading.

### Step 4 plan (2026-10-20; Q1–Q4 answered as proposed; approved and implemented; Proton check at the end of Phase 4)

The toolbar panel (`KOSToolbarWindow`, opened from KSP's launcher or Blizzy's toolbar) in two tabs, in the terminal's flat style. From the Phase 4 decision "Toolbar panel". Choices marked **(Q)** were proposals; the user chose each as proposed (2026-10-20): Q1 power read-only, Q2 flat style, Q3 number fields apply on Enter or losing focus, Q4 labels in words with the alias.

Today (upstream): one `GUILayout.Window` in KSP's skin, auto-sized. On the left, a scroll list of CPUs by vessel: part name and tag, a `power on/off/starved` box and a terminal icon button that opens or closes the window (a telnet variant of the icon). On the right, every config key in `Config`'s order, labelled by its alias (`DEFAULTFONTSIZE`, `TPORT`, …), with the description as a tooltip in one line at the bottom that otherwise shows the telnet status; `Emergency Suppress` among them. In the editor (no CPUs are tracked there) a `Reread Boot Folder` button. Hovering a CPU row highlights its part purple.

Headless (`kOS.Safe.Screen`), each with tests:

- `ToolbarPanelLayout`: at a UI scale (as `TerminalChromeLayout`, clamped 0.5–3): a fixed width (about 340 px at scale 1), header (title `kOS <version>`), tab bar, content, footer; the content's height from its rows, the window's height capped by the room the launcher leaves (upstream's `rectToFit`), and a scroll area when the content does not fit. Tests: no negative or overlapping parts, scale, cap and scroll.
- `ToolbarCpuList`: from one entry per CPU (vessel, part name, tag, the D2 state kind and text, window open, telnet clients), in the order kOS sorts them (active vessel first, then by distance; parts by depth): rows with a vessel heading at each new vessel, one row per CPU; the empty message. Tests: grouping, a CPU without a vessel, empty.
- `ToolbarSettings`: the settings tab's groups from the config keys, by their names: **Terminal** (font, `DEFAULTFONTSIZE`, `DEFAULTWIDTH`, `DEFAULTHEIGHT`, `BRIGHTNESS`, `VISUALBEEP`, `KEYCLICK`), **Telnet** (`TELNET`, `IPADDRESS`, `TPORT`), **Boot** (editor only: reread the boot folder); `SUPPRESSAUTOPILOT` goes to the footer; a key it does not know goes to **Other**, drawn by its type as today, so no key is ever hidden. **(Q4)** Labels in words (`Character size`, `Width`, `Port`, …); the hovered setting's description and its script name (`CONFIG:DEFAULTFONTSIZE`) inline at the bottom. Tests: every key exactly once, unknown keys in Other, order kept.
- `TelnetPanelStatus`: the footer's telnet line (`Telnet off`; `Telnet 127.0.0.1:5410 · 1 client`) and whether a restart is needed (the port set differs from the one listening). Tests: plurals, off, pending restart.

Game side (`KOSToolbarWindow`, `KOSStyle`, `TermWindow`):

- **CPUs tab** (shown each time the panel opens): under each vessel's name, each CPU as a row: part name and tag, the D2 state with its colored dot (`Ready`, `Running main.ks`, `Waiting in …`, `No power`, `Off`, …: power is read from the state, as in the terminal's strip), a telnet count when attached, and an `Open`/`Close` button (the user's click takes the keyboard, as today). **(Q1)** No power button (read-only, as upstream). Hovering the row highlights the part (upstream's purple, restored after). The state of a CPU whose window is closed is computed while the panel shows it (today only while its window is open or telnet is attached): `TermWindow` gains a read-only `Status`.
- **Settings tab:** the groups above with `KOSStyle` controls: toggles, the brightness slider with its value, number fields, the font and address pickers (`ListPickerDialog`, unchanged until step 5). **(Q3)** A number field applies on Enter or when the field loses focus (upstream: only on Enter; a click elsewhere threw the edit away), clamped by `Config` as now. Telnet: the toggle (the opt-in, non-loopback and background-simulation dialogs unchanged), address, port, and a `Restart telnet` button when the port set is not the one in use (the address already restarts on its own). A line points to KSP's difficulty settings for the other kOS options.
- **Footer** (both tabs): `Emergency suppress autopilot` (warning color while on; pressing it on still closes the panel, as upstream) and the telnet line.
- **(Q2)** The panel drawn in the flat style (`KOSStyle`, our header, following KSP's UI scale), not KSP's skin. Unchanged: where it opens (under or beside the launcher button, kept within the room it leaves), the launcher and Blizzy buttons, the scenes, hide-UI (`OBEYHIDEUI`), the `kOSMouseOver` lock, saving `config.xml`, the `CONFIG` suffixes and `config.xml` keys.

Intentional differences: the panel's look, size and layout; settings labelled in words (aliases in the description); `power on/off/starved` replaced by the D2 state; terminal icon buttons replaced by `Open`/`Close`; (Q3) number fields apply on losing focus; a restart button for a pending telnet port.

Not in this step: the font and address pickers' look and other dialogs (step 5), new settings, saving the panel's position, a power control (unless Q1), cascading terminal windows.

In game (end-of-Phase-4 pass): both tabs at UI scale 1.0 and 1.3, in flight with two vessels (the dev save's CPUs 1 and 2) and in the VAB (no CPUs, Boot); open/close a terminal from the panel and the keyboard following; states of a closed terminal's CPU (off, running); hover highlight on and off; each setting applied and saved (`config.xml` after a scene change), a number field by Enter and by clicking away; the font picker; telnet on, off, address and port with restart, the permission dialogs; emergency suppress; hide UI with `OBEYHIDEUI`; the panel clamped near the launcher at the top and the bottom positions; Alt+F12 locks.

As implemented (2026-10-20; behavior in `testing-and-build.md`, "Toolbar panel (Phase 4, step 4)"):

- `kOS.Safe.Screen`: `ToolbarPanelLayout` (width 340, header 22, tabs 22, padding 6, CPU rows 34, setting rows 22, notes 16 at scale 1; `WindowHeight`, `Content`, `Description`, `Footer`, `SuppressButton`, `TelnetLine`, `RowWidth`, scrolling `ClampScroll`/`ScrollThumb`/`ScrollFor`), `ToolbarCpuList` (`ToolbarCpuEntry`, `Rows`, `Height`, `PartName`, `CpuLabel`, `Detail`, `EmptyNote`), `ToolbarSettings` (`Sections`, `FooterKey`, `Detail`; groups `Terminal`, `Telnet`, `Other`) and `ToolbarNumberField`, `TelnetPanelStatus`. Tests: `ToolbarPanelTest.cs` (20 in five fixtures) and 1 palette case (dim, accent and hovered text on the panel). Gate 1247 → **1268**. Mutations (19: height cap and minimum, the description's room, thumb minimum, scroll clamp, the scroll bar's room, vessel headings twice, the tag, plurals, suppress in Settings, duplicates, case, unknown keys, focus loss, blank typing, Enter, restart while off, IPv6 brackets) each failed tests.
- Game side: `KOSToolbarWindow` (drawing replaced: `GUI.Window` without a skin, `DrawContent` in a clipped group, own scroll bar, `DrawCpuRow`, `DrawSettings` measuring and drawing in one routine, `DrawNumberField`, `DrawSlider`, `DrawTelnetRestart`, `DrawDescription`, `DrawFooter`; `ClearPartHighlight`; `ApplyPendingNumber` on tab change and close); `TermWindow.Status`/`StatusWanted`; `KOSStyle.Tab`/`TextField`. The skin, GUILayout counters and the power/icon styles are gone.
- Choices made while implementing (within the plan): a press anywhere in the panel takes the keyboard from a number field (IMGUI keeps it otherwise), so Q3's "click away" works inside the panel; a press outside the panel leaves the field typing until the panel closes; a press outside the content does not reach rows scrolled out of view; `Reread boot folder` moved to Settings, Boot (the CPUs tab says so in the editor); the restart button goes through the existing stop/start path (permission dialogs as for turning telnet on); the brightness slider rounds to hundredths.


### Step 5 plan (2026-10-20; approved by the user with Q1–Q4 as recommended; implemented; Proton check at the end of Phase 4)

The windows besides the terminal and the toolbar panel adopt the flat style (`KOSStyle`, KSP's UI scale), from the Phase 4 decision "Other windows". User approval (2026-10-20): "Go with recommended … things must be simple, elegant and modern", Q1–Q4 as recommended:

- **Q1 (EDIT-04):** the editor is a free window: moved by its header, resized by its edges and corners like the terminal (not snapped to cells); it opens under its terminal when there is room, otherwise where it fits, and stays wholly on screen.
- **Q2:** the editor's geometry is kept for the session only (not saved; the per-CPU geometry file is unchanged).
- **Q3:** the editor's confirmations name their actions (`Save and close` / `Close without saving` / `Cancel`; `Save, then open` / `Open without saving` / `Cancel`; `Discard and reload` / `Cancel`; `Retry` / `Cancel`).
- **Q4:** Esc cancels in the editor's confirmation and in the name tag; the editor has a one-line strip with the caret's line and column and a modified mark.

Today (upstream): the paste box is a translucent default-skin `GUILayout.Window` with a 10 px hint (PASTE-UI-01). The editor (`KOSTextEditPopup`) uses KSP's skin with yellow text and its own font list (no Lucida Console), is glued under the terminal (EDIT-04: at 1024×768 with a 50×36 terminal its lower part is off screen), pulls the view back to the caret on every repaint (EDIT-03), and a failed save from its Save button or Ctrl+S rethrows out of `OnGUI`. Its confirmation (`DelegateDialog`) is a KSP-skin window at a fixed offset with Yes/No/Cancel. The name tag (`KOSNameTagWindow`) opens at the part's screen position, maybe off screen, and shows `part.name`. The pickers (`ListPickerDialog`: font, address) open under the toolbar panel in KSP's skin and may leave the screen.

Headless (`kOS.Safe.Screen`, each with tests):

- `EditorScrollFollow` (EDIT-03): the view follows the caret only when the caret or the text changed since the last frame; a wheel or scroll-bar move is kept until the next caret move or edit; a resize clamps the scroll position without resetting it.
- `EditorCaretPosition`: the caret's line and column from its index.
- `FlatWindowLayout`: a small flat window's parts at a UI scale (header, content, button row, minimum size), shared by the editor, its confirmation, the name tag, the pickers and the paste box.
- `WindowPlacement` additions: below an anchor or wherever it fits (the editor), beside an anchor on either side (the pickers), centered over a window (confirmations, the paste box); every result wholly on screen.

Game side:

1. **Paste box:** an opaque flat panel over the terminal; the preview in the terminal's font and colors, the hint at the chrome's size, `Run`/`Cancel` buttons. Behavior and keys unchanged.
2. **Editor:** flat header (path, modified mark, `Reload`, `Save`, `Close`; Ctrl+R/S/E kept), a flat text area in the palette with the terminal's font choice (step 3c order), the strip (`Ln 12, Col 4 · modified`; a save error shown there, still printed on the terminal, not thrown out of `OnGUI`), EDIT-03, Q1 and Q2.
3. **Confirmation** (`DelegateDialog`): flat, centered over the editor, on screen, Q3 labels, Esc cancels.
4. **Name tag:** flat, the part's title, on screen, Esc cancels; Enter, the first focus, the editor lock and input ownership unchanged.
5. **Pickers:** flat, beside the toolbar panel and on screen; the current entry in the accent color, rows lit on hover, the wheel and our scroll bar, a close button in the header.

Not in this step: script GUIs (`GUIWindow`; their skin is script API), KSP's `PopupDialog`s (telnet permission, settings checker, bootstrapper alerts: KSP's own UI), the rest of EDIT-05 (find/replace, line numbers, highlighting), font previews or a filter in the pickers.

Intentional differences: these windows' look; the editor's placement and resizing (EDIT-04), its scrolling (EDIT-03), its strip and save errors; the confirmation labels; Esc in the confirmation and the name tag; the name tag shows the part's title; the editor uses the terminal's font order.

In game (end-of-Phase-4 pass): each window at UI scale 1.0 and 1.3; the editor opened with `edit` from CPU 1 (placed on screen at 1024×768, moved, resized by each edge, kept on screen after a resolution change), the wheel and scroll bar reading far from the caret without being pulled back, typing and arrows bringing the caret back, Ctrl+S/R/E and the buttons, each confirmation with its labels and Esc, a save error (read-only volume or a bad path) shown in the strip; the paste box readable and answered by Enter, Esc and the buttons; the name tag in flight and in the VAB (Enter, Esc, Cancel, Accept, no part detached in the VAB, on screen near the edges); the font and address pickers beside the panel, picking, scrolling, closing; Alt+F12 `kOSMouseOver` over each.

As implemented (2026-10-20; behavior in `testing-and-build.md`, "Other windows (Phase 4, step 5)"):

- `kOS.Safe.Screen`: `FlatWindowLayout` (header 20, strip 18, padding 6, rows 22, gap 4, buttons at least 56, dialogs 340, name tag 240, editor minimum 260 × 140 at scale 1; `Header`, `CloseButton`, `HeaderButtons`, `Title`, `Body`, `Strip`, `Grip`, `Buttons`, `ScrollTrack`; `FlatWindowBottom`), `WindowPlacement.BelowOrBeside`/`Beside`/`CenteredOver` (a side fits when the window is wholly on screen along its axis; the other axis is moved on), `WindowResize` (`EdgesAt` with the header's buttons kept, `Drag` to the pixel), `PixelScroll` (the toolbar panel's scroll math moved here, `ToolbarPanelLayout` delegates; plus `Reveal`), `EditorScrollFollow`, `TextPosition`. Tests: `FlatWindowTest.cs` (30 in six fixtures). Gate 1268 → **1298**. Mutations (16: following every frame, ignoring text changes, the reveal order, each side order, the fit test, centering off screen, growth and the room limit of a resize, the kept buttons, the left edge, the body's bottom, squeezing, the header's half, the column, the toolbar thumb minimum) each failed tests.
- Game side: `KOSTextEditPopup` rewritten around the same document logic (`ProcessWindow`, `DrawText`, `DrawStrip`, `PlaceNextToTerminal`, `KeepOnScreen`, `BeginResizeAtEdge`/`FollowResizeDrag`, `TrySave`); `DelegateDialog` (`Invoke` with a title; the old overload kept; `Answer`), `KOSNameTagWindow`, `ListPickerDialog` (`Summon(Rect besideRect, …)`; the old overload kept), `TermWindow.DrawPasteConfirmation`; `KOSStyle.FlatWindow`, `FlatWindowFrame`, `Paragraph`, `MeasureHeight`, `TextArea` (caret and selection colors set for that control only), `Button(…, primary)`; `FlatScrollBar` (game side, from the toolbar panel's drawing, which now uses it).
- Choices made while implementing (within the plan): the confirmation keeps upstream's choice order, the first drawn as primary; the editor's text wraps at the window's width (no horizontal scrolling); the editor can be resized without the keyboard (as the terminal) but not under a confirmation; a user's open focuses the editor's text; the name tag is movable by its header and has a close button; the pickers are 12 rows high at most and scroll to the current entry when opened; the address picker's header no longer says to restart telnet (a new address restarts the server by itself).

In game (end-of-Phase-4 pass), in addition to the paragraph above: the editor's text focused on `edit` without a click; the confirmation's Esc in flight not opening KSP's pause menu, and the name tag's Esc in the VAB (its lock is `EditorLogic`'s, not the control lock: the pause menu may open too); the disabled look of an unfocused editor; the caret and selection colors in the editor's text.

### Exit criterion

The terminal is comfortable at supported resolutions/scales, operational state is understandable, and the redesign passes the existing compatibility and in-game safety checks.

## Testing and release discipline

“No regressions” is the goal, not a guarantee that any test suite can provide. Use complementary layers:

| Layer | Main coverage |
| --- | --- |
| Unit tests | Buffer composition, dimensions, history, document transactions, persistence, protocol states |
| Headless integration | Real interpreter/compiler/session behavior together, not only screen substitutes |
| Compatibility fixtures | Existing scripts, dashboards, KSM, saves/craft, and public APIs |
| Seeded randomized tests | Resize/scroll/input sequences and fragmented network streams; retain reproducible seeds |
| Manual KSP checklist | Flight controls, focus, power/connectivity, scene transitions, fonts, and mod interactions |
| Performance checks | Large paste, sustained output, retained history, repeated resizing, slow/multiple telnet clients |

Compare unchanged behavior against upstream where feasible. For intentional bug fixes, document the expected difference rather than treating every upstream result as correct.

For each change:

- Add a regression test before or alongside the fix where feasible.
- Run the local tests and record commands, outcomes, skipped checks, and remaining manual verification.
- Validate game-dependent changes under Proton in the dev instance; record game/Proton versions and use disposable test saves.
- Keep changes focused and compatibility decisions explicit.
- Measure performance before claiming an optimization or leak fix.
- Preserve loopback/opt-in telnet exposure defaults.
- Keep the in-game runtime compatible with KSP; modern tooling does not imply a runtime upgrade.

## Release checkpoints and deferred scope

Recommended checkpoints:

1. **Reliability release:** verified local regression tests and targeted safety fixes, current appearance retained; game-dependent behavior verified under Proton. (Phase 1 reached it.)
2. **Structural release:** tested session/adapters and explicit input/lifecycle contracts, current renderer retained. (Phase 2 reached it.)
3. **QoL and presentation release(s):** staged productivity improvements followed by modernized UI.

These are checkpoints, not mandatory dates or a requirement to implement every report item before releasing.

Keep compiler/VM optimizations, steering changes, language changes, a full embedded IDE, and a new structured remote-control interface outside this roadmap unless separately justified. They require their own designs and regression strategies.
