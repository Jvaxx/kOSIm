# KOS Improved — session handoff

## Resume here

Phases 0–4 are complete (status in `docs/KOSIm-roadmap.md`), and **1.5.1.100** was released on 2026-10-20 (tag `v1.5.1.100` on commit `5bd2afab`; <https://github.com/Jvaxx/kOSIm/releases/tag/v1.5.1.100>). Every item of that release is headless-tested and passed in game under Proton.

**Post-release review, in progress (2026-10-20 to 21):** the user's review `docs/review-report.md` (of `e280f197`; items done are marked there) lists findings R-, L-, P-, D-, K-, N-. Done, each at the user's request, with no in-game test for now, in four commits on `master` (**not pushed, not deployed**): R-01 and L-01 (`5a0de50b`); P-01 to P-06 with R-04 and R-05 (`43c7f589`); P-09, P-10 and the doc fixes K-01, K-03 to K-12 (`a3000e9d`); **P-07 and P-08** (the fourth commit, 2026-10-21). Details of all four: `docs/testing-and-build.md`, "Fixes from the post-release review".

**Fourth commit (2026-10-21), P-07 and P-08:** an open terminal draws its rows, strip and header on every IMGUI event (several a frame). It now keeps what it drew while it shows the same. Nothing drawn changes.

- **P-08** (`src/kOS.Safe/Screen/TerminalRowTexts.cs` new, `TermWindow.DrawCharacters`): each row keeps its string. The same `ScreenBufferLine` with the same change tick counts as unchanged; otherwise the characters are compared with the kept string, and other `IScreenBufferLine` kinds are always compared. Rows no longer drawn are dropped, and all of them on `Close`. Measured headless: an unchanged 50×36 screen went from 9,216 bytes per event to 0 (255×160: from 171,520 to 0).
- **P-07** (`StatusStripCache.cs` and `ShortenedText.cs` new, `TermWindow.DrawStrip`/`DrawHeader`): the strip's layout, state text, chips and size text are kept until its measures, place, state, chips, notice or size change. The state and chips are read once per status (every 0.25 s). The header title is kept as fitted. The measurer delegates are made once per window. Measured headless: an unchanged strip allocates 0 bytes.
- Tests: 14 new (`TerminalDrawCacheTest.cs`: `TerminalRowTextsTest` 8, `StatusStripCacheTest` 5, `ShortenedTextTest` 1). Mutation run: 22 mutants, all killed. `minimum_tests` 1346 → 1360. Not checked in game (the P-07/P-08 entry in `testing-and-build.md`, "In-game coverage").
- Not done (other items): the other windows' titles (`KOSStyle.FlatWindow`, toolbar, name tag, list picker) still shorten on every event. They are outside P-07, and some are in P-12.

**Third commit (`a3000e9d`, 2026-10-21):**

- **P-09** (`src/kOS.Safe/Screen/ScreenLines.cs` new, `ScreenBuffer.cs`): the screen's lines are an internal list that drops its first lines without moving the others (amortized: one copy of the lines kept after at least as many dropped); `ResizeBuffer` keeps lines already at the new width (a change of rows copies none). Measured headless: a line printed with a full scrollback 2.7–2.9 µs → 0.5–0.9 µs; one `PRINT` of 5,000 lines 12–14 ms → 1.2–2.0 ms.
- **Finding PERF-01** (inherited, recorded in `testing-and-build.md`, "Known issues"): the slower long paste is not the scrollback. The interpreter rebuilds its whole program for every prompt command (`ProgramContext.AddParts` → `ProgramBuilder.BuildProgram`), so a command costs more the more commands ran since boot (median update 0.18 → 2.0 ms over 3,000 pasted commands, all of it in the rebuild; measured by temporary instrumentation, removed). A 3,000-line paste measures the same before and after P-09. Compiler/VM work is out of scope: ask the user before touching it.
- **P-10** (`src/kOS.Safe/Screen/CommandLineEditor.cs`, `src/kOS/Module/kOSProcessor.cs`): the history is saved at most every 2 s (`HISTORY_SAVE_INTERVAL`; monotonic clock, `HistoryClock` internal for tests): at once when the last save is older, else by `kOSProcessor.Update` → `SavePendingHistory(false)` after the interval, and at once in `kOSProcessor.OnDestroy` (before `DestroyObjects` nulls the interpreter). Replacing the store saves the pending commands to the old one. A game crash can lose 2 s of commands.
- **Docs:** K-01 (file links: appends and `:CLEAR` write the target, a whole-file save replaces the link; directory links followed for every write) in `testing-and-build.md` and the roadmap's binding decision, with test `AppendingToAFileLinkWritesItsTarget`; K-03 to K-07 in `testing-and-build.md` (also its stale `minimum_tests` figure); K-08 in the roadmap; K-09 to K-12 in `doc/source` (`config.rst` defaults, ranges, global settings marked; `terminal.rst` `CHARHEIGHT` 4–48; `settingsWindows.rst` Key Note 13 and the old picture); the `TelnetInputQueue` comment. K-04 and K-05 were fixed in the docs only (the copy filter keeps DEL/C1; the brightness suffix stores a float).
- **Test harness:** `ScriptHost.Transcript` failed once lines had been dropped from the scrollback (so `KOSIM_PASTE_LINES=3000` errored); fixed.
- Tests: 13 new (`ScreenLinesTest` 5, `HistoryPersistenceTest` 6, `ArchiveLinkTest` 1, `ScreenViewTest` 1). Mutation run: 20 mutants, all killed; the transcript test fails without its fix. `.ci/known-test-failures.json` `minimum_tests` 1333 → 1346.

**Earlier commits of the review** (in `testing-and-build.md`): R-01, only the telnet writer thread writes to a socket (`TelnetConnectionWorker.Post`/`Flush`, bounded 250 ms stop); L-01, a list picker closed from outside is destroyed; P-01 to P-06, no per-frame work or allocation for CPUs nobody looks at (`TerminalInputRouter`, `TerminalSession.Update(now, shownInGui)`, `ScreenSnapShot.DiffFrom`, toolbar once a frame, titles gated); R-04/R-05, missing returns and null checks. Allocation tests use `src/kOS.Safe.Test/AllocationCounter.cs`. For mutation runs, run the whole test assembly: a `-run=` filter outside the `Execution` namespace skips the setup the `ScriptHost` fixtures need.

**Next action:** ask the user which of these to do (do only what they pick):

- Deploy for the in-game checks in `docs/testing-and-build.md`, "In-game coverage" (R-01 normal and stalled client, L-01 object counts, the idle work, P-09, P-10, P-07/P-08), and/or push the four commits to `origin`.
- The report's remaining items: P-11, P-12, L-02/L-03 (decide: prune or document per-CPU files; hide or sweep archive temp files), L-04, D-01 to D-04 and R-06/R-07 (cleanup), N-01 (before the presets).
- PERF-01: whether to look at the interpreter's per-command program rebuild (compiler/VM, out of scope by default).
- **Terminal geometry presets** (user feature, decisions agreed 2026-10-20: `docs/KOSIm-roadmap.md`, "Planned: terminal geometry presets"). **Step plan written 2026-10-20** (roadmap, "Step plan" under that section), awaiting the user's approval and answers to Q1 (a partial slot counts as empty) and Q2 (no notice on apply). Implement only after approval.
- The rest of EDIT-05 (find/replace, line numbers, highlighting) or the deferred editor conveniences E1/E3/E4 (roadmap, "Workflow").
- POWER-01: a CPU powering itself off with `DOEVENT("Toggle Power")` logs `KOSBadJumpException`; compare with an upstream build first.
- Open issues and never-performed checks: `docs/testing-and-build.md`, "Known issues and open findings" and "In-game coverage" (T4, telnet G3-NUL/MENU-TYPE, #3168, …).
- A later release: raise the fourth version field and follow `docs/testing-and-build.md`, "Versions and releases".

## Workspace state (2026-10-21)

- **Repository:** the user's private GitHub repo `Jvaxx/kOSIm` is `origin`; branch `master` tracks `origin/master`. Upstream kOS is the remote `upstream`, fetch only (push URL `no_push`); local `develop` tracks `upstream/develop`. No GitHub Actions (no `.github/workflows/`). kOS is GPL-3.0: keep the license and notices if builds are shared.
- **Fourth review commit** (P-07/P-08, 2026-10-21, on `master` after `a3000e9d`; not pushed; `master` is 4 commits ahead of `origin/master`). Gate: **1360/1360** (`bash .ci/test.sh`, Mono 6.12.0; the 12 `.ci` Python tests pass). Full build (`dotnet build kOS.sln -c Release --no-incremental`) of the working tree just before committing: 0 errors, 44 warnings (none new; in `TermWindow.cs` only the old `root` CS0169). Not deployed: rebuild from the commit before deploying.
- **Third review commit** `a3000e9d` (2026-10-21; not pushed). Gate: **1346/1346** (`bash .ci/test.sh`, Mono 6.12.0; the 12 `.ci` Python tests pass). Full build (`dotnet build kOS.sln -c Release --no-incremental`) of the working tree just before committing: 0 errors, 44 warnings (none new, none in the changed files). The DLLs embed the commit ID, so rebuild from the commit before deploying and hash then. Not deployed.
- **Idle-work commit** `43c7f589` (2026-10-20; not pushed). Full build (`dotnet build kOS.sln -c Release --no-incremental`): 0 errors, 44 warnings (none new; `TermWindow.root` CS0169 predates it); `kOS.dll 10af663f…`, `kOS.Safe.dll 3fca6489…` (not deployed; the gate run after it rebuilt `kOS.Safe.dll` headless, hash `68820454…`, so run the full build again before deploying). Gate: 1333/1333 (`bash .ci/test.sh`).
- **R-01/L-01 commit** `5a0de50b` (2026-10-20, with `docs/review-report.md`; not pushed). Its full build: 0 errors, 44 warnings (none in the changed files); `kOS.dll 1d40388c…`, `kOS.Safe.dll aa4d44d3…` (not deployed). Gate: 1317/1317 (`bash .ci/test.sh`, Mono 6.12.0 for NUnit).
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

Last sessions: 2026-10-21, P-07 and P-08 (above; gate, mutation run, measurements and full build; no in-game test, as the user asked). Before that, 2026-10-21, P-09, P-10 and the doc fixes K-01, K-03 to K-12 (above; gate, mutation run, measurements and full build; no in-game test, as the user asked). 2026-10-20: fixed P-01 to P-06 with R-04 and R-05 (above; gate, mutation run and full build; no in-game test, as the user asked). Before that, fixed R-01 and L-01 from the post-release review (above; gate, mutation run and full build run; no in-game test, as the user asked). Before that, docs only: wrote the geometry-preset step plan; recorded the geometry-preset decisions; trimmed the roadmap and this handoff; folded `phase2-contracts.md` and `file-io-compatibility.md` into the roadmap and `testing-and-build.md` and removed them. Gate and build not rerun (no code change).
