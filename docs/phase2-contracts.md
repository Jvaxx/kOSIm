# Phase 2 preparation — current terminal map and proposed contracts

Status: **preparation done 2026-10-19; user decisions D1–D4 made the same day (see "Decisions" below). P2-1 implemented and checked in game 2026-10-19; P2-2 implemented and headless-tested 2026-10-19 (in-game check pending); P2-3 onward not started.** The map below describes the code before P2-1; since P2-1, `TermWindow`'s routing lives in `TerminalInputRouter`, and since P2-2 its telnet list, snapshot and output fan-out live in `TerminalSession` (unchanged behavior). This document fulfils the roadmap's "Decisions to document before major changes" for Phase 2. It records how the terminal works today (verified against the code at commit `5651bf95`), the findings this mapping turned up, the contracts proposed for each decision area, and a proposed order for the first extractions. Decisions marked **open** need the user's choice before the step that depends on them.

File references are to `src/`; `S/` = `kOS/Screen/`, `SS/` = `kOS.Safe/Screen/`, `SU/` = `kOS.Safe/UserIO/`, `U/` = `kOS/UserIO/`.

## Decisions (user, 2026-10-19)

- **D1 viewport:** one shared view offset per terminal (GUI and all telnet clients).
- **D2 scrollback:** bounded at 2,000 lines (code constant; oldest dropped, the screen never trimmed).
- **D3 resize authority:** last change wins: requests applied on the main thread in arrival order, other viewers asked to resize (today's behavior, to be made explicit and tested). As implemented (P2-2): "arrival order" is the order the main thread reads them; within one update telnet clients are drained in rotating order.
- **D4 GUI Ctrl-D/Ctrl-X:** keep sending Enter (upstream behavior); documented, not changed. INPUT-02 is closed as intended behavior; the misleading `ProcessOneInputChar` comment may be corrected in P2-1.

## 1. Current map

### Objects and ownership

- One `kOSProcessor` owns one `SharedObjects`, in which the **screen and the interpreter are the same object**: `ConnectivityInterpreter` → `Interpreter` → `TextEditor` → `ScreenBuffer` (`kOS/Module/kOSProcessor.cs` ≈ 570–603). It also creates the CPU and a `TermWindow` component.
- `TermWindow` (`S/TermWindow.cs`, 1,460 lines) is at once: the Unity window (drawing, fonts, skins, resize drag, sounds), the keyboard translator (`OnGUI` → `ProcessOneInputChar`), the input router (type-ahead queue, prompt feeding, BREAK), the telnet fan-out (attached list, NAWS resizes, diff/full repaint, title, clear) and the screen-resize listener. It lives as long as its processor; closing it only hides it.
- Telnet: a persistent `TelnetMainServer` accepts clients. Each client is a `TelnetSingletonServer` with **two threads** (reader, writer; `SU/TelnetConnectionWorker.cs`) and a welcome menu until it attaches to a processor's `TermWindow`.
- The embedded editor (`S/KOSTextEditPopup.cs` + `SS/EditorDocument.cs`) is a separate Unity text area. It never reads the terminal input queue or telnet.

### Screen model (`SS/ScreenBuffer.cs`)

- One grid per processor, shared by the GUI and every attached telnet client. Defaults to 36 rows × 50 columns (config), clamped to 1–160 rows and 1–255 columns.
- A list of buffer lines that grows with all output, and is reset only by `CLEARSCREEN`. **Scrollback is unbounded.** `topRow` is the first visible line. The cursor is stored **relative to the viewport** (`CursorRow`); writes go to `CursorRow + topRow`.
- Overlays (`SubBuffer`): the prompt's input line, and the RemoteTech progress bar. They are merged into `GetBuffer()`.
- `SetSize` keeps the content without reflowing it: narrowing cuts the right-hand side. It clamps the cursor and calls the resize notifiers synchronously, which are:
  - `TermWindow.NotifyOfScreenResize` (GUI pixels, telnet resize requests);
  - the progress bar;
  - every overlay (`AddSubBuffer` registers it; `RemoveSubBuffer` does not unregister it);
  - `TerminalStruct`, which queues the script's `RESIZEWATCHERS` callbacks.
- `PRINT AT` saves and restores the absolute cursor (`S/Interpreter.cs` ≈ 195). `CLEARSCREEN` clears the model, sends a clear to each telnet client and resets its diff snapshot (`S/TermWindow.cs` `ClearScreen`).
- Display: `TermWindow.Update` takes one snapshot about every 50 ms and uses it for the GUI and for every telnet client. Each telnet client keeps its own previous snapshot for diffing (`SS/ScreenSnapshot.cs`).

### Size authority today

Every source calls the same `SetSize`. The last size applied wins for everyone:

| Source | Path |
| --- | --- |
| Config default | `TermWindow.AttachTo` |
| GUI resize drag, font change | `TermWindow.CheckResizeDrag`, `HowManyRows/ColumnsFit` |
| Script `TERMINAL:WIDTH/HEIGHT` (minimum 15 × 3) | `kOS.Safe/Encapsulation/TerminalStruct.cs` |
| Telnet attach | `TelnetSingletonServer.ConnectToProcessor` applies that client's remembered size (initially 80×24) |
| Telnet NAWS | reader thread → `TelnetInputQueue.EnqueueResize` (coalesced per client) → main thread `TerminalInputPump` (clients drained in rotating order) → `SetSize` |
| `UnicodeCommand.RESIZESCREEN` + two chars | `TermWindow.ProcessOneInputChar` |

After a change, the other attached clients of a different size are sent a resize request (xterm `CSI 8;h;w t`), which their terminals may ignore.

### Input routing today

- **GUI keyboard:** `TermWindow.OnGUI` reads keys only while that window holds the keyboard (`InputOwnership`, Phase 1 INPUT-01), and translates them to characters or `UnicodeCommand` keys for `ProcessOneInputChar`.
- **Telnet:**
  1. The reader thread runs socket → `TelnetInputParser` → `TelnetInputDispatcher` → `TelnetInputPipeline` (`TerminalInputDecoder`) → `TelnetInputQueue` for that client.
  2. Each update, the main thread runs `TermWindow.ProcessTelnetInput` → `TerminalInputPump.DrainTelnets`, within budgets: 4,096 keys per client, 4 ms, and a 4,096-key type-ahead limit.
  3. Each key goes to `ProcessOneInputChar`.
- **Common path:** keys go into one unsynchronized type-ahead queue, `Screen.CharInputQueue`. GUI and telnet always force queuing. Each update, `ProcessUnconsumedInput` feeds the queue to the prompt (2,048 keys, 4 ms) while `Interpreter.IsWaitingForCommand()` holds. A running script reads **the same queue** through `TERMINAL:INPUT:GETCHAR` (`kOS.Safe/Encapsulation/TerminalInput.cs`, `YieldFinishedGetChar`). Who gets a key therefore depends only on whether the prompt is waiting: there is no explicit consumer.
- **BREAK** (GUI Ctrl-C, telnet Ctrl-C, IP/BRK) skips the queue: it clears the client's and the shared type-ahead, then `Interpreter.SpecialKey` → `Cpu.BreakExecution`. With communications delay, BREAK cancels the delayed command instead (`S/ConnectivityInterpreter.cs`).
- **Communications delay** (RemoteTech): whole commands, not keys, are queued in `ConnectivityInterpreter` and compiled once the delay has passed.
- **Mods:** `TermWindow.ProcessOneInputChar` (three overloads, kept for kOSPropMonitor's binary). The public `Interpreter.Type`/`SpecialKey` and the mutable `CharInputQueue` also let mods bypass routing.

### Threads today

- **Unity main thread:** all GUI work, screen mutation, prompt, CPU execution, snapshots, telnet repaint generation (into an output queue), welcome menu, and cleanup of finished connections.
- **Telnet reader, one per client:** protocol parsing, decoding, NAWS and terminal type. It writes only to locked queues and to volatile or locked client fields. **Exception:** the NAWS handler reads `ConnectedProcessor` (a Unity object; the comparison checks a cached pointer). This is low risk but outside the stated rule.
- **Telnet writer, one per client:** drains the locked output queue, maps terminal commands, sends bytes and keepalives.
- **Compile worker, one per threaded compile:** results are handed over through a `ManualResetEvent` plus `Join`. A cancelled compile keeps running to the end, and its result is thrown away (decision 4).

### Lifecycle today

- **Window close:** hides the window and releases focus. It **does not stop the processor**, and attached telnet clients keep working (they are fed even while the GUI is closed).
- **Power off or starved:** breaks execution, locks the interpreter, releases focus. Window and telnet clients stay. Power back: reboot.
- **Processor destroyed** (vessel unload or destruction, scene change):
  1. `kOSProcessor.OnDestroy` → `Cpu.BreakExecution`, `Cpu.Dispose`, `SharedObjects.DestroyObjects` (destroys the `TermWindow`).
  2. **Nothing detaches the telnet clients.** `DetachAllTelnets` has no production caller. Each client notices only because Unity's fake-null makes `ConnectedProcessor == null`; its welcome menu then takes over, but it never receives a detach message.
- **Telnet disconnect:** back to the welcome menu. Full stop closes the socket, joins the workers for up to 1 s and destroys the menu. Shutdown is idempotent.
- **Native resources:** `TermWindow`, `KOSTextEditPopup` and `GUIWindow` create textures (`Utils` texture helpers allocate new ones each time) and clone skins, and **never destroy them**. Fonts are cached by `AssetManager` (persistent; intentional). The `ManualResetEvent`s of the compile detector and the telnet worker are not disposed.
- **Locks:** `kOSTerminal` (keyboard) and `kOSMouseOver` (hover) are released on scene change and when the window is destroyed (Phase 1).

## 2. Findings from this mapping (not fixed)

| ID | Finding | Evidence | Severity |
| --- | --- | --- | --- |
| VIEW-01 | **Scrolling back moves where output is written.** At the prompt, PageUp then a command: the command and its output overwrite older lines in the middle of the scrollback, and the lines below them stay as they were. | Headless probe (scratch test, removed): 30 lines on a 10-row screen, PageUp, `print "NEW OUTPUT".` → `line 20`/`line 21` replaced by the command and its output, `line 22`…`29` after them. Code: `ScreenBuffer.AbsoluteCursorRow`, `ScrollVertical`. Upstream behavior. | Data loss (scrollback only) |
| VIEW-02 | Scrollback is unbounded: a script printing forever grows memory until `CLEARSCREEN`. Command history is unbounded too. | `ScreenBuffer.AddNewBufferLines` (no trimming), `Interpreter.commandHistory` | Memory growth |
| INPUT-02 | (Kept by decision D4.) GUI Ctrl-X / Ctrl-D send **Enter** (`0x000d`), not the detach/close code that `ProcessOneInputChar` handles (`0x0004`/`0x0018`, "If GUI, then close window"). Closing the GUI with Ctrl-D never worked. | `S/TermWindow.cs` ≈ 468–473 vs ≈ 671–681; same in upstream `8f281a45` | Minor (a documented key does the wrong thing) |
| LIFE-01 | Terminal, editor and script GUI textures and cloned skins are never destroyed. Each processor load leaks them until the scene is unloaded, at best. | `S/TermWindow.cs` `Awake`, `BuildPanelSkin`; `S/GUIWindow.cs`; `kOS/Utilities/Utils.cs` texture helpers | Native leak (needs in-game measurement) |
| LIFE-02 | A processor destroyed with telnet clients attached never detaches them. The clients rely on Unity fake-null, with no detach message, and the `TermWindow`'s list is just dropped. | `DetachAllTelnets` unused; `kOSProcessor.OnDestroy` | Minor; works by accident |
| LIFE-03 | `ManualResetEvent`s are not disposed; `RemoveSubBuffer` leaves its resize notifier registered. | `YiedFinishedThreadedDetector`, `TelnetConnectionWorker`; `ScreenBuffer.RemoveSubBuffer` | Minor |
| VIEW-03 | (Found in P2-2; **fixed 2026-10-19**.) A full telnet repaint painted the last refresh's snapshot: after a resize, a narrower client got rows of the old width (wrapped by real terminals), and rows changed between the resize and the repaint were never sent. | Seeded client-model test; `docs/testing-and-build.md`, "Full telnet repaint after a resize (VIEW-03)". Upstream behavior. | Display corruption (telnet) |

## 3. Proposed contracts

### 3.1 Screen versus viewport

Proposed contract:

- The **screen** is the last `RowCount` rows of the buffer. All writes go there: `PRINT`, `PRINT AT`, `CLEARSCREEN`, the cursor, the prompt and `TERMINAL:…` suffixes. Row 0 of `PRINT AT` is the top of the screen, never of the view.
- A **view offset** (rows above the screen) is display state only. PageUp/PageDown (and later the wheel or search) change only the view. Snapshots carry the view; writes never read it.
- Typing a key, or new output, returns the view to the screen (the simplest safe rule). In Phase 3, "keep the view while output arrives" plus a new-output indicator can replace the output half of this rule.
- Resize-watcher callbacks and `TERMINAL:WIDTH/HEIGHT` describe the screen, not the view.
- Scrollback is **bounded** (the oldest lines are dropped; the screen itself is never trimmed).

**Decided:** D1 one shared view offset; D2 2,000 lines.

### 3.2 Resize authority

The grid stays **one shared size per processor**: character-grid dashboards need one size, and per-viewer grids would break `PRINT AT`.

Proposed contract:

- Size requests come from the config default (on attach), the GUI drag or font change, scripts, telnet attach and telnet NAWS.
- They are applied on the main thread in the order received; the last applied wins.
- Every viewer of a different size is then asked to resize, and is clipped or padded if it doesn't.

This is today's behavior, made explicit and tested. **Decided (D3):** keep it (alternatives considered: script/GUI sizes sticky; smallest viewer).

### 3.3 Input ownership

Proposed contract: an **explicit active consumer** per session, decided in one place:

1. **Interrupts:** BREAK always skips queues, clears type-ahead and goes to the session.
2. **Prompt:** while the interpreter is waiting for a command.
3. **Running program:** `GETCHAR` reads type-ahead; keys not consumed stay queued for the prompt (as now).
4. **Communications delay:** whole commands are queued (unchanged).
5. **The embedded editor:** owns keys only through GUI keyboard focus (INPUT-01); it never takes telnet input.

GUI keyboard focus (which *window* gets keys) stays the INPUT-01 policy. Selection and paste (Phase 3) will go through the same queue, with the same budgets and the same interrupt path.

Behavior is unchanged. **Decided (D4):** GUI Ctrl-D/Ctrl-X keep sending Enter (INPUT-02 closed as intended behavior).

### 3.4 Thread ownership

Proposed contract:

- Transport threads only parse and map bytes, and touch only locked queues and their own client state. Every session, screen, interpreter and Unity mutation runs on the main thread.
- Telnet workers must not read Unity objects. The NAWS handler should use a flag the main thread maintains, instead of `ConnectedProcessor`.
- Compile workers touch only their own compiler inputs and outputs (as now).
- Session APIs document "main thread only". A debug check may enforce this in headless tests.

### 3.5 Resource ownership

Proposed contract:

| Owner | Owns | Cleanup (idempotent) |
| --- | --- | --- |
| Processor | session, CPU, window | `OnDestroy`: break, dispose CPU, dispose session, destroy window |
| Session (new) | screen, input router, viewer list, resize notifiers | `Dispose`: detach every viewer with a message (telnet back to the menu), clear notifiers |
| Window | textures, cloned skins, popup, sound maker | `OnDestroy`: destroy what it created |
| Telnet client | threads, socket, queues, menu | as now (already idempotent) |

Closing a view never stops a processor. Stopping a script, powering off and releasing flight controls stay separate operations.

### 3.6 API compatibility

These stay, with the same signatures and behavior:

- `TermWindow.ProcessOneInputChar` (three overloads; kOSPropMonitor binary), `TermWindow` public members (`Open`/`Close`/`Toggle`, `Print`, `ClearScreen`, `NumTelnets`, `DetachAllTelnets`, `GetRect`, `GetFontSize`, `OpenPopupEditor`, `TitleText`, `ShowCursor`).
- `Interpreter.Type`/`SpecialKey`/`IsWaitingForCommand`/`IsAtStartOfCommand`/`SetInputLock`, `IScreenBuffer` (including `CharInputQueue`, `AddResizeNotifier`), `SharedObjects.Screen`/`Interpreter`/`Window`, and the `kOSProcessor` accessors (`GetScreen`, `GetWindow`).
- New classes sit behind them; old members forward.

## 4. Proposed extraction order

Each step is behavior-preserving unless marked, gets headless tests, keeps the renderer, and is checked in game (Proton) before the next.

1. **P2-1 Input router to `kOS.Safe`. Done 2026-10-19** (`docs/testing-and-build.md`, "Terminal input router").
   - Move `ProcessOneInputChar`, `Type`, `SpecialKey`, `ProcessUnconsumedInput`, `ProcessTelnetInput`, `WipeAllTypeAhead` and the resize-escape state out of `TermWindow` into a `TerminalInputRouter`. It works through interfaces for the interpreter, the screen and the window actions (close, detach, repaint).
   - `TermWindow`'s public overloads forward to it.
   - The test fixture `FixtureTermWindow`, which today **duplicates** this code, is replaced by the real router.
   - No behavior change.
2. **P2-2 Viewer fan-out to a `TerminalSession`. Done 2026-10-19, headless** (`docs/testing-and-build.md`, "Terminal session").
   - An `ITerminalViewer` (each telnet client) replaces `TermWindow`'s telnet list. The resize notifications, diff and full repaint, title and clear fan-out move into the session, with the shared snapshot.
   - As built, the GUI window owns the session and draws its snapshot; it is not an `ITerminalViewer`, since it needs none of the streamed output.
   - Resize-authority tests use fake viewers (the D3 contract), plus NAWS end to end through the fixture.
   - No behavior change (D3: last change wins), checked against a copy of the old code on seeded sequences.
3. **P2-3 Explicit lifecycle.** Implements the §3.5 ownership table:
   - `TerminalSession.Dispose` (detach telnets with a message: LIFE-02);
   - the window destroys its textures and skins (LIFE-01; in game, measure with repeated vessel loads);
   - unregister removed overlays and dispose the events (LIFE-03);
   - the NAWS handler no longer reads Unity objects (§3.4).
4. **P2-4 Screen/viewport split (behavior change; D1 shared view, D2 2,000 lines).**
   - Separate the view offset from the write position (fixes VIEW-01). Bound the scrollback (VIEW-02).
   - Tests: PageUp then output and commands, `PRINT AT` while scrolled, resize while scrolled, `CLEARSCREEN` while scrolled, snapshots and telnet diffs with a view offset.

Phase 2 exit (roadmap): core terminal behavior tested without Unity (steps 1–2 and 4), GUI and telnet sharing it through adapters (step 2), renderer compatible (all steps).
