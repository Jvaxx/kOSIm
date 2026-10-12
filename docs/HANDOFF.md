# KOS Improved — session handoff

## Resume here

**Committed: review NET-03 (telnet key decoding across reads + bounded telnet queues/budgets) and the fixture-found PROMPT-01 fix**, as three commits: `Move telnet key decoding into a stateful TerminalInputDecoder`, `Keep a wrapped input line from overwriting output (PROMPT-01)`, `Bound telnet queues and per-update input work (review NET-03)`. The first two were checked on their own in temporary worktrees (757/757 and 762/762 headless; commit 1 full build 0 errors). Doc updates for all three are in the third. All five "Remaining" items from the previous handoff are done (tests, recordings through the pipeline, end-to-end prompt tests, cost measurement, mutations/gate/docs). The suite is green (832/832) and the full Release build passes.

**Next action:** ask the user which follow-up to start. Suggested (not yet chosen): (1) the prompt-level Ctrl-C fix (decision 4 below), small and headless-testable; (2) then a Proton checkpoint before `INPUT-01`, because much game-side code is compiled but unrun (this telnet wiring, NET-01/02, editor popup, render recovery, archive saves, raw I/O) and `INPUT-01` is mostly Unity; (3) `INPUT-01`. The user had deferred Proton checks; do not start them without approval.

### Decisions (user, 2026-10-12)

1. **Limits stay code constants**, not `CONFIG` settings (a deliberate deviation from the review's "configurable limits"): input/output backlog 262,144 each; 4,096 telnet keys and 4 ms per client per update; type-ahead limit 4,096; 2,048 prompt keys per update; 100 ms sequence timeout.
2. **Unrecognized key sequences stay typed as text** (F-keys, Ctrl+arrows, other `ESC [` sequences), as upstream.
3. **No SS3 decoding** (`ESC O A` etc.); kOS never enables application-cursor mode.
4. **Prompt-level Ctrl-C: fix later, in its own task.** Ctrl-C does not end a `WAIT` or `COMPILE` typed directly at the prompt (telnet or GUI): `CPU.BreakExecution(true)` aborts yields only when a program context exists. Confirmed headlessly; inside a program Ctrl-C works in one update. Upstream behavior. The fix must decide what a still-running `COMPILE` thread may do (it keeps running and may still write its output).

### What the three commits contain

Production (`src/kOS.Safe`):

- `UserIO/TerminalInputDecoder.cs` + `TerminalInputDialect`, `TerminalTypeNames.InputDialectFor()`: stateful key decoding (split keys joined; each CR/LF one Enter, its partner absorbed).
- `UserIO/TelnetInputQueue.cs`, `TelnetOutputQueue.cs`, `TelnetInputPipeline.cs`, `TerminalInputPump.cs`: bounded queues, control priority, overflow with hysteresis and notices, per-update budgets, round robin. Details, limits and intentional changes: `docs/testing-and-build.md`, section "Telnet key decoding and bounded telnet queues (review NET-03)".
- Fixed this session: `TelnetInputQueue.TakeOverflowEnded` lost the first episode's drop count when two episodes ended before the game thread reported (now accumulated).
- `Screen/TextEditor.cs` (PROMPT-01): `KeepCursorInBounds` adds rows before moving/scrolling. Upstream left the input line one row too high: a command wrapped at the bottom overwrote the last output row, and a line taller than the screen threw on Enter. Section "Long input line at the prompt (PROMPT-01, fixture-found)".

Game-side wiring (`src/kOS`, compiled only, not runtime-tested): `TelnetSingletonServer` (queues/pipeline, `TryReadChar`, backlog/overflow properties, client + log notices, `MainThreadUpdate` flush), `TermWindow` (`DrainTelnets`, `FeedPrompt`, title resend and mode restatement on repaint), `TelnetWelcomeMenu` (per-update budget, repaint/resize handling, no echo of kOS key codes), mappers (`InputConvert` via a fresh decoder per call; overrides kept for binary compatibility).

Tests (`src/kOS.Safe.Test`), 97 new since the last commit (22 decoder + 70 + 5):

- `UserIO/TelnetInputQueueTest` (17, incl. a threaded stress test), `TelnetOutputQueueTest` (8, incl. concurrent writers), `TelnetInputPipelineTest` (16), `TerminalInputPumpTest` (14).
- `UserIO/TelnetRecordingTest` (+7): recordings through `TelnetServerInputModel` (copy of the server's input side; **keep in step with `TelnetSingletonServer`'s `ITelnetInputSink` methods**) to recorded `Recordings/*.keys`; all chunkings give the same keys.
- `Execution/FixtureTermWindow.cs`: copy of `TermWindow`'s input routing (**keep in step**). `Execution/TelnetPromptTest` (6): split multi-line paste, upstream Enter count, interrupt during a backlog (program in `WAIT 1000`), resize before backlog, long line within limits. `Execution/TelnetPasteCostTest` (2): frame-cost proxy. `Execution/LongInputLineTest` (5): PROMPT-01.
- `ScriptHost`: `BeforeUpdate` hook; `SilentSoundMaker` (a manual break inside a program called `SoundMaker.StopAllVoices()` on null).
- `.ci/known-test-failures.json`: `minimum_tests` 735 → 832; `failures` still empty.

Docs updated: `docs/testing-and-build.md` (two new sections, review NET-03 acceptance cross-reference table, Proton checklist additions, counts), `docs/KOSIm-roadmap.md` (Phase 1 progress).

### Known limits

- Typed text beyond the input backlog is discarded with a notice (not back-pressured), so interrupts are never stuck; output overflow relies on a full repaint (title resent, modes restated, menu reprints).
- Several clients share one type-ahead; simultaneous pastes from two clients can interleave within a line (upstream: at read boundaries).
- Many-line pastes: worst update is dominated by compiling/running one line, which the per-key budget cannot pre-empt. Worst update grows with scrollback (not investigated).
- A control key read while text is allowed uses one unit of budget (negligible).

### Constraints for the next steps

The user **deferred further Proton checks**. Do not deploy, terminate/restart the game, or request immediate game verification. No UI redesign, hosted testing/release automation, or push.

Later tasks (confirm with the user): `INPUT-01` (focus/flight-control lock cleanup, mostly Unity; design + what's testable headlessly); replace reconstructed PuTTY/Windows telnet recordings with live captures; the prompt-level Ctrl-C fix (decision 4 above).

Fixture-found NET-03 (short terminal types) and EXEC-01 (repeating COMPILE error) are fixed and committed; see `docs/testing-and-build.md`.

Decided: the user chose to **leave the JSON format unchanged**. JSON from kOS 1.3+ wraps nested primitives as `{"value", "$type"}`; keep that for file compatibility.

## Workspace and constraints

- Workspace: `/home/jvz/Projects/KOS-Improved`; branch `kosim/test-baseline`.
- Base: `8f281a459b6ea0ba917c91bbbc117e68bb948199` (`fix element counting parts multiple times`), from upstream `develop`; origin remains <https://github.com/KSP-KOS/KOS>.
- Local commits on `kosim/test-baseline` (not pushed; no fork): `Add local headless test tooling and baseline`, `Fix screen buffer overlay, dimension and cursor safety`, `Prevent editor data loss on open, focus and failed I/O`, `Make archive saves atomic and add raw byte file I/O`, `Add fork roadmap, handoff and agent instructions` (checkpoint of all earlier work), then the NET-01/02 commit (`a81609e1`), the FMT-01 commit (`1dc22449`), the compatibility-fixtures commit (`38effa23`) and the NET-03/EXEC-01 commit. Use `git log`/`git status` rather than a stale inventory.
- Primary workflow: external editor + kerboscript LSP; terminal/telnet for execution/debugging. Order: tests → safety → incremental structure → QoL → UI.
- Preserve character-grid semantics, KSM/save/craft compatibility, public mod APIs, safe network defaults and predictable flight-input ownership. Runtime stays `net48`; SDK 8 is tooling only. No incidental compiler/VM/steering changes or external-file autorun.
- Local tests only. Upstream workflows are inactive under `.github/upstream-workflows/`; do not reactivate them.
- Keep `docs/improvements-overview.md` unchanged. It reviewed `d4388439c712994e47258ed7514e863f79da632b`, not this baseline; current roadmap/agent instructions supersede its hosted-CI/cross-platform recommendations.

## Implemented state and important limits

Detailed evidence and deferred runtime checklists live in `docs/testing-and-build.md`; raw I/O policy lives in `docs/file-io-compatibility.md`.

| Area | Current implementation / entry points | Limits to retain |
| --- | --- | --- |
| Local baseline | `.ci/test.sh`, strict known-failure gate, `KOSHeadless=true` library/test builds; NUnit 2.7.1/NSubstitute 1.8.1 retained. 13 initial real-model compatibility tests. Failure propagation and fresh-checkout builds previously verified. | The baseline is now empty (the 3 upstream failures were fixed by FMT-01); no tests skipped/filtered. Do not expand the allowlist to hide regressions; raise minimum count as coverage grows. |
| BUFFER-01 | `ScreenBuffer.GetBuffer()` clips overlays by row-intersection replacement; 26 regressions. | Fixed = viewport-relative, non-fixed = absolute minus TopRow; later overlays win; shared row references retained. No horizontal semantics or snapshot concurrency guarantee. Only this earlier build has guided Proton smoke evidence. |
| BUFFER-04 | `ScreenBuffer.SetSize()` normalizes before allocation/callbacks; `SubBuffer` validates before mutation; 27 regressions. | Zero model axis preserves current size; others clamp to 1–160 rows / 1–255 columns. Script minima remain 3/15. Effective same-size requests are true no-ops. Negative long-input row anchors remain valid; overlay height is uncapped. |
| BUFFER-02/03 | `ScreenSnapShotExtensions.TryGetCursorCharacter()` (23 tests), snapshot-only GUI cursor reads and scoped group/color/skin cleanup in `TermWindow`; `RenderFaultRecovery` (34 tests). | Invalid cursor hidden, not moved; snapshot interface unchanged. Font/geometry preparation and window callback recover nonfatal faults with Retry/Close/Resize, 2-second retries and 10-second diagnostic batching. Fatal/Unity GUI-exit branches propagate. Update/snapshot acquisition, input, title/lifecycle, telnet, GUI.Window internals and fallback/reporter faults are outside recovery. Unity cleanup/control behavior only simulated headlessly. |
| EDIT-01/02 | `EditorDocument` (48 tests), wired through `KOSTextEditPopup`/`DelegateDialog`. Actual-text dirty detection; separate active destination and pending open; load commits only after successful read; modal state survives focus/reopen. | Save targets active document. Failures preserve draft/request; successful save before failed load advances baseline. Incidental Close preserves draft; explicit discard-exit restores baseline. Text API semantics retained; no conflict detection/input-lock redesign. Popup/dialog adapter is compiled, not Unity-tested. |
| IO-03 | `ArchiveFileWriter` and Archive save integration (50 tests): exclusive same-directory `.kosim-save-<GUID>.tmp`, write/flush/close, then Replace existing / non-overwriting Move new; internal instance-local fault seam. | No delete/truncate fallback. Clean only owned temps; cleanup failure retains both errors/path. Interrupted saves can leave visible temps; no sweep. Native commit safety depends on runtime/filesystem; no power-loss durability, external-change detection or rollback after post-commit Open failure. Append/Clear/static archive root/link policy unchanged. |
| IO-01/02 | User-approved **Preserve text APIs** policy: additive `ReadAllRaw`/`WriteRaw`/`SaveFileRaw`, script `READRAW`/`WRITERAW(FileContent)`; copy/move/helper/boot seeding route raw. 62 tests (`RawFileIoTest`, `LegacyNewlineTest`). | Built-in raw transfers preserve bytes; raw append is nontransactional. Legacy READALL/WRITE, UTF-8 replacement/BOM/type behavior retained; Windows CRLF conversion is idempotent. Legacy archive READALL still risks binary corruption: use raw APIs. Old/derived providers retain legacy hooks until opting into raw overrides; no universal byte-exact or precompiled-mod guarantee. Exact byte capacity can reject moves previously accepted via shortened text views. |
| NET-01/02 | `kOS.Safe/UserIO`: `TelnetInputParser` (incremental RFC 854, persistent UTF-8 decoder, 1024-byte subnegotiation cap), `TelnetInputDispatcher` (text coalescing, break discard), `TelnetProtocol` (negotiation replies, NAWS clamping), `TelnetMenuText`, and `TelnetConnectionWorker` (background reader/writer, serialized writes, 5 s write timeout, cooperative idempotent shutdown with 1 s bounded join, no `Thread.Abort`). `TelnetSingletonServer` keeps its public API, and game-side stop/menu work runs in `MainThreadUpdate()`, called from `TelnetMainServer.Update()`. 83 tests (34 parser, 23 protocol, 5 dispatcher, 9 menu, 12 loopback-socket worker). | Intentional changes are listed in `docs/testing-and-build.md` (NET-01/02 section). Server, menu and mapper adapters are compiled but not tested. (Per-read mapping replaced by review NET-03.) A game-thread raw send to a stalled client can block for up to 5 s. Socket semantics have been checked only on Linux Mono; Unity/Proton telnet acceptance is deferred. |
| FMT-01 | `SafeSerializationMgr.DumpValue()` returns nested primitives inline for type-less (display) dumps only. This fixes `PRINT`/`:DUMP` of lists and lexicons, which upstream has shown as `["value"] = …` since v1.3.0.0 (cause: commit `21dcea00`; seen in issue #2939). `PrimitiveDisplayTest` (7 tests). The 3 baseline failures are removed. | JSON/message/save formats are unchanged and pinned by a test. The in-game print has not been checked under Proton. |
| Compatibility fixtures | `ScriptHost` (real `TextEditor` prompt, CPU, compiler and screen model; disposable archive) with test adapters `FixtureInterpreter` (copy of the game `Interpreter`, keep in step), `FixtureBindingManager` (`TERMINAL`), `FixtureFunctionClearScreen`, `RecordingLogger`. `ScriptFixtureTest` (19: printing, functions, triggers, fileio from source and KSM in both newline modes; CRLF boot; KSM category), `DashboardFixtureTest` (1 test, 15 snapshots), `TelnetRecordingTest` (13). Fixtures in `kerboscript_tests/fixtures/` (expected in `expected/`), recordings in `src/kOS.Safe.Test/UserIO/Recordings/`. Re-record with `KOSIM_RECORD_FIXTURES=1`. | Model of the terminal, not in-game rendering. Game key routing, mappers and telnet server are untested. PuTTY/Windows telnet streams are reconstructed, not captured. `boot_crlf.ks` is `-text` in `.gitattributes`. |
| NET-03 / EXEC-01 | `TerminalTypeNames` (kOS.Safe) used by `TerminalUnicodeMapper.GuessTypeId()`; `TerminalTypeNamesTest` (23). `CPU.IsYielding()` drops a yield whose check throws; `YiedFinishedThreadedDetector` reports once with the original trace; `CompileErrorTest` (4, real prompt). | Results unchanged for every type string upstream handled; bare `vt10` now VT100; ordinal matching. ANSI mapping still unimplemented. Ctrl-C at the prompt still cannot cancel a running `COMPILE` (code reading). Neither fix checked with a real client or in game. |
| Review NET-03 | `TerminalInputDecoder`, `TelnetInputQueue`, `TelnetOutputQueue`, `TelnetInputPipeline`, `TerminalInputPump` (kOS.Safe/UserIO), wired into `TelnetSingletonServer`, `TermWindow`, `TelnetWelcomeMenu`, mappers. 92 tests incl. recordings→keys, real-prompt paste/interrupt, cost proxy; 13 mutations. | Limits are constants (user decision). Text overflow discards with notice; output overflow → full repaint. Shared type-ahead across clients. Game adapters compiled only; no real client, frame time or stalled-client check. |
| PROMPT-01 | `TextEditor.KeepCursorInBounds` adds rows before scrolling; `LongInputLineTest` (5, real prompt). | Upstream defect (GUI and telnet). Not checked in game. |

## Latest verification and commands

Host tools: SDK 8.0.425 in `$HOME/.cache/kosim-tools/dotnet`, Mono 6.12.0, Python 3.14.7. Ignored `kOS.props.user` points to the dev instance's managed assemblies and disables CKAN dependency installation.

```bash
export PATH="$HOME/.cache/kosim-tools/dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
bash .ci/test.sh
# Full production build against dev game assemblies (not KOSHeadless):
dotnet build kOS.sln -c Release
```

Latest session (review NET-03 completion + PROMPT-01, committed, 2026-10-12):

- Start: `bash .ci/test.sh` exit 0, 757/757.
- Final: `bash .ci/test.sh` exit 0, **832 NUnit executed, 832 passed, 0 known failures, 0 skipped; 12 Python gate tests passed** (after the full build). Gate minimum raised to 832; `failures` empty.
- Full build: `dotnet build kOS.sln -c Release --no-incremental` exit 0, **0 errors, 44 existing warnings** (none new); all three assemblies rewritten. Not deployed; no game launched.
- Stability: the 214 UserIO/telnet-prompt/long-input tests passed 10 consecutive runs and 5 more with all 24 cores busy.
- Mutations (13, each reverted, files checked by SHA-1): all failed tests; table in `docs/testing-and-build.md`.
- Measurement (headless proxy, Mono 6.12, Ryzen 9 5900X): worst update for a 6,018-character pasted line 368–404 ms without limits vs 5.1–5.3 ms with them (same total); 3,000-line paste worst update 9.3–10.6 vs 7.2–7.8 ms. Reproduce the 3,000-line figures with `KOSIM_PASTE_LINES=3000 bash .ci/test.sh` (MEASURE lines in the output).
- Defect evidence: overflow-count bug shown by `EpisodesNotYetReportedAreReportedTogether` (3 reported instead of 5); PROMPT-01 shown by temporary probes (2,000+ characters typed directly crashed on Enter; `line 39` overwritten), removed after the regression tests were added; the prompt-level Ctrl-C limit shown by a temporary probe (`wait 1000.` still running 200 updates after BREAK; the same wait in a `RUNPATH` program aborted in 1 update), removed.

Earlier work-tree check (before this session's tests): 757/757; full build 0 errors, 44 warnings.

Previous session results (NET-03/EXEC-01, committed, 2026-10-12):

- Baseline at session start: 708/708 (previous session).
- Final: `bash .ci/test.sh` exit 0, **735 NUnit executed, 735 passed, 0 known failures, 0 skipped**; 5 further consecutive runs all 735/735. Gate minimum raised to 735; `failures` still empty.
- Mutations, each reverted: both EXEC-01 production changes reverted → the 3 `COMPILE` tests timed out (about 20,000 repeated errors each), `RUNPATH` test passed; detector using `throw error` → only the stack-trace test failed; `Substring` checks restored in `TerminalTypeNames` → 11 tests errored.
- Clean full build: `dotnet build kOS.sln -c Release --no-incremental` exit 0, **0 errors, 44 existing warnings**. Not deployed; no game launched.

Previous session (compatibility fixtures, 2026-10-12):

- Baseline at session start: `bash .ci/test.sh` exit 0, 675/675.
- Final: `bash .ci/test.sh` exit 0, **708 NUnit executed, 708 passed, 0 known failures, 0 skipped; 12 Python gate tests passed**. Gate minimum raised to 708; `failures` still empty.
- Mutations, each reverted: disabling the FMT-01 display fix failed 16 fixture tests; making `ScreenBuffer` wrapping ignore the cursor column failed the dashboard snapshot test.
- Stability: the 33 fixture tests passed 15 consecutive runs and 8 under full CPU load. No temporary fixture archives were left in `/tmp`.
- Clean full build: `dotnet build kOS.sln -c Release --no-incremental` exit 0, **0 errors, 44 existing warnings** (none from new files). Not deployed; no game launched.
- Defect evidence:
  - NET-03: the unchanged mapper files compiled into a scratch Mono probe threw for `ANSI`/`VT52`/`VTNT`/`dumb`/empty.
  - EXEC-01: a temporary probe test logged 198 errors in 200 updates and kept reporting after Ctrl-C.
  - Both probes were removed.

Previous implementation session (FMT-01):

- Investigation: a temporary probe test (removed) showed the garbled display and the wrapped JSON at HEAD, and that pre-1.3 raw-primitive JSON still loads. Upstream history traced the behavior to `21dcea00` (#2824, a bare-scalar `WRITEJSON` fix). Issue #2939 shows the garbled output in a real game.
- Final: `bash .ci/test.sh` exit 0, **675 NUnit executed, 675 passed, 0 known failures, 0 skipped; 12 Python gate tests passed**. Gate minimum 675; baseline `failures` is empty.
- Mutation: with the fix disabled, 675 executed with 6 failures (the 3 original plus the 3 new display tests), gate exit 1. The JSON/compatibility tests passed both ways. Fix restored.
- Clean full build: `dotnet build kOS.sln -c Release --no-incremental` exit 0, **0 errors, 44 existing warnings**. Not deployed.
- NET-01/02 session (committed `a81609e1`): 668 executed/665 passed/3 known at the time. Telnet fixtures were stable over 25 repeated runs and 8 under full CPU load. Its details are in `docs/testing-and-build.md`.

Passing headless tests are not Proton validation.

## Runtime setup and deferred acceptance

- Only runtime target now: Windows Steam **KSP 1.12.5.3190 (WindowsPlayer x64)** under **GE-Proton11-7-x86_64**, via installed UMU 1.4.4. Other game platforms are outside validation scope; avoid gratuitous breakage.
- Approved independent dev copy: `/home/jvz/Games/KSP-kOSIM-dev/`. Use its `launch-dev.sh` (`--check` for preflight), isolated `.proton-prefix` (effective Wine prefix `.proton-prefix/pfx/`), disposable `saves/kOSIM-dev` and `dev-logs`/`KSP.log`. Runtime updates default off; do not silently change Proton or reuse vanilla compatdata.
- Protect `/home/jvz/.local/share/Steam/steamapps/common/Kerbal Space Program/`, Steam settings and original prefix. No deployment there or personal-save imports; proprietary assemblies/dev tree stay outside git.
- Dev GameData has stock assets plus **the earlier overlay-only kOSIM build**. ModuleManager is absent; patches inactive. All subsequent builds are repository-only. Recheck live processes before any future deployment; previous game-running state/PIDs are not reliable across sessions.
- First isolated launch succeeded and loaded kOS. User-guided long-input/resize smoke confirmed responsiveness, cursor/text alignment and command output; inspected log had no matching range/GUI-clip exceptions. Existing `kOS: ipu: -1` error logging and a DontDestroyOnLoad warning were observed, not investigated. This is limited overlay smoke evidence, not comprehensive runtime validation.
- **All newer Unity/Proton acceptance remains deferred.** When the user resumes, safely close/verify exit before complete-assets deployment and restart with the unchanged isolated launcher. Use disposable files/save only and follow the checklists in `docs/testing-and-build.md`: dimensions/cursor/render-fault cleanup and retry controls; editor modal/save-discard-cancel/failure behavior; real archive Replace/Move/locks/permissions/metadata; raw suffixes/transfers, legacy text, new-disk boot and actual source/KSM execution/provider hooks. Also the NET-01/02 telnet checklist: real clients, non-ASCII, resize extremes, interrupt, detach/close/kill, disabling telnet, scene change/quit, stalled client; a client reporting `ANSI` stays connected (NET-03); a failing `COMPILE` at the prompt reports once and frees the prompt (EXEC-01). Headless managed Windows-flag tests and compile/pack/unpack do not verify those paths. No power-loss/full-disk experiment without explicit planning.

## Follow-on priorities

See "Resume here" for the ordering: the review NET-03 + PROMPT-01 work is committed; next the prompt-level Ctrl-C fix and a Proton checkpoint (user to choose), then `INPUT-01` and other small confirmed safety defects. Retain pending GUI/telnet equivalence, fixed communications overlay, multiple-viewer, focus/power/scene and native-resource checks; no systematic performance profile has run.

Before major structural extraction, document screen/viewport coordinates, resize authority across viewers, input/thread ownership and explicit lifecycle/resource contracts. No wholesale rewrite or UI modernization yet.
