# kOS Improved: findings, improvement plan, and compatibility constraints

## 1. Purpose and review baseline

This document is the starting point for a possible improved fork of [KSP-KOS/KOS](https://github.com/KSP-KOS/KOS). It records a source-level review, user-facing improvement opportunities, relevant upstream issues and pull requests, and the tests needed before shipping changes.

The central conclusion is:

> Improve the terminal as a dependable development and flight-operations tool, not merely as a differently textured window. Prioritize data integrity, bounds safety, input ownership, clipboard support, readable controls, and explicit operational state.

### Baseline

- Original checkout: `/home/jvz/Projects/KOS`.
- Reviewed commit: `d4388439c712994e47258ed7514e863f79da632b`.
- Tag: `1.6.0.1`.
- Baseline source: <https://github.com/KSP-KOS/KOS/tree/d4388439c712994e47258ed7514e863f79da632b>.
- Proposed fork workspace: `/home/jvz/Projects/KOS-Improved`.
- This document does not imply that the fork's source has been copied or initialized.

All source paths and line numbers below refer to that baseline. GitHub issue/PR numbers were checked against the repository API during the review. Their current status may change; recheck before implementing or incorporating upstream work.

### Scope and limitations

Reviewed areas include the terminal, popup editor, managed windows, toolbar/settings, script-created widgets, screen buffers/snapshots, fonts, persistence, telnet, logging, test project, and CI workflows. Some steering and vessel-identity code was inspected in connection with upstream PRs.

- No source modifications were made during the review.
- No KSP session, build, unit-test run, allocation profile, or network reproduction was performed.
- Source-visible defects are not described as in-game reproductions.
- Existing screenshots under `doc/source/_images/` illustrate historical UI; they are not proof of the precise current appearance.
- An open upstream issue is evidence of a report, not proof that its original behavior remains reproducible on every supported configuration.

### Evidence terminology

| Label | Meaning |
| --- | --- |
| **Code defect** | The problematic operation or state transition is visible in the reviewed source. A regression test and, where relevant, in-game reproduction are still required. |
| **Code-supported risk** | The implementation has a plausible failure mode, but its frequency, triggering conditions, or full consequences are not established. |
| **Reported problem** | An upstream issue documents symptoms. Do not assume the reviewed code path is its confirmed root cause. |
| **Design improvement** | A proposed usability, architecture, or maintainability improvement rather than a claim of incorrect behavior. |

### Priority terminology

- **P0:** resolve before relying on the editor for valuable files; potential silent data loss or wrong-file writes.
- **P1:** high-value correctness, reliability, input safety, or everyday usability.
- **P2:** important UX, diagnostics, performance, and architectural improvements.
- **P3:** optional polish or larger exploratory features.

These are proposed fork priorities, not upstream severity labels. Network robustness and file safety may deserve promotion depending on how the fork will be used.

## 2. Executive priorities

1. Fix editor change detection and save-before-switch state handling.
2. Add and execute regression tests for terminal bounds, overlays, resizing, and file operations.
3. Add terminal paste/copy and scale the entire window chrome, not just its font.
4. Centralize focus/input ownership and verify that UI actions cannot accidentally affect flight controls.
5. Expose running program, power, communications, and input-consumer state.
6. Introduce an independent terminal/session model with Unity and telnet adapters.
7. Improve the editor, file browser, and error inspection without attempting a full IDE immediately.
8. Preserve raw file bytes and harden telnet stream parsing/shutdown.
9. Bound retained history and profile allocations before declaring particular memory-leak fixes.

## 3. Source map

| Area | Principal files |
| --- | --- |
| In-game terminal | `src/kOS/Screen/TermWindow.cs` |
| Command input/history | `src/kOS/Screen/Interpreter.cs`, `src/kOS.Safe/Screen/TextEditor.cs` |
| Communications-delayed commands | `src/kOS/Screen/ConnectivityInterpreter.cs` |
| Character buffers | `src/kOS.Safe/Screen/ScreenBuffer.cs`, `SubBuffer.cs`, `ScreenBufferLine.cs`, `ScreenSnapshot.cs` |
| Script-facing terminal API | `src/kOS.Safe/Encapsulation/TerminalStruct.cs`, `TerminalInput.cs` |
| In-game file editor | `src/kOS/Screen/KOSTextEditPopup.cs`, `DelegateDialog.cs` |
| Window focus/stacking | `src/kOS/Screen/KOSManagedWindow.cs`, `GUIWindow.cs`, `KOSNameTagWindow.cs` |
| Processor/settings panel | `src/kOS/Screen/KOSToolbarWindow.cs`, `src/kOS/Suffixed/Config.cs` |
| Script-created widgets | `src/kOS/Suffixed/Widget/` |
| Fonts | `src/kOS/Module/AssetManager.cs` |
| Local archive/file I/O | `src/kOS.Safe/Persistence/Archive.cs`, `ArchiveFile.cs`, `FileContent.cs`, `PersistenceUtilities.cs`, `VolumeManager.cs` |
| Telnet | `src/kOS/UserIO/TelnetMainServer.cs`, `TelnetSingletonServer.cs`, terminal mapper classes |
| Errors | `src/kOS/Logger.cs`, `src/kOS/KSPLogger.cs` |
| Lifetime/update ownership | `src/kOS/SharedObjects.cs`, `src/kOS.Safe/UpdateHandler.cs` |
| Tests | `src/kOS.Safe.Test/`, `kerboscript_tests/` |
| Automation | `.github/workflows/build.yml`, `.github/workflows/create-release.yml`, legacy `.travis.yml` |

The baseline `TermWindow.cs` is approximately 1,372 lines. It combines rendering, geometry, keyboard translation, input queues, control locking, sounds, screen snapshots, and telnet management. This coupling is a central improvement target, not merely a style issue.

## 4. Editor correctness and data safety

### EDIT-01: same-length changes do not mark the document dirty

**Evidence:** Code defect. **Priority:** P0. **Scope:** small correctness fix plus tests.

**Source:** `src/kOS/Screen/KOSTextEditPopup.cs:452–477`, `DrawWindow()`.

The editor records `contents.Length` before and after `GUILayout.TextArea()` and sets `isDirty` only if the lengths differ:

```csharp
if (preLength != postLength)
    isDirty = true;
```

Replacing selected text with different text of the same length can therefore leave a previously clean document marked clean. For example, replacing `100` with `200` does not change the length.

**Consequence:** `ExitEditor()` may skip the save prompt, silently discarding a real change. The same flag affects reload and file-switch confirmation.

**Required change:** detect actual content changes or use an editor document revision. Dirty state should compare the current document against the last successful load/save, not just track its length.

**Acceptance tests:** same-length replacement prompts on close; insertion/deletion prompts; unchanged content does not prompt; successful save clears dirty state; failed save does not. Define whether undoing back to saved content clears dirty state and test that policy.

### EDIT-02: active document and pending open destination share state

**Evidence:** Code defect in the dirty-file transition. **Priority:** P0. **Scope:** small-to-medium state-model correction.

**Source:** `KOSTextEditPopup.cs:106–113` (`AttachTo()`), `191–196` (`SaveContents()`), `213–228` (`LoadContents()`), `283–310` (save/load delegates). Also `TermWindow.cs`, `OpenPopupEditor()`.

The editor has `volume`, `loadingVolume`, and `loadingPath`, but no separate active-document path. `SaveContents()` uses:

```csharp
volume.SaveFile(loadingPath, new FileContent(contents))
```

`AttachTo()` assigns the newly requested `loadingVolume` and `loadingPath` before calling `LoadContents()`. When the document is dirty, `LoadContents()` opens a confirmation and assigns the requested destination as well. `DelegateSaveThenLoad()` subsequently calls `SaveContents()` before loading the new file.

This can combine the current contents and old `volume` with the new file's path. It can save the old document over the requested destination, or write the requested path on the wrong volume. Calling `LoadContents()` directly while dirty also replaces the shared path after invoking the dialog. Cancel does not restore the former path.

Additionally, `Open()` resets `isDirty`, and the normal popup-open path calls it after attachment. Reopening a dirty editor is therefore not a safe document transaction. `Open()` also focuses the editor, whose `GetFocus()` unfreezes it; modal state should not depend on these window operations.

**Required model:**

- `ActiveDocument`: current volume/path, content, current revision, saved revision.
- `PendingOpenRequest`: requested volume/path, separate from the active document.
- Save always uses the active document destination.
- Save/discard/cancel resolves a transaction before replacing the active document.
- Failure to save keeps the current document and pending request recoverable.
- Opening/closing a view does not independently reset document dirty state.

**Acceptance tests:** edit A, request B, choose save: only A receives A's content, then B loads; discard leaves A unchanged; cancel preserves A and its save destination. Repeat across volumes, with missing B, with a save failure, and when reopening the popup. Dialog text must identify both the active file and requested file accurately.

### EDIT-03: cursor-following prevents independent manual scrolling

**Evidence:** Code-supported behavior, explicitly acknowledged in a source comment. **Priority:** P1.

**Source:** `KOSTextEditPopup.cs:536–556`, `KeepCursorScrolledInView()`.

The editor continuously adjusts `scrollPosition` to keep the cursor visible. The comment acknowledges interference with scrollbar scrolling. A user inspecting a distant section can be pulled back to the cursor.

**Required change:** scroll to the caret after editing/navigation, not every repaint. Manual scroll actions suspend follow mode until a subsequent caret operation requires visibility.

**Acceptance tests:** mouse wheel/scrollbar can inspect distant text without moving the caret; typing or explicit navigation brings the caret back into view; resizing does not unexpectedly reset the viewport.

### EDIT-04: editor position is glued below the terminal

**Evidence:** Code behavior/design limitation. **Priority:** P2.

**Source:** `KOSTextEditPopup.cs:170`, `481–497`, `CalcOuterCoords()`.

The editor recalculates its position from the terminal's bottom edge. It cannot be placed independently, wastes vertical space, and can extend beyond the display. Window minimum dimensions can also be smaller than the space required for controls.

**Required change:** make the editor independently movable/resizable, or implement an explicit docked mode. Clamp recoverable controls to the screen and persist usable geometry. Docking must be a chosen behavior, not unconditional repositioning.

**Acceptance tests:** moving either window follows the selected docking policy; the editor remains recoverable near screen edges, at high UI scale, and after resolution changes. Minimum dimensions keep its title/actions/content usable.

### EDIT-05: improve basic editing before building an IDE

**Evidence:** Design improvement. **Priority:** P2, with syntax highlighting/completion later.

The current editor is essentially a Unity text area plus custom shortcuts and scrolling workarounds. Useful initial additions are line numbers, caret position, dirty indicator, find/replace, indentation preservation, selected-line indent/unindent, predictable Tab behavior, actionable save errors, external-change detection, recovery/autosave, and jump-to-error.

Keep advanced development comfortable in an external editor. Do not make a full IDE or embedded browser a prerequisite for repairing the in-game editor.

**Related issue:** [#698 — Better in-game text editor](https://github.com/KSP-KOS/KOS/issues/698). Its discussion also explores an in-terminal editor that telnet users could access; decide deliberately whether the fork wants a native GUI editor, terminal editor, or both.

**Acceptance tests:** shortcuts do not alter flight controls; source positions remain correct after edits; external changes are detected without silently overwriting either version; recovery never replaces the original without confirmation.

## 5. Terminal usability and rendering

### TERM-01: terminal clipboard and selection support

**Evidence:** Missing functionality in the reviewed key handler; upstream requests. **Priority:** P1.

**Source:** `TermWindow.cs:459–543`, `ProcessKeyEvents()`; ordered input processing around `590–869`.

The in-game terminal does not have proper clipboard integration. Existing telnet paste handling and editor text-area behavior are not substitutes for terminal paste/copy.

**Required behavior:**

- Paste through ordered terminal input, not by manipulating the screen buffer directly.
- Platform-aware shortcuts; account for Control versus Command where supported.
- Mouse selection and copy of output, or start with explicit copy-output/copy-last-error actions.
- Preserve `Ctrl+C` interrupt behavior when there is no selection; define selection-copy precedence clearly.
- Preview/confirm multiline paste when it can execute commands immediately.
- Handle CRLF/LF consistently and protect against impractically large pastes.
- Keep future script mouse-input support possible; distinguish UI selection from script-delivered events.

**Related issues:** [#1564](https://github.com/KSP-KOS/KOS/issues/1564), [#2640](https://github.com/KSP-KOS/KOS/issues/2640), and [#2998](https://github.com/KSP-KOS/KOS/issues/2998).

**Acceptance tests:** ordered multiline paste, paste while busy, paste into script input, interrupt during queued paste, selection copy without interrupt, and supported keyboard layouts/platforms. A paste must not starve the game update loop.

### TERM-02: scale chrome separately from character dimensions

**Evidence:** Fixed-pixel implementation and reported accessibility problem. **Priority:** P1.

**Source:** `TermWindow.cs:167–170`, `929–980`, `1356–1369`; `TerminalStruct.cs` for script-visible dimensions.

Many controls have tiny fixed dimensions: 10-pixel labels, 12-pixel-wide font buttons, an 8-pixel-wide brightness slider, and fixed margins. Increasing `CHARHEIGHT` enlarges text/grid geometry but does not make these controls proportionally usable.

**Required change:** separate UI scale from terminal cell dimensions. Use KSP's UI scale by default for title bars, actions, padding, icons, and hit targets, with an override if needed. Keep cell count/font customization independent.

Do not treat ignoring KSP scale as automatically an implementation mistake: terminal independence was intentional. The problem is the usability of the surrounding chrome. [#2994](https://github.com/KSP-KOS/KOS/issues/2994) explicitly discusses this distinction.

**Acceptance tests:** 100%, 150%, and 200% scale; low/high resolutions; text and cursor alignment; resize/hit-testing at each scale. UI scaling must not silently change script-visible cell coordinates.

### TERM-03: reduce preference controls on the working surface

**Evidence:** Design improvement and concrete overlapping rectangles. **Priority:** P2.

**Source:** `TermWindow.cs:929–980`, `1079–1086`, `DrawTelnetStatus()`.

The bottom area contains reverse-screen, visual-beep, keyclicker, brightness, font controls, dimensions, and telnet status. Most are preferences, not frequent flight actions. The telnet status icon/label uses the same lower-left region as existing controls.

**Required change:** move preferences to a settings popover. Keep output, operational status, close/minimize, settings, and a small number of purposeful actions visible. Use layout rules rather than individually competing pixel rectangles.

Keep a retro theme as an option. Do not let decorative monitor framing dictate tiny controls, poor contrast, or wasted space.

**Acceptance tests:** attaching zero/one/multiple telnet clients does not cover actions or text; minimum window size remains usable; all preferences remain discoverable and keyboard-accessible where practical.

### TERM-04: separate viewport, scrollback, and output cursor

**Evidence:** Architectural/design limitation; existing coordinate-semantics discussion. **Priority:** P1/P2, high compatibility sensitivity.

**Source:** `ScreenBuffer.cs:41–45`, `104–107`, `MoveToNextLine()`, `ScrollVerticalInternal()`; `TextEditor.cs` overlays and cursor handling.

The baseline computes absolute output position from the visible cursor row plus `topRow`. Content, scroll position, and command-line overlays are consequently entangled. Page Up/Down already exist; this is not a proposal to add scrolling from scratch.

**Target behavior:** output positions belong to terminal content; viewport position belongs to the viewer. Reading old output does not redirect new writes. Follow new output only when the viewer is already following the bottom; otherwise show a new-output indicator. Add wheel scrolling, scrollbar, and search.

**Compatibility constraint:** `PRINT ... AT(col,line)`, `CLEARSCREEN`, script cursor access, width/height, and telnet rendering must have a documented coordinate contract. Existing scripts use the terminal as an addressable screen. Do not silently adopt a transcript-only model or reinterpret coordinates.

**Related work:** [PR #2397](https://github.com/KSP-KOS/KOS/pull/2397), including its comments about scroll-relative versus absolute cursor positions.

**Acceptance tests:** scrolling during output, fixed-position dashboards, clear while scrolled, command input spanning multiple rows, resizing scrolled content, and equivalent behavior through GUI/telnet. Keep representative existing scripts as compatibility fixtures.

### TERM-05: command history cannot return to a fresh draft

**Evidence:** Code behavior. **Priority:** P1. **Scope:** small input-model fix.

**Source:** `src/kOS/Screen/Interpreter.cs:103–119`, `ShowCommandHistoryEntry()`.

Navigation only accepts `newHistoryIndex < commandHistory.Count`. Down Arrow therefore cannot move past the newest saved command back to a blank/current draft. The text being edited before entering history is not separately saved.

**Required change:** preserve the draft when history navigation begins and restore it when leaving the newest entry. Consider reverse search and optional persistence later.

**Acceptance tests:** type a draft, navigate up/down, and recover it exactly; empty history; newest/oldest limits; multiline commands; caret position; no accidental command execution.

### TERM-06: display script, CPU, communications, and input state

**Evidence:** Design improvement. **Priority:** P1/P2.

**Source:** `TermWindow.cs`, title construction and cursor/input handling; `ConnectivityInterpreter.cs`; `KOSToolbarWindow.cs`, processor power display.

The terminal identifies vessel/processor but leaves important operational states to output/cursor inference. The UI should say whether it is compiling, running a particular program, waiting for script input, at a prompt, queued behind communications delay, disconnected, or power-starved.

Distinguish CPU-local program execution from commands being uplinked and from viewer focus. A script may continue autonomously while communications are unavailable. The current behavior of `GETCHAR` during lost connection is also reported in [#3144](https://github.com/KSP-KOS/KOS/issues/3144); define policy rather than merely changing an indicator.

**Conceptual layout:**

```text
kOS — Vessel / Processor                          Settings  ×
Running: launch.ks · Power OK · Link delay: 0.4 s
────────────────────────────────────────────────────────────
Terminal output and character-grid display

────────────────────────────────────────────────────────────
50 × 36                               Stop script | Files
```

**Safety constraint:** stopping a script, shutting down a CPU, and releasing autopilot control are different actions. The project already has emergency suppression. Label consequences and confirm destructive actions appropriately; do not introduce an ambiguous panic button.

**Acceptance tests:** state transitions during boot/compile/run/wait/end/error, signal loss/recovery, power loss/recovery, active input consumer changes, and multi-processor use.

### TERM-07: fonts and alignment need deterministic behavior

**Evidence:** Code-supported risk and upstream reports; root causes not confirmed. **Priority:** P1/P2.

**Source:** `src/kOS/Module/AssetManager.cs`, `IsFontMonospaced()` and font discovery/cache; `TermWindow.cs:259–279`, `GetFontIfChanged()`; row-label and cursor drawing around `1011–1045` and `1117–1131`.

Font discovery depends on installed system fonts and a heuristic based on a handful of glyph advances. Rendering computes cell width from `X`, paints whole rows as labels, and separately paints a cell-positioned cursor. Metric retrieval success and nonzero advance deserve explicit validation. Skin/style state inherited from other GUI code also deserves investigation when diagnosing offsets.

**Related reports:** [#2219 — Indented/cut-off terminal text](https://github.com/KSP-KOS/KOS/issues/2219), [#2483 — Text/cursor misalignment](https://github.com/KSP-KOS/KOS/issues/2483), [#3092 — Installed monospace fonts not recognized](https://github.com/KSP-KOS/KOS/issues/3092).

Do not assert that the heuristic or another mod is the proven cause of those reports. Comments describe different mod combinations and session-dependent behavior.

**Recommended direction:** a licensed, known-good bundled fallback font; validated metrics; clear fallback diagnostics; font preview; a documented policy for ligatures, unsupported glyphs, and Unicode cell width. Reset renderer-specific style offsets/padding explicitly. System-font choice can remain optional.

**Acceptance tests:** Windows/Linux/macOS fallback selection, missing font, failed metric query, several sizes, punctuation/underscore/space, long rows, supported non-ASCII text, and modded-game alignment reproduction. Full grapheme/IME support is a separate larger decision, not an implied quick fix.

### TERM-08: persist usable window geometry

**Evidence:** Design improvement and existing request. **Priority:** P2.

**Source:** terminal construction/attachment in `TermWindow.cs`; `KOSManagedWindow.cs`; `RectExtensions.cs`.

Persist terminal open state, position, dimensions, appearance overrides, and optionally stacking. Use stable processor/vessel identity, not transient window IDs. Restore geometry against the current screen size/UI scale.

**Related issues:** [#1521 — Persist terminal state](https://github.com/KSP-KOS/KOS/issues/1521), [#1181 — Persist position](https://github.com/KSP-KOS/KOS/issues/1181), [#2941 — Screen-position access](https://github.com/KSP-KOS/KOS/issues/2941).

Do not restore keyboard focus or flight-control locks blindly after loading a save. Focus restoration should be an explicit safety decision.

**Acceptance tests:** leave/reenter scene, quicksave/load, change resolution, change UI scale, vessel docking/undocking, multiple identical CPU parts, and removed processors. At least a title/action area must remain recoverable.

## 6. Terminal correctness and dimensions

### BUFFER-01: overlay removal is clamped but insertion is not

**Evidence:** Code defect. **Priority:** P1.

**Source:** `src/kOS.Safe/Screen/ScreenBuffer.cs:260–280`, `GetBuffer()`.

The overlay merge computes a clamped destination and uses it to remove rows, but inserts at the original destination:

```csharp
mergedBuffer.RemoveRange(mergeRowClamped, rowsToMergeClamped);
mergedBuffer.InsertRange(mergeRow, bufferRange);
```

An overlay partly above the visible viewport can have a negative `mergeRow`. The insertion can therefore throw despite the preceding protection. Source clipping and row-count calculations must also be checked; replacing one variable is not necessarily a complete fix.

**Related report:** [#3100 — Long input breaks the terminal](https://github.com/KSP-KOS/KOS/issues/3100) contains a `List.InsertRange` stack trace through `GetBuffer()`. This supports relevance, not a claim of an exact reproduction.

**Required change:** compute intersection of source and destination rectangles/row ranges, then copy only that intersection. Define behavior for zero-size/empty overlays and overlapping layers.

**Acceptance tests:** overlay completely/partly above, below, and inside the viewport; multiline command longer than the visible height; resize while editing; fixed progress overlays; scrollback; fuzzed dimensions and positions.

### BUFFER-02: cursor guard lacks lower-bound checks

**Evidence:** Code defect in the guard; triggering conditions require reproduction. **Priority:** P1.

**Source:** `TermWindow.cs:1027–1044`.

Cursor rendering checks upper bounds but not `cursorRow >= 0` or `cursorCol >= 0` before accessing buffer rows/cells. Negative screen-relative positions can arise when viewport/cursor state diverges.

**Required change:** validate both bounds against one coherent display snapshot. Do not mix live cursor/dimensions with older row content without an explicit safety contract.

**Acceptance tests:** negative/out-of-range cursor, resize after history recall, scrolling during input/output, and empty/temporarily shorter buffers. Invalid visual cursor positions should be clipped/hidden safely, not throw.

### BUFFER-03: exceptions can unbalance GUI groups and spam repaints

**Evidence:** Code-supported risk and upstream report. **Priority:** P1.

**Source:** `TermWindow.cs:1002–1049`, `1117–1131`, `GUI.BeginGroup()`/`EndGroup()` pairs.

An exception between a begin/end pair can leave IMGUI clipping state unbalanced and repeat on later GUI passes. [#3022](https://github.com/KSP-KOS/KOS/issues/3022) reports out-of-range terminal errors followed by GUI-clip errors and severe slowdown.

**Required change:** validate render data before indexing; use reliable cleanup around groups/state changes. Catch faults at an appropriate outer boundary, log once/rate-limit, and keep the terminal recoverable. Do not hide underlying model corruption with a blanket catch-and-ignore loop.

**Acceptance tests:** forced render/model error leaves groups balanced, close/resize still works, and logs do not grow every repaint indefinitely.

### BUFFER-04: validate sizes at the model boundary

**Evidence:** Code-supported risk. **Priority:** P1.

**Source:** `ScreenBuffer.cs:93–101`, `SetSize()`; `TermWindow.cs`, `ProcessOneInputChar()` resize sequence and `CheckResizeDrag()`; `TerminalStruct.cs` clamps.

Script suffix setters clamp dimensions, but the underlying `ScreenBuffer.SetSize()` accepts arbitrary values. GUI calculations and telnet resize input reach this method through other routes. Fixed pixel minima also do not automatically imply valid minimum row/column counts at every font size.

**Required change:** establish one validated dimension contract at the model boundary. Handle telnet's unspecified/zero dimensions deliberately. Reject or normalize invalid sizes before allocation or line splitting; cap excessive remote sizes. Avoid repeated reallocation/notifications for unchanged sizes unless an explicit force-refresh operation is required.

**Acceptance tests:** zero/negative/excessive sizes; configured minima/maxima; highest font size at minimum window size; remote resize sequences; unchanged size; simultaneous GUI/telnet resize. Avoid zero-column nonprogress loops and empty-buffer indexing.

## 7. Focus, control locking, and lifecycle

### INPUT-01: unify input ownership and lock cleanup

**Evidence:** Code-supported risk/design improvement plus related reports. **Priority:** P1.

**Source:** `TermWindow.cs:237–249`, `295–320`, `324–350`; `KOSManagedWindow.cs`; `KOSTextEditPopup.cs`; `KOSNameTagWindow.cs`; `GUIWindow.cs`.

The terminal uses a broad `ControlTypes.All` lock while managed windows have their own lock policy. Other windows use editor locking or opt out of higher-level locking. Stacking and focus are managed partly through polling mouse events rather than one shared ownership abstraction.

In `TermWindow.OnGUI()`, `ProcessKeyEvents()` occurs before the global-hide and flight-results-dialog rendering checks. A hidden-but-focused terminal can therefore reach input handling before deciding not to draw. Hidden UI does not itself release focus in `OnHideUI()`.

**Required policy:**

- At most one intended keyboard owner among kOS windows.
- Explicit behavior for camera movement, wheel input, and flight keys.
- A visible/focused view must be distinguishable from a running terminal session.
- Hiding, closing, destroying, power loss, and scene change release or transfer locks deterministically.
- Modal dialogs suspend parent interaction, including mouse edits, not only shortcut processing.
- Opening a file or dialog does not accidentally unfreeze the wrong view.
- Save-state restoration does not unexpectedly capture flight controls.

**Related reports:** [#2659 — Clickthrough](https://github.com/KSP-KOS/KOS/issues/2659), [#2694 — Warp keys](https://github.com/KSP-KOS/KOS/issues/2694), [#2949 — Period/comma alter warp](https://github.com/KSP-KOS/KOS/issues/2949), [#3012 — Camera/wheel trapping](https://github.com/KSP-KOS/KOS/issues/3012), [#2759 — Name-tag buttons click through](https://github.com/KSP-KOS/KOS/issues/2759).

**Acceptance tests:** focus cycling among terminals/editor/widgets; hidden UI; modal save dialog; part-action windows; map view; editor part manipulation; warp; CPU/vessel destruction; scene exit. Verify locks after every transition, including exception paths.

### LIFE-01: make view/session/resource ownership explicit

**Evidence:** Code-supported maintainability and resource risk. **Priority:** P2.

**Source:** `GUIWindow.cs` attachment, destruction, delayed actions, and `Dispose()`; `GUIWidgets.cs`, `Dispose()`/`ClearAll()`; `SharedObjects.cs`, `DestroyObjects()`; `UpdateHandler.cs` observers; `AssetManager.cs`; widget texture cache.

Registration, scope loss, detachment, destruction, and disposal span multiple objects. Hiding/detaching a widget is not the same as destroying its view/session/resources. For example, `GUIWindow.OnDestroy()` removes the managed-window reference, while observer removal lives in `Dispose()`; the full ownership path should be made explicit and tested rather than assuming Unity invokes `IDisposable`.

**Required change:** document who owns each GameObject, observer registration, delayed callback, skin, texture, and font. Pair registrations/unregistrations. Make cleanup idempotent and ensure delayed actions cannot access disposed views or stale program contexts.

**Acceptance tests:** repeated GUI creation/disposal, scope loss, power loss, scene change, interrupted construction, and double cleanup. Check object/observer counts, callbacks after disposal, and resource ownership with profiling.

## 8. Processor panel, settings, and files

### PANEL-01: separate processor operations from settings

**Evidence:** Design improvement. **Priority:** P2.

**Source:** `src/kOS/Screen/KOSToolbarWindow.cs`, especially `DrawWindow()` and `DrawActiveCPUsOnPanel()`.

The panel mixes processor discovery, global preferences, communications, fonts, and emergency suppression. Settings use terse scripting aliases as primary labels and a narrow fixed column; other options live in KSP difficulty settings.

**Target sections:**

1. **Processors:** search/filter, group by vessel, processor tags, power/program state, open terminal, useful safe actions.
2. **Appearance:** UI scale, font preview, default cell count, theme, optional retro effects.
3. **Communications:** server state, bind address, port, connection details, restart action, exposure warning.
4. **Advanced:** runtime/debug options, explicitly labeled global versus save-specific scope.

Use descriptive names with aliases second, e.g. `Default terminal width (DEFAULTWIDTH)`. Keep advanced options discoverable without cluttering common operations.

**Related requests:** [#370 — CPU-list improvements](https://github.com/KSP-KOS/KOS/issues/370), [#339 — Wasted panel space](https://github.com/KSP-KOS/KOS/issues/339), [#1815 — Sidebar power toggle](https://github.com/KSP-KOS/KOS/issues/1815).

**Acceptance tests:** many processors/vessels; no loaded CPUs; power-starved/off cores; high scale; long names; keyboard navigation where practical; settings scope clearly indicated.

### PANEL-02: four-character integer fields reject valid five-digit ports

**Evidence:** Code defect/limitation. **Priority:** P1. **Scope:** small.

**Source:** `KOSToolbarWindow.cs:624–661`, particularly line 635; `src/kOS/Suffixed/Config.cs`, telnet port definition.

All integer fields use `GUILayout.TextField(..., 4, ...)`, but valid telnet ports range up to 65535. The UI cannot type a five-digit port such as 54100 even though the configuration permits it.

**Required change:** choose input capacity/validation by setting, or remove the shared fixed limit and validate the parsed range.

**Acceptance tests:** 1024, 5410, 54100, 65535, empty/intermediate input, invalid characters, and out-of-range values.

### PANEL-03: settings editing/commit behavior is surprising

**Evidence:** Code behavior/design improvement. **Priority:** P2.

**Source:** `KOSToolbarWindow.cs:624–661`, `DrawConfigIntField()`.

Integer edits commit on Return but are discarded on focus loss. Other controls commit differently. Restart-required options are mixed with immediately active ones. The displayed field does not clearly explain pending versus applied state.

**Required change:** consistent edit/apply behavior; visible validation; explicit pending state; reset/default actions; clear restart requirements. Do not silently commit every half-typed value or discard edits without explanation.

**Acceptance tests:** Enter, focus loss, Escape/cancel, invalid intermediate text, external config change while editing, server restart, and settings persistence.

### FILES-01: add a small file/volume browser

**Evidence:** Design improvement. **Priority:** P2.

A file browser can reduce command memorization and make common actions visible: volume selector, current directory, files/directories, open/edit/run/copy, and boot-file selection or refresh. Show connectivity and storage limitations instead of treating all volumes like unrestricted local disks.

Keep filesystem semantics in the existing model; the UI is an adapter, not a second implementation of copy/run logic. Avoid adding a one-click destructive run/delete operation without clear destination and consequences.

**Acceptance tests:** archive/local volume, nested paths, storage full, connection unavailable, compiled/source files, case-sensitive names, and external file additions.

## 9. Script-created widget correctness

### WIDGET-01: popup selection search mutates selection repeatedly

**Evidence:** Code defect. **Priority:** P1.

**Source:** `src/kOS/Suffixed/Widget/PopupMenu.cs:12–25`, `72–82`, `108–121`.

`Choose()` uses the public `Index` property as the loop counter for both searches. Its setter schedules a change callback whenever the value changes. A single requested selection can therefore schedule callbacks for intermediate indices, including invalid/end positions. `Choose()` also does not update displayed text through the same path as the `INDEX` suffix setter.

**Required change:** search with a local index, then commit final value, visible label, and change notification once. Define behavior for absent and duplicate values and reference-equal versus display-equal options.

**Acceptance tests:** selecting first/middle/last/missing option, current option again, duplicate display labels, empty menu, callbacks installed/uninstalled, and immediate/delayed communications. Final label must match final value.

**Related report:** [#3168 — Popup `VALUE` assignment](https://github.com/KSP-KOS/KOS/issues/3168). Its reported type-conversion problem is not automatically the same defect as this search/callback issue.

### WIDGET-02: delayed actions lose GUI-event origin

**Evidence:** Code-supported correctness risk. **Priority:** P1.

**Source:** `src/kOS/Suffixed/Widget/Widget.cs:179–187`; `GUIWindow.cs:248–265`, `KOSUpdate()`; widget callback methods in `TextField.cs`/`PopupMenu.cs`.

`Widget.Communicate()` sets `guiCaused = true` while scheduling an action and resets it on return. If communications delay queues the action, it executes later with that flag false. Callback code uses the flag to choose future-update versus next-opcode scheduling.

**Required change:** carry origin metadata with the action or callback. Do not rely on a temporary mutable flag surviving asynchronous scheduling. Separate intended model updates from visible-state synchronization where necessary.

**Acceptance tests:** identical user interaction with zero/nonzero delay, expected callback priority/context, exactly-once delivery, disposal before delivery, program termination before delivery, and reordered communications paths.

**Related historical report:** [#2708 — Text-field callbacks](https://github.com/KSP-KOS/KOS/issues/2708). Do not assume this origin issue explains that older version's report.

## 10. Persistence and filesystem integrity

### IO-01: raw reads/copies should preserve bytes

**Evidence:** Code defect for certain binary content; reported problem. **Priority:** P1.

**Source:** `src/kOS.Safe/Persistence/ArchiveFile.cs:19–25`; `Archive.cs:212–240`; `PersistenceUtilities.cs:29–49`; `FileContent.cs` string/byte access.

`ArchiveFile.ReadAll()` calls newline conversion before returning content. Classification examines only the first four bytes. `TOOSHORT` is not treated as binary, and an ASCII-looking prefix is not proof that the entire file is text. Conversion decodes as UTF-8 and re-encodes it, potentially replacing arbitrary invalid bytes. A one-byte `0xFF` can become the replacement character's UTF-8 byte sequence rather than remain `0xFF`.

**Required direction:** raw file reads/copies preserve bytes; text decoding and newline normalization happen explicitly when text is requested. Define BOM, encoding, malformed-text, and newline policies. Preserve/document legacy text behavior through an intentional compatibility path rather than silently breaking scripts.

**Related issue:** [#3187 — `ArchiveFile:ReadAll` mangles files](https://github.com/KSP-KOS/KOS/issues/3187).

**Acceptance tests:** empty and 1–3-byte files, every byte value, invalid UTF-8, valid non-ASCII UTF-8, ASCII-prefix binary, KSM, compressed data, embedded CRLF, and archive/harddisk copies. Raw round trips must be byte-identical.

### IO-02: Windows newline conversion is not idempotent

**Evidence:** Code defect for already-CRLF text. **Priority:** P1.

**Source:** `Archive.cs:212–224`, `ConvertToWindowsNewlines()`.

Replacing every `\n` with `\r\n` changes existing `\r\n` to `\r\r\n`. This is independent of whether typical callers currently normalize first.

**Required change:** normalize only explicitly textual content with a policy that is idempotent. Do not normalize binary content.

**Related report:** [#639 — CRCRLF from editor paste](https://github.com/KSP-KOS/KOS/issues/639). Its original reproduction is not proven by this review, but the transformation exists in the source.

**Acceptance tests:** LF, CRLF, mixed newlines, blank lines, trailing newline, repeated conversions, and copy/save cycles on supported platforms.

### IO-03: archive saves truncate before a successful write

**Evidence:** Code-supported data-loss risk. **Priority:** P1; combine with editor safety work.

**Source:** `Archive.cs:202–207`, `SaveFile()`.

The destination is opened directly with `FileMode.Create`. A failure after truncation can destroy the original contents before a complete replacement exists.

**Required change:** write a temporary file in the same directory and perform a suitable replacement/rename after success. Handle new versus existing destinations and platform differences; retain useful failure information and clean temporary files safely. Decide durability requirements separately from atomic visibility.

**Acceptance tests:** successful replacement, write failure before/after partial output, disk full, permissions, locked file, failed replacement, new file, and interrupted operation. The old destination remains intact when replacement cannot complete.

### IO-04: directory containment cannot be a plain prefix check

**Evidence:** Code-supported containment defect in direct texture paths. **Priority:** P1/P2 hardening.

**Source:** `src/kOS/Suffixed/Widget/Widget.cs:149–151`, `GetTexture()`; similar check in `Archive.cs:56`.

A normalized path is checked with `StartsWith(archiveRoot, Ordinal)`. A sibling directory sharing the root's textual prefix can pass. For a root `/example/Scripts`, a relative texture path into `../Scripts-private/image.png` can normalize outside the archive while retaining that prefix, if the root is represented without a trailing separator.

The volume-path API has additional canonicalization; do not generalize the texture-loader failure into a claim that every archive operation is exploitable. The direct widget texture path is the clearest reviewed case. Symlinks need a separate policy because lexical normalization does not resolve them.

**Required change:** centralize normalized, directory-boundary-aware containment; choose platform-appropriate path comparisons; establish symlink/reparse-point policy and image size limits. Keep read permissions within the intended archive boundary.

**Acceptance tests:** exact root, descendants, prefix-sharing sibling, `..`, rooted input, alternate separators, case behavior, symlinks/reparse points, missing file, and excessive image size.

### IO-05: preserve volume object identity in cross-vessel operations

**Evidence:** Source-supported upstream report. **Priority:** P1/P2.

**Source:** `src/kOS.Safe/Function/Persistence.cs`, `COPYPATH`/related functions; `src/kOS.Safe/Persistence/VolumeManager.cs:187–208`, `GlobalPathFromObject()`, and `Copy()`.

Volume/volume-item arguments are converted to string-like global identifiers and resolved again through a manager. This can lose the identity of an object belonging to another vessel or an unattached volume, especially when identifiers/names are local or ambiguous.

**Related issue:** [#3188 — Cross-vessel file references](https://github.com/KSP-KOS/KOS/issues/3188).

**Required direction:** resolve validated endpoints once and preserve actual volume/item identity through the operation. Keep string path lookup as a separate adapter; consider overloads taking concrete endpoints. Respect connectivity and access rules.

**Acceptance tests:** same/cross-vessel objects, detached volumes, duplicate names, explicit string paths, volume IDs changing after docking, and destination-directory/file behavior. Failure must be clear rather than operating on a different resolved volume.

## 11. Telnet robustness and external workflows

### NET-01: parse streams incrementally

**Evidence:** Code-supported defects/risks under fragmented TCP input. **Priority:** P1.

**Source:** `src/kOS/UserIO/TelnetSingletonServer.cs:495–537`, `DoInThread()`; `TelnetProtocolScrape()` around `644` onward and negotiation consumers.

UTF-8 decoding uses `Encoding.UTF8.GetChars()` independently on each read. A multibyte character split between reads cannot be decoded correctly without retained decoder state. Telnet protocol parsing also directly consumes sequence bytes from the current read; it needs to handle partial IAC commands/subnegotiations across reads rather than assuming read boundaries equal message boundaries.

**Required change:** persistent UTF-8 decoder and explicit incremental telnet parser states. Preserve partial sequences, handle escaped IAC, validate negotiation lengths, and define malformed-input recovery.

**Acceptance tests:** split each supported protocol command and UTF-8 string at every byte boundary; several commands in one read; partial subnegotiation; escaped IAC; malformed/truncated input; resize/type negotiation followed by ordinary text.

### NET-02: EOF, exceptions, and cooperative shutdown

**Evidence:** Code-supported lifecycle risk. **Priority:** P1.

**Source:** `TelnetSingletonServer.cs:371–393`, `StopListening()`; `502–535`, input loop; `544–615`, output/check loop.

The input loop does not break on a zero-byte `Read()`, although zero means orderly EOF. Repeated reads can spin until another path notices the disconnection. Shutdown uses `Thread.Abort()` and nulls thread references instead of cooperative cancellation/join. Output-worker paths can call `StopListening()`, which disconnects processor/menu state and invokes Unity object destruction, contrary to main-thread-only expectations.

**Required change:** treat EOF explicitly, handle expected I/O exceptions, cancel workers cooperatively, unblock reads by closing the transport where appropriate, and dispatch game/Unity cleanup to the main thread. Make shutdown idempotent and avoid blocking the game indefinitely during joins or final writes.

**Acceptance tests:** orderly close, reset, peer stalls, write failure, simultaneous close from multiple paths, disable server, scene exit, and game quit. No busy spin, orphan worker, duplicate cleanup, or worker-thread Unity access.

### NET-03: bound queues and update work

**Evidence:** Code-supported resource/latency risk. **Priority:** P1/P2.

**Source:** telnet input/output queues in `TelnetSingletonServer.cs`; `TermWindow.cs:590–608`, `ProcessTelnetInput()`; character queue processing around `824–855`.

Queues are unbounded and processing can drain arbitrary pending input during one game update. A large paste or a noisy client can monopolize the frame even without malicious intent. Output can also accumulate behind a stalled receiver.

**Required change:** configurable queue limits, per-update character/time budgets, backpressure/disconnection policy, and observable queue state. Preserve order and immediate interrupt handling without silently dropping executable input.

**Acceptance tests:** sustained input, large paste, slow receiver, multiple clients, interrupt during backlog, fairness across clients, and queue-full behavior. Measure frame-time impact.

### NET-04: retain safe defaults; improve connection diagnostics

**Evidence:** Design improvement and existing network reports. **Priority:** P2.

**Source:** `src/kOS/UserIO/TelnetMainServer.cs`, opt-in permissions, bind address, listen/update paths.

The existing loopback defaults and permission dialogs are valuable. Telnet is an unauthenticated, unencrypted control interface; the fork must not imply it is safe to expose broadly. Improve status, current versus configured address/port, restart behavior, copyable connection details, and background-simulation guidance.

**Related work:** [PR #3170](https://github.com/KSP-KOS/KOS/pull/3170) proposes binding to all interfaces with application-level non-loopback rejection to work around Windows interception. Review its assumptions, security boundary, failure paths, and necessity before adoption. [PR #3192](https://github.com/KSP-KOS/KOS/pull/3192) adds non-xterm documentation. [#3202](https://github.com/KSP-KOS/KOS/issues/3202) discusses agent/tool access.

A future structured external interface may be useful, but is separate from repairing telnet. It needs explicit authentication/exposure, permissions, request/result framing, error reporting, and game-thread scheduling. Do not make it a prerequisite for terminal improvements.

## 12. Performance, retained state, and diagnostics

### PERF-01: bound scrollback, command history, and exception retention

**Evidence:** Code-visible unbounded retention. **Priority:** P2.

**Source:** `ScreenBuffer.cs:182–198`, buffer growth and resize; `Interpreter.cs`, `commandHistory`; `Logger.cs:17`, `63`, exception history.

Output adds historical buffer rows until cleared. Command and exception histories also have no configured limits. `ResizeBuffer()` reallocates every retained row at the new width, so resizing costs grow with old output, not merely visible area.

**Required change:** configurable retention limits with correct absolute row/source-history accounting. Bounded command history must not break compiler/interpreter error references that currently use absolute history indices. Error retention should avoid keeping arbitrarily large exception object graphs when structured records suffice.

**Related report:** [#3087 — Resizing memory growth](https://github.com/KSP-KOS/KOS/issues/3087).

**Caution:** retained rows and allocation churn are verified in source; the precise reported leak is not diagnosed. Do not call a retention limit a proven complete fix for #3087.

**Acceptance tests:** prolonged printing/errors/commands, repeated resizing after large output, correct trimming and cursor/viewport behavior, stable error-source references, and bounded steady-state retention.

### PERF-02: avoid unnecessary snapshots, row strings, and deep copies

**Evidence:** Code-visible allocation/work opportunities; measured impact unknown. **Priority:** P2.

**Source:** `TermWindow.cs:356–365`, `879–888`, `GetNewestBuffer()`; terminal row rendering and telnet repaint methods; `ScreenSnapshot.cs`, `DeepCopy()`; `ScreenBufferLine.cs`, `ToString()`/`ToArray()`.

The terminal samples buffers at 20 Hz even when its GUI is closed. GUI passes repeatedly build row strings and replace nulls with spaces. Telnet repainting deep-copies visible rows per client. These are candidates for lower idle cost and reduced allocations.

**Required approach:** profile first; update when content or consumer state requires it; cache row strings by revision; skip unchanged sizes; share only truly immutable snapshots. The current snapshot constructor obtains a list containing live row references, so it is not automatically safe to share indefinitely as an immutable snapshot.

Use monotonic/unscaled time for presentation/transport timing where appropriate, rather than coupling refresh/cursor behavior to game time or wall-clock changes.

**Acceptance criteria:** capture CPU time and allocations for idle hidden/open terminal, typing, heavy output, resizing, and multiple clients. Preserve responsiveness and identical rendered content; do not remove throttling simply to mask latency.

### PERF-03: make texture/font ownership explicit

**Evidence:** Code-supported resource risk. **Priority:** P2.

**Source:** `Widget.cs`, texture cache/load/reload; `AssetManager.cs`, font cache/discovery; skins/textures created in `GUIWindow.cs`, `GUIWidgets.cs`, and `TermWindow.cs`.

Reloading widget images creates new Unity textures; old cached texture ownership/destruction is not explicit. Missing image requests also create a texture before finding a file. Fonts are cached by name/size, and multiple runtime skins are instantiated.

**Required change:** clear ownership, reuse where safe, destroy replaced owned objects, avoid allocating on a missing-file path, and define cache eviction. Never destroy shared game assets. Verify cleanup with Unity object profiling rather than assuming managed garbage collection releases native assets promptly.

**Acceptance tests:** repeated image changes/missing files, font changes/sizes, GUI creation/disposal, and scene transitions; native object counts remain explainable and bounded.

### DIAG-01: expose retained errors as structured diagnostics

**Evidence:** Existing unused infrastructure and design improvement. **Priority:** P2.

**Source:** `src/kOS/Logger.cs`, `ExceptionHistory` and comments; `KSPLogger.cs`.

The logger retains errors but has no user-facing history interface. Terminal output alone is a poor error browser: later output displaces errors, copying is difficult, and source/help context is not easily actionable.

**Target error inspector:** concise message, optional verbose explanation, file/line/column where available, script stack, copy, open source, documentation link, and bounded recent history. Distinguish expected script errors from internal .NET/mod faults. Preserve diagnostics after the terminal viewport moves.

**Acceptance tests:** compile/runtime/internal error, missing source, interpreter command errors, unavailable help URL, repeated error, and copying full detail without altering vessel controls.

### DIAG-02: human-oriented, bounded structure formatting

**Evidence:** Reported readability problem and existing proposals. **Priority:** P2.

Serialization dumps and readable output have different goals. Default printing should be concise, indented where useful, bounded by depth/item count, and safe for self-reference. Do not require every field to be visited just to show a human-readable value.

**Related work:** [#2894](https://github.com/KSP-KOS/KOS/issues/2894), [PR #2909](https://github.com/KSP-KOS/KOS/pull/2909), [PR #2910](https://github.com/KSP-KOS/KOS/pull/2910). [#1598](https://github.com/KSP-KOS/KOS/issues/1598) reports self-referencing lexicon printing problems; reproduce before claiming a current crash.

**Acceptance tests:** nested containers, large collections, recursive references, missing/unloaded targets, stable summaries, and opt-in expanded output. Separate display-format changes from serialized save formats.

## 13. Architecture for a maintainable fork

### ARCH-01: terminal session independent of Unity views

**Evidence:** Structural coupling and existing proposal. **Priority:** P2, foundational but stage incrementally.

**Source:** `TermWindow.cs`; `src/kOS.Safe/Screen/`; script-facing terminal API. **Related issue:** [#2123 — Decouple terminal functionality](https://github.com/KSP-KOS/KOS/issues/2123).

Retain and strengthen the existing `kOS.Safe` boundary. Suggested responsibilities:

```text
TerminalSession (Unity-independent)
  ├── terminal content, dimensions, revisions, cursor contract
  ├── command input, ordered queue, history, interrupt policy
  ├── input consumer / interpreter state
  ├── notifications and resize events
  └── coherent display snapshots

UnityTerminalView
  ├── rendering, fonts, themes, cell geometry
  ├── viewer scroll/selection/search
  └── window/input-ownership adapter

TelnetAdapter
  ├── transport, protocol negotiation, incremental parsing
  └── screen/input adaptation

EditorDocument + EditorView
  └── document transactions independent of window lifecycle
```

Viewer scroll state can be separate per consumer, but logical screen dimensions and coordinate semantics need an explicit authority when multiple GUI/telnet clients attach. Do not let clients continually resize one another without policy.

Move logic gradually behind tests. Keep existing public overloads used by other mods; `TermWindow.ProcessOneInputChar()` contains explicit binary-compatibility accommodations for kOSPropMonitor.

**Benefits:** real unit tests, headless session/telnet testing, cleaner renderer changes, potential external compiler/tool support, and less UI lifecycle coupling.

### ARCH-02: do not require a wholesale framework/runtime rewrite

**Evidence:** Implementation strategy. **Priority:** constraint for all phases.

The fork must work within KSP's shipped Unity and .NET environment. The baseline projects target `net48`. A modern tooling/test setup does not imply that the mod runtime can be moved to modern .NET or that a newer Unity UI Toolkit is available.

Evaluate disciplined IMGUI versus a scoped uGUI view using small prototypes. First repair models and input policy; then replace rendering only if its benefits justify packaging, lifecycle, accessibility, and compatibility costs.

Do not bundle a full embedded browser/IDE as the first answer to an ugly terminal.

## 14. Test and CI gaps

### TEST-01: current GitHub build does not execute tests

**Evidence:** Workflow/action inspection. **Priority:** P1.

**Source:** `.github/workflows/build.yml`; `src/kOS.Safe.Test/kOS.Safe.Test.csproj`; legacy `.travis.yml`.

The GitHub workflow delegates to KSPBuildTools 1.1.1. Its reusable workflow restores/builds/packages, and its compile action runs restore and `dotnet msbuild`; neither contains a test execution step. The old Travis file invokes `nunit-console`, but that is not test execution in the current GitHub build path.

Reviewed external automation:

- <https://github.com/KSPModdingLibs/KSPBuildTools/blob/1.1.1/.github/workflows/build.yml>
- <https://github.com/KSPModdingLibs/KSPBuildTools/blob/1.1.1/.github/actions/compile/action.yml>

The test project references NUnit 2.7.1 and NSubstitute 1.8.1. Do not merely add `dotnet test` and assume these assemblies will be discovered/executed correctly. Select a compatible runner or deliberately migrate adapters/framework, then verify execution and failure propagation.

**Acceptance criteria:** CI reports discovered/executed counts, fails on an intentionally failing test, publishes results, and can run safe/core tests without a full KSP session. Include workflow/test/documentation changes in trigger paths as appropriate.

### TEST-02: actual terminal models lack focused regression coverage

**Evidence:** Test-tree inspection. **Priority:** P1.

There are collection, persistence, parser, serialization, opcode, and basic language integration tests. However, no focused test suite for actual `ScreenBuffer`, command-line `TextEditor`, `SubBuffer` composition, or `ScreenSnapShot` behavior was found. `TerminalFormatterTest` concerns serialization/display formatting, not the GUI buffer. The integration-test screen stub throws `NotImplementedException` for buffer retrieval, sizing, scrolling, cursor movement, and resize notifications.

**Required additions:** real model tests, not only substitutes that skip the failure-prone operations. Extract editor/history/widget selection logic into safe components where needed. Add property-based/randomized sequences for print, scroll, clear, input, and resize.

### Minimum regression matrix

| Area | Required cases |
| --- | --- |
| Document safety | Same-length edit; save/discard/cancel file switch; cross-volume switch; failed save; reopen |
| Editor viewport | Manual scroll; caret follow after edit/navigation; resize; screen-edge placement |
| Buffer composition | Negative/overflow overlays; empty buffers; long input; clear; fixed overlays |
| Dimensions | Invalid remote dimensions; extrema; unchanged sizes; font changes; concurrent viewers |
| Rendering | Cursor bounds; coherent snapshots; balanced GUI groups; font fallback/metrics |
| Input | Paste ordering; interrupt; history draft; Unicode/layouts; no flight-key leakage |
| Lifecycle | Hide/show; modal dialogs; power loss; destruction; scene change; lock cleanup |
| Widgets | One final selection callback; matching label; delayed origin; disposal before callback |
| File I/O | Arbitrary/short bytes; LF/CRLF; failed atomic replace; containment; cross-vessel identity |
| Telnet | Fragmentation at every byte; EOF/reset; malformed negotiation; queue budgets; shutdown |
| Retention/performance | Long sessions; repeated resize; native object counts; multiple clients; bounded history |
| Compatibility | Character dashboards; boot scripts; KSM/source; public mod APIs; GUI and telnet equivalence |

Manual KSP tests remain necessary for focus, flight controls, power/connectivity, native resources, and actual font/UI behavior. Headless tests should reduce—not pretend to eliminate—that requirement.

## 15. Upstream work to evaluate before duplicating it

All PRs below were open when checked. Status, review readiness, and mergeability must be rechecked. An open PR is neither an endorsement nor a ready-made fix.

| PR | Relevance | Review constraints |
| --- | --- | --- |
| [#2397 — Expose terminal cursor](https://github.com/KSP-KOS/KOS/pull/2397) | Cursor access, interactive terminal interfaces, scrolling semantics | Author later reported manual testing/readiness. Review coordinate behavior, GUI/telnet parity, and compatibility; this is not PR #2433. |
| [#3145 — Disk-space slider](https://github.com/KSP-KOS/KOS/pull/3145) | Contained part-configuration UX improvement | Verify range, discrete values, cost scaling, and saved craft behavior. |
| [#3181 — Unloaded-vessel hash fix](https://github.com/KSP-KOS/KOS/pull/3181) | `VesselTarget.GetHashCode()` currently dereferences `Vessel.rootPart.flightID` | Verify stable hashing and equality across vessel loading/unloading and use as lexicon/set keys, not only absence of a null reference. |
| [#3201 — Moment-of-inertia frame correction](https://github.com/KSP-KOS/KOS/pull/3201) | Potential steering correctness, especially long/thin craft | Related [#3167](https://github.com/KSP-KOS/KOS/issues/3167). Source uses frame transforms in `SteeringManager.FindMoI()`; reported axis mismatch needs numerical/flight regression tests, not blind adoption. |
| [#3170 — Windows telnet connections](https://github.com/KSP-KOS/KOS/pull/3170) | Reported loopback interception | Review bind-to-all plus reject-nonloopback tradeoff and failure paths; preserve exposure intent. |
| [#3186 — Deploy release docs](https://github.com/KSP-KOS/KOS/pull/3186) | Release/doc consistency | Verify versioned output, links, permissions, and reproducibility. |
| [#3192 — Non-xterm telnet docs](https://github.com/KSP-KOS/KOS/pull/3192) | External workflow guidance | Test recommendations against supported clients. |
| [#2909](https://github.com/KSP-KOS/KOS/pull/2909), [#2910](https://github.com/KSP-KOS/KOS/pull/2910) | Alternative readable-structure formatting approaches | Choose a display policy; do not merge competing approaches without review. |
| [#3169 — Compiler optimization demo](https://github.com/KSP-KOS/KOS/pull/3169) | Possible execution efficiency | Explicit demo. Requires broad semantic/timing tests; opcode budgeting and triggers matter. |
| [#3203 — Jump-stack optimization](https://github.com/KSP-KOS/KOS/pull/3203) | VM/compiled-object optimization | Author notes incompatibility and external assembler work. Do not change KSM/tool contracts as incidental cleanup. |

Additional source-backed items from this review:

- `src/kOS/Suffixed/VesselTarget.cs:551–553`: unloaded root-part dereference; assess #3181.
- `src/kOS/Control/SteeringManager.cs:779–812` and state-vector code around `578–620`: investigate the reported inertia frame mismatch; assess #3201.
- `.github/workflows/create-release.yml`: currently delegates release generation, while documentation deployment is proposed separately. Pin external automation deliberately and keep fork documentation synchronized.

## 16. Compatibility and safety rules

1. **Preserve character-grid scripting.** Keep `PRINT AT`, `CLEARSCREEN`, width/height, resize watchers, and cursor behavior deliberate and documented.
2. **Do not conflate input sources.** Prompt input, script `GETCHAR`, queued uplink commands, GUI selection, and telnet events have different ownership/policies.
3. **Keep interrupts understandable.** Copy/paste shortcuts must not remove a reliable stop path; stop and release-controls semantics must be distinct.
4. **Preserve mod-facing binary APIs.** Review public signatures and overloads, especially terminal injection used by other mods.
5. **Keep runtime compatible with KSP.** New tests/build tools must not inadvertently require a newer in-game runtime.
6. **Keep raw and textual file semantics explicit.** Introduce byte-preserving behavior without undocumented changes to existing text APIs.
7. **Do not weaken network exposure defaults.** Loopback-only intent, opt-in permissions, and clear warnings remain important.
8. **Keep save and craft compatibility visible.** Version persisted UI state; tolerate missing processors and changing display geometry.
9. **Do not restore dangerous focus state automatically.** No unexpected flight-control lock on scene/save restoration.
10. **Do not treat performance changes as purely cosmetic.** Instruction counts, trigger scheduling, callbacks, and update budgets can affect flight scripts.
11. **Do not infer root causes from issue titles.** Reproduce, characterize, test, then claim a fix.
12. **Keep each change reviewable.** Small correctness PRs before broad architectural/renderer changes; record behavior changes and migrations.

## 17. Proposed implementation sequence

### Phase 0: establish the fork and baseline

- Initialize/copy the source deliberately; record upstream commit and licensing.
- Establish build instructions and KSP reference setup without committing proprietary game assemblies.
- Enable verified test execution in CI.
- Create a compatibility fixture set: representative scripts, character dashboards, editor files, telnet clients, and saved craft.
- Add regression tests for the first correctness fixes before changing rendering.

### Phase 1: correctness and data integrity

- EDIT-01 and EDIT-02: dirty detection and safe file transactions.
- BUFFER-01 through BUFFER-04: overlay/cursor/dimension safety and GUI recovery.
- PANEL-02: integer/port input capacity.
- WIDGET-01/02: final selection and event-origin semantics.
- IO-01/02/03: explicit raw/text handling, idempotent text normalization, safe archive replacement.
- NET-01/02: stream parsing, EOF, and safe shutdown.

Ship these as focused fixes, not hidden inside a theme overhaul.

### Phase 2: visible terminal usability

- TERM-01/02/03: clipboard, scalable controls, uncluttered preferences.
- TERM-05/06: draft-preserving history and operational state.
- INPUT-01: input-ownership policy and in-game regression pass.
- TERM-07/08: dependable fonts and recoverable persisted windows.
- EDIT-03/04: independent scrolling and usable positioning.

### Phase 3: models and productivity

- Incrementally extract ARCH-01 behind established tests.
- Define viewport/coordinate contracts and implement TERM-04.
- Add a real editor document model and EDIT-05 features.
- PANEL-01/03 and FILES-01: reorganized settings and file browser.
- DIAG-01/02: error inspector and readable structured output.
- Complete lifecycle/resource ownership cleanup.

### Phase 4: measured performance and optional tooling

- Profile PERF-01/02/03 and implement bounded retention/caches safely.
- NET-03: measurable queue budgets/backpressure.
- Evaluate upstream vessel/steering fixes with appropriate fixtures.
- Consider a structured external interface, completion/highlighting, or a different renderer only with an explicit design and compatibility plan.
- Evaluate compiler/VM optimizations separately; never merge them just to make the UI feel faster.

## 18. Recommended next design documents

Before major implementation, write short decisions for:

1. `terminal-coordinate-contract.md`: logical screen, output cursor, scrollback, viewer position, resize authority, and `PRINT AT` compatibility.
2. `input-and-flight-safety.md`: keyboard owner, interrupts, paste, camera controls, communications policy, modal/hidden UI.
3. `editor-document-transactions.md`: active/pending files, dirty revisions, save failure, external changes, recovery.
4. `persistence-byte-and-text-semantics.md`: raw versus text I/O, newline/encoding policy, migration.
5. `terminal-session-architecture.md`: safe model interfaces, Unity/telnet adapters, lifecycle ownership.
6. `test-and-release-plan.md`: automated/manual matrices, supported KSP/platform combinations, performance baselines, docs/versioning.

## 19. Definition of a successful first fork release

A successful first release does not need every feature above. It should at least provide:

- No same-length-edit data loss or wrong-file save transaction.
- Stable long input, overlay clipping, resizing, and cursor rendering.
- Working terminal paste and a useful copy path.
- Readable, scaled controls without overlapping telnet status.
- Clear script/power/communications state.
- Predictable focus, hide, and close behavior without flight-input leakage.
- Byte-preservation tests and robust telnet fragmentation/EOF handling.
- CI that actually executes regression tests.
- Documented compatibility decisions and clear release notes.

The character-grid identity and optional retro aesthetic can remain. Reliability, legibility, explicit state, and low-friction development should become the defaults.
