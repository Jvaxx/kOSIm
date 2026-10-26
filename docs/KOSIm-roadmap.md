# Roadmap and known issues

## Direction and scope

External editor + optional kerboscript LSP for authoring; the in-game terminal and
telnet for execution/debugging. The popup editor remains a safe fallback, not an IDE.
The implementation order was baseline/tests → safety → incremental extraction →
workflow → UI. Preserve scripts, KSM, saves/craft and public mod APIs; keep `net48`.

Validation targets Windows KSP **1.12.5.3190 / GE-Proton11-7** on Linux only.
Native Linux/Windows/macOS and other game versions are unvalidated, not deliberately
removed. Tests and releases are local/manual; no hosted automation.

## Completed since upstream 1.6.0.1

| Area | Implemented |
| --- | --- |
| Baseline | Local NUnit execution gate, real-model terminal tests, source/KSM/dashboard compatibility fixtures |
| Safety | Editor dirty/save-switch transactions; clipped overlays/cursors; dimension validation; render recovery; atomic saves; raw I/O; volume identity and move-to-self checks |
| Boundaries | Unity-independent input router/session, explicit lifecycle and thread ownership, separate output screen/view |
| Workflow | Draft-preserving saved history and Ctrl+R; confirmed paste; copy/selection; CPU status; wheel and follow-output |
| UI | Scaled green IMGUI chrome, saved geometry, scrollbar, font fallback, CPU/Settings tabs, independent editor and clearer dialogs |
| Distribution | `kOSImproved` CKAN metadata and solver checks; deterministic release ZIP with license/source materials; KSP 1.12-only metadata (local tooling checks; no game pass) |
| Follow-up | Shared geometry presets; background opacity; queued telnet writes; picker cleanup; bounded idle/drawing work; throttled history saves; stale per-CPU/temp-file cleanup |
| 1.6.0.104 (unreleased; user-reported game pass) | Per-save command history shared by all CPUs (old per-CPU files deleted); toolbar right-click opens/closes the active vessel's terminals |

The agreed phases 0–4 are complete. Functional Proton passes were recorded for these
features, with exceptions below; do not interpret that as exhaustive platform or
edge-case coverage. Details: [compatibility contracts](compatibility.md),
[changelog](../CHANGELOG.md), [current verification](HANDOFF.md).

Historical fork releases: `1.5.1.100`–`.102` (incorrect version prefix, unchanged).
Latest release: **1.6.0.103**; the version correction is not an upstream rebase. It is
the first release with a `kOSImproved` `.ckan`; see [CKAN packaging](ckan.md). No next implementation phase has been selected.

## Prioritized follow-up

1. **Publication readiness:** finish [repository/history and distribution checks](publication.md).
   No visibility change, release, deployment or history rewrite is implicit.
   Follow the [NetKAN submission](ckan.md#netkan-submission-maintainer-action-after-the-first-release)
   ([#11747](https://github.com/KSP-CKAN/NetKAN/pull/11747)) through merge and indexing.
2. **Telnet edge cases:** investigate tiny-grid runaway and negotiation loops before
   expanding remote use. Retain opt-in/loopback defaults and bounded executable input.
3. **Finish targeted game checks:** pending-history flush on immediate game exit;
   picker component counts; stalled telnet client during vessel switch; missing-sound
   install message. See [manual matrix](testing-and-build.md#game-checks).
4. **Small review cleanup, when justified:** P-12 per-event work; D-02 test-only public
   seams; R-06 picker window IDs; R-07 scene-change error handling (details below).
5. **Only with an agreed design:** interpreter rebuild cost, further editor conveniences
   or a different renderer. No incidental compiler/VM rewrite or runtime upgrade.

## Open findings and limitations

These are recorded findings, not claims that all have been reproduced on current HEAD.
Historical evidence is in the pre-cleanup docs at `9c0c83a4`; reproduce and add tests
before claiming a fix. Labels are retained for continuity.

- **T4 (telnet):** two clients with one typing into a 1×1 grid caused extreme memory
  growth/slowdown; grids at script minima worked. T4-DISP: tiny-grid output can be only
  scroll codes. T4-NEG: repeated `DO ECHO`/`DO SGA` receives repeated `WILL`, allowing
  negotiation loops with non-RFC-1143 clients.
- **G3-NUL / MENU-TYPE:** full repaint sends empty cells as NUL, shifting text in real
  terminals; an 80×24 welcome header can show `INITIAL_UNSET`. Menu wraps below 50
  columns. ANSI mapping is not implemented. Malformed-input logs are not rate-limited.
  Simultaneous clients can interleave text within a line; private-use input acts as kOS keys.
- **GUI:** narrowing can leave the empty prompt overlay blanking rows until a key;
  a pre-`CLEARSCREEN` selection can highlight replacement text. AltGr+V on the tested
  French Mac layout produces `?` under Wine. Delayed RemoteTech opens can still take focus.
- **POWER-01:** self power-off through `DOEVENT("Toggle Power")` logged
  `KOSBadJumpException`; compare upstream before assigning origin.
- **LIFE-04:** disposed script GUIs retain their window/skin/texture until power-off or
  scene change (deliberately left unchanged). The widget texture cache has no eviction.
- **Script GUI:** upstream [#3168](https://github.com/KSP-KOS/KOS/issues/3168), assigning
  popup `VALUE` from a string, is not addressed by the selection/callback fix.
- **Files:** legacy text reads remain lossy for binary; use raw APIs. Atomic replacement
  does not provide external-change detection or power-loss durability. Link behavior and
  the concurrent-filesystem limitation are documented in [compatibility.md](compatibility.md#files-and-volumes).
- **PERF-01 (inherited):** every prompt command rebuilds the accumulated program
  (`ProgramContext.AddParts` → `ProgramBuilder.BuildProgram`, then instruction-pointer
  lookup). Temporary headless instrumentation on Mono 6.12 over 3,000 commands measured
  median updates growing from ~0.18 to ~2.0 ms in that rebuild. The scrollback improvement
  did not fix long-paste growth. Not an in-game frame-time measurement; compiler/VM work
  needs separate semantic/timing design.

### Remaining post-release review cleanup

- **P-12:** repeated `KOSMouseBlocking.Show` polling/blocker updates per IMGUI event;
  paste-area name concatenation; per-char `TelnetOutputQueue.Write` strings; empty-array
  allocation in `TerminalInputDecoder`; `MouseBlocking` lookup closure; repeated
  `KOSStyle.ChromeFont` lookups. Profile before changing; no claimed runtime gain.
- **D-02:** consider `internal` + existing `InternalsVisibleTo` for fork-only test seams
  in history/file-path/layout/palette/budget/worker/parser/queue types. Check external
  compatibility first; do not indiscriminately shrink public upstream APIs.
- **R-06:** `ListPickerDialog` uses `title.GetHashCode()` as its IMGUI ID; investigate
  collision-safe IDs consistent with other windows.
- **R-07:** input and mouse scene-change cleanup differ in exception handling;
  align once-only logging without masking failed lock release.

Deferred: EDIT-05 find/replace, line numbers and highlighting; external-editor
conveniences E1 archive location, E3 rerun key, E4 overwrite warning. E2 file picker was
dropped. No automatic execution on file changes, themes API, embedded IDE, structured
remote-control protocol, steering or language changes are planned.
