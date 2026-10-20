# KOS Improved — session handoff

## Resume here

Phases 0–4 are complete (status in `docs/KOSIm-roadmap.md`), and **1.5.1.100** was released on 2026-10-20 (tag `v1.5.1.100` on commit `5bd2afab`; <https://github.com/Jvaxx/kOSIm/releases/tag/v1.5.1.100>). Every item of that release is headless-tested and passed in game under Proton.

**Post-release review, in progress (2026-10-20):** the user's review `docs/review-report.md` (of `e280f197`; items done are marked there) lists findings R-, L-, P-, D-, K-, N-. Done so far, each at the user's request, with no in-game test for now: R-01 and L-01 (first commit), then **P-01 to P-06 with R-04 and R-05** (second commit). All are headless-tested, built and committed, **not deployed** (details: `docs/testing-and-build.md`, "Fixes from the post-release review").

**Idle work (P-01 to P-06, R-04, R-05; second commit):**

- `src/kOS.Safe/UserIO/TerminalInputRouter.cs`: early returns for an empty type-ahead (P-01) and no clients (P-02); the closure-building code moved to `DrainClients`/`FeedPrompt`, since C# allocates captured variables on method entry (the allocation test caught that); Ctrl-D/Ctrl-X null-check the interpreter (R-05).
- `src/kOS.Safe/UserIO/TerminalSession.cs`: `Update(now, shownInGui)` takes the snapshot only while the GUI shows it or a viewer is attached, catches up at once when someone looks, keeps the 50 ms refresh pace (beeps); one timer; a viewer's copy kept when the diff is empty and `SameLayout` holds (P-03). `Update(now)` = shown.
- `src/kOS.Safe/Screen/ScreenSnapshot.cs`: `DiffFrom` writes chunks as found, no per-row allocation, byte-identical output (P-04); `SameLayout`; `DeepCopy` via `ScreenBufferLine.CopyOf` (one copy). `IScreenSnapShot` documents live rows.
- `src/kOS/Screen/TermWindow.cs`: passes `IsOpen` to the session; `ProcessTelnetInput` returns without clients; `RefreshStatus` gated on someone looking, titles included (P-06), `RefreshStatusNow` before a telnet attach; `return` after `Close()` in `Update` (R-04).
- `src/kOS/Screen/KOSToolbarWindow.cs`: CPUs, rows, config keys and settings sections once a frame (P-05).
- Tests: `src/kOS.Safe.Test/AllocationCounter.cs` (Mono's `GC.GetAllocatedBytesForCurrentThread` by reflection; fails loudly if absent), 3 in `TerminalInputRouterTest`, 7 in `TerminalSessionTest`, 6 in the new `Screen/ScreenSnapshotCostTest.cs` (includes the previous `DiffFrom` as reference). Mutation run: 21 mutants, all killed (one survived at first, the copy skip without its layout check; `AScreenNarrowedWithoutARepaintIsDiffedAtItsNewWidthAfterwards` was added for it). Note for mutation runs: run the whole assembly; a `-run=` filter skips the setup the `ScriptHost` fixtures need and they all error.
- `.ci/known-test-failures.json` `minimum_tests` 1317 → 1333. Docs: `testing-and-build.md` (fix entry, K-02 wording of the D2 refresh, in-game checks), `review-report.md` statuses (K-02 too), roadmap status.

**R-01 and L-01 (first commit):**

- **R-01** (`src/kOS.Safe/UserIO/TelnetConnectionWorker.cs`, `src/kOS/UserIO/TelnetSingletonServer.cs`): only the writer thread writes to a telnet socket. New `Post` (bounded 64 KiB queue, sent before the handler's next output) and `Flush(timeout)`; `Send` throws off the writer thread once the workers run; `TrySend` removed. Detach and goodbye go through the output queue (fixes R-02 and R-03 as a side effect); negotiation bytes are posted; `StopListening` waits at most 250 ms (`Flush`) before `Shutdown`. Note: the report lists the writer/reader paths (`OnWriterTick`, `Negotiate`, the keepalive) among game-thread callers; they are worker threads, but `Negotiate` now posts too.
- **L-01** (`src/kOS/Screen/ListPickerDialog.cs`, `src/kOS/Screen/KOSToolbarWindow.cs`): `Close()` destroys the picker (idempotent); `GoAway` closes the address picker too; the toolbar's extra `Destroy` calls removed.
- Tests: 6 new in `src/kOS.Safe.Test/UserIO/TelnetConnectionWorkerTest.cs` (one `TrySend` test replaced). Mutation run: 7 mutants of the worker (direct write in `Post`, posted after the handler's output, `Flush` not waiting, no thread guard, `Flush` ignoring its timeout, no backlog cap, `Flush` from the writer waiting for itself), all killed. L-01 has no headless test (Unity object lifetime).
- `.ci/known-test-failures.json` `minimum_tests` raised 1312 → 1317.

**Next action:** ask the user whether to deploy for the in-game checks listed in `docs/testing-and-build.md`, "In-game coverage" (R-01 normal and stalled client, L-01 object counts, the idle-work checks), or to continue with the report's next items (its section 8: K-01 and K-09–K-12 doc fixes, then P-09 measured), only as the user picks them. Other candidates:

- **Terminal geometry presets** (user feature, decisions agreed 2026-10-20: `docs/KOSIm-roadmap.md`, "Planned: terminal geometry presets"). **Step plan written 2026-10-20** (roadmap, "Step plan" under that section), awaiting the user's approval and answers to Q1 (a partial slot counts as empty) and Q2 (no notice on apply). Implement only after approval.
- The rest of EDIT-05 (find/replace, line numbers, highlighting) or the deferred editor conveniences E1/E3/E4 (roadmap, "Workflow").
- POWER-01: a CPU powering itself off with `DOEVENT("Toggle Power")` logs `KOSBadJumpException`; compare with an upstream build first.
- Open issues and never-performed checks: `docs/testing-and-build.md`, "Known issues and open findings" and "In-game coverage" (T4, telnet G3-NUL/MENU-TYPE, #3168, …).
- A later release: raise the fourth version field and follow `docs/testing-and-build.md`, "Versions and releases".

## Workspace state (2026-10-20)

- **Repository:** the user's private GitHub repo `Jvaxx/kOSIm` is `origin`; branch `master` tracks `origin/master`. Upstream kOS is the remote `upstream`, fetch only (push URL `no_push`); local `develop` tracks `upstream/develop`. No GitHub Actions (no `.github/workflows/`). kOS is GPL-3.0: keep the license and notices if builds are shared.
- **Idle-work commit** (2026-10-20, on `master`, after the R-01/L-01 commit; not pushed). Full build (`dotnet build kOS.sln -c Release --no-incremental`): 0 errors, 44 warnings (none new; `TermWindow.root` CS0169 predates it); `kOS.dll 10af663f…`, `kOS.Safe.dll 3fca6489…` (not deployed; the gate run after it rebuilt `kOS.Safe.dll` headless, hash `68820454…`, so run the full build again before deploying). Gate: 1333/1333 (`bash .ci/test.sh`).
- **R-01/L-01 commit** (2026-10-20, on `master`, with `docs/review-report.md`; not pushed). Its full build: 0 errors, 44 warnings (none in the changed files); `kOS.dll 1d40388c…`, `kOS.Safe.dll aa4d44d3…` (not deployed). Gate: 1317/1317 (`bash .ci/test.sh`, Mono 6.12.0 for NUnit).
- **Last code commit `5bd2afab`** (version 1.5.1.100). Its build (0 errors, 44 warnings): `kOS.dll dee200fb…`, `kOS.Safe.dll 4791c901…`; release zip `kOS-v1.5.1.100.zip` sha256 `6bf31b80…` (local copy in the ignored `artifacts/`). Gate: 1312/1312.
- **Dev instance** `/home/jvz/Games/KSP-kOSIM-dev/`: the release zip is deployed (hashes equal to the build's); the previous deploy is in `kOS-GameData-backups/kOS-before-v1.5.1.100-20261020`. Smoke test 2026-10-20: Player.log shows `kOS v1.5.1.100+5bd2afab…` at the main menu with no exception (`dev-logs/Player.log.v1.5.1.100-smoke-20261020`). The game is closed.
- Dev save `kOSIM-dev`: vessel with two CPUs on one part, told apart by tag (`cpu down`, `cpu top`); the telnet menu order is not fixed. Settings: 2560×1440 fullscreen, UI scale 1.3. The dev copy carries the fullscreen focus-loss patch (`docs/testing-and-build.md`).
- Harmless leftovers: `TerminalDefaultWidth` 55 in `config.xml` (was 50); `config.xml` may still read `Courier New Bold` although Lucida Console was picked (written on a scene change; check after one); test files `1:/e.ks`, `0:/p5test.ks`; geometry file `2828206133.txt` from an older vessel.
- Launcher `launch-dev.sh`: the Steam kOS shortcut's options (Wine Wayland driver, `-force-d3d11 -window-mode borderless`) plus MangoHud; opt-outs `KOSIM_WAYLAND=0`, `KOSIM_MANGOHUD=0`. Still unchecked under Wayland: the overlay and the remote tools (`hk.sh`, the `grim` region, the clipboard bridge).

## Constraints (still binding)

- Runtime: Windows Steam **KSP 1.12.5.3190** under **GE-Proton11-7** (UMU 1.4.4), dev copy only; runtime stays `net48`.
- Protect the vanilla install, Steam settings and the original prefix. Never close or kill the game without asking; deploy only with it closed, after a backup.
- Local tests only. Push only to `origin`, never to `upstream`.
- User decisions: `docs/KOSIm-roadmap.md`, "Binding decisions". Keep `docs/improvements-overview.md` unchanged.

## Commands

```bash
export PATH="$HOME/.cache/kosim-tools/dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
bash .ci/test.sh                                   # headless gate (PYTHON=python if needed)
dotnet build kOS.sln -c Release --no-incremental  # full build against the dev game (kOS.props.user)
```

Deploying, launching, logs, the remote-testing tools and what needs the user: `docs/testing-and-build.md`. Per-session test records up to the release are in git history (this file at `5bd2afab`).

Last sessions (2026-10-20): fixed P-01 to P-06 with R-04 and R-05 (above; gate, mutation run and full build; no in-game test, as the user asked). Before that, fixed R-01 and L-01 from the post-release review (above; gate, mutation run and full build run; no in-game test, as the user asked). Before that, docs only: wrote the geometry-preset step plan; recorded the geometry-preset decisions; trimmed the roadmap and this handoff; folded `phase2-contracts.md` and `file-io-compatibility.md` into the roadmap and `testing-and-build.md` and removed them. Gate and build not rerun (no code change).
