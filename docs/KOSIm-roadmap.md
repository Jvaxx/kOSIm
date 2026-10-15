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

## Current platform and testing scope

- **Local tests only.** No hosted CI, remote runners, or hosted release automation. Run the local suite before completing changes and retain local results; failure-propagation checks remain required.
- **One runtime target for now:** the Windows Steam build of KSP under Proton on the user's Linux machine. Native Linux, native Windows, and macOS game validation are deferred, not implied by upstream compatibility.
- Approved development instance: `/home/jvz/Games/KSP-kOSIM-dev/`; use its `KSP_x64_Data/Managed` for builds and its GameData/disposable saves for game tests. It is an independent stock copy without personal saves; the tested local kOSIM build is now deployed.
- Use the dev instance's `launch-dev.sh` with installed UMU and GE-Proton11-7 and its isolated `.proton-prefix`. First launch initialized the isolated prefix and opened a visible KSP window; logs identify KSP 1.12.5.3190 and loaded kOS assemblies. The overlay build passed a guided GUI smoke check. Build `e943ecf4` was deployed on 2026-10-12 for a guided Proton checkpoint (dev instance only): the telnet checks passed, apart from a runaway at a 1×1 telnet size with two clients (recorded as a known edge case); on 2026-10-15 the prompt, dimension/cursor and raw I/O/archive-save checks passed; the editor, GUI, boot-seeding, render-fault-recovery (temporary injection build) and output-overflow checks passed too, completing the checkpoint (see the handoff). Never reuse the vanilla prefix or silently change runtime versions.
- Vanilla source `/home/jvz/.local/share/Steam/steamapps/common/Kerbal Space Program/` remains untouched and is not a deployment target.
- Distinguish host Linux/Mono headless tests, compilation against game references, and actual Proton/Unity gameplay checks. None substitutes for the others.
- Record the exact KSP/Proton versions for reproducible game checks. Preserve scripts, saves, craft, and APIs within this target; avoid unnecessary platform-specific breakage elsewhere without promising untested support.
- The original review is retained unchanged; its hosted-CI and cross-platform recommendations are superseded by this scope.

See [testing-and-build.md](testing-and-build.md) for commands and evidence.

## Starting point

- Upstream: <https://github.com/KSP-KOS/KOS>.
- Local workspace: `/home/jvz/Projects/KOS-Improved`.
- Initial clone: `develop`, commit `8f281a459b6ea0ba917c91bbbc117e68bb948199`.
- Review document: [improvements-overview.md](improvements-overview.md).
- The review used commit `d4388439c712994e47258ed7514e863f79da632b`, not the clone's starting commit. Revalidate findings before implementing fixes.
- Session progress and unfinished work belong in [HANDOFF.md](HANDOFF.md).

This is an agreed direction, not a claim that builds, tests, reproductions, or implementation have already been completed.

## Phase 0 — Trustworthy local baseline

### Work

- Establish reproducible build and test commands, including KSP reference setup without committing proprietary game assemblies.
- Choose a verified test runner or deliberately migrate the existing test framework. The current test project uses NUnit 2.7.1 and targets `net48`; adding `dotnet test` alone is not sufficient evidence of execution.
- Make the local test command execute tests, report discovered/executed counts, retain XML results, and propagate failures. Verify with an intentionally failing test, then remove that failure.
- Require local test runs after code/test-tooling changes and before completing or releasing work; record commands/results in the handoff. Do not add hosted workflows.
- Document the specific Steam KSP/Proton runtime target and local tool versions. The dev copy/build references are established; verify its isolated first launch and create a disposable test save.
- Revalidate the review's highest-priority findings against the cloned baseline.
- Assemble compatibility fixtures: boot and flight scripts, character-grid dashboards, script input, compiled KSM files, telnet sessions, and representative saves/craft where practical.
- Record existing failures separately from regressions introduced by the fork.

### Status

**Headless work complete.** The local build/test commands, gate and baseline are in place. The suite is fully green (832/832 after the review NET-03 and PROMPT-01 work) with an empty known-failure list. The dev instance and its isolated Proton launcher are defined and smoke-tested.

Compatibility fixtures now cover these headlessly (details in [testing-and-build.md](testing-and-build.md#compatibility-fixtures-phase-0)):

- Representative scripts (printing, functions/delegates, triggers/waits, file I/O, a CRLF boot file), run from source and from compiled KSM with identical recorded output, in both archive newline modes.
- A character-grid dashboard checked against full-screen snapshots of the real screen model.
- Telnet byte streams (one live inetutils capture; PuTTY and Windows telnet reconstructed) replayed whole and fragmented.

Still manual, on the Proton checklist:

- Saves and craft.
- Real KSP types and mod-facing APIs.
- In-game input routing, rendering and fonts.
- Terminal mappers and the telnet server with real clients.

The fixtures exposed two upstream defects, NET-03 and EXEC-01 (see Phase 1). Both are now fixed with regression tests.

### Exit criterion

Reliable local build/test commands, a documented baseline and known failures, and a defined Proton game-test setup. Passing compilation alone does not satisfy this phase.

## Phase 1 — Correctness and safety

Ship small, independently reviewable changes with regression tests rather than bundling fixes into a redesign.

### Current progress

- `BUFFER-01`: row-intersection overlay clipping implemented; 26 new host-Mono regressions pass, including long input, resize, and seeded compositions. Full build against dev assemblies passes. User-guided Proton GUI long-input/resize smoke check remained responsive, with no matching range/GUI-clip faults in inspected KSP.log. User also confirmed text/cursor alignment during resize and correct command output, passing the guided GUI smoke scope. Telnet and wider gameplay verification remain pending; cursor guards, dimension validation, and GUI cleanup are not part of this fix.

- `BUFFER-04`: model dimension normalization and effective no-op resizing implemented, with 27 host regressions passing and a successful full build. Script minima are unchanged; zero model axes preserve their existing size; other model requests clamp to 1–160 rows / 1–255 columns. Safe output cursor upper bounds after shrinking and pre-mutation subbuffer validation are included. Not yet deployed or Proton-tested.

- `BUFFER-02`: snapshot-only cursor reads with full lower/upper bounds implemented through a safe extension (public snapshot interface unchanged), with 23 passing host regressions and a successful full build. `BUFFER-03`: scoped group/color/skin cleanup plus bounded font/window render recovery implemented and compiled; 34 new headless tests pass. Nonfatal failures use fallback Retry/Close/Resize, 2-second automatic retries, and 10-second diagnostic batching; fatal/Unity GUI-exit control flow propagates. Actual Unity fault injection/control/group verification remains deferred. This boundary does not cover snapshot acquisition, input, title/lifecycle, telnet, or GUI.Window internals. No new deployment.

- `EDIT-01/02`: `EditorDocument` now separates active/pending volume/path, compares text against the last successful load/save, and commits loads only after reading/validation. 48 host regressions pass using real disks/archives and injected I/O failures. Popup open/focus no longer clears dirty state or bypasses modal confirmations; save/discard/cancel, accurate path messages and retryable failure dialogs are wired and compiled. Actual Unity interaction/reopen/modal checks remain deferred. Legacy volume text/newline APIs are unchanged; archive replacement was intentionally separate (see IO-03 below).

- `IO-03`: `Archive.SaveFile()` prepares an exclusively created sibling temporary file, flushes/closes it, then commits with `File.Replace` for existing files or non-overwriting `File.Move` for new ones. No delete/truncate fallback. Owned temporaries are cleaned on pre-commit failure; expected cleanup failures retain both exceptions and the temporary path. 50 host regressions pass, including real filesystem commits and injected faults/races, plus an editor integration case; full build succeeds. Legacy byte/text/newline behavior and public signatures remain unchanged. Actual Unity/Proton Replace/Move, locks, permissions and metadata checks are deferred. This is not a power-loss durability, external-change conflict, symlink-policy or append/clear safety guarantee.

- `IO-01/02`: user approved the additive **Preserve text APIs** policy. New `ReadAllRaw`/`WriteRaw`/`SaveFileRaw` and script `READRAW`/`WRITERAW` preserve built-in bytes; copy/move/file-copy-helper/boot seeding use raw paths. Legacy READALL/WRITE/UTF-8 replacement/BOM/type behavior remains, with idempotent Windows CRLF conversion. 62 new host cases pass, including both managed Windows-flag branches, all byte values, suffixes, transfer/failure/provider hooks and headless CRLF compile/KSM pack-unpack. Old/derived providers retain legacy hooks until explicitly opting into raw overrides; no universal provider guarantee. Compatibility and residual legacy-read risks are documented in [file-io-compatibility.md](file-io-compatibility.md). Full build passes; actual Proton boot/suffix/transfer/text/mod-provider acceptance remains deferred.

- `NET-01/02`: an incremental telnet parser with a persistent UTF-8 decoder, plus cooperative, bounded, idempotent session shutdown, are implemented in `kOS.Safe/UserIO` and wired into the existing server. Its public API is unchanged and game-side cleanup now runs on the game thread. Upstream option-byte leaks (Ctrl-X/Ctrl-C injected into input), IAC escape loss, split-read faults, the EOF spin and self-aborting shutdown were confirmed before the change (harness/code reading) and are covered by 83 headless regressions, including real loopback sockets. Full build passes. The server/menu adapters and Unity/Proton socket behavior are not runtime-tested; see [testing-and-build.md](testing-and-build.md) for the deferred checklist.

- `FMT-01`: the 3 recorded "known" test failures were a real upstream display regression. Since v1.3.0.0, `PRINT`/`:DUMP` of lists and lexicons showed nested primitives as `["value"] = …` sub-dumps (upstream issue #2939 shows it in a game). Display dumps now keep primitives inline, and JSON/message/save formats are unchanged and pinned. The suite is fully green (675/675) and the known-failure baseline is empty. The user decided to keep the JSON format unchanged.

- `NET-03` (fixture-found): telnet terminal types shorter than 5 characters (Microsoft Telnet's `ANSI`, `VT52`, `dumb`, empty) made the mapper factory throw on the reader thread and end the session. Type recognition moved to `kOS.Safe.UserIO.TerminalTypeNames`, which the game mapper calls; such types now get the plain mapper. 23 headless tests. Not checked with a real Windows client. This label is separate from the review's NET-03 (queue budgets/backpressure), below.

- `EXEC-01` (fixture-found): a `COMPILE` that failed at the prompt re-reported its error on every update and blocked the prompt until reboot. The CPU now drops a yield whose check throws, and the threaded detector reports its error once, with the original stack trace. 4 headless tests on the real prompt. Not checked in game.

- Review `NET-03` (implemented and headless-tested): telnet keys split across socket reads now decode as one key (stateful `TerminalInputDecoder`, 100 ms timeout for a lone Escape), and blank lines in pastes keep their Enter. Both telnet queues are bounded (262,144 characters each). Interrupts, resizes and repaint requests overtake waiting text; an interrupt discards the text typed before it. A full input backlog discards further text with a notice until it drains to half; a stalled client's unsent output is replaced by a full repaint. Telnet input and prompt typing are limited per update (4,096 and 2,048 keys, 4 ms), clients take turns. 92 headless tests, including the recordings replayed to keys, end-to-end paste and interrupt tests on the real prompt, and 13 mutations. Headless proxy: the worst update for a 6,000-character pasted line fell from about 370–400 ms to about 5 ms. The game adapters are compiled, not run; real clients, frame time and stalled clients under Proton are deferred. Limits are code constants (user decision).
- `PROMPT-01` (fixture-found by the paste tests): a command wrapped at the bottom of the screen overwrote the last output row, and a line taller than the screen made Enter throw. `TextEditor` now adds the rows before scrolling. 5 headless tests. Not checked in game.
- `IO-05` (2026-10-15): path functions keep a volume or file object's own volume (and relative paths the current volume) instead of looking its name up again; see the exit review below.
- Prompt-level Ctrl-C (found here, fixed 2026-10-15 in its own task, decision 4): Ctrl-C now ends a `WAIT` typed at the prompt; a running `COMPILE` (or `RUNPATH` compile) is cancelled, the prompt waits for its thread and its result is discarded. Headless-verified (`PromptBreakTest`), committed; Proton check deferred by the user. See `docs/testing-and-build.md`, "Ctrl-C at the prompt (decision 4)".

### Priorities

- Continue focused local safety work. Archive byte/text policy, replacement, editor transactions, scoped render recovery and telnet parsing/shutdown are implemented/host-tested within documented limits; target-runtime acceptance remains deferred. Keep all pending GUI/editor/archive/raw I/O/boot/telnet checks until the user resumes Proton.
- Focus and flight-control lock cleanup during hide, close, destruction, power loss, and scene changes (`INPUT-01`).
- Other contained, confirmed defects: port-field capacity (`PANEL-02`, fixed and headless-tested 2026-10-15) and widget selection (`WIDGET-01`, fixed and headless-tested 2026-10-15) and callback origin under comm delay (`WIDGET-02`, fixed and headless-tested 2026-10-15), when adequately testable.

The editor's low feature priority does not lower the priority of preventing data loss. Preserve legacy text API behavior through an explicit compatibility policy rather than changing it incidentally.

### INPUT-01 design (agreed with the user 2026-10-15; keyboard half and mouse half step 1 implemented, compiled and headless-tested; in-game checks deferred)

**Current behavior** (upstream code, checked against `7fa54cff`):

- A focused terminal sets `ControlTypes.All` under the shared name `kOSTerminal` (`TermWindow.Lock`), plus the base `ALLBUTCAMERAS` lock under its per-window name in flight/planetarium. The editor-lock branch in `Lock()` (`IsUnlocked(All)` right after setting `All`) never runs; harmless, since `All` covers the editor bits. `VesselUtils.cs:220` reads and restores `kOSTerminal` by name: **keep the lock names** (other mods may read them too).
- `Open()` always takes focus, whoever opens: part menu, toolbar, action group, or script (`doevent("Open Terminal")` goes through the same KSP event, `PartModuleFields.cs:553`).
- F2 hide and `FlightResultsDialog`: `OnGUI` runs `ProcessKeyEvents()` before those checks (`TermWindow.cs:335`), so an invisible terminal keeps reading keys and keeps its locks.
- Power loss: the interpreter ignores input but the window keeps focus and locks.
- `KOSTextEditPopup.GetFocus/LoseFocus` override without calling the base: **the editor sets no lock**, so typed keys may reach KSP (candidate cause of #2694, #2949; to confirm in game). `KOSNameTagWindow` locks only in the VAB/SPH (re-applied every `OnGUI`), not in flight.
- Script GUI windows (`GUIWindow`) opt out of locking on purpose (#2568); unchanged.
- Close, vessel loss (`Update` → `Close`) and `OnDestroy` release locks. Focus moves only by mouse click (`KOSManagedWindow.UpdateLogic`).

**Agreed policy:**

1. **Camera:** keep locking everything (`All`) while a terminal or the editor has keyboard focus. #3012 stays open; clicking outside releases.
2. **Focus on open:** only an open by the user takes focus (part-menu event clicked by the user, toolbar button). Action groups and scripts open the window without focus and without locks. Scripted invocations are told apart by a scope flag set by kOS around `evt.Invoke()`/`act.Invoke()` in `PartModuleFields`; `Activate(KSPActionParam)`/`Toggle(KSPActionParam)` open unfocused.
3. **Hide / dialog / power:** F2 hide (when `ObeyHideUI`) or the flight-results dialog: stop reading keys and release the locks; take focus back when the UI returns, if the window still wanted it. Power loss: release and do not take it back (flight keys and action groups work at once); the user clicks to refocus.
4. **One keyboard owner** among kOS windows; release on close, destroy, vessel loss and scene change on every path, including exceptions; cleanup idempotent.
5. **Editor and in-flight name-tag window** take the same lock as the terminal while focused (name-tag: in flight too).
6. **Scope of this pass:** keyboard and locks only. Mouse click-through (#2659, #2759) and modal mouse blocking follow as the mouse half below.

**Mouse half (agreed with the user 2026-10-15; step 1 implemented, compiled and headless-tested; in-game checks deferred):**

- **Approach:** step 1, kOS's own hover lock with no dependency (works without ClickThroughBlocker; the user runs a click-through mod, likely CTB). Step 2, an optional CTB integration by reflection, only if an in-game check with the user's CTB shows a conflict.
- **Hover scope:** while the mouse is over any drawn kOS window (terminals, editor, its confirm dialog, name tag, script GUIs and their popup menus, toolbar panel, list picker, telnet dialogs), lock the scene's mouse controls under a separate name `kOSMouseOver`: flight `CAMERACONTROLS | MAP_UI | TARGETING`; editor `EDITOR_SOFT_LOCK | CAMERACONTROLS`; space center `CAMERACONTROLS | KSC_UI`; tracking station `CAMERACONTROLS | TRACKINGSTATION_UI | MAP_UI`. Never keys, flight controls or staging; the #2568 keyboard opt-out stays.
- **Presses and release:** a press started over a kOS window keeps the lock until released (drags, resizes); one started on the scene is not taken over. Release two frames after the lock was last wanted, so a click that closes a window (#2759) or ends a drag does not reach the scene. Scene change releases at once.
- **Modal:** the editor stays frozen under its confirm dialog; the terminal stays usable; the hover lock keeps dialog clicks out of the scene.
- **Shape:** `kOS.Safe.UserIO.MouseBlocking` (pure policy, 20 tests) + `kOS.Screen.KOSMouseBlocking` (KSP sink, per-frame driver addon); `VesselUtils.SetTarget` lifts it like `kOSTerminal`. Details and the pending in-game list: `docs/testing-and-build.md`, "Mouse over kOS windows (INPUT-01, mouse half)". #2659 (key order with CTB) is not addressed by step 1.

**Implementation shape:** a small pure policy class in `kOS.Safe` (window states in: open, wants focus, visible/hidden, dialog, powered, kind; out: the owner and the locks each window should hold) with headless tests for every transition; a thin Unity adapter in `kOS` applies only the differences through `InputLockManager`, keeping the lock names. No change to `GUIWindow` locking.

**In-game checks (Proton, need the user):** first a baseline on the current build: do keys typed in the editor popup reach KSP (`.`/`,` warp, staging off)? Then after the fix: focus cycling between terminals and the editor; F2 while typing and back; flight-results dialog; power loss/restore; action-group and boot-script open (no focus taken); part-menu and toolbar open (focus taken); map view; VAB name tag; vessel destruction; scene exit; no `kOSTerminal`/kOS locks left after each (log `InputLockManager.lockStack` on demand, a debug-only helper).

### Exit review (2026-10-15)

Every P0/P1 finding in `docs/improvements-overview.md` was checked against the current code (`5057a0a3`).

**Fixed and headless-tested; Proton-checked at the `e943ecf4` checkpoint** (details there; listed unperformed checks still apply): `EDIT-01/02`, `BUFFER-01`–`04`, `IO-01/02/03`, `NET-01/02/03`, plus the fixture-found `FMT-01`, `EXEC-01`, `PROMPT-01`.

**Fixed and headless-tested; in-game checks deferred by the user:** prompt-level Ctrl-C (decision 4), `INPUT-01` keyboard/locks, `PANEL-02`, `WIDGET-01`, `WIDGET-02`, `IO-05`, `IO-04` hardening, `INPUT-01` mouse half step 1 (policy only; the Unity side is untested).

**Still open in Phase 1 scope:**

- `IO-05` — **fixed and headless-tested (2026-10-15; in-game check deferred)**: path functions (`COPYPATH`, `MOVEPATH`, `DELETEPATH`, `OPEN`, `EXISTS`, `CD`, JSON, `RUNPATH`, `EDIT`, …) keep a volume or file/directory object's own volume (it must be attached and in range), and paths without a volume id use the actual current volume; strings with a volume id unchanged. Before the fix the second `data` disk's objects (and relative paths after switching to it) acted on the first one, and moving or deleting a file object from a detached disk deleted the other disk's same-named file. 23 tests; see `docs/testing-and-build.md`, "Volume identity in path arguments (IO-05)". `PATH` values, `LOGFILE` and the `COMPILE` output: **fixed as PATH-01 (2026-10-15, 10 tests)**, see "PATH values keep their volume (PATH-01)". `SWITCH`/`RENAME` attach check: dropped by the user. Also found: `MOVEPATH` onto itself deleted the file (upstream); **fixed as MOVE-01 (2026-10-15, 10 tests; Proton case check pending)**, see "Move onto itself (MOVE-01)".
- `IO-04` — the reviewed prefix escape does not apply as deployed (`SafeHouse.ArchiveFolder` keeps its trailing separator; archive `VolumePath` segments cannot leave the root). **Hardening done (2026-10-15, headless-tested; in-game check deferred):** `DirectoryContainment` (kOS.Safe) checks by directory boundary for `Widget.GetTexture` (each candidate file, so the archive folder itself cannot become `Script.png`) and `Archive`; user-approved symlink policy (follow links to read/write/list, a delete never enters one: `LinkSafeDelete`) and image limits (16 MiB file, 4096 pixels a side, script error). 27 tests; see `docs/testing-and-build.md`, "Archive containment, symlinks and image limits (IO-04 hardening)". Limits: a save onto a file link replaces the link; Wine's reporting of Unix symlinks unverified.
- `INPUT-01` mouse half (click-through #2659/#2759, modal mouse blocking) — **step 1 done (2026-10-15, policy headless-tested, game side compiled only; in-game checks deferred)**: kOS's own `kOSMouseOver` hover lock over every kOS window (see "Mouse half" above). Step 2 (optional ClickThroughBlocker integration) waits for an in-game check with the user's CTB; #2659 (key order with CTB) still open.

**Reclassified, not Phase 1:** `EDIT-03` (editor follows the caret on every repaint — confirmed, `KOSTextEditPopup.cs:465`) and `TERM-05` (history cannot return to the draft — confirmed, `Interpreter.cs:112`) are P1 in the review but workflow behavior; `TERM-05` is Phase 3 item 2, `EDIT-03` fits there too. `TERM-01/02/04/06/07` are Phase 3/4 terminal and UI work.

**Test items:** `TEST-01` is resolved by policy (local gate `.ci/test.sh` with a result checker and minimum count; no hosted CI). `TEST-02` is largely covered (real `ScreenBuffer` overlay/cursor/dimension suites, the real CPU and prompt in `ScriptHost`, long-input and paste tests); history-model tests come with `TERM-05`.

**Exit criterion status: not met.** All Phase 1 code items are done; remaining is the in-game verification of the fixes listed as deferred above (now including `IO-05`, `IO-04` and the `INPUT-01` mouse half, whose CTB check decides step 2 and #2659).

### Exit criterion

High-risk failures are reproduced and covered by tests, fixes have appropriate in-game verification, and unrelated behavior remains unchanged.

## Phase 2 — Structural boundaries

Extract responsibilities one at a time while retaining the current renderer. Strengthen the existing `kOS.Safe` boundary rather than replacing the interpreter or creating a duplicate runtime.

### Target responsibilities

```text
Terminal session / existing interpreter
  - character-grid content and cursor
  - ordered input, history, active input consumer
  - operational state and coherent display snapshots
  - explicit lifecycle

Adapters
  - in-game view and input ownership
  - telnet transport/protocol and screen adaptation
  - existing script-facing and mod-facing APIs

Editor document model
  - active document and pending open request
  - dirty state and safe document transactions
```

### Decisions to document before major changes

- **Screen versus viewport:** reading old output must not redirect new writes or break `PRINT AT`, `CLEARSCREEN`, cursor access, or resize watchers.
- **Resize authority:** define what happens when GUI and multiple telnet viewers request different sizes.
- **Input ownership:** distinguish prompt input, script `GETCHAR`, communications-delayed commands, selection, paste, and interrupts.
- **Thread ownership:** transport workers process bytes; game/session mutations and Unity cleanup run on the appropriate thread.
- **Resource ownership:** views, sessions, workers, subscriptions, callbacks, fonts, and textures have explicit owners and idempotent cleanup.
- **API compatibility:** retain public signatures and compatibility shims used by other mods, including terminal input injection.

Closing a view must not implicitly stop a processor. Stopping a script, powering off a processor, and releasing flight controls remain distinct operations.

### Exit criterion

Core terminal behavior can be tested without Unity, GUI and telnet share tested behavior through adapters, and the existing renderer remains compatible.

## Phase 3 — Workflow quality of life

Suggested priority order:

1. **Safe paste and useful copy:** ordered input, multiline execution confirmation, bounded work per update, copy output/error details, and an unambiguous interrupt path.
2. **Command history:** preserve and restore the draft during history navigation. Reverse search and optional bounded persistence can follow; define retention/privacy behavior.
3. **Debugging:** bounded recent diagnostics with source locations and script stacks; copy actions; explicit running/waiting/prompt, power, and communications state.
4. **Output navigation:** wheel scrolling, selection, search, follow-output behavior, and a new-output indicator without losing character-addressable dashboards.
5. **External-editor conveniences:** show the resolved archive location; a lightweight file/volume picker; explicit run/rerun actions; external-change detection before embedded-editor overwrites.

Do not automatically execute scripts on file changes by default. Editing a flight script must not unexpectedly run it. File UI actions should use existing filesystem and execution semantics rather than implement parallel logic.

Queue limits and update budgets must preserve input ordering, responsiveness, and reliable interrupts; do not silently drop executable input.

### Exit criterion

External editing plus in-game execution/debugging is demonstrably easier, with GUI/telnet compatibility and input-safety coverage for each feature.

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
- Validate game-dependent changes under Proton in `/home/jvz/Games/KSP-kOSIM-dev/`; record game/Proton versions and use disposable test saves. Do not modify the vanilla installation.
- Keep changes focused and compatibility decisions explicit.
- Measure performance before claiming an optimization or leak fix.
- Preserve loopback/opt-in telnet exposure defaults.
- Keep the in-game runtime compatible with KSP; modern tooling does not imply a runtime upgrade.

## Release checkpoints and deferred scope

Recommended checkpoints:

1. **Reliability release:** verified local regression tests and targeted safety fixes, current appearance retained; game-dependent behavior verified under Proton.
2. **Structural release:** tested session/adapters and explicit input/lifecycle contracts, current renderer retained.
3. **QoL and presentation release(s):** staged productivity improvements followed by modernized UI.

These are checkpoints, not mandatory dates or a requirement to implement every report item before releasing.

Keep compiler/VM optimizations, steering changes, language changes, a full embedded IDE, and a new structured remote-control interface outside this roadmap unless separately justified. They require their own designs and regression strategies.
