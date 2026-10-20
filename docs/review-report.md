# kOSIm code and repository review (after release 1.5.1.100)

Reviewed: `master` at `e280f197` (release commit `5bd2afab`, base upstream develop
`8f281a45`). Date: 2026-10-20.

## Scope and method

- Read the kOSIm diff against `8f281a45` and the subsystems it touches: the terminal
  window and session, the screen buffer and snapshots, the input router and pump,
  command history, telnet, input ownership and mouse blocking, the toolbar panel and
  pickers, archive I/O, link deletion, per-CPU files, the `.ci` gate, and the docs
  (`docs/`, `doc/source`).
- The Pi worker (GPT 6.1 Sol) did two read-only sweeps: dead and duplicate code, and
  docs against code. I re-checked every Pi claim listed here against the current code
  unless the item says otherwise.
- **Not run:** the headless gate (`bash .ci/test.sh`), any build, and any in-game or
  Proton test, as requested. Nothing here is a reproduction. Performance items are
  from reading the code, not from profiling: they describe allocation and complexity,
  not measured frame time.
- Origin labels:
  - **kOSIm**: introduced or changed by this fork.
  - **inherited**: present in upstream `8f281a45`.
  - **inherited, touched**: upstream code that kOSIm edited nearby without fixing.
- Severity:
  - **High**: can stall or break the game, or lose data.
  - **Medium**: a real defect or leak with a limited effect.
  - **Low**: inefficiency, inconsistency or doc error.
  - **Info**: worth knowing; no action required.

Overall, the new code is careful. It is defensive about Unity lifetimes, thread
ownership and bounded resources, and it is well documented. I found no data-loss bug.
The findings cluster in four places:

1. Blocking telnet sends that are still on the game thread.
2. One picker lifecycle leak.
3. Per-frame work that runs whether or not anyone is watching.
4. Doc statements that drifted from the code. Most of these are inherited, but several
   are in paragraphs kOSIm rewrote.

---

## 1. Correctness and robustness

### R-01 (High, kOSIm and inherited): game-thread telnet sends can still block for seconds

**Status: done** (2026-10-20). Only the writer thread writes to the socket; game- and
reader-thread output is queued (`TelnetConnectionWorker.Post`, the output queue), and
`StopListening` waits at most 250 ms (`Flush`). This also resolves R-02 and R-03. Headless
tests and full build only; not yet checked in game. Details: `testing-and-build.md`,
"Fixes from the post-release review".

`TelnetSingletonServer.SendTextRaw(byte[])` (`src/kOS/UserIO/TelnetSingletonServer.cs:408`)
calls `connection.Send`, which is `TrySend(buffer, Timeout.Infinite)`
(`src/kOS.Safe/UserIO/TelnetConnectionWorker.cs:127-130`). `TrySend` takes `writeLock`
and then calls `stream.Write`, which can block for up to the 5 s `SendTimeout` (line 82).
The writer thread holds the same lock while it writes queued output to a client that
has stopped reading.

So a game-thread raw send to a stalled client waits up to 5 s for the lock, and then up
to 5 s more for its own write. Game-thread callers:

- `DisconnectFromProcessor` (`:244-253`): **two** raw sends (title and the
  `{detached}` line). It is reached from `TermWindow.OnDestroy`, then
  `session.Dispose`, then detach on every vessel unload or scene change with a client
  attached.
- `StopListening` (`:432-444`): calls `DisconnectFromProcessor()` **before** its own
  goodbye `TrySend(…, GOODBYE_LOCK_TIMEOUT_MS)`. That defeats the 250 ms protection the
  goodbye was given: the unbounded sends run first.
- `LineAtATime` / `Negotiate` (`:522-530`), `AllowTerminalResize` (`:541`, `:553`) and
  the menu/raw output paths (`:627-629`, `:680`, `:753`).

`testing-and-build.md:315` records "a raw send … can block the game thread up to 5 s".
Two parts of that are understated:

- the worst case is a multiple of 5 s per call site;
- `StopListening`'s bounded goodbye does not bound the shutdown.

Suggestion:

- Route `DisconnectFromProcessor`'s two sends through `TrySend` with
  `GOODBYE_LOCK_TIMEOUT_MS`, or through the output queue.
- More generally, give every game-thread raw send a short lock timeout and treat a miss
  as a stalled client (request a stop).
- Only the writer thread should ever wait indefinitely.

### R-02 (Low, inherited): the detach title is sent unmapped

`DisconnectFromProcessor` (`TelnetSingletonServer.cs:252`) sends
`(char)TITLEBEGIN + detachMessage + (char)TITLEEND` through `SendTextRaw(string)`,
without `terminalMapper.OutputConvert`. The client receives two raw private-use
codepoints, UTF-8 encoded, instead of the xterm title escape.

`StopListening`'s goodbye title (`:444`) does convert, so the two disagree. This is the
same as upstream, but cheap to fix with the same `OutputConvert` call.

### R-03 (Low, kOSIm): detach output can overtake queued output

The detach messages are raw writes that bypass `TelnetOutputQueue`. Screen output still
queued for the writer thread can therefore arrive **after** "detached", or be dropped by
the stop.

This is cosmetic, but it is the only ordering exception left after NET-03 made
everything else go through the queue.

### R-04 (Low, inherited): `TermWindow.Update` continues after deciding to close

**Status: done** (2026-10-20, with P-01 to P-06, R-04 and R-05). `Update` returns after `Close()`; the telnet detach still runs through `OnDestroy`. Built; no headless test (Unity).

`TermWindow.Update` (`src/kOS/Screen/TermWindow.cs:372-378`) calls `Close()` when
`shared`, the vessel or the parts are gone, but does not `return`. It goes on to:

- `session.Update`;
- `ProcessTelnetInput`;
- `ProcessUnconsumedInput`: the router reads `host.HasBooted`, which is
  `shared.Processor.HasBooted` (`:898-900`) and has no null check;
- `shared.Screen.BeepsPending` (`:395`).

With `shared == null` that is an NRE every frame. With a vessel that has no parts, it
keeps feeding input to a dying CPU.

Upstream has the same shape (upstream `TermWindow.cs:355-365`), and in practice
`AttachTo` runs before the first `Update`. Adding `return;` after `Close()` is a
one-line hardening, but check that the telnet detach path still runs, through
`OnDestroy`.

### R-05 (Low, inherited): Ctrl-D path without a null check

**Status: done** (2026-10-20, with P-01 to P-06, R-04 and R-05). Ctrl-D/Ctrl-X without an interpreter do nothing. Test: `TerminalInputRouterTest`.

`TerminalInputRouter` (`src/kOS.Safe/UserIO/TerminalInputRouter.cs:205`) calls
`host.Interpreter.IsAtStartOfCommand()` without checking `host.Interpreter`. The other
router paths check (`:112-113`).

### R-06 (Info, inherited): `ListPickerDialog` uses `title.GetHashCode()` as its window ID

`src/kOS/Screen/ListPickerDialog.cs:176`. A collision with another IMGUI window ID is
unlikely, but it would make the two windows share drag and focus state.

`UniqueId`-style IDs are already used elsewhere in kOSIm.

### R-07 (Info, kOSIm): exception handling on scene change is inconsistent

`KOSInputOwnership.OnSceneLoadRequested` (`src/kOS/Screen/KOSInputOwnership.cs:124-127`)
calls `Instance.ReleaseAll()` without a try/catch. Its twin in `KOSMouseBlocking`
(`src/kOS/Screen/KOSMouseBlocking.cs:166-177`) wraps the call and logs the first
failure.

If `ReleaseAll` ever throws, the exception goes to KSP's event dispatch, and kOS's own
once-only logging does not see it. Make the two handlers match.

---

## 2. Lifecycle and leaks

### L-01 (Medium, kOSIm and inherited): the IP-address picker leaks a component on each toggle-close

**Status: done** (2026-10-20). `ListPickerDialog.Close()` destroys the component, and
`GoAway` closes the IP picker. Built; the in-game object-count check is still to do.

`ListPickerDialog.Close()` (`ListPickerDialog.cs:122-125`) only sets `running = false`.
`OnGUI` returns at once when `!running` (`:154-155`), so the `Destroy(this)` at the end
of `OnGUI` (`:182-183`) is reached only when the dialog closes **itself** (a pick or its
close button), never after an external `Close()`.

- **Font picker:** the toolbar destroys it explicitly (`KOSToolbarWindow.cs:839-841`,
  and `GoAway` at `:204-209`).
- **IP picker:** it does not.
  - Clicking the address button again (`:872-876`) runs `ipAddrPicker.Close();
    ipAddrPicker = null;` with no `Destroy`.
  - `GoAway` (`:203-209`) ignores `ipAddrPicker` completely.

Each open-then-toggle-closed cycle leaves a disabled `ListPickerDialog` on the toolbar's
long-lived GameObject. Its `MouseBlocking.Area` stays registered, because `OnDestroy`
never runs. The area stops blocking a frame later (`MouseBlocking.IsVisible`), but its
raycast blocker is only hidden, not destroyed: `Hide` versus the destroy in
`Unregister`.

The leak is small per cycle, but unbounded. Suggestions:

- Make `Close()` call `Destroy(this)`. Upstream's comment says the dialog is "meant to
  be a one-off instance", and every caller nulls its reference.
- Failing that, mirror the font picker in both places.

`testing-and-build.md:327` lists "list picker … open-close object counts" as never
performed. This is the case it would catch.

### L-02 (Low, kOSIm): per-CPU history and geometry files are never removed

`kOSProcessor.CpuFilePath` (`src/kOS/Module/kOSProcessor.cs:579-592`) writes
`saves/<save>/kOS/{history,terminal}/<persistentId>[-n].txt`. Nothing deletes them when
a vessel is destroyed or recovered, or when a part is removed.

- Each file is small: history is at most 500 commands, geometry at most 4 KiB.
- But the folder grows without bound over a long career.
- `persistentId` reuse could, in principle, hand an old history to a new CPU.

Options:

- prune files whose `persistentId` is not in `FlightGlobals.persistentPartIds` (or the
  save's protovessels) at save load;
- or document it next to the "quickload does not roll it back" note
  (`testing-and-build.md:192`).

### L-03 (Low, kOSIm): leftover archive temp files are visible to scripts

`ArchiveFileWriter` writes `.kosim-save-<guid>.tmp` beside the destination
(`src/kOS.Safe/Persistence/ArchiveFileWriter.cs:24`). After a crash mid-save, the file
stays, as documented in `testing-and-build.md:267` ("no sweep"), and `LIST`/`OPEN` show
it in the archive.

Possible fixes:

- hide the `.kosim-save-*.tmp` pattern in archive listings;
- or sweep files older than a minute when the archive is first listed.

### L-04 (Info, kOSIm): unused textures still loaded

`TermWindow.installTextures` (`TermWindow.cs:79-86`, checked at `:191`) still loads 7
`dds` textures that are no longer drawn, to detect a broken install. This is a
deliberate decision (`testing-and-build.md:186`).

Two side effects to note:

- the textures stay resident;
- the missing-install message still names files the window no longer needs.

A cheaper check would be `File.Exists` on one asset, or on the plugin DLL path.

---

## 3. Performance (from reading the code; nothing measured)

None of these is a hot spot on its own: they are per-frame or per-tick costs paid per
CPU. They add up with many CPUs and become GC pressure. Under Unity's non-generational
Boehm GC on KSP 1.12, steady small allocations end in periodic stalls. The items are
listed roughly by payoff.

### P-01 (Medium, kOSIm): allocations every frame per CPU while idle at the prompt

**Status: done** (2026-10-20, with P-01 to P-06, R-04 and R-05). Early return on an empty type-ahead; the closures moved to a separate method, because C# allocates a method's captured variables on entry, before any early return (the allocation test caught it). Measured: 0 bytes per idle update. Details for all items: `testing-and-build.md`, "Fixes from the post-release review".

`TerminalInputRouter.ProcessUnconsumedInput` (`TerminalInputRouter.cs:106-130`) builds
`new InputBudget(…)` (which starts a `Stopwatch`) plus two closures every `Update` while
the interpreter waits for a command. This happens even when `CharInputQueue` is empty,
which is nearly always.

Fix: return early when `screen.CharInputQueue.Count == 0`.

### P-02 (Medium, kOSIm): telnet input processing allocates every frame with no clients

**Status: done** (2026-10-20, with P-01 to P-06, R-04 and R-05). Early returns in `TermWindow.ProcessTelnetInput` and the router (closures in a separate method, as P-01). Measured: 0 bytes per update without clients.

`TermWindow.ProcessTelnetInput` (`TermWindow.cs:815-821`) runs
`session.Viewers.Where(…).ToArray()` every frame per CPU. Then
`ProcessClientInput` (`TerminalInputRouter.cs:~70-96`) builds arrays of delegates and
closures for each client, and a budget factory.

Fix: return early when `session.ViewerCount == 0`. That covers 99% of play time.

### P-03 (Medium, kOSIm and inherited): every terminal takes a snapshot every 50 ms

**Status: done** (2026-10-20, with P-01 to P-06, R-04 and R-05). Snapshot only while the window is open or a client is attached, taken at once when someone starts looking; one timer; a viewer's copy kept when nothing changed and the layout is the same; `DeepCopy` copies once; the `IScreenSnapShot` doc states that rows are live. Measured: 0 bytes per refresh with nobody looking. Checked: skipping snapshots does not leave the prompt overlay's position stale for any reader that matters (`TextEditor.UpdateSubBuffers` only feeds the merge; `MarkRowsDirty` only touches change times).

`TerminalSession.Update` (`src/kOS.Safe/UserIO/TerminalSession.cs:106-125`) builds
`new ScreenSnapShot(Screen)` every 50 ms for **every** CPU, whether or not its window is
open and whether or not a client is attached. Each snapshot:

- allocates a list of row references;
- pads rows;
- merges sub-buffers (`ScreenBuffer.GetBuffer`, `ScreenBuffer.cs:368-405`).

This is inherited (`GetNewestBuffer` did the same), but the session rewrite made it easy
to gate: take the snapshot only when the GUI is open or `ViewerCount > 0`.

Related:

- `lastSnapshot` and `lastRefresh` (`:63-64`, `:110-118`) are two timers with the same
  interval, updated together. One timer would do.
- `Repaint` (`:278`) runs `Snapshot.DeepCopy()` per viewer every 50 ms even when
  `DiffFrom` returned an empty string. When nothing changed, the previous copy can be
  kept.
- `DeepCopy` (`ScreenSnapshot.cs:~68-79`) copies each line twice: `line.ToArray()`,
  then `ArrayCopyFrom`.
- `RepaintFull` (`:296`, `:344`) builds a fresh snapshot and then deep-copies it. The
  copy is needed only because `GetBuffer` returns live `buffer` lines, so a snapshot is
  not a snapshot.

That last point is worth stating in the `IScreenSnapShot` doc. It is also why `DiffFrom`
can never skip rows by identity.

### P-04 (Low, kOSIm): `DiffFrom` allocates for unchanged rows

**Status: done** (2026-10-20, with P-01 to P-06, R-04 and R-05). Chunks written as found (no list, no row string, no padding rows); the output is byte-identical to the previous code on 3,000 random screen pairs. Measured: an unchanged screen's diff allocates the same few bytes at 5×20 and 100×200.

Since VIEW-05 removed the change-time skip, `ScreenSnapShot.DiffFrom`
(`src/kOS.Safe/Screen/ScreenSnapshot.cs:102-218`) compares every row, which is correct.
But for **every** row it also:

- runs `new List<DiffChunk>()` (`:135`);
- runs `newRow.ToString()` (`:163`), which builds a string the width of the row;
- for missing or empty rows, allocates `new ScreenBufferLine(…)` (`:121`, `:125`).

That is per viewer, every 50 ms. Fix:

- allocate the list lazily, or reuse one cleared list;
- call `ToString()` only when `diffs.Count > 0`.

### P-05 (Low, kOSIm and inherited): toolbar panel rebuilds its model several times per OnGUI event

**Status: done** (2026-10-20, with P-01 to P-06, R-04 and R-05). The CPUs, their rows, the config keys and the settings sections are worked out once a frame (`Time.frameCount`). Built; no headless test (Unity).

`KOSToolbarWindow.OnGUI` (`src/kOS/Screen/KOSToolbarWindow.cs:397-470`) runs
`GatherCpus()` (`:476`) on **every** IMGUI event: Layout, Repaint and each input event.
That means:

- iterating all processors;
- running LINQ over part modules;
- allocating a `ToolbarCpuEntry` per CPU.

Then, within one event:

- `ToolbarCpuList.Rows` is computed three times: `ContentHeight` in `OnGUI` (`:427`
  through `:506`), `ContentHeight` again in `DrawContent` (`:560`), and `DrawCpus`
  (`:604`).
- On the settings tab, `GetConfigKeys()` and `ToolbarSettings.Sections` run twice to
  measure and once to draw (`:679-681`, `:783`).

Fix: gather once per frame (cache by `Time.frameCount`) and compute rows once per
event.

### P-06 (Low, kOSIm): the status refresh recomputes titles even when unseen

**Status: done** (2026-10-20, with P-01 to P-06, R-04 and R-05). Titles and state only while someone looks; worked out again at once when a telnet client attaches, so it gets the current title. Built; no headless test (Unity).

`TermWindow.RefreshStatus` (`TermWindow.cs:1745-1771`) gates `DescribeStatus` on
`IsOpen || ViewerCount > 0 || StatusWanted`. But every 0.25 s per CPU, unconditionally, it:

- recomputes `CalcualteTitle()` and `CalculateHeaderTitle()` (each runs
  `Modules.OfType<KOSNameTag>()`, `Split(' ')` and string formatting);
- calls `session.ChangeTitle(…)`.

Move both into the gated block. Telnet only needs the title when a viewer is attached,
and `AttachViewer` already sends it.

### P-07 (Low, kOSIm): the status strip is rebuilt on every IMGUI event

`TermWindow.DrawStrip` (`TermWindow.cs:1320-1327`) allocates a `new StatusStrip` with
measuring lambdas, and the size string `ColumnCount + "x" + RowCount`, on every
`OnGUI` event of an open terminal. Cache both, invalidated on a size, state or font
change.

### P-08 (Low, inherited): rows converted to strings on every event

`DrawCharacters` (`TermWindow.cs:1255`) runs `line.ToString().Replace('\0', ' ')` for
each visible row on every `OnGUI` event: two strings per row per event, times about 2–4
events per frame.

This is the largest steady allocation of an open terminal. A per-line cache keyed on the
line's change stamp would remove almost all of it. This is the "large-paste frame time"
area the checklist mentions.

### P-09 (Low, kOSIm): a full scrollback shifts the whole list per printed line

`ScreenBuffer.ScrollScreen` (`src/kOS.Safe/Screen/ScreenBuffer.cs:489-494`) runs
`buffer.RemoveRange(0, excess)` as soon as `topRow > MAX_SCROLLBACK`. With the 2000-line
scrollback full, every new line shifts about 2000 references.

This is the likely cause of the open note "the worst update of a 3,000-line paste grows
with the scrollback" (`testing-and-build.md:319`). Possible fixes:

- trim in batches (for example, let the scrollback overshoot by 10% and then drop it in
  one `RemoveRange`);
- or switch to a ring buffer.

Related:

- `ResizeBuffer` (`:307`) reallocates every line, scrollback included, on every
  `SetSize`.
- `SetSize` sums the resize notifiers with LINQ (`:189`).

### P-10 (Low, kOSIm): command history saved synchronously on every command

`CommandLineEditor.RecordCommand` (`src/kOS.Safe/Screen/CommandLineEditor.cs:79-85`)
calls `historyStore.Save(…)` on the game thread after **every** recorded command. Each
save:

- serializes up to 500 commands;
- creates the directory;
- writes a temp file;
- runs `File.Replace`.

Under Proton every one of those is a Wine syscall chain. This is fine at a human typing
rate. It costs more for scripts that feed the prompt, or for pastes of many lines (each
line is a recorded command).

Suggestion: mark the history dirty and save at most every N seconds, plus on destroy.
The geometry file already does this (`testing-and-build.md:192`).

### P-11 (Low, kOSIm): link checks walk the tree twice under Wine

Under Wine, `LinkSafeDelete.IsLink` (`src/kOS.Safe/Persistence/LinkSafeDelete.cs:77-85`)
makes two `wine_get_unix_file_name` P/Invokes per entry (`ResolvesElsewhere`).

- `Archive.Delete` calls `CheckDeletable` (`Archive.cs:217`), which walks the whole tree
  through `FirstLinkIn`. Then `LinkSafeDelete.DeleteDirectory` walks it again.
- `MOVEPATH` (`VolumeManager.cs:399`) checks the tree before the copy and delete.
- `FirstLinkIn` (`:91-105`) also builds a new `FileInfo`/`DirectoryInfo` from
  `child.FullName` for each child it already holds as a `FileSystemInfo`, which is an
  extra stat per entry.

This only matters for large directory deletes. Pass the `FileSystemInfo` down, and
consider a combined check-then-delete walk that collects the entries once.

### P-12 (Info, kOSIm): small per-event costs

- `KOSMouseBlocking.Show` (`KOSMouseBlocking.cs:99-115`) calls `Update()` (input polling,
  lock decision, `UpdateBlockers` over all areas) for every window on every `OnGUI`
  event. That is harmless, as the comment says, but it is N windows × M events of work
  that the per-frame driver already does. Calling only the lock decision would be
  enough.
- `TermWindow.cs:714` concatenates `"TermWindowPaste" + UniqueId` every frame while the
  paste box shows. The name is only used at registration, so compute it lazily.
- `TelnetOutputQueue.Write(char)` (`src/kOS.Safe/UserIO/TelnetOutputQueue.cs:84`)
  allocates `ch.ToString()` per char.
- `TerminalInputDecoder`'s pending-bytes check allocates `new byte[0]`.
- `MouseBlocking.IsInsideMyExposedPortion` allocates a `FindIndex` lambda.
- `KOSStyle.ChromeFont` reads `HighLogic.Skin` on each lookup.

---

## 4. Dead, test-only and duplicated code

### D-01 (Low, kOSIm): public members never referenced

I verified each of these with grep over `src/`:

- `InputOwnership.IsSuspended` (`src/kOS.Safe/UserIO/InputOwnership.cs:78`).
- `ToolbarPanelLayout.MaximumScroll` (`src/kOS.Safe/Screen/ToolbarPanelLayout.cs:168`).
- `TermWindow.CheckResizeDrag()` (`TermWindow.cs:1598-1601`): protected and never
  called. Only `CheckResizeDrag(false)` is used (`:1147`, the fault fallback), so the
  `updateScreenSize == true` branch of `CheckResizeDrag(bool)` (`:1603`) is
  unreachable. Either it is kept for subclass API compatibility (then say so in a
  comment, as `SetupBackingConfigInts` and `CheckHoverOnPreviousGUIElement` do at
  `KOSToolbarWindow.cs:979`/`:1045`), or it can go along with the dead branch.

### D-02 (Info, kOSIm): members used only by tests

Pi found these, and I spot-checked them. They are fine as test seams, but `internal`
plus `InternalsVisibleTo` (already used by kOS.Safe.Test) would keep them off the public
API:

- `CommandHistory.Clear`
- `CommandHistoryFile.Path` and `TerminalGeometryFile.Path`
- `FlatWindowLayout.ScrollThumb`/`ScrollFor` and `ToolbarPanelLayout.ScrollThumb`/`ScrollFor`
- `PaletteColor.Over`/`ContrastRatio`
- `InputBudget.Taken`
- `TelnetConnectionWorker.ReaderAlive`/`WriterAlive`/`IsEventClosed`
- `TelnetInputParser.NOP`/`HasPendingInput`
- `TelnetInputPipeline.Dialect`
- `TelnetInputQueue.Count`/`IsOverflowing`/`DiscardedByBreakCount`/`OverflowCount`/`HighWaterMark`
- `TelnetOutputQueue.HighWaterMark`

### D-03 (Low, kOSIm and inherited): duplicated logic

- **VT100 key tables:** `TerminalInputDecoder.cs:246-264` and
  `TerminalVT100Mapper.cs:179-202`. The old one is kept for binary compatibility; one
  should be built from the other so they cannot drift.
- **Part title first word**, in four places:
  - `ToolbarCpuList.cs:89-94`
  - `TelnetWelcomeMenu.cs:229-230`
  - `TermWindow.CalcualteTitle` and `CalculateHeaderTitle` (`TermWindow.cs:1806-1829`)
- **Terminal-type dispatch:** `TerminalTypeNames.cs:28-34` and
  `TerminalUnicodeMapper.cs:58-65`.
- **Rectangle clamping:** `WindowPlacement.cs:20-23`/`130-136` and
  `RectExtensions.cs:38-46`.
- **Title code:** `CalcualteTitle` and `CalculateHeaderTitle` duplicate the name-tag
  lookup. `CalcualteTitle` is also misspelled. It is `internal` and inherited, so
  renaming it is safe within the assembly.

### D-04 (Info, inherited): `DumpDepthSort` is debug code that nothing calls

`KOSManagedWindow.cs:261`/`:277` contain only commented-out calls to `DumpDepthSort`,
and its loop body is empty.

---

## 5. Docs against code

### Contradictions in kOSIm's own docs

- **K-01 (Medium):** the link write policy contradicts itself.
  - `testing-and-build.md:271` (IO-04) and `KOSIm-roadmap.md:130` say archive links are
    "followed to read, **write** and list".
  - `testing-and-build.md:317` (known issues) says "a save onto a file link replaces the
    link with a regular file". `ArchiveFileWriter` behaves that way: temp file, then
    `File.Replace`/`Move` over the link.
  - Directory links are followed for writes; file links are not.
  - Fix the IO-04 line and the roadmap's binding decision to say so.
- **K-02 (Low; done 2026-10-20 with P-06):** `testing-and-build.md:173` says the status refreshes "only while the
  GUI is open or a client is attached". It also refreshes while the toolbar's CPUs tab
  asks for it (`StatusWanted`, `TermWindow.cs:1753`). Title work runs unconditionally
  (P-06).
- **K-03 (Low):** `testing-and-build.md:180` says the frame is "2 at UI scale 1.5 and
  above". The border is `Scaled(1)`, rounded away from zero
  (`TerminalChromeLayout.cs:81`, `:96-99`), which gives 3 px from scale 2.5 up to the
  3.0 clamp.
- **K-04 (Low):** `testing-and-build.md:168` (COPY-01) says "control characters other
  than tab/LF dropped". Only C0 is dropped; DEL and C1 pass through
  (`TerminalCopy.cs:50-61`). Either the doc or the filter should change. Dropping DEL
  and C1 too seems to be the intent.
- **K-05 (Low):** `testing-and-build.md:183` says `TERMINAL:BRIGHTNESS` "now reads what
  was set". The value is stored as a float (`TerminalStruct.cs:145-146`), so 0.7 still
  reads back as 0.699999988… whenever it was set by script. Only the per-frame
  overwrite was removed.
- **K-06 (Low):** `testing-and-build.md:254` says "each CR or LF is one Enter". CR LF,
  LF CR and CR NUL pairs are coalesced into one Enter, which is correct behaviour. Say
  "each line break".
- **K-07 (Low):** `testing-and-build.md:41` says the gate matches known failures by
  "exact name and message". `check-test-results.py:41` matches the message by
  **substring fragments** (`message_contains`). This is dormant while the list is
  empty. The `upstream_commit` and `upstream_test_count` fields of
  `known-test-failures.json` are not read by the checker.
- **K-08 (Info):** `KOSIm-roadmap.md:96` says the hovered terminal "never" takes keys.
  `CAMERACONTROLS` still passes camera keys while hovering, as
  `testing-and-build.md:262` already says. Align the roadmap wording.

### Inherited errors in user docs kOSIm edited (`doc/source`)

kOSIm rewrote paragraphs next to each of these, so a reader will take them as current:

- **K-09 (Low, inherited, touched):** `doc/source/structures/misc/config.rst`.
  - `:387` says `DEFAULTFONTSIZE` has "range = [6,20]".
  - `:399-400` says "at least 6 … no greater than 30".
  - The code clamps to `TerminalStruct.MINCHARPIXELS`–`MAXCHARPIXELS`, which is 4–48
    (`src/kOS/Suffixed/Config.cs:87-89`). kOSIm's own header buttons use 4–48
    (`testing-and-build.md:204`).
  - kOSIm edited lines 393-398 between those two statements.
- **K-10 (Low, inherited, touched):** `doc/source/structures/misc/terminal.rst:169` gives
  `CHARHEIGHT` as "[4..24]". The code allows 4–48.
- **K-11 (Low, inherited, touched):** `doc/source/general/settingsWindows.rst:116`, Key
  Note 13 of the Difficulty-settings section, describes a terminal brightness slider.
  `kOSCustomParameters` has no brightness parameter; `TerminalBrightness` is a
  `config.xml` key, shown on kOSIm's new Settings tab. kOSIm rewrote the panel section
  of this file.
- **K-12 (Low, inherited):** other `config.rst` mismatches:
  - `:53` gives the IPU default as 150; the code has 200
    (`src/kOS/Module/kOSCustomParameters.cs:53`).
  - The `UCP`, `SAFE`, `AUDIOERR` and `VERBOSE` defaults are documented as False, but
    default to true (Pi; not re-verified one by one).
  - `:351` calls the key "Brightness"; it is "TerminalBrightness".
  - `:35-38` says every non-telnet setting is local to the save file. The terminal
    defaults (font, size, brightness, visual beep, key click) and the suppress setting
    are global in `config.xml`. kOSIm's own `settingsWindows.rst` now says "saved for
    all games", so the two pages contradict each other.

### Doc nuance (Info)

`TelnetInputQueue`'s comment says text typed after a break is kept. The dispatcher drops
text after a break within the same read, until the socket drains. This matches upstream
and is documented as such in the NET-03 notes; the queue's own comment should say the
same.

---

## 6. Planned work that the code does not support yet

- **N-01 (Info):** the geometry-presets step plan expects `CpuTextFile.Write` to report
  failure. Today it returns `void`, and a failure is only logged once per CPU
  (`src/kOS.Safe/Screen/CpuTextFile.cs:84-98`). Changing it to return `bool` is
  backward-compatible (it is `internal`). Note it in the plan's first step, so the
  preset UI does not assume it.

---

## 7. Checked and found sound

Reviewed without findings worth listing:

- `TelnetInputParser` and `TerminalInputDecoder` (persistent UTF-8, split sequences,
  Escape timeout);
- `TelnetInputPipeline`, `TelnetInputDispatcher` and `TelnetInputQueue` (bounds,
  break handling);
- `TelnetConnectionWorker` shutdown (cooperative, bounded join, no `Thread.Abort`);
- the `TelnetOutputQueue` overflow-to-repaint path;
- reader-thread logging (it uses thread-safe `Debug.Log`);
- `CommandHistory` and `CommandHistoryFile` (lazy load, caps);
- `CpuTextFile` (size cap, once-only logging);
- `ArchiveFileWriter` (no destructive fallback);
- the CRLF normalization in `ConvertToWindowsNewlines`;
- `VolumeManager` move-onto-itself and volume identity;
- `LinkSafeDelete` (guard inside `DeleteLink` as a second line of defence);
- `KOSStyle` (styles cached per size; one solid texture);
- `TerminalChromeLayout` scale clamping, `WindowPlacement` and `WindowKeeper`;
- `TerminalGeometry` and `TerminalGeometryFile` (validation, write throttling);
- `GUIWindow`'s delayed GUI actions, `Widget` and `PopupMenu`;
- `VesselUtils.SetTarget` (mouse lock lifted in `finally`);
- `Utils`'s texture cache and `DestroyCreated`;
- `TelnetMainServer` and `TelnetWelcomeMenu` budgets;
- `CPU`'s cancel-and-keep for running compiles and `RunningProgramPath`;
- `TextEditor` and `SubBuffer` argument checks;
- version properties (`kOS.props` 1.5.1.100).

**Not reviewed in depth:** the second half of `KOSTextEditPopup.cs` (from about line
360), `DelegateDialog` and `KOSNameTagWindow`. Their kOSIm diffs are small styling and
placement changes.

---

## 8. Suggested order

1. **R-01 (done):** bound every game-thread telnet send. Add a regression test with a stalled
   fake stream, `DisconnectFromProcessor` and `StopListening` returning within the
   bound.
2. **L-01 (done):** `ListPickerDialog.Close()` destroys the component, and `GoAway` handles the
   IP picker. Then do the "list picker open-close object count" in-game check.
3. **P-01, P-02, P-03 (gating), P-06 (done, with P-04, P-05, R-04 and R-05):** early returns when there is no input, no viewer
   or no window. These are small, testable changes that remove most of the idle
   per-CPU work.
4. **K-01** and **K-09–K-12:** doc fixes. The link policy is a stated binding decision,
   so correct it first.
5. **P-09** (batched scrollback trim) together with the open "large paste grows with
   the scrollback" item, measured before and after.
6. **P-04, P-05, P-07, P-08, P-10:** allocation and I/O trims, ideally with a simple
   allocation counter in a headless test so the gains can be shown, not assumed.
7. **L-02, L-03:** decide on pruning or hiding, then document.
8. **D-01–D-04, R-02–R-07:** cleanup when touching those files.
