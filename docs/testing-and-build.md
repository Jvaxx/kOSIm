# Local building and testing KOS Improved

## Scope: Steam KSP under Proton

The current runtime target is **KSP's Windows Steam build running under Proton on this user's Linux machine**. Native Linux, native Windows, macOS, other KSP versions, and other machines are not part of the current validation commitment. Avoid unnecessary breakage in their code paths, but do not claim untested support.

The approved development instance for **all game references, deployment, and game tests** is:

```text
/home/jvz/Games/KSP-kOSIM-dev/
```

It was independently copied from `/home/jvz/.local/share/Steam/steamapps/common/Kerbal Space Program/` and checksum-verified. The vanilla source remains untouched and is not a deployment target. Both contain the Windows `KSP_x64.exe` and `KSP_x64_Data/Managed` assemblies; Steam build ID is `03190`.

The dev copy is approximately 5.6 GiB and initially contained only stock `Squad`/`SquadExpansion` GameData; the tested local kOSIM build is now deployed under `GameData/kOS`. Only stock scenarios/training saves were retained; personal saves, screenshots, CKAN state, generated logs/part database/thumbs, and settings backups were excluded. `settings.cfg` was copied independently for familiar input/display settings. No hard links/symlinks to the vanilla source were used.

Steam's KSP compatibility mapping selects installed `GE-Proton11-7-x86_64`. The dev launcher uses that same tool through installed UMU 1.4.4. First launch initialized the dedicated prefix and opened a visible game window; logs report **KSP 1.12.5.3190 (WindowsPlayer x64)** and show kOS assemblies/parts loading. The overlay-only build passed the guided GUI long-input/resize smoke check; the newer dimension/cursor/GUI-cleanup build is not deployed or game-verified. The user explicitly deferred further Proton tests; continue local implementation without deployment until requested.

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

They concern lexicon terminal formatting: primitive structures are expanded into nested value dumps instead of the inline representation expected by these assertions. `SafeSerializationMgr.DumpValue()` checks `IDumper` before primitive structure conversion. This is source-supported context, not a completed in-game diagnosis or a decision to change serialization.

The fork adds **13 character-grid compatibility tests**, **26 overlay regression tests**, **27 dimension regressions**, **23 cursor-read regressions**, **34 render-recovery regressions**, **48 editor document regressions**, **50 archive-save regressions**, **62 raw I/O/newline regressions**, and **83 telnet regressions**, so the current suite executes **668 tests: 665 passed, the same 3 failed, 0 skipped**. The local gate has **12 additional Python unit tests**.

`.ci/known-test-failures.json` records exact known failing names, expected assertion-message fragments, reasons, and the minimum current test count. **No test is ignored or filtered out.** The raw NUnit process still exits nonzero for these failures. The local regression gate tolerates only this recorded baseline and rejects:

- New failing tests or changed assertion messages for a known failure.
- Errors, invalid/inconclusive/unexecuted tests, and inconsistent XML counters.
- Empty/missing/malformed reports, duplicate names, or a count below the recorded minimum.
- A runner exit status inconsistent with the reported failures.
- A formerly known failure that now passes, until investigated and removed from the baseline.

A passing gate **does not mean the entire suite is green**. Known failures are printed locally and retained in `artifacts/tests/Release/nunit.xml` (ignored by Git). There are no report uploads or GitHub summaries. Resolve known failures in focused follow-ups; do not expand the baseline to silence regressions. Maintain the minimum count as tests are added; it detects count drops, not every substitution of one test for another.

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
| KSP under Proton | 1.12.5.3190; kOS loads; user-guided GUI long-input/resize responsiveness, alignment, and command output pass; no matching logged faults; telnet (including the NET-01/02 changes) and wider gameplay coverage incomplete |
| Other game platforms/versions | Outside current validation scope |

Initial terminal fixtures cover default dimensions, addressed writes, cell overwrites, newline/wrap behavior, scrollback/clamping after a clear, clear/reset, grid growth, notifier registration/removal, fixed overlays, deep-copy snapshots, and empty snapshots. They use real models, not the integration-test screen stub. Overlay regressions now cover partial/full clipping above and below, overlays spanning both boundaries, extreme integer positions, fixed versus scrollback-relative placement, disabled/empty/wiped/shortened overlays, layer order, row-reference preservation, and long command input across width changes. A seed-3100 reference test exercises 200 compositions with resize and scroll. Dimension regressions now cover zero/negative/excessive requests, effective no-op identity/timestamps/notifications, normalized callbacks, safe writing after shrinking, negative long-input anchors, overlay width propagation, script suffix bounds, and 100 seeded resize requests (seed 3104). Cursor reads now have 23 headless regressions; actual Unity GUI group/color cleanup, fault recovery, and lifecycle checks still require runtime validation.

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

**Not covered.** No test runs `TelnetSingletonServer`, `TelnetMainServer`, `TelnetWelcomeMenu` or the terminal mappers. Mappers still convert each read's text separately, so a `\r\n` or an `ESC [` sequence split across reads is unchanged pre-existing behavior. A raw send from the game thread to a client that has stopped reading can block for up to the 5 s write timeout; the goodbye message waits at most 250 ms. Malformed-input log lines are not rate-limited.

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
