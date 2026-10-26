# Build and test locally

## Scope and prerequisites

- **Build tooling:** .NET SDK 8 (last full-build verification: 8.0.425), Mono 6.12,
  Python 3. The mod runtime remains **.NET Framework 4.8 (`net48`)**, not .NET 8.
- **Validated runtime:** Windows Steam KSP **1.12.5.3190**, Unity 2019.4.18f1,
  **GE-Proton11-7 / UMU 1.4.4** on Linux. Other platforms/versions are unvalidated.
- **Local checks only.** `.ci/` is the local test tooling, despite its inherited name.
  Reference-only upstream GitHub workflows remain disabled under `.github/upstream-workflows/`.
- Use a disposable, isolated game copy for game references and testing; never commit
  or redistribute proprietary KSP/Unity assemblies or import personal saves.

Current verification: [HANDOFF.md](HANDOFF.md). Behavior contracts/limits:
[compatibility.md](compatibility.md). Release procedure: [RELEASECHECKLIST.md](../RELEASECHECKLIST.md).

## Headless gate (no KSP installation required)

From the repository root, with `dotnet`, `mono` and Python on `PATH`:

```bash
bash .ci/test.sh                    # PYTHON=python if python3 is unavailable
```

The gate runs Python tooling tests, the source-version check, then builds and runs
**NUnit 2.7.1** with the pinned NUnit.Runners console. **`dotnet test` does not run this suite.**
Equivalent explicit steps:

```bash
python3 -m unittest discover -s .ci -p 'test_*.py' -v
python3 .ci/check-version.py
dotnet msbuild src/kOS.Safe.Test/kOS.Safe.Test.csproj \
  -restore -target:RunHeadlessTests -property:KOSHeadless=true \
  -property:Configuration=Release -property:PythonExecutable=python3 -nologo
```

`KOSHeadless=true` builds only the safe projects against standard framework references
and the bundled SharpZipLib, bypassing KSPBuildTools. **Do not use it for a mod build.**
Results are written to ignored `artifacts/tests/Release/nunit.xml`; the previous report
is removed first. NuGet restore may need network access on a fresh machine.

`.ci/known-test-failures.json` currently has **no known failures** and a **1,460-test
minimum**. Raise the minimum as coverage grows. The checker rejects missing/malformed
reports, duplicate test names, count inconsistencies, errors/skips/inconclusive tests,
new/changed failures, unexpected passes of a known failure, and inconsistent runner
exit status. Known-failure message matching uses all listed substring fragments;
upstream count/commit fields are historical metadata, not executable checks.

Failure propagation was previously checked with a temporary failing test. For a
non-trivial fix, verify regression coverage fails without it, then restore and rerun.
Never commit deliberate failures. Use the full suite: fixture-only NUnit `-run=`
filters outside `Execution` can omit the setup required by `ScriptHost`.

## Full mod build

Use a legally owned isolated **Windows** KSP copy. Create ignored **`kOS.props.user`**
next to `kOS.sln` (case matters). Replace the example paths with your absolute paths:

```xml
<Project>
  <PropertyGroup>
    <KSPBT_GameRoot>/absolute/path/to/KSP-dev</KSPBT_GameRoot>
    <KSPBT_ManagedPath>/absolute/path/to/KSP-dev/KSP_x64_Data/Managed</KSPBT_ManagedPath>
    <KSPBT_InstallCKANDependencies>false</KSPBT_InstallCKANDependencies>
  </PropertyGroup>
</Project>
```

```bash
dotnet build kOS.sln -c Release --no-incremental
python3 .ci/check-version.py --built
```

Outputs stay in `Resources/GameData/kOS/`; nothing is deployed automatically.
The historical full-build baseline is **0 errors, 44 warnings**; investigate new
warnings rather than treating that number as a permanent acceptance rule.
Use `KSP_x64_Data/Managed`, not native Linux `KSP_Data` for the validated target.

Headless runs rebuild safe outputs without mod metadata. **Run the full build again
before packaging/deploying.** `check-version.py --built` also checks that the generated
`kOS.version` KSP range matches the explicit values in `src/kOS/kOS.csproj`. The built-version checker examines generated assembly
metadata and JSON, not DLL contents; inspect binary metadata separately for releases.
Builds embed their source commit, so rebuild after committing before recording hashes.

## Packaging and CKAN checks

```bash
python3 .ci/package.py --allow-dirty --output-dir artifacts/tests/package   # local test ZIP
python3 .ci/ckan.py --test artifacts/tests/package/kOS-v<version>-dirty.zip --output artifacts/tests/package/test.ckan
python3 .ci/ckan-solver-check.py --test artifacts/tests/package/kOS-v<version>-dirty.zip
```

New files must be tracked (at least staged) to be packaged. Release packages need a
clean committed tree; see the [release checklist](../RELEASECHECKLIST.md). The solver check
is manual: it needs network access and the `ckan` command, and is not part of
`.ci/test.sh`. See [CKAN packaging](ckan.md) for what it covers and how it isolates CKAN.

## Test harness maintenance

- `ScriptHost` runs real prompt input, compiler/CPU and `ScreenBuffer`, with fixed
  0.02 s updates and a disposable archive copied from `kerboscript_tests`.
  `GatedScript` controls background compiles. Fixtures fail on recorded script errors.
- Keep adapters in step with game code: `FixtureInterpreter` ↔ `Interpreter`;
  `FixtureTermWindow` ↔ `TermWindow` input/session/update order;
  `TelnetServerInputModel` ↔ `TelnetSingletonServer` input sink/viewer behavior.
  **Do not update** `PreSessionTermWindowFanOut`: it is the frozen pre-extraction
  equivalence oracle, except its explicitly marked view fixes.
- Compatibility fixtures in `kerboscript_tests/fixtures/` cover source and compiled
  KSM, both newline modes, a CRLF boot and fixed-position dashboard snapshots.
  Preserve recorded upstream quirks unless a change is explicitly reviewed.
- `KOSIM_RECORD_FIXTURES=1` rewrites expected output/traces and deliberately fails.
  Record twice if Unix output changes, review differences, retain `.windows.out`
  only where it differs. `.gitattributes` protects the CRLF fixture's actual bytes.
- Telnet recordings: `inetutils-xterm` is a live GNU inetutils 2.8 capture;
  `putty-xterm` and `windows-telnet-ansi` are **reconstructed**, not real-client passes.
  Tests replay whole, split at every byte, bytewise and with seeded chunking.
- `AllocationCounter` provides Mono per-thread allocation evidence. Such checks and
  mutation runs do not establish Unity native-resource counts or game frame times.

## Game checks

A headless pass, full build and Proton pass are separate evidence. Report exactly
what ran and what did not. For each game pass record build commit/version, DLL hashes,
KSP/Proton/mod versions, steps/results and a saved Player.log **locally**, not a growing
transcript in this repository.

Deploy only with the game closed and approval for the target copy: back up
`GameData/kOS`, preserve `Plugins/PluginData/` and saves, copy reviewed outputs without
blind deletion, then compare deployed hashes. Do not close/kill a running game or
change Steam settings/prefixes without permission.

For the maintainer's existing isolated copy, the configured location is
`$HOME/Games/KSP-kOSIM-dev` (also in ignored `kOS.props.user`). Its local
`launch-dev.sh --check` performs preflight; `launch-dev.sh` uses UMU, GE-Proton11-7
and that copy's `.proton-prefix`, not the vanilla Steam prefix. Do not substitute
host Wine or another Proton silently. Launcher, setup notes, probes, disposable saves,
backups and detailed logs are **local-only**, not repository prerequisites.

The recorded launch configuration uses the Wine Wayland driver,
`-force-d3d11 -window-mode borderless`, and optional MangoHud. This particular copy
also has a local fullscreen-focus-loss workaround recorded in its `setup-notes.md`;
that game-file patch is not part of kOSIM and is not a general installation step.
For live Unity logs inspect the prefix's
`drive_c/users/steamuser/AppData/LocalLow/Squad/Kerbal Space Program/Player.log`.
Actual mouse/focus/clipboard actions require user verification; do not infer them
from startup logs or a running process.

### Regression matrix

- Source/KSM boot and flight scripts; `PRINT AT`, cursor, clear, resize watchers.
- GUI and telnet together: prompt versus `GETCHAR`, paste/interrupt, view/follow,
  resize, slow receiver, disconnect, CPU disappearance and scene changes.
- Focus/flight locks (Alt+F12), mouse blocking, F2, modal dialogs, power/connectivity
  loss/recovery, name tag, editor, toolbar; script opens must not steal focus.
- Fonts/cursor/cell alignment at several sizes, input layouts, clipboard, UI scales
  and screen edges/resolution changes; native objects after repeated open/close.
- Per-save shared history across CPUs/telnet, per-CPU geometry, shared presets, legacy
  five-field preset opacity fallback, restart/reload, prune safeguards and legacy
  history deletion; archive raw bytes/atomic failures/link safety.
- Toolbar right-click (stock and Blizzy): open/close all active-vessel terminals, no
  focus taken, stepped default positions, no left-click/panel side effects.
- Opacity at 0/partial/100%, reverse/beep and no alpha stacking; popup outside/Esc
  dismissal, typing suppression, focus/hide/power/fault dismissal, no click-through,
  no opacity-only resize notifications or stale hot controls/blockers.

### Remaining gaps from prior passes

Functional checks of phases 0–4, follow-up review fixes, presets and opacity passed
under KSP 1.12.5.3190 / GE-Proton11-7 in October 2026. Preset/opacity passes were
user-reported, not individually instrumented. Exact versions and latest gate/build
results are in [HANDOFF.md](HANDOFF.md). Still unverified or only partially covered:

- Pending history on **immediate game exit within 2 s of a previous command** (the
  earlier attempt saved before exit), picker component counts, stalled client during
  vessel switch, missing-sound error UI. Vessel destruction/unload with attached clients.
- Exhaustive opacity UI scales/resolution changes, starved power, flight results,
  deliberate render faults, resize/telnet notifier instrumentation and native counts.
- Comm delay/RemoteTech overlays; all starting/disconnected/starved CPU state variants;
  F2 with `ObeyHideUI` off; SPH name-tag locks and CTB focus modes; full-restart history.
- Volume objects across undocking, `EDIT` of file objects, Windows-side locked
  destinations, third-party volume providers; real PuTTY/Microsoft Telnet and stalled
  repaint recovery. Wayland remote-driving tools and overlay were not fully checked.

1.6.0.104 (dev copy, 2026-10-26): the maintainer reported the following working; this was
not individually instrumented, and Blizzy's toolbar is not installed in the dev copy:

- **Right-click toggle:** with 2+ CPUs on the active vessel, open and close; mixed
  state (one open first); no keyboard/flight-control capture until a terminal is
  clicked; stepped positions for windows without geometry and their save. Check that
  right-click doesn't toggle the panel or the left-click state, in map view too,
  and that it does nothing in VAB/SPH. Blizzy only if installed.
- **Shared history:** a command typed on one CPU recalled by Up and Ctrl+R on another
  and over telnet; vessel switch, quickload and scene change keep it; `history.txt`
  written under the save; old `kOS/history/` files removed (Player.log line) after a
  main-menu load, without touching geometry files.

Open bugs are in the [roadmap](KOSIm-roadmap.md), not hidden in old session logs.
