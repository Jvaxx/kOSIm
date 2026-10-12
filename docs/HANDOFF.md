# KOS Improved — session handoff

## Resume here

**NET-01/02 (telnet parsing, decoding, EOF and shutdown) is implemented, headless-tested and committed**, alongside BUFFER-01–04, EDIT-01/02 and IO-01–03. No unfinished edit is pending. Phase 0 remains partially complete: broader compatibility fixtures are missing.

The user **deferred further Proton checks**. Continue local implementation/tests; do not deploy, terminate/restart the game, or request immediate game verification. No UI redesign, hosted testing/release automation, or push. Commit only when the user asks (they did for the checkpoint and NET-01/02 commits).

Suggested next tasks (all headless; confirm the choice with the user):

1. Investigate the 3 known lexicon-formatting failures (`SafeSerializationMgr.DumpValue()`, `IDumper` before primitive handling). Fix or document; remove baseline entries only once they're fixed.
2. Phase 0 compatibility fixtures: boot/flight scripts, character-grid dashboards, compiled KSM round-trips, and recorded telnet byte streams replayed through `TelnetInputParser`/`TelnetInputDispatcher`.
3. Optional telnet follow-up: terminal mappers (`src/kOS/UserIO/Terminal*Mapper.cs`) still convert each read separately, so `\r\n` or `ESC [` split across reads is mishandled (pre-existing). This needs the mappers moved to `kOS.Safe` or given a stateful wrapper, plus tests.
4. `INPUT-01` (focus/flight-control lock cleanup) is mostly Unity behavior. Design and test what's possible headlessly; real verification waits for Proton.

## Workspace and constraints

- Workspace: `/home/jvz/Projects/KOS-Improved`; branch `kosim/test-baseline`.
- Base: `8f281a459b6ea0ba917c91bbbc117e68bb948199` (`fix element counting parts multiple times`), from upstream `develop`; origin remains <https://github.com/KSP-KOS/KOS>.
- Local commits on `kosim/test-baseline` (not pushed; no fork): `Add local headless test tooling and baseline`, `Fix screen buffer overlay, dimension and cursor safety`, `Prevent editor data loss on open, focus and failed I/O`, `Make archive saves atomic and add raw byte file I/O`, `Add fork roadmap, handoff and agent instructions` (checkpoint of all earlier work), then the NET-01/02 commit. Use `git log`/`git status` rather than a stale inventory.
- Primary workflow: external editor + kerboscript LSP; terminal/telnet for execution/debugging. Order: tests → safety → incremental structure → QoL → UI.
- Preserve character-grid semantics, KSM/save/craft compatibility, public mod APIs, safe network defaults and predictable flight-input ownership. Runtime stays `net48`; SDK 8 is tooling only. No incidental compiler/VM/steering changes or external-file autorun.
- Local tests only. Upstream workflows are inactive under `.github/upstream-workflows/`; do not reactivate them.
- Keep `docs/improvements-overview.md` unchanged. It reviewed `d4388439c712994e47258ed7514e863f79da632b`, not this baseline; current roadmap/agent instructions supersede its hosted-CI/cross-platform recommendations.

## Implemented state and important limits

Detailed evidence and deferred runtime checklists live in `docs/testing-and-build.md`; raw I/O policy lives in `docs/file-io-compatibility.md`.

| Area | Current implementation / entry points | Limits to retain |
| --- | --- | --- |
| Local baseline | `.ci/test.sh`, strict known-failure gate, `KOSHeadless=true` library/test builds; NUnit 2.7.1/NSubstitute 1.8.1 retained. 13 initial real-model compatibility tests. Failure propagation and fresh-checkout builds previously verified. | Gate passes with 3 visible upstream failures; no tests skipped/filtered. Do not expand the allowlist to hide regressions; raise minimum count as coverage grows. |
| BUFFER-01 | `ScreenBuffer.GetBuffer()` clips overlays by row-intersection replacement; 26 regressions. | Fixed = viewport-relative, non-fixed = absolute minus TopRow; later overlays win; shared row references retained. No horizontal semantics or snapshot concurrency guarantee. Only this earlier build has guided Proton smoke evidence. |
| BUFFER-04 | `ScreenBuffer.SetSize()` normalizes before allocation/callbacks; `SubBuffer` validates before mutation; 27 regressions. | Zero model axis preserves current size; others clamp to 1–160 rows / 1–255 columns. Script minima remain 3/15. Effective same-size requests are true no-ops. Negative long-input row anchors remain valid; overlay height is uncapped. |
| BUFFER-02/03 | `ScreenSnapShotExtensions.TryGetCursorCharacter()` (23 tests), snapshot-only GUI cursor reads and scoped group/color/skin cleanup in `TermWindow`; `RenderFaultRecovery` (34 tests). | Invalid cursor hidden, not moved; snapshot interface unchanged. Font/geometry preparation and window callback recover nonfatal faults with Retry/Close/Resize, 2-second retries and 10-second diagnostic batching. Fatal/Unity GUI-exit branches propagate. Update/snapshot acquisition, input, title/lifecycle, telnet, GUI.Window internals and fallback/reporter faults are outside recovery. Unity cleanup/control behavior only simulated headlessly. |
| EDIT-01/02 | `EditorDocument` (48 tests), wired through `KOSTextEditPopup`/`DelegateDialog`. Actual-text dirty detection; separate active destination and pending open; load commits only after successful read; modal state survives focus/reopen. | Save targets active document. Failures preserve draft/request; successful save before failed load advances baseline. Incidental Close preserves draft; explicit discard-exit restores baseline. Text API semantics retained; no conflict detection/input-lock redesign. Popup/dialog adapter is compiled, not Unity-tested. |
| IO-03 | `ArchiveFileWriter` and Archive save integration (50 tests): exclusive same-directory `.kosim-save-<GUID>.tmp`, write/flush/close, then Replace existing / non-overwriting Move new; internal instance-local fault seam. | No delete/truncate fallback. Clean only owned temps; cleanup failure retains both errors/path. Interrupted saves can leave visible temps; no sweep. Native commit safety depends on runtime/filesystem; no power-loss durability, external-change detection or rollback after post-commit Open failure. Append/Clear/static archive root/link policy unchanged. |
| IO-01/02 | User-approved **Preserve text APIs** policy: additive `ReadAllRaw`/`WriteRaw`/`SaveFileRaw`, script `READRAW`/`WRITERAW(FileContent)`; copy/move/helper/boot seeding route raw. 62 tests (`RawFileIoTest`, `LegacyNewlineTest`). | Built-in raw transfers preserve bytes; raw append is nontransactional. Legacy READALL/WRITE, UTF-8 replacement/BOM/type behavior retained; Windows CRLF conversion is idempotent. Legacy archive READALL still risks binary corruption: use raw APIs. Old/derived providers retain legacy hooks until opting into raw overrides; no universal byte-exact or precompiled-mod guarantee. Exact byte capacity can reject moves previously accepted via shortened text views. |
| NET-01/02 | `kOS.Safe/UserIO`: `TelnetInputParser` (incremental RFC 854, persistent UTF-8 decoder, 1024-byte subnegotiation cap), `TelnetInputDispatcher` (text coalescing, break discard), `TelnetProtocol` (negotiation replies, NAWS clamping), `TelnetMenuText`, and `TelnetConnectionWorker` (background reader/writer, serialized writes, 5 s write timeout, cooperative idempotent shutdown with 1 s bounded join, no `Thread.Abort`). `TelnetSingletonServer` keeps its public API, and game-side stop/menu work runs in `MainThreadUpdate()`, called from `TelnetMainServer.Update()`. 83 tests (34 parser, 23 protocol, 5 dispatcher, 9 menu, 12 loopback-socket worker). | Intentional changes are listed in `docs/testing-and-build.md` (NET-01/02 section). Server, menu and mapper adapters are compiled but not tested. Mappers still work per read. A game-thread raw send to a stalled client can block for up to 5 s. Socket semantics have been checked only on Linux Mono; Unity/Proton telnet acceptance is deferred. |

## Latest verification and commands

Host tools: SDK 8.0.425 in `$HOME/.cache/kosim-tools/dotnet`, Mono 6.12.0, Python 3.14.7. Ignored `kOS.props.user` points to the dev instance's managed assemblies and disables CKAN dependency installation.

```bash
export PATH="$HOME/.cache/kosim-tools/dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
bash .ci/test.sh
# Full production build against dev game assemblies (not KOSHeadless):
dotnet build kOS.sln -c Release
```

Latest implementation-session results (NET-01/02 session):

- Baseline before changes: `bash .ci/test.sh` exit 0, 585 executed/582 passed/3 known failures.
- Final: `bash .ci/test.sh` exit 0, **668 NUnit executed, 665 passed, 3 known failures, 0 skipped; 12 Python gate tests passed**. Gate minimum raised to 668. XML: `artifacts/tests/Release/nunit.xml` (ignored).
- Flakiness: the `kOS.Safe.Test.UserIO` fixtures (83 tests) passed 25/25 repeated runs, and 8/8 with all 24 cores busy (`mono nunit-runner/nunit-console.exe kOS.Safe.Test.dll -run:kOS.Safe.Test.UserIO` from `src/kOS.Safe.Test/bin/Release/net48`).
- Clean full build: `dotnet build kOS.sln -c Release --no-incremental` exit 0, **0 errors, 44 existing warnings** (none in telnet code). Outputs remain in repository `Resources/GameData/kOS`; not deployed.
- Upstream scraper faults were confirmed by compiling the original `TelnetProtocolScrape` code into a temporary Mono harness (scratch only, not committed). No game, telnet client or Proton check was run.

Three unchanged upstream lexicon-formatting failures (reproduced before edits; original baseline 302 executed/299 passed):

1. `kOS.Safe.Test.Collections.LexiconTest.CanFormatNumericKeys`
2. `kOS.Safe.Test.Collections.MixedCollectionPrintingTest.CanPrintListInLexicon`
3. `kOS.Safe.Test.Serialization.TerminalFormatterTest.CanSerializeLexicons`

They expect inline primitives, but dumps expand them; inspect `SafeSerializationMgr.DumpValue()` (`IDumper` before primitive handling) when investigating separately. No serialization fix made. Passing compilation or the known-failure gate is not a fully passing NUnit suite or Proton validation.

## Runtime setup and deferred acceptance

- Only runtime target now: Windows Steam **KSP 1.12.5.3190 (WindowsPlayer x64)** under **GE-Proton11-7-x86_64**, via installed UMU 1.4.4. Other game platforms are outside validation scope; avoid gratuitous breakage.
- Approved independent dev copy: `/home/jvz/Games/KSP-kOSIM-dev/`. Use its `launch-dev.sh` (`--check` for preflight), isolated `.proton-prefix` (effective Wine prefix `.proton-prefix/pfx/`), disposable `saves/kOSIM-dev` and `dev-logs`/`KSP.log`. Runtime updates default off; do not silently change Proton or reuse vanilla compatdata.
- Protect `/home/jvz/.local/share/Steam/steamapps/common/Kerbal Space Program/`, Steam settings and original prefix. No deployment there or personal-save imports; proprietary assemblies/dev tree stay outside git.
- Dev GameData has stock assets plus **the earlier overlay-only kOSIM build**. ModuleManager is absent; patches inactive. All subsequent builds are repository-only. Recheck live processes before any future deployment; previous game-running state/PIDs are not reliable across sessions.
- First isolated launch succeeded and loaded kOS. User-guided long-input/resize smoke confirmed responsiveness, cursor/text alignment and command output; inspected log had no matching range/GUI-clip exceptions. Existing `kOS: ipu: -1` error logging and a DontDestroyOnLoad warning were observed, not investigated. This is limited overlay smoke evidence, not comprehensive runtime validation.
- **All newer Unity/Proton acceptance remains deferred.** When the user resumes, safely close/verify exit before complete-assets deployment and restart with the unchanged isolated launcher. Use disposable files/save only and follow the checklists in `docs/testing-and-build.md`: dimensions/cursor/render-fault cleanup and retry controls; editor modal/save-discard-cancel/failure behavior; real archive Replace/Move/locks/permissions/metadata; raw suffixes/transfers, legacy text, new-disk boot and actual source/KSM execution/provider hooks. Also the NET-01/02 telnet checklist: real clients, non-ASCII, resize extremes, interrupt, detach/close/kill, disabling telnet, scene change/quit, stalled client. Headless managed Windows-flag tests and compile/pack/unpack do not verify those paths. No power-loss/full-disk experiment without explicit planning.

## Follow-on priorities

See "Resume here" for the ordering: formatter failures, compatibility fixtures, the optional mapper follow-up, then `INPUT-01` and other small confirmed safety defects. Retain pending GUI/telnet equivalence, fixed communications overlay, multiple-viewer, focus/power/scene and native-resource checks; no systematic performance profile has run.

Before major structural extraction, document screen/viewport coordinates, resize authority across viewers, input/thread ownership and explicit lifecycle/resource contracts. No wholesale rewrite or UI modernization yet.
