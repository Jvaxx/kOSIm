# Local building and testing KOS Improved

## Scope: Steam KSP under Proton

The current runtime target is **KSP's Windows Steam build running under Proton on this user's Linux machine**. Native Linux, native Windows, macOS, other KSP versions, and other machines are not part of the current validation commitment. Avoid unnecessary breakage in their code paths, but do not claim untested support.

The approved development instance for **all game references, deployment, and game tests** is:

```text
/home/jvz/Games/KSP-kOSIM-dev/
```

It was independently copied from `/home/jvz/.local/share/Steam/steamapps/common/Kerbal Space Program/` and checksum-verified. The vanilla source remains untouched and is not a deployment target. Both contain the Windows `KSP_x64.exe` and `KSP_x64_Data/Managed` assemblies; Steam build ID is `03190`.

The dev copy is approximately 5.6 GiB and initially contained only stock `Squad`/`SquadExpansion` GameData; the tested local kOSIM build is now deployed under `GameData/kOS`. Only stock scenarios/training saves were retained; personal saves, screenshots, CKAN state, generated logs/part database/thumbs, and settings backups were excluded. `settings.cfg` was copied independently for familiar input/display settings. No hard links/symlinks to the vanilla source were used.

Steam's KSP compatibility mapping selects installed `GE-Proton11-7-x86_64`. The dev launcher uses that same tool through installed UMU 1.4.4. First launch initialized the dedicated prefix and opened a visible game window; logs report **KSP 1.12.5.3190 (WindowsPlayer x64)** and show kOS assemblies/parts loading. The overlay-only build passed the guided GUI long-input/resize smoke check. Build `e943ecf4` was deployed on 2026-10-12; its checkpoint results are in "Proton checkpoint 2026-10-12" below (telnet done, other groups pending).

**Local tests only.** There are no active GitHub Actions build/test/release workflows. Original upstream workflow copies are retained as inactive `.disabled` files in `.github/upstream-workflows/`. Do not reactivate them or add hosted automation without a new user decision. The `.ci` folder name is retained for local test tools; it does not require or imply hosted CI.

Do not install the fork into the vanilla source, alter its saves/settings, or modify Steam launch options or its Proton prefix without approval. Use the dev copy for references and testing. It is outside Steam's managed installation and was not registered as a Steam shortcut or Steam Cloud destination. Never commit or redistribute the copied proprietary game files.

## Isolated dev launch

```bash
~/Games/KSP-kOSIM-dev/launch-dev.sh --check  # preflight; does not launch the game
~/Games/KSP-kOSIM-dev/launch-dev.sh
```

The executable launcher lives in the dev instance, resolves its own working directory, and explicitly selects:

- Proton: `$HOME/.local/share/Steam/compatibilitytools.d/GE-Proton11-7-x86_64`.
- UMU: installed `umu-run` 1.4.4, with `GAMEID=umu-default` (no automatic game-specific protonfixes).
- Dedicated compatibility-data directory: `~/Games/KSP-kOSIM-dev/.proton-prefix`; initialized on first launch, with effective Wine prefix `.proton-prefix/pfx/`. The vanilla `steamapps/compatdata/220200` prefix is not reused.
- Proton logs: `~/Games/KSP-kOSIM-dev/dev-logs`; game logs/saves remain in the dev instance.
- `UMU_RUNTIME_UPDATE=0` by default to avoid automatic updates of the installed UMU runtime.

Dependency preflight and shell syntax passed. **First launch opened a visible game window and loaded kOS assemblies/parts.** Process environment confirmed dev-only compatibility-data/install paths and working directory. Logs: instance-local `KSP.log`, `dev-logs/launch-20261011-205701.log`, and `dev-logs/steam-default.log`. The launcher warned `unable to use parent for game drive, path /home`, but KSP started; no workaround or runtime change was made. This startup smoke test does not prove standalone UMU behaves identically to Steam or validate terminal gameplay.

Use `KOSIM_PROTON_PATH=/path/to/another/installed/Proton` or `KOSIM_RUNTIME_UPDATE=1` only deliberately, and record changes. Do not launch the copied Windows executable directly with host Wine or silently substitute another Proton version.

On first launch, create a new disposable sandbox save, e.g. `kOSIM-dev`. Do not import existing career saves. Instance-local `setup-notes.md` records provenance and initial state.

## Three distinct validation layers

1. **Headless tests on the Linux host:** fast checks against real `kOS.Safe` models, run using host Mono. These are not tests under Proton or Unity.
2. **Full mod compilation:** compile against the installed Windows KSP managed assemblies using the host SDK. Successful linking is not proof of runtime behavior.
3. **Game checks under Proton:** verify Unity rendering, keyboard/mouse/focus, clipboard/fonts, file behavior, telnet, power/connectivity, and lifecycle in the actual target runtime.

The in-game target remains `net48`. SDK 8 is a build tool, not a migration to modern .NET in-game; KSP uses its bundled Unity runtime.

## Local headless regression tests

Prerequisites on the Linux host:

- .NET SDK 8.0.x (the `dotnet` runtime alone is not enough).
- Mono.
- Python 3.8+ and Bash.
- Network access to NuGet for the first restore; this is dependency retrieval, not hosted testing.

This machine has a user-cache SDK. For the current setup:

```bash
export PATH="$HOME/.cache/kosim-tools/dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
bash .ci/test.sh
```

The script can also be invoked by absolute path from another directory. If Python is named `python` rather than `python3`, use `PYTHON=python bash .ci/test.sh`.

Equivalent explicit commands:

```bash
python3 -m unittest discover -s .ci -p 'test_*.py' -v
dotnet msbuild src/kOS.Safe.Test/kOS.Safe.Test.csproj \
  -restore -target:RunHeadlessTests -property:KOSHeadless=true \
  -property:Configuration=Release -property:PythonExecutable=python3 -nologo
```

`KOSHeadless=true` excludes KSPBuildTools from the two safe projects and uses standard .NET Framework references plus the repository's existing SharpZipLib DLL. It excludes game integration/packaging targets rather than inventing a fake KSP installation. Use it for the safe test project, **not** for the full mod/release build.

NUnit 2.7.1 and NSubstitute 1.8.1 remain unchanged. The pinned NUnit.Runners 2.7.1 package supplies a compatible console runner. A build-output copy uses `.ci/nunit-console.exe.config` to select CLR 4 without modifying the NuGet cache. Generic host-selection logic is retained in the harness, but no native Windows test run is promised or required for the current scope.

Do not substitute `dotnet test` and assume it executes these legacy NUnit tests.

## Baseline and local regression gate

At upstream commit `8f281a459b6ea0ba917c91bbbc117e68bb948199`, the unchanged suite executed **302 tests: 299 passed, 3 failed, 0 skipped** on Linux/Mono. The three failures are:

- `kOS.Safe.Test.Collections.LexiconTest.CanFormatNumericKeys`
- `kOS.Safe.Test.Collections.MixedCollectionPrintingTest.CanPrintListInLexicon`
- `kOS.Safe.Test.Serialization.TerminalFormatterTest.CanSerializeLexicons`

**All three are now fixed (FMT-01).** They exposed a real, user-visible upstream regression rather than stale expectations.

- **Cause.** Upstream commit `21dcea00` ("Fixes #2824", in every release since v1.3.0.0) made `PrimitiveStructure` an `IDumper` so `WRITEJSON` could accept a bare scalar. `SafeSerializationMgr.DumpValue()` checks `IDumper` before primitives, so nested numbers, strings and booleans became `{"value": …}` sub-dumps.
- **Effect.** In terminal display (`PRINT` of a list or lexicon, and `:DUMP`, which use the type-less dump), every nested primitive printed as an empty `[i] = ` line followed by `["value"] = …`. KSP-KOS/KOS issue #2939 (May 2021) shows exactly this in real game output for a `GETMODULE` print.
- **Fix.** Type-less dumps return nested primitives inline. Typed dumps are untouched: JSON files, inter-CPU/vessel messages and saved message queues keep the kOS 1.3+ wrapped format byte-for-byte. Every non-display caller already used `includeType = true`.
- **Tests.** `PrimitiveDisplayTest` adds 7 regressions: inline list, module-field and nested-collection printing; a pinned JSON format; bare-scalar JSON (#2824); loading pre-1.3 raw-primitive JSON; and typed dumps keeping wrapped primitives. With the fix disabled as a temporary mutation, the 3 original tests and the 3 new display tests failed while the JSON and compatibility tests passed.
- **Not checked.** The in-game terminal output has not been checked under Proton.

The fork adds **13 character-grid compatibility tests**, **26 overlay regression tests**, **27 dimension regressions**, **23 cursor-read regressions**, **34 render-recovery regressions**, **48 editor document regressions**, **50 archive-save regressions**, **62 raw I/O/newline regressions**, **83 telnet regressions**, **7 display-formatting regressions**, **33 compatibility-fixture tests**, **23 terminal-type regressions** (NET-03), **4 compile-error regressions** (EXEC-01), **92 telnet decoding/queue/budget tests** (review NET-03: 22 decoder tests plus 70 queue, pipeline, pump, recording, prompt and cost tests) **5 long-input-line regressions** (PROMPT-01), then 30 for prompt-level Ctrl-C and the INPUT-01 policy (gate 862), **21** for PANEL-02, **8** for WIDGET-01, **11** for WIDGET-02, **23** for IO-05, **27** for IO-04 and **20** for the INPUT-01 mouse policy, **10** for MOVE-01 and **10** for PATH-01, so the current suite executes **992 tests: all 992 pass, 0 skipped**. The local gate has **12 additional Python unit tests**.

`.ci/known-test-failures.json` records exact known failing names (currently none), expected assertion-message fragments, reasons, and the minimum current test count. **No test is ignored or filtered out.** The raw NUnit process exits nonzero for any failure. The local regression gate tolerates only the recorded baseline and rejects:

- New failing tests or changed assertion messages for a known failure.
- Errors, invalid/inconclusive/unexecuted tests, and inconsistent XML counters.
- Empty/missing/malformed reports, duplicate names, or a count below the recorded minimum.
- A runner exit status inconsistent with the reported failures.
- A formerly known failure that now passes, until investigated and removed from the baseline.

With an empty baseline, a passing gate now means every executed test passed. If known failures are ever recorded again, they are printed locally and retained in `artifacts/tests/Release/nunit.xml` (ignored by Git). There are no report uploads or GitHub summaries. Resolve known failures in focused follow-ups; do not expand the baseline to silence regressions. Maintain the minimum count as tests are added; it detects count drops, not every substitution of one test for another.

The runner deletes the previous report before execution so a stale report cannot masquerade as a new run. Run the local suite after code/test-tooling changes and before considering work complete or publishing a build; record commands, counts, failures, and unperformed checks in `docs/HANDOFF.md`. Local-only testing relies on this discipline rather than a remote merge gate.

### Failure-propagation verification

Locally verified with a temporary NUnit test `CiFailureProbe.IntentionalFailure` calling `Assert.Fail`: 316 executed during the final probe, raw runner exit 4, and test-command exit 1 because the new failure was not in the baseline. The source was removed and the normal suite rebuilt afterward. Gate unit tests cover new/changed/fixed failures, invalid results, and runner crashes. The probe's historical name does not imply hosted execution.

If rechecking this path, use a temporary test, confirm a nonzero exit, remove it, and rerun the suite. Never commit an intentional failing probe or add it to the baseline.

## Full mod build against the dev instance

The ignored repository-local `kOS.props.user` now sets the dev game root/Windows managed path and disables CKAN dependency installation. From the repository root:

```bash
export PATH="$HOME/.cache/kosim-tools/dotnet:$PATH"
dotnet build kOS.sln -c Release
```

This command was verified against the dev instance: **0 errors, 44 existing warnings**. The explicit equivalent, useful without that local configuration, is:

```bash
export PATH="$HOME/.cache/kosim-tools/dotnet:$PATH"
KSP_ROOT="$HOME/Games/KSP-kOSIM-dev"
dotnet build kOS.sln -c Release \
  -p:KSPBT_GameRoot="$KSP_ROOT" \
  -p:KSPBT_ManagedPath="$KSP_ROOT/KSP_x64_Data/Managed" \
  -p:KSPBT_InstallCKANDependencies=false
```

Do not pass `KOSHeadless` here. `kOS.props.user` is local/ignored and case-sensitive on Linux; recreate it or use the explicit flags in a fresh checkout. Use `KSP_x64_Data/Managed`, not the native-Linux `KSP_Data` path. Never commit proprietary game assemblies.

Normal outputs are workspace DLLs/version metadata under `Resources/GameData/kOS`, **not automatic deployment**. The overlay-only build was explicitly deployed before first launch. The newer dimension/cursor/GUI-cleanup build remains in the repository; it has not been deployed. The user has deferred further Proton checks. No other mods are installed, including ModuleManager; its patches are currently inactive. To deploy a chosen build after local tests pass, from the repository root:

```bash
rsync -a Resources/GameData/kOS/ "$HOME/Games/KSP-kOSIM-dev/GameData/kOS/"
```

Run this only with the dev game closed. It copies the complete mod assets/binaries, not just DLLs, and does not delete local settings. For later file removals, review stale files separately rather than blindly deleting the destination. Do not deploy into the vanilla Steam GameData or add unrelated mods automatically.

Earlier full builds succeeded against stripped KSP 1.12.5 references stored outside the repository at `$HOME/.cache/kosim-tools/ksp-1.12.5-stripped`, with 0 errors and 44 existing warnings on a fresh build. Those results remain useful historical evidence, but do not validate the installed game or Proton behavior. The dev copy's actual game references are now the default for the current target.

## Validation status and remaining coverage

| Layer | Current evidence |
| --- | --- |
| Host: Arch Linux, SDK 8.0.425, Mono 6.12.0, Python 3.14.7 | Headless suite and gate tested locally |
| Stripped KSP 1.12.5 references | Full Release solution build succeeds locally; historical reference-only check |
| Dev instance copied from Steam Windows KSP | Copy checksum-verified; full Release build against its assemblies succeeds |
| Isolated UMU/GE-Proton11-7 launcher | First launch initialized dev prefix; visible KSP window, process isolation verified |
| KSP under Proton | 1.12.5.3190; kOS loads; user-guided GUI long-input/resize responsiveness, alignment, and command output pass; no matching logged faults; 2026-10-12 checkpoint of `e943ecf4`: telnet checklist passed with real inetutils clients, apart from a 1×1-size runaway with two clients (known edge case) and the unexercised output-overflow path; 2026-10-19 pass of the Phase 1 fixes (see "Proton pass 2026-10-19"): Ctrl-C, MOVE-01, IO-05/PATH-01, IO-04 images, WIDGET-01/02, PANEL-02 and most INPUT-01 keyboard/mouse checks passed, three defects fixed (GAMEEVENTS-01, EXEC-02, IO-04 link deletes under Wine refused); name tags and ClickThroughBlocker passed; 2026-10-15: prompt (P1–P3), dimension/cursor and raw I/O/archive-save checks passed over telnet with GUI screenshots; editor E1–E12, GUI G1–G3, boot seeding B1, render-fault recovery R1–R4 (temporary injection build) and telnet output overflow T7d-3 passed |
| Other game platforms/versions | Outside current validation scope |

Initial terminal fixtures cover default dimensions, addressed writes, cell overwrites, newline/wrap behavior, scrollback/clamping after a clear, clear/reset, grid growth, notifier registration/removal, fixed overlays, deep-copy snapshots, and empty snapshots. They use real models, not the integration-test screen stub. Overlay regressions now cover partial/full clipping above and below, overlays spanning both boundaries, extreme integer positions, fixed versus scrollback-relative placement, disabled/empty/wiped/shortened overlays, layer order, row-reference preservation, and long command input across width changes. A seed-3100 reference test exercises 200 compositions with resize and scroll. Dimension regressions now cover zero/negative/excessive requests, effective no-op identity/timestamps/notifications, normalized callbacks, safe writing after shrinking, negative long-input anchors, overlay width propagation, script suffix bounds, and 100 seeded resize requests (seed 3104). Cursor reads now have 23 headless regressions; actual Unity GUI group/color cleanup, fault recovery, and lifecycle checks still require runtime validation.

### Proton pass 2026-10-19 (Phase 1 fixes)

KSP 1.12.5.3190 (WindowsPlayer x64), GE-Proton11-7-x86_64 via UMU 1.4.4, isolated dev prefix, save `kOSIM-dev`, `test-vessel` on the pad. **A second CPU was added to the save** (a copy of the radial kOS part, `persistentId 80427999`, no boot file; backup `saves/kOSIM-dev/persistent.sfs.before-2cpu-20261019`), for the two-disk checks. Builds: `df2c5f0e` first (exposed GAMEEVENTS-01), then `df2c5f0e` + GAMEEVENTS-01 (`kOS.dll 18f7ba00…`, `kOS.Safe.dll ec0a66d5…`) for most checks, then the final build with EXEC-02 (`kOS.dll c54a276a…`, `kOS.Safe.dll 3f97f5bb…`) for the last check. The deployed GameData from before is backed up in `kOS-GameData-backups/kOS-before-df2c5f0e-20261019-132530` (it holds `e943ecf4`). Logs: `dev-logs/{Player,KSP}.log.gameevents-nre-20261019`, `…pass-20261019-run2`.

**Method.** Scripted checks over telnet with a scratch client (`kt.py`: RFC 1143 negotiation, a minimal VT screen model, steps `cpu`/`type`/`ctrlc`/`up`/`until`/`screen`); scripts and fixtures in the dev archive under `Ships/Script/kosim-check/p2/` (disposable; symlink targets in `kosim-link-target/` beside the game). Lock state read from screenshots (`grim` on the game's monitor) of KSP's Alt+F12 *Input Locks* window. Mouse and keyboard steps by the user, who reported deviations only (none). **Note:** pointer warps by the compositor (`hyprctl … cursor.move`) do not reach the game; only real mouse movement does, so mouse checks need the user.

**Defects found and fixed in this pass** (compiled into the final build; both headless gates 992/992, full Release build 0 errors, 44 warnings):

- **GAMEEVENTS-01 (INPUT-01 game adapters, would have broken every terminal):** `KOSInputOwnership.Instance` and `KOSMouseBlocking.Instance` added *static* methods to KSP `GameEvents`; KSP's `EvtDelegate` constructor reads the handler's target and throws a `NullReferenceException` for a static method. The first terminal's `TermWindow` constructor failed (that CPU disappeared from the telnet menu, 909 NREs from `TelnetOutputUpdate` within a minute), and the hide-UI/show-UI/scene-change hooks were never registered. Fix: both register instance methods of a small `GameEventHooks` object (as upstream `ConnectivityManager` does with `RefreshEventSingleton`). The kOS game assembly has no headless coverage; verified in game (no exceptions; both CPUs listed; locks below).
- **EXEC-02 (regression from EXEC-01 + decision 4):** a `RUNPATH` whose compile failed showed "Index was out of range" (and in the headless suite an `ArgumentNullException`) instead of its compile error. The threaded detector's error path calls `BreakExecution(false)`, which cancels every yield, including itself: `Cancel()` re-entered `IsFinished()` and cleared the stored error (rethrown as null), and `CPU.IsYielding()` then removed the failed yield by a stale index. Fix: the detector takes and clears its error before breaking; `IsYielding` removes yields by reference and skips indices the check removed. `CompileErrorTest.FailedRunpathCompileStillReportsOnce` now asserts the reported error's type (it only counted errors); it fails with either half of the fix reverted. In game (final build): a parse error and a `CLOBBERBUILTINS` error from `RUNPATH` each reported once, prompt free.

**Passed:**

| Item | Check (telnet unless noted) | Result |
| --- | --- | --- |
| Ctrl-C (decision 4) | `wait 5.` + Ctrl-C | prompt back in 0.5 s, rest of line not run |
| | `compile` of a 40,000-line file (≈10 s) cancelled after 2 s | prompt back after 6.5 s (waits for the thread), no KSM |
| | `runpath` of it cancelled during its compile | "Program aborted.", program not run, prompt after 6.8 s |
| | normal compile, then `runpath` of the KSM; Up after Ctrl-C | KSM written and runs; first Up gives the empty entry, second the command (as upstream) |
| | GUI terminal (user): `wait 5.` and the compile cancelled, Up twice | as above; no `g.ksm` written |
| MOVE-01 | archive under Proton: `movepath` `a.txt`→`A.txt`, `A.txt`→its directory, `a.txt`→its directory | each refused ("Can't move onto itself"), file kept; ordinary rename and back work |
| IO-05 / PATH-01 | `io05.ks`: two CPUs, both disks renamed `data`; volume objects, file objects, `processor` volume, `switch to 2.` + relative paths, kept `path()` and its `:VOLUME`, `log`/`compile` to a kept PATH, `data:/` string | 13/13 PASS |
| IO-04 images | GUI label `IMAGE`: 32 px and 4096×1 PNG, `img/../img/ok` accepted; 4097×1, 1×4097 PNG, 4097×2 JPEG, 16 MiB + 1 file, `../kosim-outside` (real file outside the archive) | refused with the documented script errors |
| IO-04 links | read through a directory link, a link inside a directory and a file link; `list` through a link; write through a directory link | all work |
| WIDGET-01 | popup `VALUE` (vector options): by text, same value again, by identity, missing | one `ONCHANGE` per change, none when unchanged, label follows, missing → index −1 and empty label |
| WIDGET-02 | user clicks with `EXTRADELAY` 2 and 0 while a long `WHEN` trigger runs | every `ONCLICK` ran outside the trigger |
| PANEL-02 | user typed 54100 in the toolbar panel, Enter, telnet off/on | server listened on 54100 and accepted a client; restored to 5410 via `CONFIG` |
| INPUT-01 keyboard | `DOEVENT "Open Terminal"` from a script | terminal opened, no `kOSTerminal` lock (screenshot) |
| | user: focus by click; click outside (SAS key works); typed `t` not SAS; editor and terminal focus cycling, editor `.`, Exit/discard; F2 and back; `m` typed vs map; Toggle Power (lock released, click does not refocus); part-menu Open Terminal and Close; toolbar open; `SET TARGET` while focused | as expected (no deviation reported) |
| INPUT-01 mouse | `kOSMouseOver` held while the real pointer was over a terminal, released when it left (screenshots) | pass |
| | user: wheel over terminal/script GUI/toolbar panel; camera drag crossing a terminal; terminal drag past the edge and resize; script GUI popup menu over the scene; map clicks under a terminal; F2 over a terminal; `SET TARGET` while hovering; Space Center and Tracking Station (panel wheel); scene changes | as expected; no kOS lock left after the scene changes (screenshot) |

**IO-04 links under Wine (found in this pass; fixed by refusal, user decision 2026-10-19: a delete must remove the link, never its target).** Wine reports a Unix *directory* symlink as a reparse point but a *file* symlink as the file it points to (attributes, file index and final path are the target's; `wine_get_unix_file_name` returns the target's Unix path). Deleting a link cannot be done safely from the game: `DeleteFileW` on a file link deletes the **target** (also when opened with `FILE_FLAG_OPEN_REPARSE_POINT | DELETE_ON_CLOSE`), and `RemoveDirectory` on a directory link fails with "access denied". Before the fix, `DELETEPATH` and `MOVEPATH` of a file link deleted its target (the link was left dangling; Wine then lists nothing), and a directory containing a link was deleted up to the link. The upstream code has the same file-link behavior under Proton (not compared in game).

Fix: `LinkSafeDelete.LinksCanBeDeleted` is false under Wine (`ntdll` exports `wine_get_version`). `Archive.Delete`, and `VolumeManager.Move` for an archive source before copying, refuse an entry that is or contains a link, with "Can't delete <path>: it is or contains a link, and under Wine (Proton) deleting a link would delete what it points to. Remove the link outside the game." Nothing is deleted or copied. Under Wine, an entry counts as a link if it is a reparse point, or if its Unix path is not its parent's Unix path plus its own name. A name that differs only in case counts as a link when an entry with exactly that name exists, which covers a link to a sibling named in a different case. An unmappable path also counts as a link (refusing is safe). On Windows and on Mono the link is still deleted itself, as before. `Archive.Clear` (tests only) throws at the first link under Wine. Headless: 4 tests simulate Wine through an internal override (`ArchiveLinkAndImageTest.UnderWine…`); they catch a missing delete check (2 failures), a missing move pre-check (1) and a non-recursive search (1). The Wine branch itself cannot run headless: the real `LinkSafeDelete` was run under GE-Proton11-7's Wine in a throwaway prefix (scratch probe). It found file links (also through a differently cased path), directory links, links deeper in a folder, a link to a case-only sibling and an archive folder reached through a link, and `DeleteFile` on a file link threw and kept the target. In game (`kOS.dll b61668f1…`, `kOS.Safe.dll f674a24d…`): `DELETEPATH` of a file link (two casings), of a directory link and of a directory holding links, and `MOVEPATH` of a file link were each refused with every target kept. Deleting a plain file through a differently cased path, a plain move and the delete of a directory without links worked. Links were still read through.

**Name tags and ClickThroughBlocker (user, after installing ModuleManager 4.2.3, ClickThroughBlocker 2.1.10.23, ToolbarController 0.1.9.14 and HarmonyKSP 2.2.1 in the dev instance only):** the in-flight name tag (lock while open), #2759 (name tag Accept/Cancel over a part in the VAB), and with CTB the keys `c`, `m`, `t`, `r` typed into a focused terminal and the mouse checks over kOS windows: all as expected. No conflict with CTB was seen, so INPUT-01 step 2 (CTB integration) is not needed for now. CTB's own focus mode setting was not varied.

**Not performed / pending:**

- CTB's focus mode setting (only its default was used); name tag in the SPH.
- Flight-results dialog, vessel destruction, F2 with `ObeyHideUI` off, editor confirm dialog with typing (only Exit/discard), baseline key leak of the editor popup on `e943ecf4`, a volume object kept across undocking, `EDIT` of a file object, the archive save onto a file link, two-client and stalled-client telnet.

### Proton checkpoint 2026-10-12

Build `e943ecf4` (deployment details in `docs/HANDOFF.md`), KSP 1.12.5.3190 (WindowsPlayer x64), GE-Proton11-7-x86_64 via UMU 1.4.4, isolated dev prefix, disposable save `kOSIM-dev` (vessel `test-vessel` on the pad). Telnet client: GNU inetutils `telnet` on the host, `TERM=xterm`, to 127.0.0.1:5410 (enabled in game with `SET CONFIG:TELNET TO TRUE.`).

Logs: KSP.log is buffered and lags behind, so each check is read in Unity's live `Player.log` (`.proton-prefix/pfx/drive_c/users/steamuser/AppData/LocalLow/Squad/Kerbal Space Program/Player.log`), with KSP.log checked as well once it has been written.

| # | Check | Result | Log evidence |
| --- | --- | --- | --- |
| T1 | Telnet welcome menu, attach to the CPU, mirrored screen, typed `PRINT` shown in telnet and GUI | Pass (user: no stray characters) | `TelnetMainServer started listening on 127.0.0.1 5410`, `got an incoming connection from 127.0.0.1`; no exceptions/errors |
| T2a | Typing with Backspace | Pass | none |
| T2b | Non-ASCII `héllo ñ ü € 日本` typed in inetutils telnet (no `-8`) | Pass: echoed and printed intact in telnet; also intact in the shared output read back by a second client | none |
| T2c | Enter on an empty prompt | Pass: nothing happens, same as upstream (user) | none |
| T2d | Multi-line paste with a blank line | Pass. User paste ran both lines. Scripted client (`ktel.py`-style raw socket, declines NAWS, terminal type `XTERM`) sent a 5-line paste with a blank line in one write, once with CR LF and once with CR NUL: a counter printed `n=2` both times, so each line ran once and the blank line neither lost nor added an Enter | none |
| T3a–e | Real client (inetutils): Left ×3 + insert, Home/End; Up/Down history; PageUp/PageDown scroll by 10; holding Left/Right on a long line; lone Escape, then Backspace and a command | Pass (user): expected outputs, no `[D`/`[C`/`^[` garbage, lone Escape did not eat or delay the following keys | no exceptions in Player.log |
| T4a–c | Real client: window made as small and as large as possible (foot, font zoom), back to normal, wrapped command | Pass (user): no garbled redraw; reported sizes not noted | none |
| T4-edge | Scripted client NAWS edge values while the user's client stayed attached to the same CPU | **FAIL — game slowed to ~0.1 fps.** Run 1 (19:58:02): 0×0 kept 80×24, 1×1 no command output, 300×200 → 255×160, 65535×65535 → 255×160, 80×24 OK. Run 2 (19:58:32): 1×1 and 2×2 no output, 15×3 OK, 80×24 no output. Run 3 (19:59:10): 1×1 output only two scroll codes; 80×24 repaint had clear+title but no rows; the command printed nothing in 3 s. 19:59:44: a new connection got `Connected to the kOS Terminal Server.` but no welcome menu in 9 s. `KSP_x64.exe` at ~260% CPU, RSS 14.9 → 16.7 GB in 20 s (fluctuating). User: game not frozen but ~0.1 fps. Killed with SIGTERM (user approved), exited in ~3 s; save `persistent.sfs` intact | Player.log and KSP.log: nothing but connection lines after 19:58 — no exception, no render-recovery diagnostic, no overflow notice. (The only exception of the session, a `KOSParseException` for `isdf` at 19:55:22, was a user typing slip during T3: a normal syntax error.) |
| T4-diag | Headless probe (temporary, real prompt via `ScriptHost`): resize 1×1, 2×2, 1×80, 24×1, then type and run the command | Model **not** the cause: every size typed and ran in ≤4 ms, no errors. Remaining suspects are game-only: GUI terminal drawing at a tiny size, telnet output (diff/repaint, xterm resize request `ESC[8;h;wt` sent to the *other* client), or their interaction. Not yet reproduced in game in isolation; upstream behavior not yet compared | — |
| T4-repro | Controlled reproduction, second and third launches (same build; background sampler of KSP RSS each second; logs kept in `dev-logs/*checkpoint-run{1,2,3}-20261012`, proxy capture in `dev-logs/proxy_*`) | See the findings below | no exceptions in any run |
| T5 | Ctrl-C with type-ahead (fourth launch, real client): `RUNPATH` of disposable `Ships/Script/kosim-check/waitlong.ks` (`wait 30.`), `PRINT "typed ahead".` typed during the wait, then Ctrl-C; then a new command | Pass (user): program stopped at once, neither the rest of the program nor the typed-ahead line ran, the prompt worked afterwards | `Breaking Execution Manually Contexts: 2`, context popped; no exceptions (only syntax errors for stray typed input) |
| T5e | Ctrl-C after `WAIT 5.` typed directly at the prompt | Known limitation confirmed in game (decision 4): not interrupted; the prompt returned after the wait | `Breaking Execution Manually Contexts: 1` logged, wait not aborted |
| T6a | Paste of one 6,017-character line (`print "<6000 chars>":length.`) into inetutils telnet in foot | Pass (user): ran, prompt fine, no freeze | none |
| T6b | Paste of 3,002 lines (158 KB, below the 262,144 backlog), each incrementing a counter and printing; last line prints the counter | Pass (user reported "ok" against the expected `PASTE DONE kosimp=3000`); frame rate stayed fine throughout | no overflow notice (expected), no exceptions; RSS flat ~3.66 GB |
| T6c | Same paste, Ctrl-C after a few seconds | Pass (user): output stopped, prompt worked | one `Breaking Execution Manually Contexts: 1`; no exceptions |
| T7a | Ctrl-D at an empty prompt, then re-attach | Pass (user): back to the welcome menu, re-attach repainted | none |
| T7b | Closing the foot window running an attached telnet | Pass: welcome menu (read by scripted client) shows 0 telnets on the CPU; no `telnet` process left | no errors, no "threads did not exit" |
| T7c | `kill -9` of the attached `telnet` process | Pass: 0 telnets afterwards | no errors |
| T7d | Client suspended (`Ctrl-]`, `z`) for ~30 s during sustained output (`kosim-check/spam.ks`, 200,000 `print`s), then `fg` | Session closed by kOS: telnet reported "Connection closed by foreign host" on resume. Frame rate fine while suspended (user). Cause: the keepalive check (`IsHung`: a terminal-type request each interval, client dropped if nothing came back by the next one), **same as upstream** `IsHung()`. Not a regression. The output-overflow → full-repaint path was therefore **not exercised**: a suspended client is dropped long before a normal diff rate fills the 262,144-character backlog; it needs a slow-but-answering client or a very high output rate. Not checked in game | no overflow log (consistent); script stopped by the user's Ctrl-C in the GUI terminal (`Breaking Execution Manually Contexts: 2`) |
| T9 (NET-03 fixture-found) | Scripted client reporting terminal type `ANSI`, `VT52`, `dumb` (upstream threw for all three on the reader thread and ended the session) | Pass: each stayed connected, attached, ran `PRINT` and answered; after Ctrl-D the redrawn menu showed the applied type (`ANSI`, `VT52`, `dumb`). Real Microsoft Telnet not available here | no exceptions |
| T8 | Two real inetutils clients attached to one CPU: menu count, typing in each shown in both and in the GUI, Ctrl-D on one while the other keeps working | Pass (user) | no exceptions |
| T10 | `SET CONFIG:TELNET TO FALSE.` in the GUI with one client attached and one at the menu; new connection refused; then `TRUE` and reconnect | Pass (user): both disconnected cleanly, game smooth, reconnect works after re-enabling | `TelnetMainServer stopped listening on 127.0.0.1 5410`, then `started listening`, new connection; no exceptions; RSS flat |
| T11a | Scene change FLIGHT → SPACECENTER with a session attached, menu without CPU, back to FLIGHT, re-attach | Pass (user): no hang, menu empty then lists the CPU again, attach and `PRINT` work | scene changes logged, `SceneChangeCleaner` ran; no exceptions |
| T11b | Quitting the game (main menu → Quit) with a session attached | Pass (user): clean exit | launcher exit 0; KSP.log ends with `TelnetMainServer stopped listening on 127.0.0.1 5410` and normal shutdown; no ERR/EXC |
| T3-split | Keys split across socket writes (scripted client): `PRINT "split abcdef`, two Left arrows each sent as `ESC`, `[`, `D` 20 ms apart, `X`, End as `ESC`,`[`,`4`,`~` 20 ms apart, `".` Enter | Pass: printed `split abcdXef`, no `[D`/`~` text. (Separate writes 20 ms apart on loopback; separate reads very likely but not proven at the socket level) | none |
| T5-early | Client closing its socket without detaching (scripted client, 3 times) | Pass so far: the welcome menu afterwards counts 1 telnet on the CPU (only the user's), so closed sessions are removed | connections logged; no errors. A kill/close of a real client is still to be checked (T5) |

**Session 2026-10-15 (fifth launch, same build, game confirmed closed and hashes rechecked before launch).** Driven by Claude over telnet with a scripted client (`ktel.py` + a minimal VT renderer handling CUP/SU/ED, since pyte lacks `ESC[S`), GUI checked by screenshots of the KSP window (`grim`, workspace 6). Input came through telnet, not GUI keystrokes; the screen model and prompt editor are shared. Player.log read from line 16685 on.

| # | Check | Result | Log evidence |
| --- | --- | --- | --- |
| P1 (PROMPT-01) | 50×36: loop printing `line 0`…`line 79`, then an 85-`a` `PRINT "…":LENGTH.` wrapped at the bottom | Pass: before Enter, `line 79` stays above the wrapped command in the telnet stream (two `ESC[S` then the command on the last rows) and in the GUI screenshot; prints `85` | none |
| P2 | 6,017-character line sent in one write into a 40×10 telnet session | Pass: ran, printed `6000`, prompt fine | none |
| P3 (EXEC-01) | `COMPILE "0:/kosim-check/bad.ks" TO "0:/kosim-check/bad.ksm".` at the prompt | Pass: syntax error reported once (one `Syntax:`/one verbose block), no KSM written, next command runs | one `KOSParseException` (expected). A first attempt without `0:` gave the expected `Can't find file '1:/…'` |
| G3a (BUFFER-04) | `SET TERMINAL:WIDTH TO 0. SET TERMINAL:HEIGHT TO -5.`; then 1000×1000; repeated same size | Pass: `15x3`, `255x160` (telnet got `ESC[8;3;255t`, `ESC[8;160;255t`), `same 60x30` | none |
| G3b | 130-character command typed, NAWS 20×5 then 70×20 while editing, Home, End, Enter | Pass: prints `130` | none |
| G3c | Width 20: `PRINT "edge" AT(18,2).`; `PRINT "far" AT(500,500).`; Up Up history | Pass, upstream semantics: `ed` written and the rest cut at the edge; out-of-range AT clamps to the last row/column (`MoveCursor`) and wraps; history recalls the right command | none |
| G3d (BUFFER-02) | 20×5, 110-character command: Home + Right ×3, screenshot; End, screenshot | Pass: at Home the input start is above the viewport and the GUI hides the cursor (no fault, view does not scroll to it); at End the cursor shows after `ENGTH.`; prints `110` | no range/GUI-clip errors |
| G5a (IO-01/02) | `kosim-check/io.ks`: READRAW/READALL lengths; COPYPATH archive→local→archive of all 256 bytes, CRLF text, BOM text, malformed UTF-8, CRLF `.ks`; MOVEPATH round trip; directory copy; WRITERAW twice; legacy READALL/CLEAR/WRITE of a CRLF file ×3; WRITEJSON twice; COMPILE + RUNPATH KSM; RUNPATH of the CRLF source from `1:` | Pass: all round trips byte-identical (host comparison), move source removed, `wr.bin` = 512 exact bytes, legacy rewrite stays 33 bytes (no CRCRLF growth), JSON saved with CRLF (Windows text branch active under Proton, as designed), KSM and CRLF source both ran (`value 42`). READRAW vs legacy READALL: crlf 33/29, `.ks` 88/85 | none |
| G5b (IO-03) | Archive destination made read-only on the host (`chmod 444`), WRITEJSON over it; then writable again and retry | Pass: `Access to the path is denied.` at the prompt, original bytes unchanged (sha256), no `.kosim-save-*` left; retry succeeded (`a=3`) | one `UnauthorizedAccess…` trace from `File.Replace` (expected) |

| E1 (EDIT-01/02) | GUI editor, same-length change (`one`→`ONE`), Exit | Pass (user): "edited, save?" dialog with Yes/No/Cancel; WASD blocked while open; Cancel keeps the draft | none |
| E2 | Exit → No, reopen | Pass: closes, reopened text is the saved one; file hash unchanged | none |
| E3 | Dirty A, `EDIT` B from the terminal, Cancel | Pass (user): dialog names both files correctly; editor blocked while the dialog is up; stays on A | none |
| E4 | Same, Yes | Pass: A saved (`TWO`), B loaded, B untouched. A was saved with CRLF (original LF): legacy Windows text save, active under Proton, as designed | none |
| E5 | `EDIT` a missing file, Exit without typing | Pass: opens empty, no file created | none |
| E6 | Archive file made read-only on the host, Save; then writable, Save again | Pass (user): `[File Save failed: Access to the path is denied.]` in the terminal, editor open, draft kept; file bytes unchanged, no `.kosim-save-*`; retry saved, Exit without prompt | `UnauthorizedAccessException` logged (expected) |
| E7 | Dirty draft, terminal Close, reopen | Pass (user): draft still there and still dirty (Exit prompts) | none |
| E8 | `EDIT` a directory | Pass (user): "cannot load … points to a directory" with Retry/Cancel; WASD works again after Cancel | none |
| E9 | Dirty archive A → `EDIT "1:/kio/crlf.txt"` → Yes; edit, Exit → Yes | Pass: A saved on the archive (CRLF stable, no CRCRLF), local file saved with the edit, CRLF kept (read back over telnet) | none |
| E10 | Dirty A → `EDIT` a directory → Yes | Pass: A saved first, then the directory error; after Cancel A stays open and clean | none |
| E11 | Dirty draft, CPU power off via Toggle Power, back on | Pass (user): terminal and editor gone while off, no orphan dialog, WASD works; draft back with the save dialog; nothing saved until the user chose Yes | none |
| E12 | Local disk filled over telnet to 8 bytes free, save a longer draft; free space, retry | Pass: `[File Save failed: File save failed for '1:/kio/crlf.txt'. Check space on device.]`, file unchanged (33 bytes), draft kept; retry saved (55 bytes) | none |

| G1 (BUFFER-01/04) | GUI keyboard: 70-character command typed, window dragged much narrower/wider/back while editing, End, Enter | Pass (user): clean rewrap, cursor aligned, printed `70`. Home not tested in the GUI (no Home key on the user's keyboard; covered over telnet, G3b/G3d) | none |
| G2 | Smallest window + largest font, `PRINT`, Reverse Screen, brightness, back to 12px/normal | Pass (user): no glitches, no stutter | none |
| G3 | F5 quicksave, `PRINT`, hold F9 quickload, reopen terminal, `PRINT`, WASD | Pass (user): no hang, terminal reopens and runs, flight controls not stuck | Player.log for the whole 2026-10-15 session: only expected script/file errors |

| FMT-01 | Over telnet: `PRINT` of a nested list/lexicon, a lexicon with a list, an empty list | Pass: primitives inline (`[0] = 1`, `["k"] = "v"`), nesting indented, no `["value"] = …` sub-dumps | none |

| B1 (IO-01/02) | CRLF boot script `0:/boot/kosim_boot.ks` (161 bytes) selected in the VAB, vessel launched; the script copies its seeded `1:` copy back to the archive | Pass: boot ran on the new disk (user); seeded copy byte-identical to the source, CRLF kept (`cmp`) | none |
| R1 (BUFFER-03) | **Temporary fault-injection build** (approved; `kOS.dll` sha256 `013b069c…`, never committed): `DrawTerminalGui` throws inside the clipping group while a visible row contains `KOSIM-FAULT-ON`; marker printed over telnet | Pass: fallback "Terminal display fault. See KSP.log; retrying automatically. CPU execution is unchanged." with Retry/Resize/Close (screenshot); rest of KSP UI drawn normally (groups/colors unwound); CPU kept running (telnet `PRINT` answered) | one report per ~10 s, each `4 failed attempts suppressed` (2-s retries); `InvalidOperationException: kOSIM injected row-draw fault` |
| R2 | During the fault: Retry ×n, Resize drag, move by title bar, WASD | Pass (user) | Retry did not reset the report budget (one batch said `6 … suppressed`) |
| R3 | `CLEARSCREEN` over telnet | Pass: GUI recovered by itself within ~3.5 s (screenshot), no further reports. (A first attempt did not clear because the user's ZQSD presses had gone into the focused terminal's input line, making `qzdCLEARSCREEN.`; correct behavior) | none after recovery |
| R4 | Fault again: Close in the fallback, flight keys unfocused, reopen from the toolbar, focus the fallback and press a flight key; clear | Pass (user): closes, vessel responds while unfocused, reopens in the fallback, **flight control locked while the fallback window is focused**; recovered after clear | none |
| T7d-2 | Client that stops reading but sends `IAC NOP` every 2 s, during `spam.ks` | Pass, by design: server send queue grew to ~1.2 MB (kernel buffers), then the 5-s send timeout (NET-01/02) dropped the client; the session was removed (menu 0 telnets), game unaffected, a new client could Ctrl-C the script. Earlier, a 26-s stall: Ctrl-C sent while stalled stopped the program at once | no log line for the timeout drop |
| T7d-3 | **Output overflow**: 254×160 client reading at ~60 KB/s with NOPs, `CONFIG:IPU` 2000 (restored to 200), `spam.ks` | Pass: `connection 1 is not keeping up; dropped unsent output and repainting (1 times, 253998 characters so far)` at ~15 s, then `(7 times, 1794675 …)` 10 s later (rate-limited log); client got full repaints (9 `ESC[2J`), screen rebuilt from the last one coherent; Ctrl-C ended the script; session alive. At 80×24 (~80 KB/s max: diffs at 20/s) or with a ~100 KB/s reader no overflow occurs | as described |

Minor findings (recorded only): **G3-NUL**, after a resize the telnet full repaint sends empty cells inside a row as NUL bytes (`ok after AT\x00…ed`); real terminals do not advance on NUL, so later text in that row shifts left in the client (GUI fine). Telnet rendering is unchanged by kOSIM, so probably upstream; not verified. **G3-UI**, at 20 columns the GUI window's bottom controls (font size, Close, toggles, "telnet client" label) overlap; cosmetic, probably upstream. **MENU-TYPE (upstream)**, the welcome menu header repaints only on a size change, not when the terminal type arrives, so a client whose size equals the 80×24 default keeps seeing `type = INITIAL_UNSET` (and a raw U+E002 command character before the type is known); the mapper itself is set correctly. Same in upstream's `TelnetSingletonServer`. Note for scripted clients: a NAWS width/height of 255 must be sent as `IAC IAC`; kOS discards the unescaped form as malformed (logged) and may lose the next typed characters.

**Not yet checked:** from group 5, an externally locked destination (needs a Windows-side lock) and third-party providers. Real PuTTY and Microsoft Telnet were not available.

#### T4 findings (open, recorded as a known edge case — no code changed)

Each step below was watched by the user (frame rate) and by the RSS sampler.

| Setup | Result |
| --- | --- |
| Scripted client alone, NAWS 1×1, command typed 20 s later | Healthy (≥60 fps, RSS flat). Telnet output at 1×1 is **only scroll codes, never the characters** (61 `ESC[S` for a 54-character echo plus 7-character output): a display defect of the telnet screen diff on a 1-row screen (finding T4-DISP; origin not yet compared with upstream). GUI terminal shows nothing at 1×1 |
| Old scripted client (answers every WILL with DO) alone, 80×24, 30 s | Healthy; command answered. That client and kOS ping-pong `WILL ECHO`/`WILL SGA` ↔ `DO` forever (~57 KB/s, ~10,000 round trips/s). Upstream `TelnetConsumeDo` has the same answer-every-DO rule, so this is upstream behavior with a non-RFC-1143 client; real clients do not loop (finding T4-NEG, low priority). All first-session scripted connections looped this way |
| Scripted compliant client, 255×160 | Healthy; answered `255x160` |
| Two scripted clients (A passive at 120×40, B resizing to 1×1 then typing), B compliant, then B looping | Healthy both times; menu works afterwards |
| **User's inetutils `telnet` in foot (121×60) attached through a recording `socat` proxy; scripted compliant B resizes to 1×1, types the command 20 s later** | **Reproduced.** RSS flat through the resize (+200 MB once), then from the moment B typed at the 1×1 prompt (20:35:01) RSS rose 3.9 → 6.1 GB in 40 s and frames slowed to about one per 0.3–0.4 s. Wire traffic stayed tiny: the real client sent 79 bytes in total (handshake, attach, terminal-type answers every 10 s); kOS sent it the same 61 scrolls as the solo case, one per slow frame. **No feedback loop on the wire: the runaway is inside the game** |
| First session (user attached, looping B: 0×0, 1×1, 300×200, 65535×65535, 80×24, commands typed after each) and its replay | Reproduced both times (RSS up to 16.7 GB and 9.4 GB; ~0.1 fps; user's client responding with several seconds of latency; no welcome menu for new connections) |

**Boundary check (fourth launch, user's client attached directly, scripted compliant client resizing and typing):** 15×3 (the script minimum) and 20×5 both answered, RSS flat for 15 s after typing, frame rate normal (user). **User decision: treat T4 as a known edge case** (telnet sizes below the script minimum, with a second real client attached) and continue the checklist; no old-build comparison or diagnostic build for now. T4-DISP and T4-NEG are recorded only.

So far: the trigger is typing at the prompt on a 1×1 screen while the user's real client is attached; the negotiation loop and large sizes are not needed. The headless prompt/model handle the same typing at 1×1 in milliseconds, and a scripted passive client did not trigger it, so the difference between that client and the real one (size 121×60 vs 120×40, attach timing, or something else) is not yet identified. Not yet compared with the previous (overlay-only) build or upstream.

### Compatibility fixtures (Phase 0)

These fixtures record what representative scripts, dashboards and telnet clients produce today, so later changes show up as diffs. They add no production change.

**Harness.** `src/kOS.Safe.Test/Execution/ScriptHost.cs` runs kerboscript the way the in-game terminal does: commands are typed key by key at the real `TextEditor` prompt line, echoed, compiled into the interpreter context and run by the real CPU. Output goes into the real `ScreenBuffer` model that the window and telnet draw. The archive is a disposable copy of `kerboscript_tests`. Game time moves only in fixed 0.02 s updates, so `WAIT` and triggers are deterministic. The older `BaseIntegrationTest` stub (one string per `Print` call, `PRINT AT` coordinates ignored, no grid) is unchanged.

Game-assembly pieces are replaced by small test adapters, all in `ScriptHost.cs`:

- `FixtureInterpreter`: a copy of the game's `kOS.Screen.Interpreter` on the real `TextEditor`, with the same Enter handling, input lock and cursor-preserving `PRINT AT`. History navigation keys are left out. **Keep it in step with `src/kOS/Screen/Interpreter.cs`.**
- `FixtureBindingManager`: only `TERMINAL`, backed by the real `TerminalStruct`.
- `FixtureFunctionClearScreen`: `CLEARSCREEN` clears the same model as `TermWindow.ClearScreen()`, without the telnet notification.
- `RecordingLogger`: records the script errors that the game prints. Fixtures fail on any error.
- `Opcode.InitMachineCodeData()`/`CompiledObject.InitTypeData()` are called as `kOSProcessor.OnAwake()` does. Without them, KSM packing throws.

**Fixtures** (`kerboscript_tests/fixtures/`, expected output in `fixtures/expected/`):

| Fixture | Covers | Tests |
| --- | --- | --- |
| `printing.ks` | scalars, strings, booleans, lists, lexicons, nesting, queue/stack/range/set; locks in FMT-01 | source + KSM, both newline modes |
| `functions.ks` | parameters/defaults, recursion, closures, anonymous functions, `:BIND`/`:CALL`, built-in delegates, locks | source + KSM, both newline modes |
| `triggers.ks` | `WHEN`/`THEN` with `PRESERVE`, `ON` with `RETURN TRUE`, `WAIT 0`, `WAIT n`, `WAIT UNTIL`, `FROM` loops | source + KSM, both newline modes |
| `fileio.ks` | create/write/`WRITELN`/`READALL`/iterator, `LOG`, `COPYPATH`/`MOVEPATH`/`DELETEPATH`, JSON round trip, `READRAW`/`WRITERAW`, `RUNPATH`/`RUNONCEPATH` with parameters, directory listing | source + KSM, both newline modes |
| `boot/boot_crlf.ks` | boot file with CRLF line endings (multi-line string literal included), run by the real `CPU.Boot()` boot path from source and from KSM | both newline modes |
| `dashboard/hud.ks` | `PRINT AT` layout, shorter overwrites, wrapping past the right edge, plain `PRINT` scrolling followed by a redraw, `CLEARSCREEN`, resize watcher on window/telnet-side and script-side resizes | 15 full-screen snapshots |

- **Source vs compiled.** Each script is compiled with the `COMPILE` command and the KSM is run with `RUNPATH`. KSM output must equal source output and the recording.
- **Newline modes.** Each script runs with the managed Windows flag off and on (test-only reflection, as in the IO-01/02 tests). The game under Proton takes the Windows branch, where text saves add CRs. Only `fileio` differs: a 3-line file is 29 bytes on Unix and 32 on Windows (`fileio.windows.out`). Archive reads always fold CRLF, so the boot script's CRLF string literal reads back as LF in both modes.
- **Recorded quirks, kept as-is:** `PRINT ""` adds no line; `ROUND(2.5)` is 2 (banker's rounding); `RUNONCEPATH` skips a file already run by `RUNPATH`; because `PRINT AT` restores the cursor, a later plain `PRINT` writes from where the cursor was, over dashboard text; `OFF` written over `ARMED` leaves `OFFED`.
- **Re-recording.** Run with `KOSIM_RECORD_FIXTURES=1` to rewrite expected files and traces. Recording tests always fail, so a recording run never passes as a check. Record twice when Unix output changes, because a `.windows.out` file is kept only while it differs from the Unix one. Review the diff before committing. `.gitattributes` keeps `boot_crlf.ks` byte-exact, and a test fails if its CRLFs are lost.

**Telnet recordings** (`src/kOS.Safe.Test/UserIO/Recordings/`; each `read:` line is one socket read in hex, and each `.trace` file is the expected server-side result, including kOS's negotiation replies and clamped sizes):

- `inetutils-xterm`: **captured live** from GNU inetutils telnet 2.8, `TERM=xterm`, 80x24, against a loopback listener sending kOS's connect sequence and replies. Enter arrives as `CR NUL` and the terminal type as `XTERM`.
- `putty-xterm` and `windows-telnet-ansi`: **reconstructed, not captured**, because neither client is installed here. They follow PuTTY's telnet backend defaults (active negotiation, `XTERM`, CR LF, DEL, keepalive NOP, UTF-8) and Microsoft Telnet's (passive, `ANSI` then `VT100`, 120x30, CR LF, BS). They include a split UTF-8 character, a 300-column NAWS clamped to 255, `IAC IP` with type-ahead, and a terminal-type change mid-input. Replace them with live captures when those clients are available.

Each recording is replayed as recorded, at every split point inside each read, byte at a time, and in 150 seeded random chunkings (seeds 3, 17, 2026). Fragmented replays report more data available until the end of each original read, so the break-discard policy sees the same reads. After adjacent text is joined, all replays must match the trace. Since the review NET-03 work, the same reads also go through the server's input side to the keys kOS reads (`.keys` files), which every chunking must reproduce. The game's `TelnetSingletonServer`, terminal mappers and welcome menu remain untested; the tests use copies of their input logic.

**Evidence.**

- Mutations, each reverted:
  - Disabling the FMT-01 display fix failed 16 script-fixture tests: every printed list/lexicon, from source and KSM, in both modes.
  - Making `ScreenBuffer` wrapping ignore the cursor column failed the dashboard snapshot test at the wrap step.
- Stability: the 33 fixture tests passed 15 consecutive runs, and 8 more with every core busy. Script fixtures wait for the background compile thread with 1 ms sleeps, bounded by the update limit.
- Full gate: 708 executed, 708 passed.

**Defects found** (both since fixed; see the next two sections):

- **NET-03, short telnet terminal types.** `TerminalUnicodeMapper.GuessTypeId()` calls `Substring(0,5)` and `Substring(0,4)`, so any terminal type shorter than 5 characters throws `ArgumentOutOfRangeException`. That includes `ANSI` (Microsoft Telnet's first answer), `VT52`, `VTNT`, `dumb` and the empty string. The exception happens in `ClientTerminalType`'s setter on the reader thread, and with NET-01/02 a worker fault closes the session. The unchanged mapper files, compiled into a scratch Mono probe, threw for all five strings, while `XTERM`, `xterm-256color` and `VT100` mapped normally. This is upstream code, and the parser and dispatcher deliver `ANSI` correctly (see the Windows trace). It was not reproduced with a real Windows client or in the game.
- **EXEC-01, repeating compile error at the prompt.** When `COMPILE` is typed at the prompt on a file with a syntax error (or any other compile-thread failure), the CPU reports the error **on every update**. Headless: 198 errors in 200 updates. Ctrl-C does not stop it, and the prompt never runs another command until reboot. Cause: `YiedFinishedThreadedDetector.IsFinished()` rethrows the stored exception without clearing it, and the yield stays in the CPU's list, because `BreakExecution(false)` only aborts yields when a program context exists. `COMPILE` yields from the interpreter context (`RUNPATH`/`RUN` push a program context and are not affected). `throw childException` also discards the original stack trace. This is upstream code unchanged since 2017. The in-game flood is inferred from the game logger printing each error, not observed.

**Remaining manual coverage (cannot run headlessly):**

- Save and craft loading.
- Real KSP types: vessel, part and module suffixes, flight bindings.
- Public mod-facing APIs against real KSP assemblies.
- The game `Interpreter`/`TermWindow` key routing (prompt versus script input, history navigation, paste, interrupts).
- Terminal mappers and `TelnetSingletonServer` with real clients.
- Fonts and rendering of these dashboards in the Unity window.

These stay on the Proton checklist. A fixture is a model of the terminal, not proof of in-game rendering.

### Telnet key decoding and bounded telnet queues (review NET-03)

This is the review's NET-03 ("bound queues and update work"), together with a fix for keys split across socket reads. It is separate from the fixture-found NET-03 below.

**Upstream behavior.**

- The terminal mappers converted each socket read on its own. A key sequence split between reads (`ESC [` | `A`) arrived as typed text, and a `CR` | `LF` split gave two Enters. Within one read, `CR LF CR LF` gave one Enter, so a blank line was lost.
- Both telnet queues were unbounded. `TermWindow.ProcessTelnetInput()` drained every client completely each update, and `ProcessUnconsumedInput()` fed the prompt with no limit, so a large paste or a noisy client could take a whole frame. Output kept growing behind a client that stopped reading.

**Changes** (new classes in `kOS.Safe/UserIO`; the game classes adapt them):

- `TerminalInputDecoder` (`TerminalInputDialect` Plain/VT100/Xterm): the mappers' input mappings, with state kept between reads. An unfinished sequence is held until no input has arrived for 100 ms, then passed on as typed, so a lone Escape still arrives. `TerminalTypeNames.InputDialectFor()` picks the dialect the same way the mapper factory picks a mapper.
- `TelnetInputQueue`: thread-safe and bounded (262,144 characters). An interrupt discards the text typed before it and is read first. A resize keeps only the latest size, is read first and its three characters are never split. A repaint request is read first. When full, typed text is discarded until the backlog drains to half. The start and end of each overflow are reported once, with the number of characters lost.
- `TelnetOutputQueue`: bounded (262,144 characters). When a write would exceed it, the unsent backlog is dropped and the caller requests a full repaint. A single write is never split, and a `DIE` in dropped output still ends the session.
- `TelnetInputPipeline`: decoder plus queue for one client. A terminal-type change flushes the old decoder; an interrupt resets it.
- `TerminalInputPump`: `InputBudget` (characters and time per update) and the two per-update loops. `DrainTelnets` takes clients in turn (round robin), each with its own budget, and moves text only while the type-ahead is below its limit; control events are always processed. `FeedPrompt` is the `ProcessUnconsumedInput` loop with a budget.
- `TelnetSingletonServer` uses the queues and pipeline. Its public members keep their signatures, plus `TryReadChar(includeText, out ch)`, `InputBacklog`, `DiscardedInputCount`, `OutputBacklog` and `OutputOverflowCount`. Overflow is reported to the client (a bell and a `[kOS telnet: ...]` line) and to the log; output-overflow logging is limited to once per 10 s. `MainThreadUpdate()` runs the sequence timeout.
- `TermWindow` uses `DrainTelnets` and `FeedPrompt`. A repaint request also resends the title, and a full repaint restates the reverse-screen and visual-beep modes, because both may have been in dropped output.
- `TelnetWelcomeMenu` reads a limited number of keys per update, reprints on a repaint request, skips a resize's size characters, and no longer echoes other kOS key codes into the selection (upstream added them to it as garbage).
- The mappers' `InputConvert` uses a fresh decoder per call, so callers of that public method keep per-call results. The VT100/xterm overrides remain as thin delegations for binary compatibility; `ConvertVT100InputCSI` is kept but unused. The server no longer calls `InputConvert`.

**Limits** (code constants by user decision, not `CONFIG` settings):

| Limit | Value |
| --- | --- |
| Input backlog per client | 262,144 characters; after overflow, input resumes at half |
| Output backlog per client | 262,144 characters, plus at most one write |
| Telnet keys moved per client per update | 4,096 and 4 ms (the first key always) |
| Type-ahead below which telnet text is moved | 4,096 keys |
| Type-ahead keys fed to the prompt per update | 2,048 and 4 ms |
| Unfinished key sequence timeout | 100 ms |
| Welcome menu keys per update | 4,096 |

**Intentional behavior changes:**

- Keys split across reads decode as one key. Each CR or LF is one Enter; only its partner directly after it (or NUL after CR) is absorbed, so blank lines in pastes are kept.
- Interrupts, resizes and repaint requests overtake typed text that is still waiting. An interrupt discards everything typed before it (the terminal's type-ahead was already wiped by an interrupt).
- Only the latest of several unread resizes is applied.
- A large paste is typed over several updates. Typed text beyond the 262,144-character backlog is discarded, with a notice to the client and the log, rather than queued without limit.
- Output a stalled client could not take is replaced by a full repaint. Non-screen output in the dropped part is lost: the title is resent, modes are restated, and the welcome menu reprints itself.
- Artifacts that are not real keys decode differently: `ESC [ A ~`, and an ESC directly before U+009B.

Unchanged: the key mappings themselves, including keeping a lone ESC and dropping the ESC/U+009B of an unrecognized `ESC [` sequence. Unrecognized sequences such as function keys and Ctrl+arrows are still typed as text, and SS3 (`ESC O A`) is not decoded, as upstream; the user decided to keep both.

**Tests** (75 new, plus the 22 decoder tests from the previous session):

- `TelnetInputQueueTest` (17): order; controls with text held back; repaint and break priority and coalescing; break discarding only earlier text (protocol and typed Ctrl-C); resize coalescing, priority and an unsplit triple; overflow start, hysteresis, notices once, episodes reported together, break ending an overflow; statistics; argument checks; a threaded producer/consumer stress test that checks order, unsplit resizes and that every character is read, dropped or discarded.
- `TelnetOutputQueueTest` (8): no drop within capacity; drop and repaint request; oversized single write; DIE in dropped and sent output; statistics; three concurrent writers with no split or unaccounted write.
- `TelnetInputPipelineTest` (16): dialect per terminal type; split key; flush only after the timeout, counted from the latest text; a late LF after a timed-out CR; type switch flushing (or not, for the same dialect); break resetting the decoder.
- `TerminalInputPumpTest` (14): character and time budgets (injected clock); per-client budgets; turns; a noisy client not starving a quiet one; full type-ahead holding text but not controls; controls after the budget; `FeedPrompt` stopping when a command starts, on a refused key, at its budget and after a wipe.
- `TelnetRecordingTest` (+7): the three recordings through the parser, dispatcher, pipeline and queue (`TelnetServerInputModel`, a copy of the server's input side) to the keys kOS reads, checked against recorded `.keys` files; every split, byte-at-a-time and 150 seeded chunkings give the same keys.
- `TelnetPromptTest` (6, real prompt through `ScriptHost` and `FixtureTermWindow`, a copy of `TermWindow`'s input routing): a multi-line paste with every CR LF split across reads runs each line once, whether all reads arrive before the first update or one per update; the same reads through the upstream conversion give 7 Enters instead of 4; an interrupt during a 33,000-key paste (a program in `WAIT 1000`, 4,096 keys in the type-ahead, the rest in the telnet queue) aborts the program in one update and discards both, after which the prompt works; a resize is applied before a waiting backlog; a 10,000-character pasted line runs within the per-update limits.
- `TelnetPasteCostTest` (2): the measurement below, asserting only the deterministic bounds.

Keep `TelnetServerInputModel` in step with `TelnetSingletonServer`'s `ITelnetInputSink` methods, and `FixtureTermWindow` with `TermWindow`'s `ProcessTelnetInput`, `ProcessUnconsumedInput`, `ProcessOneInputChar`, `Type`, `SpecialKey` and `WipeAllTypeAhead`.

**Defect fixed while testing.** If two overflow episodes ended before the game thread reported them, the second episode's count replaced the first's, so the client was told too few characters were lost. The counts now add up.

**Headless frame-cost proxy** (`TelnetPasteCostTest`): wall time per update of the window's input work plus the CPU update, the paste sent in 1,460-byte reads. "No limits" removes all four limits, which approximates upstream's loops. Linux Mono 6.12, Ryzen 9 5900X; four runs of the long line and three of the 3,000 lines (those with `KOSIM_PASTE_LINES=3000`; the suite uses 500 lines):

| Paste | Limits | Updates | Worst update | Total |
| --- | --- | --- | --- | --- |
| one 6,018-character line | none | 1 | 368–404 ms | 368–404 ms |
| one 6,018-character line | game | 90–98 | 5.1–5.3 ms | 368–400 ms |
| 3,000 lines (37,890 characters) | none | 3,000 | 9.3–10.6 ms | 4.2–4.7 s |
| 3,000 lines (37,890 characters) | game | 3,000 | 7.2–7.8 ms | 4.0–4.7 s |

- Typing at the prompt costs about O(n²) in the line length (the input line is re-wrapped per key), so a long pasted line used to stall one frame. It is now spread over updates at no extra total cost.
- A many-line paste already ran one line per update, because the prompt stops at each Enter while the command runs. The worst update there is mostly compiling and running one line, which a per-key budget cannot pre-empt; the limits mainly keep the type-ahead short.
- The worst update in the 3,000-line case grows with the scrollback (2.5 ms at 500 lines). Not investigated.
- This is not frame time in the game. Real frame time needs KSP.

**Mutations**, each reverted (files checked byte-identical afterwards); every one failed tests:

| Mutation | Failing tests |
| --- | --- |
| Break keeps earlier text | 9, including the end-to-end interrupt test |
| Text read before controls | 10 |
| No overflow hysteresis | 1 |
| Overflow counts overwritten | 1 |
| Output never dropped | 3 |
| Dropped DIE forgotten | 1 |
| Flush ignores the timeout | 2 |
| Per-read decoding (flush after every read) | 8, including the recording chunk test |
| Type-ahead limit ignored | 2, including the end-to-end interrupt test |
| Telnet budget ignored | 4 |
| Prompt budget ignored | 3, including both end-to-end long-line tests |
| No round robin | 2 |
| PROMPT-01 reverted | 7 (5 errors, 2 failures) |

**Known limits:**

- Text beyond the input backlog is discarded with a notice rather than back-pressured, so an interrupt is never stuck behind a full queue. A Ctrl-C typed after more than about 262K unread characters still arrives promptly, because the reader keeps reading.
- Several clients share one terminal type-ahead. Two clients pasting at once can now interleave within a line (upstream interleaved at read boundaries). Fairness between clients is tested only with the pump's unit tests.
- A control key read while text is allowed uses one unit of that update's budget.
- Ctrl-C does not end a `WAIT` (or `COMPILE`) typed directly at the prompt, telnet or GUI: `CPU.BreakExecution` aborts yields only when a program context exists. Upstream behavior, confirmed headlessly; the user decided to fix it later in its own task. Inside a program, Ctrl-C works (tested above).
- Typed characters in the kOS private-use range still act as kOS key codes (pre-existing).
- `TelnetSingletonServer`, `TermWindow`, the welcome menu and the mappers are compiled, not run; the tests use copies of their input logic.

#### Review NET-03 acceptance tests

| Review acceptance test | Status |
| --- | --- |
| Sustained input | Headless: threaded queue stress test; 3,000-line paste |
| Large paste | Headless: long line and many lines on the real prompt, with measured per-update cost |
| Slow receiver | Headless: output queue drop and repaint request, DIE kept. Deferred: a stalled real client under Proton |
| Multiple clients | Headless: per-client budgets and turns (unit). Deferred: several real clients on one CPU |
| Interrupt during backlog | Headless: real prompt, one update, backlog discarded |
| Fairness across clients | Headless: noisy client cannot starve a quiet one (unit) |
| Queue-full behavior | Headless: overflow, hysteresis, notices, break during overflow |
| Measure frame-time impact | Headless proxy above. Deferred: in-game frame time |
| Configurable limits | Not done by user decision: code constants |

**When Proton resumes**, in addition to the NET-01/02 checklist below: paste a few thousand lines and one very long line into an attached telnet session and watch frame rate; Ctrl-C during that paste; hold an arrow key and press Escape alone (the 100 ms timeout); suspend a client (Ctrl-Z in `telnet`) during sustained output, resume it and check it repaints; two clients on one CPU; check the overflow notices and KSP.log lines.

### Long input line at the prompt (PROMPT-01, fixture-found)

Found by the long-paste test above. It affects the GUI terminal as well as telnet.

**Upstream behavior.** When typing wrapped the input line past the bottom row, `TextEditor.KeepCursorInBounds()` moved the line up by the rows to scroll, then scrolled, then added the new rows. Scrolling stops at the end of the buffer, so it came up one row short and the line sat one row too high:

- A command that wrapped at the bottom of the screen overwrote the last row of output. Headless: after printing `line 0` … `line 39`, a 76-character command replaced `line 39`.
- A line taller than the screen, typed from the top row, was anchored above the buffer, so Enter threw `ArgumentOutOfRangeException` in `ScreenBuffer.PrintLine` and the command never ran. Headless: 2,000 characters on the default 50x36 screen.

`TextEditor` and these `ScreenBuffer` paths are unchanged from upstream.

**Fix.** Add the rows before moving and scrolling. Nothing else changes.

**Tests.** `LongInputLineTest` (5, real prompt): the last output row survives a wrapped command; lines of 1,800, 2,000 and 5,000 characters run and the prompt works afterwards; six wrapped commands on a 5-row screen keep all their output. All existing overlay and dimension tests pass unchanged. Reverting the fix fails 7 tests.

**Not checked in game.** On the Proton checklist: type a command longer than one row at the bottom of a full screen and check the row above; paste a line longer than the screen.

### Short telnet terminal types (NET-03, fixture-found)

This NET-03 label comes from the fixtures. It is not the review's NET-03 (queue budgets and backpressure in `improvements-overview.md`), which is described in its own section above.

**Fix.** The prefix checks moved into `kOS.Safe.UserIO.TerminalTypeNames` (`IsXterm`, `IsVT100`), which use `StartsWith(…, OrdinalIgnoreCase)` and accept `null`. `TerminalUnicodeMapper.GuessTypeId()` calls them, so its signature and results are unchanged for every string upstream handled. Short types such as `ANSI`, `VT52`, `VTNT`, `dumb` and the empty string now get the plain `TerminalUnicodeMapper`, as any other unknown type does. A bare `vt10` is now VT100 (upstream threw). Matching is ordinal rather than current-culture; the prefixes contain no culture-sensitive letters. `ANSI` mapping is still not implemented (`TerminalAnsiMapper` is unused, as upstream).

**Tests.** `TerminalTypeNamesTest` (23): short, empty and null types; known types against the upstream algorithm, kept as an oracle; 2000 seeded strings (seed 3103) agreeing with upstream wherever upstream did not throw. Mutation: restoring the `Substring` checks failed 11 tests.

**Limits.** The game mapper and `TelnetSingletonServer` are compiled, not tested. A Windows telnet client has not been run against kOS; on the Proton checklist, connect with a client that reports `ANSI` and confirm the session stays open.

### Failed COMPILE at the prompt (EXEC-01, fixture-found)

**Fix.**

- `CPU.IsYielding()` removes a yield whose `IsFinished()` throws, then rethrows (`throw;`). Any detector that fails is reported once, rather than every update.
- `YiedFinishedThreadedDetector.IsFinished()` clears its stored exception and thread before rethrowing, returns `true` afterwards, and rethrows with `ExceptionDispatchInfo`, so the logged trace still shows where the compile failed (`ThreadExecute`/`ThreadFinish`).

Error text, exception types and the existing `BreakExecution(false)` call are unchanged. `RUNPATH`/`RUN` already removed the yield through the program context's abort and behave as before.

**Tests.** `CompileErrorTest` (4, real prompt through `ScriptHost`): a failed `COMPILE` reports one error, 200 more updates add none, the next command runs and no KSM is written; a later `COMPILE` and run of a valid file work; the logged trace contains `ThreadExecute`; a failing `RUNPATH` still reports once.

Mutations, each reverted:

- With both production changes reverted, the three `COMPILE` tests timed out with about 20,000 errors each. The `RUNPATH` test passed.
- With the CPU change kept and the detector using `throw error`, only the stack-trace test failed.

**Limits.** Not checked in game. Ctrl-C at the prompt still does not cancel a `COMPILE` that is running (upstream behavior, from code reading: a manual break at the interpreter does not abort yields).

### Ctrl-C at the prompt (decision 4)

**Defect (upstream).** A manual break with only the interpreter context (a command typed at the prompt) skipped the rest of the typed line but kept the CPU's yields, so Ctrl-C did not end `WAIT` or `COMPILE … TO …` typed at the prompt (confirmed in game, T5e). Two related upstream races, found by the new tests: Ctrl-C during a `RUNPATH` compile freed the prompt at once while the compile thread kept running, and the Ctrl-C handler itself parsed and compiled the emptied line on the game thread. The compiler (`KSScript`: one scanner, parser and context table per CPU) is not thread-safe, so either could compile at the same time as a running compile thread.

**Fix (user decision: cancel, then wait for the thread).**

- `CPU.BreakExecution(true)` at the prompt now ends the yields too, through `CancelAllYields()`, which also replaces `AbortAllYields()` in the program branch. `WAIT`, `GETCHAR` and next-tick yields end at once.
- A compile still running on its thread cannot be stopped: `YiedFinishedThreadedDetector.Cancel()` marks it and it stays in the yield list, so the CPU and the prompt wait for the thread (normally well under a second). When the thread ends, `ThreadFinish()` is skipped and any error dropped: no KSM written, no program parts added, nothing printed. A thread that already ended is discarded at once. `ThreadCancelled()` (new virtual hook) stops the compile stopwatch, as `ThreadFinish()` does.
- `CPU.IsFinishingCancelledCompile` (public property on `CPU`, not on `ICpu`, so external `ICpu` implementations are unaffected) is checked by `Interpreter.IsWaitingForCommand()`: typed keys stay queued as type-ahead until the cancelled thread ends.
- The game `Interpreter`'s Ctrl-C handler still does what Enter does to the screen and history (the empty history entry is kept, as upstream) but no longer parses or compiles the emptied line. Compiling `""` only appended an empty `pushscope`/`popscope` pair (probe, removed), so nothing else changes without a communications delay. **Intentional change under a comm delay:** `ConnectivityInterpreter` no longer queues and later "deploys" an empty command after Ctrl-C (in batch mode it no longer adds an empty line). Enter on an empty prompt is unchanged.
- Inside programs the visible behavior is unchanged ("Program aborted.", context popped), except that the prompt now waits for a still-running `RUNPATH` compile thread instead of accepting commands at once.

**Tests.** `Execution/PromptBreakTest` (7) on the real CPU and prompt: `WAIT` typed at the prompt ends on Ctrl-C (the rest of the line does not run); Ctrl-C at an idle prompt; a held `COMPILE` cancelled (prompt busy until the thread ends, no KSM, no overlap); a cancelled compile of a broken file reports nothing; an uninterrupted compile still writes and runs; Ctrl-C during a held `RUNPATH` compile (prompt busy, "Program aborted.", program not run, no overlap); a second Ctrl-C while waiting. `ScriptHost` now uses `GatedScript`, the real `KSScript` with a gate that holds background (non-interpreter) compiles and records overlapping `Compile` calls. `FixtureInterpreter` mirrors the game `Interpreter`'s `NewLine`/Ctrl-C/`IsWaitingForCommand` changes (**keep in step**). Before the fix 5 of the 7 failed (WAIT not ended; KSM written; error reported; prompt free during the `RUNPATH` compile). Mutations, each reverted, all failing the gate: no yield cancel at the prompt; cancelled thread still finishing; prompt not gated; Ctrl-C compiling the emptied line (overlap); running compile dropped instead of kept. Gate: 839/839, `minimum_tests` 832 → 839; full Release build 0 errors, 44 existing warnings.

**In game 2026-10-19: passed** over telnet and in the GUI (see "Proton pass 2026-10-19"); the pass also found and fixed EXEC-02 (a failed `RUNPATH` compile reported an index error). Original note: the game `Interpreter`/`ConnectivityInterpreter` changes are compiled only; needs a Proton check (Ctrl-C on `WAIT 5.` and on a slow `COMPILE` in the GUI and over telnet, then a normal `COMPILE`, history Up after Ctrl-C). A thread that finished but was not yet seen is discarded by `Cancel()`; that path has no dedicated test (it needs thread timing).

### Input ownership policy (INPUT-01, headless part)

`kOS.Safe.UserIO.InputOwnership` holds the agreed keyboard-ownership policy (`docs/KOSIm-roadmap.md`, "INPUT-01 design") with no Unity or KSP state. Windows register (with or without a control lock; script GUI windows take none) and report user opens, unfocused opens (action group, script), clicks, closes, power, suspension (hidden UI or flight-results dialog) and removal. It answers which window has the keyboard and applies lock changes to an `IInputLockSink` at once, unlocks before locks so the terminals' shared `kOSTerminal` lock name is handed over cleanly. A refused change is reported and retried by the next call; while an unlock is refused no new lock is taken. Removal and `ReleaseAll()` are idempotent. **Not wired into the game yet**: the Unity adapter (`TermWindow`, `KOSManagedWindow`, `KOSTextEditPopup`, `KOSNameTagWindow`, `kOSProcessor` scripted-open flag) is the next step, and only then do in-game checks apply.

**Tests.** `UserIO/InputOwnershipTest` (23): user and unfocused opens, focus transfer with a shared lock name (unlock before lock), clicks outside and on closed windows, a lockless GUI window taking the keyboard, close, suspension and return (including a user open or a close during it), power loss for good, removal and `ReleaseAll` twice, no redundant sink calls, refused lock/unlock retried, removal despite a refused unlock, null arguments. Mutations, each reverted and each failing the gate: suspension ignored (3 failures), power loss keeping focus (1), lock regardless of `TakesLock` (1), every open taking focus (7), a click on a closed window focusing it (1), locking despite a refused unlock (1), removal keeping focus (2). Gate 862/862, `minimum_tests` 839 → 862.

### Input ownership in the game (INPUT-01, game adapter)

`kOS.Screen.KOSInputOwnership` holds the one shared `InputOwnership` (all vessels and CPUs share one keyboard) with a sink that sets and removes `ControlTypes.All` under `kOSTerminal` (`TermWindow.CONTROL_LOCKOUT`) for every lock-taking window: only one window owns the keyboard, and kOS's targeting workaround (`VesselUtils`) and other mods look that name up. It tracks hide-UI (with `ObeyHideUI`) and `FlightResultsDialog` as suspension, retries refused lock changes each frame (first failure logged), and releases everything on `onGameSceneLoadRequested`.

Wiring: `KOSManagedWindow` registers each window on first use (re-registering if `OptOutOfControlLocking` changes, as `GUIWindow` does in `Awake`), routes `Open(bool takeFocus)`, `Close`, `IsOpen`, `IsPowered`, clicks (`GetFocus`/`LoseFocus`; a click outside releases only from the window that had focus) and `OnDestroy` to the policy, and no longer unfocuses other windows in `BringToFront`. `Open()` keeps its signature and means a user's open. `TermWindow` reads keys and blinks its cursor only while `HasKeyboard`; its own `Lock`/`Unlock` and second per-window lock are gone. `KOSTextEditPopup` is frozen without the keyboard (as losing focus did) and now takes the lock while focused; `DelegateDialog` gives it the keyboard back after a choice. `KOSNameTagWindow` takes the lock in flight until it closes (the VAB/SPH keeps its `EditorLogic` lock). `PartModuleFields` marks `DOEVENT`/`DOACTION` as scripted; `kOSProcessor` opens unfocused from an action group or a scripted event; `GUIWidgets.Show` opens unfocused.

**Intentional behavior changes** (agreed policy): an action group, `DOEVENT "Open Terminal"`, `DOACTION` or `GUI:SHOW` no longer takes the keyboard and flight controls; hidden UI and the flight-results dialog release the keyboard and locks and give them back after; power loss releases for good and an unpowered terminal cannot be focused by a click (its input was ignored anyway); the editor popup (also in the VAB/SPH) and the in-flight name-tag window lock all controls while focused, the name tag until Enter/Accept/Cancel. Known limit: an event delayed by RemoteTech runs outside the scripted mark and still takes focus.

**Verification:** compiled only. `dotnet build kOS.sln -c Release --no-incremental` 0 errors, 44 warnings (kOS.dll `573e8720…`, kOS.Safe.dll `790fe880…`); gate 862/862 (the policy tests; the Unity adapter has no headless coverage). **In game 2026-10-19:** passed (name tags after installing ModuleManager) except the items listed as not performed in "Proton pass 2026-10-19" (that pass also fixed GAMEEVENTS-01, which broke the first terminal). Original list: baseline on `e943ecf4` (do keys typed in the editor popup reach KSP: `.`/`,` warp, staging?); then on this build: focus cycling between terminals, editor and a GUI window; F2 while typing and back (with `ObeyHideUI` on and off); flight-results dialog; power loss and restore; action-group and boot-script `DOEVENT` open (no focus); part-menu and toolbar open (focus); editor confirm dialog then typing; map view; in-flight and VAB name tag; `SET TARGET` while a terminal is focused; vessel destruction; scene exit; no `kOSTerminal` lock left after each.

### Mouse over kOS windows (INPUT-01, mouse half)

**Finding (code map, 2026-10-15):** only a *focused* terminal or editor held a lock (`ControlTypes.All` as `kOSTerminal`, which is also the keyboard's); an unfocused terminal, the editor's confirm dialog, the toolbar panel, script GUIs (#2568 opt-out), the list picker and the telnet dialogs locked nothing, so a click, the wheel or a drag over them also reached the camera, map, targeting or the editor's part picking. The name tag uses up its mouse events and holds an `EditorLogic` lock in the VAB/SPH, yet #2759 (clicking Accept/Cancel picks the part under it) was reported; the cause was not reproduced here (suspected: the lock is lifted in `Close()` inside the click's OnGUI). #2659 is a key-ordering conflict with ClickThroughBlocker (upstream PR #2729, reverted): not addressed by this step.

**Policy (user decisions 2026-10-15):** kOS takes its own hover lock with no dependency (an optional ClickThroughBlocker integration is a later step, only if an in-game check with the user's CTB shows a conflict). While the mouse is over any drawn kOS window, the scene's *mouse* controls are locked under a separate lock name, `kOSMouseOver`: flight `CAMERACONTROLS | MAP_UI | TARGETING`; VAB/SPH `EDITOR_SOFT_LOCK | CAMERACONTROLS`; space center `CAMERACONTROLS | KSC_UI`; tracking station `CAMERACONTROLS | TRACKINGSTATION_UI | MAP_UI`; other scenes nothing. Never keys sent to the vessel, flight controls or staging; the keyboard lock (`kOSTerminal`, focus only) and the #2568 keyboard opt-out of script GUIs are unchanged. Modal: the editor stays frozen under its confirm dialog (as before), the terminal stays usable, and the hover lock keeps the dialog's clicks out of the scene.

`kOS.Safe.UserIO.MouseBlocking` (no Unity/KSP state): windows report the rectangle they drew each frame (`Show`); a window counts only if it drew this frame or the previous one, so one that stops drawing (closed, hidden with the UI, destroyed, a drawing fault) stops blocking by itself. The lock is wanted while the mouse is over a counted window; while a button is held, the press decides: one that started over a kOS window keeps the lock wherever it goes (moving or resizing a window), one that started on the scene never takes it (a camera drag crossing a window is not cut off). It is released `ReleaseDelayFrames` (2) frames after it was last wanted, so the scene does not see the mouse-up of a click that closed a window (#2759) or ended a drag. `ReleaseAll` (scene change) releases at once and forgets old drawings and presses. Refused lock changes are retried by the next update.

**Game side:** `kOS.Screen.KOSMouseBlocking` (one shared instance; KSP sink with the scene masks above; `onGameSceneLoadRequested` → `ReleaseAll`; first error logged) and `KOSMouseBlockingDriver` (`KSPAddon.Startup.EveryScene`, updates every frame from `Input.mousePosition` and buttons 0–2). `Show` also updates at once, so the lock is set in the OnGUI before the next frame's scene update. Reporting windows: `TermWindow`, `KOSTextEditPopup` and `GUIWindow` (via `KOSManagedWindow.BlockMouseOver`), a script GUI's popup menu, `DelegateDialog`, `KOSNameTagWindow` (every scene), the toolbar panel (`KOSToolbarWindow`), `ListPickerDialog`, and the three telnet permission dialogs. Each unregisters in `OnDestroy`. `VesselUtils.SetTarget` lifts and restores `kOSMouseOver` as it does `kOSTerminal` (it holds `TARGETING`), now in a `finally`.

**Intentional changes / side effects:** hovering any kOS window blocks the wheel, camera drags, map clicks and editor part picking under it; `CAMERACONTROLS` also covers KSP's camera *keys*, so they pause only while the mouse is over a kOS window. Upstream kOS had no lock for unfocused windows.

**Tests:** `UserIO/MouseBlockingTest` (20, one step per frame: update, then the windows that draw): nothing shown; hover locks from the next update and only once; outside every window; Unity `Rect.Contains` edges, empty and negative rects; release after exactly two frames, several updates in one frame; return within the delay without churn; a window that stops drawing; the #2759 sequence (press, release, window gone: still locked on the mouse-up frame and the next); a drag started on a window kept outside it until two frames after release; a drag started on the scene not taken over a window, a hover after release is; two windows; `Hide`; the latest rectangle; `ReleaseAll` (twice, old drawings and presses forgotten); `Unregister` (twice, null; `Show`/`Hide` on it throw); refused lock and unlock retried; null sink. **Mutations, reverted** (`mut_input01.sh`, scratch): no release delay (8 tests failing), one frame longer (8), press ownership ignored (3), press decided every frame (3), stale drawings kept (3), only this frame's drawing counted (14), `ReleaseAll` keeping drawings (1), `Hide` doing nothing (1), state set before the sink call (1), `Unregister` keeping the area (1), right edge inclusive (1). Equivalent, survives: `ReleaseAll` not clearing the press owner (the next press decides again because `buttonWasHeld` is cleared). The KSP adapter has no headless coverage.

**Verification:** gate 972/972, `minimum_tests` 952 → 972; `dotnet build kOS.sln -c Release --no-incremental` 0 errors, 44 warnings (GameData `kOS.dll` `743544b0…`, `kOS.Safe.dll` `0de9204f…`). **In game 2026-10-19:** passed without and with ClickThroughBlocker 2.1.10 (keys `c`/`m`/`t`/`r`, #2659), and #2759 in the VAB passed; no CTB integration needed for now. Original list (with and without the user's ClickThroughBlocker): wheel and camera drag over an unfocused terminal, editor, confirm dialog, script GUI, toolbar panel (flight, map, VAB/SPH, space center, tracking station); map clicks (maneuver nodes, targets) and editor part picking under a window; #2759: name tag Accept/Cancel over a part in the VAB/SPH and in flight; dragging and resizing a terminal off its edge; a camera drag crossing a window; script GUI popup menu over the scene; `SET TARGET` while hovering a terminal; F2 with the mouse over a window; scene changes with the mouse over a window; #2659 key `c` and other keys with CTB installed; no `kOSMouseOver` (or `kOSTerminal`) lock left after each (Alt+F12 input locks list).

### Settings panel integer fields (PANEL-02)

**Defect (upstream):** every integer field in the toolbar settings panel used `GUILayout.TextField(..., 4, ...)`, so a valid five-digit telnet port (10000–65535, e.g. 54100) could not be typed although `TPORT` allows up to 65535. **Fix:** `kOS.Safe.Encapsulation.Suffixes.ConfigIntInput` gives each field the length its key's range needs (port 5, terminal width/height 3, font size 2; a minus sign counts) and holds the unchanged parsing (spaces trimmed, empty reads as 0 so the field can be cleared while typing, invariant integer; anything else drops the typing). `KOSToolbarWindow.DrawConfigIntField` uses both. Commit-on-Return, revert-on-focus-loss and `ConfigKey` clamping are unchanged (PANEL-03 is a separate item), so committing an empty field still gives the key's minimum. **Tests:** `Structure/ConfigIntInputTest` (21): 1024, 5410, 54100, 65535 typed and committed; per-key lengths; 65536/99999/80/-5 clamped; empty and whitespace; spaces, `+` and leading zero; invalid text (`54a`, `5 410`, `5410.0`, `1,024`, `-`, overflow); null key. Mutations, reverted: fixed length 4 (4 failures), minus sign ignored (1). Gate 883/883, `minimum_tests` 862 → 883; full Release build 0 errors, 44 warnings. **In game 2026-10-19: passed.** Original check: type 54100 in the panel, Return, restart telnet, connect.

### Popup-menu selection by value (WIDGET-01)

**Defect (upstream):** `PopupMenu.Choose()` (`POPUPMENU:VALUE` setter) used the public `Index` property as its loop counter, and the setter schedules the `ONCHANGE` callback on every change. One selection therefore scheduled callbacks for every option it passed (choosing the last of three from the second: 0, 1, 2; a missing value: 1, 2, 3, 0, 1, 2, 3, -1 — including out-of-range indices), and the shown text was not updated (unlike the `INDEX` setter), so the label could differ from the value. **Fix:** `kOS.Safe.Encapsulation.OptionSearch.IndexOf` searches with a local index, keeping the matching rules (the same object first, then the first option whose shown text — its `OPTIONSUFFIX` — equals the value's, ordinal and case-sensitive, else -1); `Choose()` then sets `Index` once (at most one callback, none when unchanged) and the shown text to the chosen option, or empty when nothing matched. **Intentional change:** choosing a missing value now clears the label (upstream left the old text while `VALUE` returned empty). `#3168` (type conversion on `VALUE`) is a separate report and not addressed. **Tests:** `Collections/OptionSearchTest` (8): the upstream loop's notifications as an oracle; first/middle/last by identity; identity before an earlier equal label; first of duplicate labels, scalars by text; case sensitivity; the display function deciding the text; missing value, empty menu, null value; null arguments. Mutations, reverted: identity skipped (1 failure), case-insensitive text (1), missing gives 0 (2). Gate 891/891, `minimum_tests` 883 → 891; full Release build 0 errors, 44 warnings. The one-commit `Choose()` itself is Unity-side (compiled only); **In game 2026-10-19: passed** (vector options: a string `VALUE` still hits #3168). Original check: a GUI script setting `VALUE` with an `ONCHANGE` that prints, for present, current and missing values.

### Script GUI actions under communications delay (WIDGET-02)

**Defect (upstream):** `Widget.Communicate()` set `guiCaused` only while it scheduled an action. Without a delay the action ran inside that call, so widget callbacks (`ONCLICK`, `ONTOGGLE`, `ONCHANGE`, `ONRADIOCHANGE`, label text updaters) were scheduled as GUI callbacks (`CallbackOnce` priority; toggle/change/radio on a future update). With a delay (RemoteTech signal delay, or `GUI:EXTRADELAY`) the action ran later from `GUIWindow.KOSUpdate` with the flag already off, so the callback was scheduled as if the script had changed the value: `NoChange` priority, which the CPU fires regardless of the current priority (`CPU.ProcessTriggers`), so it could run inside a `WHEN`/`ON` trigger, the cooked-control update or another GUI callback, and toggle/change/radio ran at the next opcode instead of the next update. Also: the delay queue ran an action before removing it, so a throwing action stayed first and was retried every update (blocking the rest), and an earlier-timed action queued during delivery would have been removed undelivered; an exception inside `Communicate` left `guiCaused` set.

**Fix:** `kOS.Safe.Screen.GuiCause` marks each action with its origin (`Mark`), so it is GUI-caused whenever it is delivered; nesting and exceptions restore the previous state. `kOS.Safe.Screen.DelayedGuiActions` replaces `GUIWindow`'s list: time-then-arrival order, per-widget removal on dispose, delivery removes each action before running it (at most once; an exception propagates and the rest follow on the next update). `Widget.guiCaused` is now a read-only property over `GuiCause`; `GUIWindow` keeps its delay rules, overlay and connection check. **Unchanged:** behavior without a delay; script-made changes; the delay time and overlay. **Tests:** `Screen/GuiActionsTest` (11): the upstream flag losing the origin (oracle); GUI-caused with and without delay; script change not GUI-caused; nesting and exception restore; order and due-only delivery; exactly once; removal before delivery; a failing action not retried; an action queued during delivery; a delayed follow-up waiting; null arguments. Mutations, reverted: origin not carried (1 failure), no restore on exception (1), remove after running (1 error), unstable order (3), removal ignored (1). Gate 902/902, `minimum_tests` 891 → 902; full Release build 0 errors, 44 warnings. The `Widget`/`GUIWindow` wiring is compiled only; **In game 2026-10-19: passed.** Original check: a GUI with `SET gui:EXTRADELAY TO 2.` and an `ONCLICK`/`ONCHANGE` that prints, clicked while the script is in a long `WHEN` trigger, compared with no delay.

### Volume identity in path arguments (IO-05)

**Defect (upstream):** path functions turned a volume or file/directory argument into `name:/path` (`VolumeManager.GlobalPathFromObject`) and looked the name up again, which finds the *first* volume of that name. A path without a volume id (`"x.ks"`, `"/x.ks"`) was likewise resolved by the current volume's *name*. With two volumes named `data` (volumes 1 and 2):

- `copypath(open("2:/x.txt"), ...)` copied volume 1's file; a volume-2 object as destination wrote to volume 1; `deletepath`, `open`, `exists`, `cd`, `readjson`/`writejson` and `runpath` of volume-2 objects acted on volume 1.
- After `switch to 2.` or `cd(open("2:/d"))`, `open("x.txt")` and `copypath("x.txt", ...)` used volume 1.
- A file object from a disk no longer attached (e.g. after undocking) was looked up by name too: `movepath`/`deletepath` on it **deleted the other disk's same-named file** with no error.
- The subdirectory guard compared the paths' volume ids, so `copypath(open("2:/d"), "2:/d/sub")` (`data:` vs `2:`) copied a directory into itself, while copying between two different disks both named `data` was refused.

**Fix:** `VolumeArguments.GetVolumeFromObject(this IVolumeManager, object, GlobalPath)` (`src/kOS.Safe/Persistence/VolumeArguments.cs`) gives the volume a path argument names: a `Volume`/`VolumeItem`'s own volume, which must be one of the manager's volumes ("Volume not found: "data" is not attached to this processor" otherwise) and pass `CheckRange` (the archive out of reach in the game, as before through the id lookup); the current volume for a path without a volume id (same test as `GlobalPathFromString`); otherwise the id lookup as before. It replaces `GetVolumeFromPath` in `CD`, `COPYPATH`, `MOVEPATH`, `DELETEPATH`, `WRITEJSON`, `READJSON`, `EXISTS`, `OPEN`, `CREATE`, `CREATEDIR`, `RUN`, `LOAD` (`RUNPATH`/`RUNONCEPATH`/`COMPILE`) and the game's `EDIT`. New `VolumeManager.Copy`/`Move`/`CopyDirectory` overloads take the resolved volumes and never look the paths' ids up again; the old `GlobalPath` overloads resolve once and call them; the subdirectory guards compare volumes, not ids. `COPYPATH`/`MOVEPATH` use the new overloads when `VolumeMgr` is a `VolumeManager` (the game's `ConnectivityVolumeManager` and the headless one); another `IVolumeManager` keeps the old copy/move. `IVolumeManager`, `GlobalPathFromObject` and `PATH()` are unchanged.

**Unchanged:** strings and `PATH` values with a volume id (`"data:/x"` still names the first `data` volume; `"2:/x"` is exact); file vs directory destination rules; messages other than the new one. **Intentional changes:** a directory cannot be copied or moved into its own subdirectory through another spelling of the same volume (`archive:` vs `0:`, a name vs a number); a volume or file object of a detached disk now fails ("Volume not found") in every function above instead of using a same-named disk (an unnamed one already failed). **Not covered:** `PATH` values, `LOGFILE` and the `COMPILE` output path (fixed afterwards as PATH-01, see "PATH values keep their volume (PATH-01)"); `SWITCH TO` and `RENAME VOLUME` take a volume object directly (no name lookup) but do not check that it is attached (user: not pursued).

**Tests:** `Execution/VolumeIdentityTest` (21, real prompt/CPU, real `Harddisk`s named `data` beside the archive): file, directory and whole-volume objects as copy source; volume, file and directory objects as destination; copy and move between the two same-named disks; move (source deleted only on its disk); `deletepath`, `open`/`exists`, `writejson`/`readjson`, `cd` and `runpath` of volume-2 objects; relative and volume-less absolute paths after `switch to 2.`; string paths by name and by number (unchanged); a volume or file not in the manager fails with "Volume not found" and changes nothing (copy, move, delete); a volume renumbered by remove/re-add (docking) still names itself; copy into itself through an object refused. `VolumeEndpointRangeTest` (2): an object out of range fails; a string resolves through the manager. Before the copy/move fix 13 of its 14 script tests failed as described; with the helper reduced to the old name lookup 21 of the 23 tests fail (the two string-path tests pass). Mutations, reverted: object ignored (13 failures, 1 error, copy/move tests), no membership check (2), no range check (1), subdirectory guard by ids (1 failure, 1 error; the copy into itself ran ~140 s), no current-volume branch (2). The membership/range/guard mutations ran when the helper was a `VolumeManager` method, before it moved unchanged to the extension. Gate 925/925, `minimum_tests` 902 → 925; full Release build 0 errors, 44 warnings. **In game 2026-10-19: passed** for two processors, objects, `switch to` and relative paths; still unchecked: a volume object across undocking, `EDIT` of a file object. Original list: two processors whose disks share a name, `copypath(core:volume, ...)`/`processor(...):volume`, `switch to` the second one then relative paths, a volume object kept across undocking, `EDIT` of a file object (compiled only).

**Found while fixing IO-05 (upstream), fixed separately as MOVE-01:** `MOVEPATH` of a file onto itself deleted it and returned `True`; see "Move onto itself (MOVE-01)".

### PATH values keep their volume (PATH-01)

**Defect (upstream, the part IO-05 left):** a `PATH` value held only `name:/path`, so with two disks named `data` (volumes 1 and 2) `path()` after `switch to 2.`, `path(open("2:/x.txt"))` and `path(path("2:"):volume)` — and everything derived from them (`:PARENT`, `:ROOT`, `:COMBINE`, `:CHANGENAME`, `:CHANGEEXTENSION`) — named volume 1: `:VOLUME`, `open`, `exists`, `copypath`, `deletepath` and the other path functions acted on the first disk. `LOGFILE` (`LOG … TO`) and the `COMPILE … TO` output turned their argument into a string at once, so a PATH value, a file object (its *name*, relative to the current directory) or a relative name after `switch to 2.` also went to the wrong place. Shown red by the new tests before the fix (6 of the 8 PATH tests; the LOG/COMPILE tests by mutation).

**Fix:** `PathValue.OwnVolume` (new, additive): the volume a path was made from — the current volume for `path()` and a path without a volume id, a `Volume`/`VolumeItem`'s own volume, or the argument PATH value's — kept by every derived path. `VolumeArguments.OwnVolume(object)` (Volume, VolumeItem or PATH value) is used by `GetVolumeFromObject`, so every IO-05 path function now honours a PATH value's volume with the same checks (`CheckAttachedAndInRange`: attached, else "Volume not found: … is not attached", and in range). `:VOLUME` uses the kept volume with those checks. `LOGFILE` and the explicit `COMPILE` output pass the argument object through `GlobalPathFromObject`/`GetVolumeFromObject` (default `.ksm` output unchanged).

**Unchanged:** printing, comparing (`=`, lexicon keys) and saving a PATH value use `name:/path` only; a PATH value from a `"name:/…"` or `"2:/…"` string, and one loaded from JSON (format unchanged), is by name/number as before; `scriptpath()`. **Intentional changes:** a PATH value of a detached disk fails ("Volume not found") instead of using a same-named disk; a PATH value made before a `switch to` keeps its volume; `log … to` / `compile … to` a relative name use the current volume itself, and a file or directory object its own path and volume (upstream used the object's name relative to the current directory).

**Tests** (added to `Execution/VolumeIdentityTest`, now 31): `path()`, `path("rel")`, `path("/abs")` and `path():volume` after `switch to 2.` (printed as `data:/`); a path of a file object and of a volume; derived paths (`:PARENT`, `:ROOT`, `:COMBINE`, `:CHANGENAME`, `:CHANGEEXTENSION`, `path(p)`); `copypath`/`deletepath` of a kept path; `"data:/x"` and `"2:/x"` strings unchanged (also after a switch); a detached disk's path fails in `copypath` and `:VOLUME`; renumbering survives; a JSON-saved path is by name; `LOG` to a relative name after a switch, a kept path and a file object in a subdirectory; `COMPILE` to a relative name after a switch and to a kept path, then `runpath` of the result. **Mutations, reverted** (`mut_path.sh`, scratch): derived paths drop the volume (5 tests failing), `path()` keeps none (1), a relative string keeps none (1), path functions ignore a path's volume (8), `:VOLUME` by name (2), `:VOLUME` without the attach check (1), `LOGFILE` by name (1), `LOGFILE` path from the string (1, after making the file-object case a subdirectory file), `COMPILE` output by name (1). Gate 992/992, `minimum_tests` 982 → 992; full Release build 0 errors, 44 warnings. **In game 2026-10-19: passed.** Original check: `path()` after `switch to` a second processor's same-named disk, `log`/`compile` there.

### Move onto itself (MOVE-01)

**Defect (upstream):** `MOVEPATH` copies, then deletes the source. When the copy landed on the source itself, the file was rewritten with its own bytes and then deleted, and `MOVEPATH` returned `True`; a directory moved into its own parent was copied onto itself and then deleted with everything in it. Upstream's only guard compared the given paths ("subdirectory of itself"), so it missed: the same path (`movepath("0:/a.txt", "0:/a.txt")`), the file's own directory (`"0:/"`, a volume object), an alias (`archive:` for `0:`, a disk's name for its number), a relative path, a default extension (`"p"` opens `p.ks`), and another case on a disk (disks ignore case: `X.TXT` is `x.txt`; `1:/D` is `d`, so `movepath("1:/d", "1:/D")` went to `d/d`). Shown red by the new tests before the fix.

**Fix:** `VolumeManager.Move` (both overloads) finds the copy's target as `Copy` does (a directory destination receives the source's resolved name) and refuses, before copying, a target naming the source (`Can't move onto itself: …`) or, for a directory, a target inside it (the existing "subdirectory of itself" error), comparing the opened source's path. Names are compared exactly, then ignoring case: on a disk a case variant is the same entry; on the archive `Archive.NameSameEntryIgnoringCase` decides by the file system: two existing paths differing in case are the same entry unless both match the directory entries' names exactly (only possible on a case-sensitive file system), and a path that does not exist is another name. **Unchanged:** moves between volumes, ordinary moves and renames, `COPYPATH` onto itself (rewrites the same bytes; nothing lost). **Intentional changes:** these moves fail with a script error instead of deleting; a case-only rename on a disk (`x.txt` → `X.txt`) and, under Proton, on the archive is refused (upstream deleted the file). On a case-sensitive archive (native Linux) `a.txt` → `A.txt` stays an ordinary move.

**Tests:** `Execution/MoveOntoItselfTest` (10, real prompt, the archive and a disk named `data`): file onto itself, into its own directory (with and without the trailing `/`, disk root), through `archive:`, the disk name, objects and a relative path, by its default extension, another case on the disk, directory onto its parent (also by object and `..`), into its own subdirectory in another case, ordinary moves (rename, into a sibling, to the parent, extension default, across volumes), archive names differing in case are different files on Linux (rename and replace), `COPYPATH` onto itself unchanged. **Mutations, reverted** (`mut_move.sh`, scratch): guard not called (7 tests failing), exact names only (2), archive case variants always the same (1), prefix check removed (1), file into a directory not resolved (4), the given source path compared instead of the opened one (1). **Survives:** archive case variants never the same — the case-insensitive branch is reachable only on a case-insensitive file system (Proton), not headlessly. Gate 982/982, `minimum_tests` 972 → 982; full Release build 0 errors, 44 warnings. **In game 2026-10-19: passed.** Original check: under Proton, `movepath("0:/a.txt", "0:/A.txt")` and `movepath("0:/A.txt", "0:/")` with `a.txt` present must fail and keep the file.

### Archive containment, symlinks and image limits (IO-04 hardening)

**Finding:** the reviewed prefix escape does not apply as deployed (exit review), but both checks were text-prefix checks. `Widget.GetTexture` (script GUI images: a label's `IMAGE`, a style's `BG`) used `StartsWith(SafeHouse.ArchiveFolder)` and was safe only because that folder keeps its trailing separator; `Archive.GetArchivePath` trims a trailing `/` (on native Linux its root has none) and is safe only because `VolumePath` refuses `..`. `GetTexture` also checked the path *before* appending `.png`: for the archive folder itself (`"."`, `"sub/.."`), `Script` + `.png` names `Ships/Script.png` beside the archive; only the trailing separator kept upstream from accepting it. Links were followed everywhere, deletes included (`Directory.Delete(…, true)`), and images were read and decoded whatever their size.

**Containment:** `kOS.Safe.Persistence.DirectoryContainment` (`IsWithin`, `IsBelow`, `Combine`): both paths normalized with `Path.GetFullPath`, trailing separators trimmed (a file-system root keeps its own), then equal or followed by a separator. Ordinal on every platform: on a case-insensitive file system a differently cased path to the same place is refused, never the reverse. Lexical (links are not resolved). `ArchiveTextureFiles.Candidates` gives `GetTexture`'s files (`name.png`, then `name`), each strictly below the archive folder; `Archive.GetArchivePath` uses `IsWithin`.

**Symlink policy (user decision 2026-10-15):** links the player made in the archive are followed to read, write and list; a delete never enters one. `LinkSafeDelete` (a link is an entry with `FileAttributes.ReparsePoint`): `DELETEPATH` of a link removes the link; deleting a directory (also the source of a `MOVEPATH`) removes the links inside, not their targets; `Archive.Clear` (tests only) empties the folder and keeps it, so a linked archive folder stays a link. **Under Wine (2026-10-19)** a delete or move of an entry that is or contains a link is refused instead (see "Proton pass 2026-10-19"). **Recorded limit:** a save onto a *file* link (the IO-03 atomic replace) replaces the link with a regular file and leaves the target unchanged (Linux Mono; following it would need the link's target, which net48 cannot read). Directory links are followed for saves.

**Image limits (user decision 2026-10-15):** `ArchiveTextureFiles.ReadImage` refuses a file over 16 MiB (16,777,216 bytes) before reading it, and a PNG (`IHDR`) or JPEG (first frame header, past other segments and fill bytes) wider or taller than 4096 pixels before decoding (at most 64 MiB as RGBA); a script error (`KOSException`) names the path the script gave and the limit. Other contents are passed to the loader unchanged (it accepts PNG/JPEG only, as before). `GetTexture` uses it. The texture cache is unchanged (never evicts; not addressed).

**Unchanged:** which textures and archive paths are accepted in the deployed layouts; the path error (`KOSInvalidPathException`, "Path refers to parent directory"). **Intentional changes:** images over the limits fail; the texture path error names the script's path, not the resolved host path; `Archive.Clear` keeps the folder.

**Tests:** `Persistence/DirectoryContainmentTest` (13, real `Script` beside `Script-private`, no/one/two trailing separators): exact root, descendants, prefix-sharing sibling (`Script-private`, `Scriptx`, `Script.png`), `..` leaving and staying inside, rooted input, alternate separators (`\` a file-name character outside Windows), case, file-system root, nulls; texture candidates, the archive folder itself refused (a real `Script.png` beside it), outside refused, missing files left to the caller; `Archive` paths with both root forms. `ArchiveLinkTest` (8, real symlinks via libc `symlink`; the headless suite runs on Linux): a linked directory read, listed and written through; delete of a linked directory, of a directory with directory/file/dangling links inside, of a file link; a move through a link; `Clear` with links inside and of a linked archive folder; the file-link save (recorded). `ArchiveImageLimitTest` (6): PNG at 4096×4096 read, 4097 wide/tall and 2³²−1 refused; JPEG frame header after APP0, a DHT and fill bytes (baseline and progressive), refused over the limit; no frame header, truncated, empty and non-image data passed through; 16 MiB read, 16 MiB + 1 refused (sparse files).

**Mutations, reverted:** containment — the upstream prefix check (5 failures), no separator after the prefix (5), texture of the archive folder allowed (1), no separator trimming (1 failure, 1 error); links not recognized (5 errors: link targets deleted); upstream `Clear` (1); images — no file-size check (1), no pixel check (2), height unchecked (2), DHT taken as a frame header (1), fill bytes not skipped (1). **The upstream recursive delete passes every link test on host Mono 6.12** (it already does not enter links; a scratch probe agreed): the explicit delete makes the policy independent of the runtime's recursive delete, which is unverified for Unity's Mono under Wine. Gate 952/952, `minimum_tests` 925 → 952; full Release build 0 errors, 44 warnings.

**In game 2026-10-19:** image limits and containment passed; links are followed to read/write/list; under Wine a delete or move of an entry that is or contains a link is refused (deleting a link there would delete its target) — see "Proton pass 2026-10-19". Original list: a GUI image by a relative path and a `../` path refused under Proton's Windows paths; an oversized PNG/JPEG refused with a script error; a symlinked folder in the dev archive (made on the Linux side): listed and read through, `DELETEPATH` of it and of a directory containing it (target kept) — how Wine reports a Unix symlink (`ReparsePoint` or not) decides whether the policy applies in the game.

### Telnet input parsing and session lifecycle (NET-01/02)

**Upstream behavior confirmed before the change.** The original `TelnetProtocolScrape` code was compiled unchanged into a temporary Mono harness, with logging and socket calls stubbed out. It showed four faults:

- Replies to known options leaked the option byte into typed input. `IAC WILL TERMTYPE` injected 0x18 (Ctrl-X) and `IAC DO SGA` injected 0x03 (Ctrl-C).
- An escaped `IAC IAC` swallowed the following data byte.
- A command or subnegotiation cut off at the end of a read threw `IndexOutOfRangeException` when the buffer ended exactly there. With the production 4096-byte buffer it read stale bytes left over from earlier reads instead.
- Each read was UTF-8-decoded on its own, so a character split across reads was corrupted.

Reading the code also showed four more problems:

- A zero-byte read (the client closing) made the reader loop spin forever.
- `StopListening()` ran on the writer thread and aborted that same thread before closing the stream or destroying the welcome menu.
- Unity objects were destroyed from worker threads and from a finalizer.
- Two threads could write to the socket at the same time.

**Changes.** The protocol logic now lives in `kOS.Safe/UserIO`:

- `TelnetInputParser` is an incremental RFC 854 parser. It keeps its state and a persistent UTF-8 decoder between reads. Subnegotiations are capped at 1024 bytes. Malformed or truncated input is discarded and reported, and end of stream turns an unfinished character into U+FFFD.
- `TelnetInputDispatcher` decides ordering and break handling (details below).
- `TelnetProtocol` holds the negotiation replies and the NAWS size rules.
- `TelnetMenuText` holds the welcome-menu layout helpers.
- `TelnetConnectionWorker` owns the socket and the reader/writer threads. The threads are background threads. Writes are serialized and time out after 5 s. Shutdown is cooperative, idempotent and bounded: it shuts down and closes the socket to unblock a pending read, then joins each thread for up to 1 s. There is no `Thread.Abort`.

`TelnetSingletonServer` keeps its public members and adapts these classes. Worker threads now only request a stop. `TelnetMainServer.Update()` calls `MainThreadUpdate()` for each session on the game thread, which ends a session (detach from the CPU, welcome-menu cleanup) and keeps the welcome menu attached when no CPU is.

**Intentional behavior changes:**

- Break handling: `IAC BRK`/`IAC IP` still queues BREAK and discards the type-ahead from that read and any input already buffered. Window-size, terminal-type and negotiation messages inside the discarded input are now still processed instead of being dropped as raw bytes.
- NAWS sizes: a zero field still means unchanged. Other values are clamped to the screen buffer limits (255 columns, 160 rows).
- Clients refusing terminal type or NAWS: the warning no longer ends in a literal `DIE` and no longer claims kOS cannot work. These clients were never actually disconnected, because the enum was concatenated as text.
- The welcome menu is detached as soon as a CPU is chosen.
- `StopListening()` destroys the menu's GameObject rather than only the component.
- A width narrower than a word or message no longer crashes `WordBreak`/`CenterPadded`.

**Tests.** There are 83 headless regressions:

- **Parser (34):** every single and paired split point of a mixed stream, byte-at-a-time feeding, 800 seeded random streams (seeds 1, 7, 1234, 98765), option leaks, escaped IAC, NAWS edge values, wrong lengths, oversized and interrupted subnegotiations, invalid commands, pending state, and end of stream.
- **Protocol rules (23)** and **dispatcher (5)**.
- **Menu layout (9):** checked against an oracle copy of the original algorithm.
- **Worker (12):** real loopback sockets. They cover reassembly of split writes, client EOF ending the reader, shutdown unblocking a pending read, concurrent and worker-initiated shutdown, a stalled callback with a bounded join, shutdown before start, RST write failure, a stalled client timing out a send, TrySend giving up while another sender is blocked (and shutdown releasing it), and handler-fault reporting.

The `.UserIO` tests passed 25 consecutive runs, and 8 more with every core busy.

**Not covered.** No test runs `TelnetSingletonServer`, `TelnetMainServer`, `TelnetWelcomeMenu` or the terminal mappers. (Keys split across reads were later fixed by the review NET-03 work above.) A raw send from the game thread to a client that has stopped reading can block for up to the 5 s write timeout; the goodbye message waits at most 250 ms. Malformed-input log lines are not rate-limited.

**When Proton resumes**, test with the default loopback-only setting:

- PuTTY and/or `telnet` against the welcome menu and an attached CPU.
- Typing non-ASCII text.
- Resizing, including very small and very large windows.
- Ctrl-C and the telnet interrupt, with type-ahead.
- Detaching with Ctrl-D/X.
- Closing the client window, and killing the client process.
- `SET CONFIG:TELNET TO FALSE` with sessions attached.
- Scene changes and quitting the game with a session attached.
- A stalled client, e.g. a suspended `telnet` with sustained output.

Afterwards, check KSP.log for thread, Unity main-thread and socket exceptions. Socket timeout and close-unblocks-read semantics have only been checked on Linux Mono, not under Unity Mono on Proton.

### Raw I/O and legacy newline compatibility (IO-01/02)

The user approved **Preserve text APIs**. See [file-io-compatibility.md](file-io-compatibility.md) for the full policy, script examples, residual legacy-read risks and third-party-provider migration notes. New virtual `VolumeFile.ReadAllRaw()`/`WriteRaw(byte[])` and `Volume.SaveFileRaw()` add explicit raw I/O without new abstract/interface requirements. Built-in archive raw paths bypass conversion; local-disk defaults already preserve bytes. Script `READRAW` returns FileContent and `WRITERAW(FileContent)` appends exact bytes without an automatic newline or implicit string encoding. Archive raw saves use the same prepared replacement writer as text saves.

Copy/move routes (including directory members), `SaveFile(VolumeFile)` and new-disk boot-file seeding now read/save raw. This intentionally preserves original CRLF and invalid bytes instead of text-normalizing transfers. Legacy READALL/WRITE/editor/compiler/JSON/log text entry points remain; FileContent UTF-8 replacement fallback, BOM retention, classification, binary view and serialization remain unchanged. The Windows text converter now normalizes CRLF→LF→CRLF so repeated conversions do not add CRs; lone/already-doubled CRs are not guessed away. **Legacy archive READALL can still corrupt short invalid/ASCII-prefix binary content. Use raw APIs for byte integrity.** Raw append remains nontransactional.

Default raw methods call legacy implementations for existing custom providers. Derived Archive/ArchiveFile providers also fall back unless explicitly overriding raw methods, preventing inherited raw calls from bypassing old SaveFile/ReadAll/Write policy hooks. Their inherited methods do not receive a byte-exact guarantee. No precompiled external mod loading was tested.

62 permanent cases (46 raw/provider/compile, 16 newline/legacy) exercise real disposable Archive/Harddisk, all single-byte values, corpus transfers across all built-in routes, directories/file-to-directory, missing/empty/BOM/non-ASCII/malformed/ASCII-prefix binary/mixed-newline/KSM/gzip bytes, failure/source retention, suffix invocation/error handling, serialization and buffer independence. Capacity cases also verify that exact CRLF byte counts can reject a transfer that the legacy shortened view would have allowed, retaining the source. They scope and restore the private managed Windows flag using **test-only reflection**; no production config change and no Windows/Proton-runtime claim. A boot-style byte-exact CRLF source compiles headlessly, and real generated KSM content packs/saves/reads/unpacks; no CPU execution or Unity processor initialization occurred.

Six temporary pre-fix characterization probes observed actual FF→EF-BF-BD conversion, 128 changed single-byte values, repeated CRLF expansion, and corrupted successful copy/move followed by source deletion. Full probe run: 529 executed/526 passed/3 known failures, gate exit 0 (`/tmp/kosim-byte-text-characterization.log`); probes removed. Temporary legacy-copy and old-newline mutations produced 583 executed/14 failures and 583 executed/13 failures (including 3 known) respectively, gate exit 1; both removed. Initial implementation run had one new suffix cast-error mismatch (581 executed/4 failures including known); explicit FileContent validation now raises a kOS cast error instead of an opaque CLR cast failure.

Final `bash .ci/test.sh` with cached SDK on PATH: **585 executed/582 passed/3 known failures/0 skipped**, 12 Python passes, exit 0 (`/tmp/kosim-byte-text-final-tests.log`). `dotnet build kOS.sln -c Release`: 0 errors, 44 existing warnings (`/tmp/kosim-byte-text-full-build.log`). Documentation source updated; no Sphinx-rendered documentation build was run. No deployment/game activity; Proton remains deferred.

When Proton resumes, use disposable archive/local files to verify READRAW/WRITERAW, COPYPATH/MOVEPATH and directories with exact bytes; compare legacy READALL/WRITE/editor/log/JSON LF/CRLF/non-ASCII/BOM behavior; repeat text saves without CRCRLF growth; seed a new disk from an archive CRLF boot script and run source/compiled KSM fixtures. Check real provider/mod hooks and IO-03 locks/permissions/replacement behavior separately. Never use personal scripts. Header-payload tests are not compiled execution and managed Windows-flag tests do not replace this runtime checklist.

### Prepared archive saves (IO-03)

`Archive.SaveFile()` retains its public signature, directory/path checks, exact requested destination, byte conversion and post-save `Open()` behavior. Instead of opening the destination with `FileMode.Create`, internal `ArchiveFileWriter` creates `.kosim-save-<GUID>.tmp` in the destination's directory using `CreateNew`, write-only access and `FileShare.None`. It writes the existing converted byte array, flushes and closes before committing. Existing destinations use `File.Replace(temp, destination, null)`; new ones use non-overwriting `File.Move`. Existence is only a routing hint: a newly appeared destination blocks Move, and a disappeared destination blocks Replace. Unsupported or failed replacement does **not** fall back to deleting/truncating the original. No backup history or automatic retry is added.

On failure, only a temporary whose exclusive creation returned successfully is deleted; collision files are never owned/deleted. Expected cleanup failures throw a persistence exception with both original and cleanup exceptions and the temporary path. An interrupted/crashed process or failed cleanup can leave visible temporary files: there is no startup sweep, directory-list filter, or automatic deletion of recovery data. Parent directories may remain after a failed save. The test seam is instance-local/internal, exposed only to the friend test assembly; no public filesystem hook or global I/O override is added. Archive's existing static root is unchanged.

**Guarantees and limits:** preparation failures do not touch destination bytes. Commit safety/atomic visibility relies on the runtime/filesystem's Replace/Move implementation; no guarantee is claimed for native partial-commit failures or unsupported/network filesystems. Flush/close is not directory fsync or a power-loss durability protocol. After a successful commit, the retained `Open()` call can fail; the new data is already committed and no rollback is claimed. Existing-file concurrent edits are not detected. Replacement can affect file identity, links and metadata according to platform semantics; no new containment/symlink policy is established. `ArchiveFile.Write()` append and `Clear()` remain separate unchanged paths. That session deliberately retained legacy `ConvertToWindowsNewlines`/`ReadAll` behavior; the approved additive raw I/O and idempotent newline changes above are now implemented separately.

50 NUnit cases use disposable real archives and an adapter/stream around real System.IO operations. They cover new/existing successful commits, exclusive sibling creation, complete-and-closed data at commit, failures at create/start/partial write/flush/close/existence/commit, permission/sharing/unsupported-operation injections, cleanup errors with both exceptions, collisions, appeared/disappeared/directory destinations, nested/empty/long/exact names, old file-object refresh, unrelated leftovers, retry, post-commit Open failure, and failed archive saves preserving editor transactions. Disk-full and sharing/permission failures are **injected**, not actual full-disk or Proton lock tests. Interrupted preparation is simulated cancellation, not an actual killed process/power-loss experiment. Host tests perform real successful Replace/Move and real collision/race/directory failures; they do not validate Windows Unity under Proton.

A temporary direct-`FileMode.Create` mutation made the full gate exit 1: 523 executed, 1 error/34 failures (3 known). The existing-file partial-write regression observed 18 original bytes replaced by 4 bytes. This is a fault-injected legacy-pattern mutation on host Mono, not a game reproduction. Mutation removed. Archive-session final `bash .ci/test.sh` with cached SDK on PATH: **523 executed/520 passed/3 known failures/0 skipped**, 12 Python passes, exit 0 (`/tmp/kosim-archive-final-tests.log`). `dotnet build kOS.sln -c Release`: 0 errors, 44 existing warnings (`/tmp/kosim-archive-full-build.log`). No deployment/game activity; Proton remains deferred.

When the user resumes Proton (KSP 1.12.5.3190 / GE-Proton11-7), use only disposable dev-instance archive files to check real new/repeated Save/editor SaveThenLoad, nested/exact names, text/KSM and newline compatibility, actual Replace support, read-only/permission-denied and externally locked destinations, ACL/metadata/file-object behavior, and failure diagnostics/retry. Inject a temporary partial-write/flush/replace failure to confirm old bytes and dirty draft/pending target survive and only owned temporary files are cleaned. Investigate any unsupported operation without introducing a destructive fallback. Controlled interruption/recovery checks need disposable files and explicit planning; never fill the user's real disk or use personal scripts.

### Editor document transactions (EDIT-01/02)

`EditorDocument` in `kOS.Safe` owns the active volume/path, draft and last successful load/save text separately from the pending open request. Dirty detection compares actual text ordinally; undoing back to the saved text clears dirty state. Save always uses the active volume/path, and a load replaces active state only after all file reads/path validation succeed. Failed saves retain dirty text and the request; failed loads retain the old document and request (if saving succeeded first, that document is correctly clean). Cancel preserves the old draft/location; discard-switch only replaces it after a successful load. Explicit discard-exit restores the baseline. Missing files still open empty and clean without creating a file until Save. Existing extension resolution, `ReadAll().String` and `FileContent` text semantics are retained; no raw-byte/newline/atomic-archive fix is included.

The popup no longer clears dirty state on Open/Close, and modal state is independent of focus freezing. While a dialog is invoked, text/buttons/shortcuts/resize are blocked even if the popup gains focus or is reopened. Additional file requests do not retarget the unresolved dialog. Dialogs identify active and requested global paths separately. File errors leave confirmation choices intact with an error message; initial/clean load errors have Retry/Cancel. Direct Close cancels any pending open/dialog but preserves the draft; explicit No-save exit discards. Hidden-parent dialogs are not drawn. Fatal/Unity GUI-exit errors, including wrapped branches, are not presented as retryable file errors. These adapter behaviors are source-reviewed and compiled, **not Unity-tested**; headless tests exercise the actual document model, not the popup, GUI.enabled, focus, or dialog controls.

48 NUnit regressions cover actual content changes/undo, baseline advancement, save/discard/cancel on one disk/archive and across disk↔disk/archive, real full-disk rejection, injected save/open/read failures and retry/cancel/discard, missing/new files, initial failure recovery, directories, reload, extension resolution, pending target stability and path mutation. Archive has a static root upstream, so tests do not pretend two independently rooted archives are supported. Temporary legacy-pattern mutations confirmed coverage: length-only dirty detection produced 469 executed/10 errors/26 failures (3 known); pending-path saving produced 469 executed/1 error/12 failures (3 known). Both gates exited 1; mutations removed before final runs. These are deliberate mutation checks, not execution/reproduction of the old Unity popup.

Editor-session local command: `bash .ci/test.sh` with the cached SDK on PATH — **473 executed/470 passed/3 known failures/0 skipped**, 12 Python passes, gate exit 0 (`/tmp/kosim-editor-final-tests.log`). Full `dotnet build kOS.sln -c Release` against dev assemblies — 0 errors, 44 existing warnings (`/tmp/kosim-editor-full-build.log`). No deployment or game activity; Proton remains deferred.

Pending manual checks when the user resumes Proton: same-length replace followed by Exit/Reload/file switch; insertion/deletion/undo; reopen a dirty popup; focus/click the parent during each modal and confirm text/buttons/shortcuts/resize remain blocked; save/discard/cancel A→B on the same and different volumes, checking titles/messages and both file contents; initial/missing/directory loads; capacity failure then retry/discard/cancel; successful A save followed by failed B load; explicit discard-exit versus incidental view close/power hiding. Check input/flight-control behavior and no background/orphan dialogs. Use only disposable files in the dev instance, not personal scripts. Archive replacement now has separate host coverage above; actual interrupted-write/runtime verification and external-change conflict detection remain outstanding.

### Cursor read safety and scoped render recovery (BUFFER-02/03)

`TermWindow` now captures one display snapshot for both rows and cursor coordinates. A new `ScreenSnapShotExtensions.TryGetCursorCharacter()` helper checks negative/upper bounds against snapshot height, available rows, actual row width, and the current viewport before indexing. Invalid cursors are hidden, not repositioned; null cells render as spaces just like normal terminal rows. Null/missing cursor rows are unavailable. No `IScreenSnapShot` member was added (avoids breaking external implementations), no script cursor semantics changed, and snapshot rows remain mutable/shared rather than newly immutable or thread-safe.

23 tests cover valid reads, negative/extreme positions, clipped/empty/short/null rows, snapshot versus later live-cursor movement/resize, snapshot height, null-cell conversion, and real long-input Home navigation producing a negative display cursor. Before correction, a headless extraction of the legacy GUI bounds predicate produced 7 errors and 5 new assertion failures (391 executed; also 3 known formatter failures), gate exit 1. This is a reproduction of extracted guard logic, not execution of Unity's renderer or an in-game fault reproduction.

Both explicit `GUI.BeginGroup` calls in `TermWindow` pair with `GUI.EndGroup` in nested `finally` cleanup, including if style restoration fails; cursor drawing uses one group. Scoped GUI color/text-color restoration is included. Null display rows are left blank.

`RenderFaultRecovery` now wraps font/geometry preparation before `GUI.Window` and the normal window-drawing callback. A per-view monotonic `Stopwatch` gate retries automatically every **2 seconds** after nonfatal failures and emits at most one diagnostic batch per **10 seconds**, including suppressed failed-attempt counts. Successful draws, changing error types, and explicit retries do not reset the log budget. The gate retains no exception objects or model data. Fatal runtime errors (including wrapped/aggregate branches) are rethrown; the Unity adapter also rethrows direct/wrapped/aggregate `ExitGUIException` control flow.

On failure/cooldown, default-skin fallback controls show a fault notice plus Retry/Close/Resize. Resize changes window pixels only, deferring model resize into a guarded retry; the original protected `CheckResizeDrag()` signature is retained. CPU execution, input ownership/queues, and telnet paths are unchanged. No automatic model clearing, script-error-history insertion, or screen-print logging: diagnostics go directly to Unity's game log with the original exception. The fallback remains outside the guard; logging/fallback failures deliberately propagate rather than recursively recovering.

34 headless tests cover cooldowns, shared font/draw stages, explicit retry, persistent/intermittent/changing faults, aggregate report budgets, recovery, per-view isolation, excluded control-flow/fatal errors, configuration, reporter failure, and an injected real-snapshot row-read fault with **simulated** group/color cleanup and fallback continuation. These were added alongside implementation; no new pre-fix in-game reproduction is claimed. Actual Unity clipping stacks, control behavior, and the concrete `ExitGUIException` adapter were not executed by headless tests.

Recovery-session local checks: **425 executed/422 passed/3 known failures/0 skipped**, 12 Python passes; full Release build against dev assemblies: 0 errors, 44 existing warnings. User deferred Proton testing; no new deployment. Later inject a temporary nonfatal row-draw/font fault in the dev build and verify fault notice, Close/Resize/Retry, 2-second retry cadence, 10-second diagnostic batches, restored groups/colors/skin, eventual recovery, normal GUI-exit behavior, and no CPU/input/flight-lock regression. Also repeat long-input Home/End/history, resize/output/scroll, quicksave/load, power/blink/reverse/brightness checks.

**Boundary limits:** snapshot acquisition in `Update`, input processing, title/lifecycle code, telnet output, and `GUI.Window` itself are outside this recovery boundary. It is not a blanket catch for the entire terminal and cannot repair model corruption. Those paths and any fallback/diagnostic fault need separate investigation. Telnet rendering is unchanged. BUFFER-03's scoped implementation is now present; its Unity/game acceptance checks remain unperformed.

### Dimension safety fix (BUFFER-04)

`ScreenBuffer.SetSize()` normalizes before allocating/notifying: zero preserves that axis (matching telnet's existing unspecified-axis behavior), other values clamp to **1–160 rows / 1–255 columns**. The model continues to support small grids; script suffix minima remain **3 rows / 15 columns** with the same maxima. Existing public `TerminalStruct` fields remain fields; their default maxima now reference model constants. Model caps apply even if another mod raises the script fields.

An unchanged effective size is now a no-op: no replacement rows, timestamp changes, resize callbacks, watcher events, or callback-directed scroll. This is an intentional behavior change, not a force-refresh API. Actual resizes still notify once and apply callback scroll returns. Shrinking clamps output cursor upper bounds and column lower bounds before callbacks; negative row anchors used by long command input remain intact. Existing content width-truncation and scrollback ownership are otherwise unchanged.

`SubBuffer.SetSize()` rejects negative rows or nonpositive columns with `ArgumentOutOfRangeException` before touching dimensions/rows/timestamps. Empty overlays and `Wipe()` remain supported; overlay height is not capped because long input may exceed viewport height. This is validation safety, not a transaction guarantee for allocation failures or observer exceptions, nor a bound on retained scrollback/input memory.

Before implementation, the 364-test run produced 4 errors and 18 failures (including the 3 known formatter failures), gate exit 1. One new one-cell assertion was then corrected: filling its only cell intentionally scrolls it out of view; the test now scrolls back before checking. An `int.MaxValue` allocation case was added only after caps were implemented to avoid unsafe allocation in the unfixed baseline. Final suite: 368 executed/365 passed/3 known failures/0 skipped; all 27 dimension tests pass. Full Release build against dev assemblies: 0 errors, 44 existing warnings.

**Not yet deployed or Proton-tested.** Close the dev game before deploying/restarting. In the disposable save, check script limits (negative/zero requests still clamp to 3/15), normal and repeated same-size requests, largest font at minimum GUI window size, narrower/wider resize with long input, addressed output and execution after shrinking. Follow with local telnet NAWS zero/oversized requests; the headless suite tests the real model and suffixes, not the Unity input adapter or network parser. Keep telnet exposure defaults unchanged. The overlay smoke test used the earlier build and is not evidence for this new implementation.

### Overlay clipping safety fix (BUFFER-01)

`ScreenBuffer.GetBuffer()` now replaces only rows in the visible source/destination intersection rather than removing/inserting ranges. Fixed positions remain viewport-relative; non-fixed positions remain absolute rows minus `TopRow`. Later registered overlays win. Whole rows and their references are retained, with no new horizontal/`PositionColumn` semantics and no mutation of base rows. Empty/unavailable source rows are ignored; position arithmetic uses `long` to avoid overflow. This does not validate dimensions, repair cursor/input scrolling, or make snapshots immutable/thread-safe.

Before the fix, the 341-test suite produced 9 new overlay errors (including real `TextEditor` long-input and resize cases), 3 known formatter failures, and a failing local gate. After the fix, all 26 overlay tests pass on host Mono. This is a model reproduction, not an exact in-game reproduction of upstream #3100 or proof that every long-input defect is resolved.

**Limited Proton GUI smoke evidence:** in disposable `kOSIM-dev` under KSP 1.12.5.3190/GE-Proton11-7, the user opened a terminal and reported it remained responsive after the guided long-input/resize instructions (20 columns/3 rows; three-alphabet `PRINT` input; narrower/wider resize; Page Up/Down; submission). The inspected 7009-line instance `KSP.log` contained no exception or range/GetBuffer/GUI-clip fault matches; the earlier `kOS: ipu: -1` error-log statement remains. On follow-up, the user explicitly confirmed text/cursor alignment during resize and correct printed output after Enter. This guided GUI smoke check passes within that scope. Telnet, fixed progress overlays, and broader gameplay checks remain pending; this is not exhaustive BUFFER-01 runtime validation.

For further checks, with the dev game closed, build/deploy as above, launch via its isolated launcher, and use the disposable save. In a terminal narrowed to a few rows, type a command longer than its height, resize narrower/wider while editing, and navigate scrollback. Check GUI and local telnet output, cursor visibility/alignment, and any fixed communications-delay progress overlay. Confirm no out-of-range/GUI-clip errors in dev logs and record KSP/Proton versions. Negative cursor guards and balanced GUI groups are separate outstanding safety work.

Before user-facing changes, add focused regression cases and a Proton game pass covering:

- Boot and representative flight scripts, source and compiled KSM execution.
- Fixed-position dashboards (`PRINT AT`, cursor, `CLEARSCREEN`, dimensions/resize watchers).
- Prompt versus script `GETCHAR`, communications delay/loss, paste, interrupts, and multiple viewers.
- Focus/hide/modal/close/scene transitions and absence of flight-key leakage or stale locks.
- Save/craft loading, power loss/recovery, GUI/telnet equivalence, and mod-facing terminal injection.
- Fonts/clipboard/input, UI scales, file paths/case handling, local telnet connections, native resource cleanup, slow receivers, and large-paste frame time.

Keep a small baseline smoke-test save/script set for comparison with original kOS in the same development environment. Record KSP version/build, Proton version, relevant mod versions, reproduction steps, and logs. Steam/Proton updates can change the environment even when the mod is unchanged.

The original review's cross-platform and hosted-CI recommendations are historical; the current roadmap and agent instructions supersede them.
