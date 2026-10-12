# KOS Improved — session handoff

## Resume here

**NET-03 and EXEC-01 are fixed and committed** (the user asked for both, with regression tests). Phase 0's headless work was already complete; the suite is fully green (**735/735**) and the known-failure list is empty.

- **NET-03** (fixture-found; not the review's queue-budget NET-03, which stays open): telnet terminal types shorter than 5 characters (`ANSI`, `VT52`, `VTNT`, `dumb`, empty) made `TerminalUnicodeMapper.GuessTypeId()` throw on the reader thread and end the session. Recognition moved to `src/kOS.Safe/UserIO/TerminalTypeNames.cs`; the game mapper delegates to it, so short types get the plain mapper.
- **EXEC-01**: a `COMPILE` that failed at the prompt re-reported its error every update and blocked the prompt until reboot. `CPU.IsYielding()` now drops a yield whose check throws; `YiedFinishedThreadedDetector` reports once and keeps the original stack trace (`ExceptionDispatchInfo`).

Details, tests and mutations are in `docs/testing-and-build.md` (sections "Short telnet terminal types" and "Failed COMPILE at the prompt").

The user **deferred further Proton checks**. Continue local implementation/tests; do not deploy, terminate/restart the game, or request immediate game verification. No UI redesign, hosted testing/release automation, or push. Commit only when the user asks.

Next tasks, in suggested order (confirm with the user):

1. Optional telnet follow-up: terminal mappers (`src/kOS/UserIO/Terminal*Mapper.cs`) still convert each read separately, so `\r\n` or `ESC [` split across reads is mishandled (pre-existing). This needs the mappers moved to `kOS.Safe` or given a stateful wrapper, plus tests. Moving them would also let the recorded telnet streams run through the mappers. Keep the public `kOS.UserIO` types (mods may reference them).
2. `INPUT-01` (focus/flight-control lock cleanup) is mostly Unity behavior. Design and test what's possible headlessly; real verification waits for Proton.
3. Review NET-03 (queue budgets/backpressure for large pastes and stalled clients) from `docs/improvements-overview.md`; NET-01/02 did not bound the queues.
4. When convenient, replace the reconstructed PuTTY and Windows telnet recordings with live captures (`src/kOS.Safe.Test/UserIO/Recordings/`).

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
| NET-01/02 | `kOS.Safe/UserIO`: `TelnetInputParser` (incremental RFC 854, persistent UTF-8 decoder, 1024-byte subnegotiation cap), `TelnetInputDispatcher` (text coalescing, break discard), `TelnetProtocol` (negotiation replies, NAWS clamping), `TelnetMenuText`, and `TelnetConnectionWorker` (background reader/writer, serialized writes, 5 s write timeout, cooperative idempotent shutdown with 1 s bounded join, no `Thread.Abort`). `TelnetSingletonServer` keeps its public API, and game-side stop/menu work runs in `MainThreadUpdate()`, called from `TelnetMainServer.Update()`. 83 tests (34 parser, 23 protocol, 5 dispatcher, 9 menu, 12 loopback-socket worker). | Intentional changes are listed in `docs/testing-and-build.md` (NET-01/02 section). Server, menu and mapper adapters are compiled but not tested. Mappers still work per read. A game-thread raw send to a stalled client can block for up to 5 s. Socket semantics have been checked only on Linux Mono; Unity/Proton telnet acceptance is deferred. |
| FMT-01 | `SafeSerializationMgr.DumpValue()` returns nested primitives inline for type-less (display) dumps only. This fixes `PRINT`/`:DUMP` of lists and lexicons, which upstream has shown as `["value"] = …` since v1.3.0.0 (cause: commit `21dcea00`; seen in issue #2939). `PrimitiveDisplayTest` (7 tests). The 3 baseline failures are removed. | JSON/message/save formats are unchanged and pinned by a test. The in-game print has not been checked under Proton. |
| Compatibility fixtures | `ScriptHost` (real `TextEditor` prompt, CPU, compiler and screen model; disposable archive) with test adapters `FixtureInterpreter` (copy of the game `Interpreter`, keep in step), `FixtureBindingManager` (`TERMINAL`), `FixtureFunctionClearScreen`, `RecordingLogger`. `ScriptFixtureTest` (19: printing, functions, triggers, fileio from source and KSM in both newline modes; CRLF boot; KSM category), `DashboardFixtureTest` (1 test, 15 snapshots), `TelnetRecordingTest` (13). Fixtures in `kerboscript_tests/fixtures/` (expected in `expected/`), recordings in `src/kOS.Safe.Test/UserIO/Recordings/`. Re-record with `KOSIM_RECORD_FIXTURES=1`. | Model of the terminal, not in-game rendering. Game key routing, mappers and telnet server are untested. PuTTY/Windows telnet streams are reconstructed, not captured. `boot_crlf.ks` is `-text` in `.gitattributes`. |
| NET-03 / EXEC-01 | `TerminalTypeNames` (kOS.Safe) used by `TerminalUnicodeMapper.GuessTypeId()`; `TerminalTypeNamesTest` (23). `CPU.IsYielding()` drops a yield whose check throws; `YiedFinishedThreadedDetector` reports once with the original trace; `CompileErrorTest` (4, real prompt). | Results unchanged for every type string upstream handled; bare `vt10` now VT100; ordinal matching. ANSI mapping still unimplemented. Ctrl-C at the prompt still cannot cancel a running `COMPILE` (code reading). Neither fix checked with a real client or in game. |

## Latest verification and commands

Host tools: SDK 8.0.425 in `$HOME/.cache/kosim-tools/dotnet`, Mono 6.12.0, Python 3.14.7. Ignored `kOS.props.user` points to the dev instance's managed assemblies and disables CKAN dependency installation.

```bash
export PATH="$HOME/.cache/kosim-tools/dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
bash .ci/test.sh
# Full production build against dev game assemblies (not KOSHeadless):
dotnet build kOS.sln -c Release
```

Latest session results (NET-03/EXEC-01, 2026-10-12):

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

See "Resume here" for the ordering: the optional mapper follow-up, then `INPUT-01` and other small confirmed safety defects. Retain pending GUI/telnet equivalence, fixed communications overlay, multiple-viewer, focus/power/scene and native-resource checks; no systematic performance profile has run.

Before major structural extraction, document screen/viewport coordinates, resize authority across viewers, input/thread ownership and explicit lifecycle/resource contracts. No wholesale rewrite or UI modernization yet.
