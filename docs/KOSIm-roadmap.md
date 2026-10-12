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
- Use the dev instance's `launch-dev.sh` with installed UMU and GE-Proton11-7 and its isolated `.proton-prefix`. First launch initialized the isolated prefix and opened a visible KSP window; logs identify KSP 1.12.5.3190 and loaded kOS assemblies. The overlay build passed a guided GUI smoke check; the newer dimension/cursor/GUI-cleanup build is not deployed or game-verified. User deferred further Proton tests; continue local implementation without deploying until requested. Never reuse the vanilla prefix or silently change runtime versions.
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

### Priorities

- Continue focused local safety work. Archive byte/text policy, replacement, editor transactions, scoped render recovery and telnet parsing/shutdown are implemented/host-tested within documented limits; target-runtime acceptance remains deferred. Keep all pending GUI/editor/archive/raw I/O/boot/telnet checks until the user resumes Proton.
- Focus and flight-control lock cleanup during hide, close, destruction, power loss, and scene changes (`INPUT-01`).
- Other contained, confirmed defects such as port-field capacity and widget selection/callback behavior, when adequately testable.

The editor's low feature priority does not lower the priority of preventing data loss. Preserve legacy text API behavior through an explicit compatibility policy rather than changing it incidentally.

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
