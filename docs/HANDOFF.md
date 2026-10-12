# KOS Improved — session handoff

## Resume here

**Next task: local NET-01/02 telnet safety work.** BUFFER-01–04, EDIT-01/02 and IO-01–03 are implemented, headless-tested and compiled within the limits below. No unfinished edit in those areas is pending. Phase 0 remains partially complete: broader compatibility fixtures are missing.

The user **deferred further Proton checks**. Continue local implementation/tests; do not deploy, terminate/restart the game, or request immediate game verification. No UI redesign, hosted testing/release automation, automatic commit or push.

1. Read `AGENTS.md`, `docs/KOSIm-roadmap.md` and `docs/testing-and-build.md`; inspect `git status` and preserve existing work.
2. Run the local baseline (commands below).
3. Revalidate `src/kOS/UserIO/TelnetSingletonServer.cs`: per-read UTF-8 decoding/protocol scraping, zero-byte EOF, worker shutdown and main-thread ownership. The original review's NET-01/02 findings are leads, not proof against current code.
4. First extract a small testable incremental byte parser/persistent UTF-8 decoder. Cover every split boundary, multiple commands per read, escaped IAC, partial subnegotiation/NAWS, malformed/truncated input, EOF and zero/oversized resize requests. Preserve protocol/public APIs and loopback/opt-in exposure defaults.
5. Follow with cooperative cancellation, read unblocking, bounded joins and idempotent shutdown separately. Keep game/Unity cleanup on the owning thread; test orderly close/reset/stalls/write failure/simultaneous shutdown. No transport/compiler/terminal rewrite.

## Workspace and constraints

- Workspace: `/home/jvz/Projects/KOS-Improved`; branch `kosim/test-baseline`.
- HEAD/base: `8f281a459b6ea0ba917c91bbbc117e68bb948199` (`fix element counting parts multiple times`), from upstream `develop`; origin remains <https://github.com/KSP-KOS/KOS>.
- All project work remains unstaged/uncommitted, including untracked docs, test tooling, models and tests. No fork, push or project commit made. Use `git status` rather than a stale file inventory.
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

## Latest verification and commands

Host tools: SDK 8.0.425 in `$HOME/.cache/kosim-tools/dotnet`, Mono 6.12.0, Python 3.14.7. Ignored `kOS.props.user` points to the dev instance's managed assemblies and disables CKAN dependency installation.

```bash
export PATH="$HOME/.cache/kosim-tools/dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
bash .ci/test.sh
# Full production build against dev game assemblies (not KOSHeadless):
dotnet build kOS.sln -c Release
```

Latest implementation-session results:

- Tests: exit 0; **585 NUnit executed, 582 passed, 3 known failures, 0 skipped; 12 Python gate tests passed**. Gate minimum: 585. XML: `artifacts/tests/Release/nunit.xml` (ignored). Temporary log: `/tmp/kosim-byte-text-final-tests.log`.
- Full build: exit 0, **0 errors, 44 existing warnings**. Temporary log: `/tmp/kosim-byte-text-full-build.log`. Outputs remain in repository `Resources/GameData/kOS`; not deployed.
- `git diff --check` and edited/new-file whitespace checks passed. API RST source updated; rendered Sphinx docs not built.
- Latest session only condensed this handoff; no source/tooling changes and no tests/build/game checks rerun. Documentation whitespace checked.

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
- **All newer Unity/Proton acceptance remains deferred.** When the user resumes, safely close/verify exit before complete-assets deployment and restart with the unchanged isolated launcher. Use disposable files/save only and follow the checklists in `docs/testing-and-build.md`: dimensions/cursor/render-fault cleanup and retry controls; editor modal/save-discard-cancel/failure behavior; real archive Replace/Move/locks/permissions/metadata; raw suffixes/transfers, legacy text, new-disk boot and actual source/KSM execution/provider hooks. Headless managed Windows-flag tests and compile/pack/unpack do not verify those paths. No power-loss/full-disk experiment without explicit planning.

## Follow-on priorities

After NET-01/02: focus/flight-control lock cleanup (`INPUT-01`), other small confirmed safety defects, wider boot/flight/KSM/save/craft/API fixtures, and separate investigation of the 3 formatter failures. Retain pending GUI/telnet equivalence, fixed communications overlay, multiple-viewer, focus/power/scene and native-resource checks; no systematic performance profile has run.

Before major structural extraction, document screen/viewport coordinates, resize authority across viewers, input/thread ownership and explicit lifecycle/resource contracts. No wholesale rewrite or UI modernization yet.
