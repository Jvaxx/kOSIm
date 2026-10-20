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
| 4 — Modern, simpler UI | Decisions agreed 2026-10-20. Step 1 (terminal chrome) approved and implemented 2026-10-20: headless-tested, full build clean, **not yet checked in game**. |

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
- **Settings popover** (from the header button): reverse screen, visual beep, key click, a horizontal brightness slider, character size. Nothing else on the working surface. Script API unchanged (`TERMINAL:REVERSE`, `VISUALBEEP`, `BRIGHTNESS`, `CHARHEIGHT`, …).
- **Chrome scale:** header, strip and popover follow KSP's UI scale; cells follow `CHARHEIGHT`.
- **Geometry:** each CPU's terminal position and size saved in the save folder next to its history (`saves/<save>/kOS/…`, by `persistentId`); a window that would open off screen is moved back on, also after a resolution or UI-scale change. Resizing by the grip or an edge, snapped to whole cells, with a size overlay while dragging.
- **Scroll bar:** thin, shown only while the view is scrolled back; draggable.
- **Themes:** classic green stays the default; Amber and a modern neutral theme (light grey on near-black) as options.
- **Toolbar panel:** two tabs. *CPUs* (default): per vessel, each CPU's D2 state, power and Open/Close; hovering highlights the part; *Suppress autopilot* stays visible in the footer with the telnet status. *Settings*: grouped Terminal defaults, Telnet (live status, "restart to apply"), Boot (reread boot folder); the hovered setting's description inline. Config keys and `config.xml` stay compatible.
- **Other windows:** the paste box (PASTE-UI-01), the editor, the name tag and dialogs adopt the style; **EDIT-03** (the editor follows the caret on every repaint) is fixed with the editor.
- Telnet is unchanged (its title already carries the state).

### Steps

Each with headless tests; the Proton checks of all steps are done in one pass at the end of Phase 4 (user decision 2026-10-20):

1. **Terminal chrome:** style module, header, flat frame, status strip, settings popover, chrome UI scale.
2. **Geometry and scrolling:** saved geometry, keep on screen, edge resizing with the size overlay, the scroll bar.
3. **Themes.**
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
