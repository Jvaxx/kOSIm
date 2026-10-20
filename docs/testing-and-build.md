# Building and testing KOS Improved

How to build, test and check kOSIM in game, what it changes from upstream, and what is still open. Per-step test lists, mutation results and pass logs up to Phase 3 are in git history (this file at `876e1a29`); session progress is in [HANDOFF.md](HANDOFF.md).

## Scope and environment

- **Runtime target:** KSP's Windows Steam build under Proton on this Linux machine: **KSP 1.12.5.3190** (WindowsPlayer x64, Unity 2019.4.18f1), **GE-Proton11-7** through **UMU 1.4.4**. Native Linux, native Windows, macOS and other KSP versions are outside the validation scope; avoid breaking their code paths, but claim no support.
- **In-game runtime stays `net48`.** SDK 8 is a build tool only.
- **Host tools:** Arch Linux, .NET SDK 8.0.425 (user cache `~/.cache/kosim-tools/dotnet`), Mono 6.12.0, Python 3.14.
- **Dev instance** `/home/jvz/Games/KSP-kOSIM-dev/`: a checksum-verified copy of the Steam install (Steam build 03190), used for all game references, deployment and game tests. Provenance in its `setup-notes.md`. Never commit or redistribute its game files.
- **Vanilla install** `~/.local/share/Steam/steamapps/common/Kerbal Space Program/`, Steam settings and the Steam prefix (`compatdata/220200`): never touched, not a deployment target.
- **Local tests only.** Upstream workflows are kept inactive under `.github/upstream-workflows/`; do not reactivate them or add hosted automation without a user decision. `.ci/` holds local tools only.

## Validation layers

1. **Headless tests** on the host (Mono) against the real `kOS.Safe` classes: not Unity, not Proton.
2. **Full build** against the dev instance's Windows assemblies: linking, not runtime behavior.
3. **Game checks under Proton**: rendering, keyboard/mouse/focus, clipboard, fonts, files under Wine, telnet, power/connectivity, scenes, lifecycle.

None substitutes for another. Report what actually ran: a build is not a test run, and a headless pass is not an in-game check.

## Headless tests and the local gate

```bash
export PATH="$HOME/.cache/kosim-tools/dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
bash .ci/test.sh                 # PYTHON=python if Python is not python3
```

Explicit equivalent:

```bash
python3 -m unittest discover -s .ci -p 'test_*.py' -v
dotnet msbuild src/kOS.Safe.Test/kOS.Safe.Test.csproj \
  -restore -target:RunHeadlessTests -property:KOSHeadless=true \
  -property:Configuration=Release -property:PythonExecutable=python3 -nologo
```

- `KOSHeadless=true` builds the two safe projects without KSPBuildTools (standard .NET Framework references plus the repository's SharpZipLib). Never use it for the full build.
- NUnit 2.7.1 with the pinned NUnit.Runners console (`.ci/nunit-console.exe.config` selects CLR 4). **Do not use `dotnet test`**: it does not run these tests.
- The runner deletes the previous report first; results go to `artifacts/tests/Release/nunit.xml` (ignored by git).
- **Gate** (`.ci/known-test-failures.json`): known failures by exact name and message (currently none), and `minimum_tests` (currently **1247**; raise it as tests are added). It rejects new or changed failures, a known failure that now passes, errors, invalid/inconclusive/unexecuted tests, inconsistent counters, a missing or malformed report, duplicate names, a count below the minimum, and a runner exit status inconsistent with the results. No test is ignored or filtered. Do not expand the baseline to hide a regression.
- Failure propagation was verified with a temporary `Assert.Fail` test (runner exit 4, gate exit 1). To recheck: add a temporary failing test, confirm a nonzero exit, remove it, rerun. Never commit it.
- Running one fixture alone (`-run=`) fails in `ScriptHost.Boot` for fixtures outside the `Execution` namespace (their `[SetUpFixture]` does not run): use the full gate.
- Add a regression test with each fix; run a mutation (revert the fix, see the new tests fail) for non-trivial ones.

## Test harness

- **`ScriptHost`** (`src/kOS.Safe.Test/Execution/ScriptHost.cs`) runs kerboscript as the in-game terminal does: keys typed at the real prompt line, compiled and run by the real CPU, output into the real `ScreenBuffer`. Game time advances only in fixed 0.02 s updates, so `WAIT` and triggers are deterministic. The archive is a disposable copy of `kerboscript_tests`. It uses `GatedScript` (the real `KSScript` with a gate that holds background compiles and records overlapping compiles).
- **Copies of game code: keep them in step** when the game side changes:
  - `FixtureInterpreter` ↔ game `kOS.Screen.Interpreter`: both extend the shared `CommandLineEditor` (history keys and search); keep `NewLine`/`EndLine`/BREAK handling and `IsWaitingForCommand` in step.
  - `FixtureTermWindow` ↔ `TermWindow`: its `ITerminalInputHost` members, session calls and update order (the router and session themselves are shared code).
  - `TelnetServerInputModel` ↔ `TelnetSingletonServer`'s `ITelnetInputSink` methods and `ITerminalViewer` role.
  - `PreSessionTermWindowFanOut` is a **frozen** copy of the pre-P2-2 fan-out used as an equivalence oracle; do not keep it in step (it carries only the marked VIEW-03 and view-clamp fixes).
- Other adapters in `ScriptHost.cs`: `FixtureBindingManager` (only `TERMINAL`), `FixtureFunctionClearScreen`, `RecordingLogger` (records printed script errors and resolves interpreter source lines as `KSPLogger.TraceLog` does; fixtures fail on any error). `Opcode.InitMachineCodeData()`/`CompiledObject.InitTypeData()` are called as `kOSProcessor.OnAwake()` does (KSM packing needs them).
- **Compatibility fixtures** (`kerboscript_tests/fixtures/`, expected output in `fixtures/expected/`): `printing.ks`, `functions.ks`, `triggers.ks`, `fileio.ks` (each from source and compiled KSM, in both newline modes; only `fileio` differs on Windows: `fileio.windows.out`), `boot/boot_crlf.ks` (CRLF boot file through the real `CPU.Boot()`), `dashboard/hud.ks` (15 full-screen snapshots of `PRINT AT` layouts, scrolling, `CLEARSCREEN`, resize watchers). Recorded quirks kept as upstream: `PRINT ""` adds no line; `ROUND(2.5)` is 2; `RUNONCEPATH` skips a file already run by `RUNPATH`; a plain `PRINT` after `PRINT AT` writes from the restored cursor, over dashboard text.
- **Re-recording:** `KOSIM_RECORD_FIXTURES=1` rewrites expected files and traces; recording runs always fail. Record twice when Unix output changes (a `.windows.out` is kept only while it differs). Review the diff. `.gitattributes` keeps `boot_crlf.ks` byte-exact.
- **Telnet recordings** (`src/kOS.Safe.Test/UserIO/Recordings/`): `inetutils-xterm` captured live (GNU inetutils telnet 2.8); `putty-xterm` and `windows-telnet-ansi` **reconstructed** from those clients' defaults (replace with live captures when available). Replayed whole, at every split point, byte at a time and in 150 seeded chunkings, to `.trace` (server side) and `.keys` (keys kOS reads).

## Full build and deployment

`kOS.props.user` (ignored, case-sensitive) points the build at the dev instance and disables CKAN dependency installation:

```bash
dotnet build kOS.sln -c Release --no-incremental
# without kOS.props.user:
#   -p:KSPBT_GameRoot="$HOME/Games/KSP-kOSIM-dev" \
#   -p:KSPBT_ManagedPath="$HOME/Games/KSP-kOSIM-dev/KSP_x64_Data/Managed" \
#   -p:KSPBT_InstallCKANDependencies=false
```

- Expected: **0 errors, 44 existing warnings**. Outputs go to `Resources/GameData/kOS/` (nothing is deployed automatically). Use `KSP_x64_Data/Managed`, not the native-Linux `KSP_Data`.
- **Hashes:** the build is deterministic, but it embeds the commit ID (`v1.0.0+<commit>` in Player.log), so a build made before committing differs from the committed one. **Record `sha256sum` of `kOS.dll`/`kOS.Safe.dll` after committing**, from a build of that commit; the hash recorded in a later docs commit still describes the earlier commit's build.
- **Deploy** (dev instance only, game closed):
  1. `pgrep -af 'KSP_x64|wineserver|umu'`: nothing running.
  2. `cp -a GameData/kOS kOS-GameData-backups/kOS-before-<name>-<date>` (in the dev instance).
  3. `rsync -a Resources/GameData/kOS/ "$HOME/Games/KSP-kOSIM-dev/GameData/kOS/"` (no `--delete`; review stale files separately).
  4. Compare the deployed hashes with the build's.

## In-game testing

### Launch and logs

```bash
~/Games/KSP-kOSIM-dev/launch-dev.sh --check   # preflight only
~/Games/KSP-kOSIM-dev/launch-dev.sh
```

- The launcher selects `~/.local/share/Steam/compatibilitytools.d/GE-Proton11-7-x86_64`, `umu-run` 1.4.4 with `GAMEID=umu-default`, the isolated prefix `~/Games/KSP-kOSIM-dev/.proton-prefix` (Wine prefix `pfx/`), `UMU_RUNTIME_UPDATE=0`, Proton logs in `dev-logs/`. Its warning `unable to use parent for game drive, path /home` is harmless. Override only deliberately (`KOSIM_PROTON_PATH`, `KOSIM_RUNTIME_UPDATE=1`) and record it; never run the game with host Wine or another Proton silently.
- The main menu appears about 30 s after launch. The live log is Unity's **Player.log**: `.proton-prefix/pfx/drive_c/users/steamuser/AppData/LocalLow/Squad/Kerbal Space Program/Player.log` (KSP.log is buffered). Keep a copy per pass in `dev-logs/`.
- **Close the game** only with the user's permission: with the game window focused (`hyprctl activewindow`; see "Focusing the game window" below), `hyprctl dispatch 'hl.dsp.window.close()'` (exits in seconds). Never kill it unasked.

### Dev instance state

- Save **`kOSIM-dev`** (disposable), vessel `test-vessel` on the pad with **two kOS CPUs** (the second, `persistentId 80427999`, added to `persistent.sfs`; backup `persistent.sfs.before-2cpu-20261019`). Boot file `/boot/kosim_boot.ks` (CRLF) on the first. The telnet menu order of the two CPUs is not fixed: check its "GUI Open" column.
- GameData: stock, kOS, ModuleManager 4.2.3, ClickThroughBlocker 2.1.10.23, ToolbarController 0.1.9.14, HarmonyKSP 2.2.1 (dev instance only). kOS `config.xml` (`GameData/kOS/Plugins/PluginData/kOS/`): telnet **enabled** on 127.0.0.1:5410 with permanent listen permission. The dev `settings.cfg` resolution is left alone (user decision).
- Saved command history: `saves/kOSIM-dev/kOS/history/<persistentId>.txt`.
- Disposable scripts in the dev archive `Ships/Script/kosim-check/` (`p2/`, `p24/`, `p3/`, `p3r/`; `p2/` holds symlinks into `kosim-link-target/` beside the game for the link checks). `p3r/`: `fill.ks` (120 numbered lines), `slow2.ks` (a line a second for 2 minutes), `main.ks`/`lib.ks` (main program naming), `longwait.ks`. `p3/`: `fmon.ks` (largest real-time gap between physics ticks), `other.ks`/`otherpower.ks` (part events or power toggle to the other CPU), `rterr.ks` (runtime error two calls deep).
- Never import personal saves or scripts into the dev instance.

### Driving the game remotely

- **Telnet** (tools in the dev instance's `dev-tools/`):
  - `kt.py`: a step client with a VT screen model (answers `DO TTYPE`/`SB TTYPE SEND` with `XTERM`, `DO NAWS` with a size, acknowledges each `WILL` once). Enter = CR, Ctrl-C = 0x03, Ctrl-D detaches, PageUp/PageDown = `ESC[5~`/`ESC[6~`, Ctrl-L repaints; the title arrives as OSC 0/2. Match output lines anchored, not the echoed command. A NAWS value of 255 must be sent as `IAC IAC`.
  - `kd.py NAME CPU [W H]`: a persistent client: screen in `NAME.screen` (on change), output in `NAME.log`, every title change with a timestamp in `NAME.titles`, commands through the FIFO `NAME.in` (`line …`, `send …` with escapes, `naws W H`, `quit`). Use the GUI's size (50×36) for a client on the GUI's CPU (the last resize wins).
  - `vw.sh NAME`: logs the shared view's first and last rows on each change (scrolling, wheel lines per notch).
- **GUI keys:** `hk.sh "MODS" KEY…` sends real key events to the focused game window through Hyprland 0.56's `hl.dsp.send_key_state`, holding modifiers as keys (`Control_L`, `Shift_L`; Unity under Wine needs the key state). Keys are keysym names of the user's **French Mac (AZERTY)** layout: digits and `period` are not available, so type text over telnet. `wtype` does not work (XWayland ignores its keymap).
- **Clipboard:** `wl-copy`/`wl-paste`; save and restore the user's clipboard.
- **Screenshots:** `grim -g "2400,270 1920x1080"` (the game window on HDMI-A-2; check `hyprctl clients`). KSP's Alt+F12 *Input Locks* window shows lock state.
- **The user is needed for:** the main menu and loading the save, all **mouse** steps (compositor pointer warps do not reach the game), and focus clicks: a terminal opened by a script event, or after a power cycle, has no keyboard until clicked. F5/F9 and Escape reach KSP only after a click outside the terminal (its `ControlTypes.All` lock).
- **Script tips:** a trigger set inside a program ends with the program; type triggers at the prompt to produce output while the prompt waits. Keys pressed while a program runs wait in the type-ahead (PageUp/PageDown too; the wheel works at once). Avoid built-in names (`v`, `q`, …) as variables (`CLOBBERBUILTINS`). Don't `pkill -f` a pattern the running shell command also contains.
- **Wine probes without the game:** build a small `net48` exe and run it with GE-Proton's `files/bin/wine` and a throwaway `WINEPREFIX` in the scratchpad, never the game's prefix.
- **In-game probes:** a throwaway `KSPAddon` assembly built with `mcs` against the game's `Managed/` DLLs (`-noconfig -nostdlib`, mscorlib/System/System.Core/Assembly-CSharp/UnityEngine*.dll; Assembly-CSharp also needs `UnityEngine.UI`), deployed as its **own** GameData folder (kOS untouched), logging with a fixed prefix and saving a screenshot with `ScreenCapture.CaptureScreenshot`; removed after the run. Example: `dev-tools/font-probe/`.
- **Focusing the game window** (to close it): `hyprctl dispatch 'hl.dsp.focus({ window = "address:0x…" })'` with the address from `hyprctl clients`; check `hyprctl activewindow` before closing.

### Fonts under Proton (step 3a probe, 2026-10-20)

Probe `dev-tools/font-probe/FontProbe.cs` in the dev instance; one launch to the main menu (KSP 1.12.5.3190, Unity 2019.4.18f1, GE-Proton11-7); logs `dev-logs/Player.log.fontprobe-20261020` (lines `kOSIM font probe:`), screenshot `dev-logs/fontprobe-shot-20261020.png`. Font: JetBrains Mono 2.304 (`JetBrainsMono-2.304.zip` SHA-256 `6f6376c6…7bbf`, OFL 1.1).

- **Unity lists OS fonts from the files in `C:/windows/Fonts` only** (`Font.GetPathsToOSFonts`: 23 paths, 23 names; the prefix has no Consolas, Liberation or DejaVu). The Latin monospace ones: **Courier New**, **Courier New Bold**, **Lucida Console** (the CJK fonts were not tested).
- **The terminal's font under Proton was Courier New Bold** (`TerminalFontName` "Choose Font" is not found; the first fallback the OS has wins). Since step 3c it is Lucida Console (below, "Terminal font fallback order").
- **A bundled TTF cannot be loaded at run time:** a private GDI registration (`AddFontResourceEx`, `FR_PRIVATE`) succeeds (1) but Unity never lists or finds the font, whether registered before or after kOS lists the fonts; `Font.CreateDynamicFontFromOSFont("JetBrains Mono", …)` silently returns **Arial** (proportional: `i` 4 px, `W` 15 px at 16). `new Font(path)` (Unity's internal `CreateFontFromPath`) returns an empty, non-dynamic font with no glyphs, also for a missing file, without an exception. TextCore's `FontEngine` is public only for loading faces and glyph metrics (rendering and `FontAsset` are internal), so it cannot give IMGUI a `Font`. Left: an asset bundle built with Unity 2019.4.18 (not pursued, user decision).
- **`Font.HasCharacter` counts glyphs reached through OS fallback** (Courier New Bold "has" `∀⇒ℝ漢☃`), so it cannot tell which font loaded; compare advances (`IsFontMonospaced`) or look.
- Cell metrics at `CHARHEIGHT` 16: Courier New Bold advance 10, ascent 14, line height 18, `█` from −6 to 15; Lucida Console advance 10, ascent 17, line height 21, `█` from −5 to 17 (taller glyphs, so descenders reach lower in a 16 px row: check the bottom row's padding in game with 3c). Advances are whole pixels and constant at sizes 8–24 for both; cursor ticks at `column × advance('X')` stayed aligned over 40 columns in the screenshot.

## Behavior changes from upstream

Intentional differences from upstream kOS (`8f281a45`), by area. Each was agreed with the user where a choice was involved; the issue labels (`BUFFER-01`, `D2`, …) are the original findings and decisions.

### Terminal screen and view

- **Screen and view are separate** (P2-4, decisions D1/D2). All output, `PRINT AT` (row 0 = screen top), the cursor, the prompt, `CLEARSCREEN`, `TERMINAL:WIDTH/HEIGHT` and resize watchers act on the screen; PageUp/PageDown (10 rows) and the wheel move only the **view**, one per terminal, **shared by the GUI and all telnet clients**. Before (VIEW-01), a command typed after PageUp wrote into the middle of the scrollback. `ScrollVertical` now moves the view (a mod calling it no longer moves where output goes). `CursorRowShow` is view-relative; out of view the GUI draws no cursor and telnet puts it at the nearest edge.
- **Scrollback is bounded at 2,000 lines** (`ScreenBuffer.MAX_SCROLLBACK`); the screen itself is never trimmed. `TopRow` counts dropped lines.
- **Follow-output** (O2): output no longer brings a scrolled-back view to the bottom; it stays on its lines (or the oldest left, if the scrollback drops them) and the state shows `New output` until the view is back. Typing, Enter, Ctrl-C at the prompt and `CLEARSCREEN` bring it back. A view at the bottom follows output as before.
- **Mouse wheel** (O1) over the GUI terminal's characters scrolls the shared view 3 lines per notch (measured under Proton), also while a program runs. Hovering a kOS window locks camera controls, so the camera does not zoom.
- **A shorter screen keeps its bottom** (VIEW-04): the cursor's row stays on the bottom row, rows above go into the scrollback, rows below the cursor are dropped. Upstream kept the top row, so old rows reappeared and later output overwrote them.
- **Size requests are normalized** (BUFFER-04): 0 keeps that axis; other values clamp to 1–160 rows / 1–255 columns in the model (script minima stay 3 rows / 15 columns). An unchanged effective size is a no-op (no callbacks, no watcher events). `SubBuffer.SetSize` rejects negative rows and non-positive columns.
- **Overlay composition** (BUFFER-01) replaces only the visible intersection of rows (no exceptions on clipped overlays); **cursor reads** are bounds-checked on the snapshot (BUFFER-02); an invalid cursor is hidden.
- **Render fault recovery** (BUFFER-03): faults in font/geometry preparation and window drawing show a fallback ("Terminal display fault…" with Retry/Close/Resize), retried every 2 s, logged at most once per 10 s with suppressed counts; the CPU keeps running. GUI groups and colors are always unwound. Fatal errors and `ExitGUIException` still propagate. Outside the boundary: snapshot acquisition, input, title, telnet output, `GUI.Window` itself.
- A wrapped command at the bottom of the screen no longer overwrites the last output row, and a command taller than the screen runs (PROMPT-01).

### Prompt and history

- **History** (P3-1, TERM-05; `CommandHistory`): Up from the line being typed keeps it, and Down past the newest brings it back with its cursor. The last **500** commands are kept. Empty commands and Ctrl-C lines are not recorded; repeats of the newest are skipped (as upstream). Interpreter error locations use one number per line, so line 2 of a multiline command shows the right line (upstream showed the next command or failed); "interpreter, line N" counts lines, and the 16-bit `Opcode.SourceLine` limit (32,767) applies to lines.
- **Saved history** (H3): each CPU's history is saved to `saves/<save>/kOS/history/<persistentId>.txt` (`-<n>` appended for extra kOS modules on one part; flight scene only), UTF-8 without BOM, a header line, `\`/`\n`/`\r` escaped; files over 16 MiB are not read. Read on first use, written (through the atomic archive writer) after each recorded command; failures are logged once per CPU, never shown at the prompt. **A reboot keeps the history** (upstream cleared it). Quickload/revert do not roll it back; docking keeps it; clear it by deleting the file. A destroyed part's file stays.
- **Leading space** (H4): a command starting with a space runs but is not recorded or saved.
- **Ctrl+R search** (H1/H2; `CommandLineEditor`, GUI and telnet): `(search) 'query': match` on the input line; typing extends the query, Backspace shortens it, Ctrl+R again finds the next older match; Enter runs the match (nothing if there is none); Esc or Ctrl+C leaves with the line unchanged (Ctrl+C in a search does not break); cursor keys, Ctrl-A/E, Delete and Tab take the match for editing. **Telnet Ctrl-R no longer repaints** (Ctrl-L still does; the welcome menu keeps Ctrl-R).
- **Ctrl-C at the prompt** (decision 4) ends a `WAIT`/`GETCHAR` typed there, and cancels a running `COMPILE` or `RUNPATH` compile: the prompt waits for the compile thread (it cannot be stopped), then discards its result (no KSM, nothing printed). The Ctrl-C handler no longer compiles the emptied line; under a comm delay no empty command is queued. A failing `COMPILE` reports its error once (EXEC-01; upstream repeated it every update and blocked the prompt), and a failing `RUNPATH` compile reports its own error (EXEC-02).

### Paste, copy and selection (GUI terminal)

- **Paste** (P3-2/P3-3): Ctrl+V and Shift+Insert (not with Alt, so AltGr+V still types). One line is typed at once; text with a line break asks first ("Paste" box: line count, first lines, "Run N lines"/"Cancel"; Enter runs, Esc cancels, Ctrl+C cancels and breaks; other keys type nothing). CR LF/CR/LF become one Enter, tab is kept, other control characters and private-use key codes are dropped, so pasted text cannot break, detach or resize. Over **65,536** characters it is refused whole with a message. Closing the terminal or losing power drops a waiting paste. Telnet paste is unchanged.
- **Ordering:** a key that may be queued (GUI typing, mods with `allowQueue`) now waits behind keys already in the type-ahead instead of jumping ahead of a long paste.
- **Copy** (P3-4): **Ctrl+Shift+C** copies the selection if there is one, else the rows in view (trailing spaces and blank rows trimmed, LF between rows). **Ctrl+Shift+E** copies the last error printed on this CPU's terminal with its location (kept across reboots, in memory; control characters other than tab/LF dropped: COPY-01). A 2-second notice shows in the status strip before the size (`Copied N lines`, `Copied the last error`, `Nothing to copy`, `No error to copy`, `Copy failed (see KSP.log)`). Upstream ignored Shift: **Ctrl+Shift+C no longer breaks and Ctrl+Shift+E no longer moves to the line end in the GUI.**
- **Mouse selection** (O3): dragging over the characters (or their padding) selects cells in reading order (instead of moving the window; the header and the status strip move it); a click clears it; dragging past the top or bottom scrolls the view a line every 50 ms. Selections follow their text as output scrolls. Wrapped lines copy as separate lines.

### CPU state (D2)

- The GUI status strip shows the state (Phase 4; before, the bottom bar after the size: `50x36   Running 0:/launch.ks`), and the telnet title gets ` - <state>`. Nothing is printed on the terminal. States: `Off`, `No power`, `Starting`; `Ready`; `Running <program>` / `Waiting in <program>` (the first program `RUN`/`RUNPATH` or the boot file loaded, not a library it runs); `Running a command` / `Waiting in a command`; then `Delay 2.4 s` (in place of `Ready`), `No connection`, `New output`, joined with ` | `. Refreshed at most every 0.25 s, only while the GUI is open or a client is attached (this also bounds telnet title updates; upstream set the title on every `OnGUI`).
- "Waiting" means held by a yield for at least 0.5 s of game time; once shown it is **kept until 1.5 s pass without a held wait** (`WaitingDisplay`, STATUS-01), so a loop on `WAIT 1` shows `Waiting in` steadily. The prompt, another program or a power cycle start afresh.

### Terminal window (Phase 4, step 1)

Our own chrome in IMGUI (`TermWindow`; layout `kOS.Safe.Screen.TerminalChromeLayout`, strip `StatusStrip`, style `kOS.Screen.KOSStyle`). Not yet checked in game.

- **Header and frame** instead of KSP's window skin and the monitor bezel: the title `vessel · CX-4181 · tag` (no tag part without a name tag; ` - ` and `...` if KSP's font lacks `·`/`…`, logged once as `kOS: chrome font …`), shortened with an ellipsis; a settings button and a close button (step 3: smaller/larger text and close). A 1-pixel frame (2 at UI scale 1.5 and above) in the terminal's green while the terminal has the keyboard. The telnet title is unchanged.
- **The window moves only by its header and the strip** (upstream: anywhere not on a control). The body (characters, padding, the gutter of the step 2 scroll bar) does not move it; the wheel works over the whole body.
- **Status strip** (one line, replaces the bottom bar): the D2 state after a dot colored by its kind (Ready, Running, Waiting/Starting, Delay, No power, Off); then chips when they apply: `Delay …` (when not already the state), `No connection`, `New output` (a click brings the view back to the screen), `N telnet client(s)` (replaces the "N telnet clients attached" label: **BAR-01**); on the right the copy notice, then the size, then the resize grip. When narrow: chips go lowest priority first (telnet count, New output, Delay, No connection), then the state is shortened with an ellipsis, then the notice, the size, the state text and the dot go. Nothing overlaps. Under delay at the prompt the state reads `Delay 2.4 s`, as in the title.
- **Settings popover** (the header's settings button; **removed in step 3**, below): Reverse screen, Visual beep, Key click, Brightness (horizontal slider), Character size (−/+ in steps of 2, 4–48 px). It replaces the bottom toggles, the vertical brightness slider, the font −/+ buttons and the Close button. It closes with its button, Esc (while the terminal has the keyboard), a press outside it, or closing the terminal; a click on it counts as a click on the terminal (`KOSManagedWindow.ContainsPoint`), so the keyboard stays. Covered by the `kOSMouseOver` lock. Script API unchanged. **The settings are set only when changed:** upstream set brightness, reverse and visual beep from its controls on every frame, so `TERMINAL:BRIGHTNESS` read back float-rounded (0.7 → 0.699999988…) while the window was open; it now reads what was set.
- **Size:** for the same cells the window has another pixel size (at UI scale 1: width = 6 + columns × cell width + 8, height = 20 + 12 + rows × cell height + 18, at least 150 px wide; upstream +65/+100). The chrome follows KSP's UI scale (`GameSettings.UI_SCALE`, clamped 0.5–3; the dev instance uses 1.3); cells follow `CHARHEIGHT`. A UI-scale change keeps the cells and resizes the window. A resize drag stops at the script minimum, 15 × 3 cells (upstream 200 × 200 px); the fault fallback keeps 200 × 200.
- Removed: `TermWindow.DrawTelnetStatus` (protected). The bezel and button textures stay in `GFX/` unused; the missing-texture screen still checks them as a sign of a broken install.
- Unchanged: the grid rendering, cursor, selection highlight, render-fault fallback and missing-texture screens (now on the window background), the paste box (step 5), focus and lock behavior.

### Terminal window geometry and scroll bar (Phase 4, step 2)

`TermWindow`; logic in `kOS.Safe.Screen` (`TerminalGeometry`/`TerminalGeometryFile`, `WindowPlacement`, `TerminalResize`, `TerminalScrollBar`; shared file code `CpuTextFile`). Not yet checked in game.

- **Saved geometry:** each CPU's window position, cells and character height are saved to `saves/<save>/kOS/terminal/<persistentId>.txt` (same name and flight-only rule as the history: `-<n>` for extra kOS modules on a part; nothing in the editor scene). A header line, then `x`, `y`, `columns`, `rows`, `charheight` as `key = value`; UTF-8 without BOM; files over 4 KiB are not read; a value that is not a whole number or is outside the script limits (or ±100,000 px for the position) is ignored and its default used. Written through the atomic archive writer when something changed, checked every 0.5 s, at most every 2 s, never while mouse button 0 is held, and once more when the window is destroyed; failures logged once per CPU. **No file is written until the geometry changes.** Every change counts: moving, resizing, the header's size buttons (the popover's character size in step 2), a script's `TERMINAL:WIDTH`/`HEIGHT`/`CHARHEIGHT`, a telnet client's size (D3). Quickload and revert do not roll it back; delete the file to go back to the defaults.
- **Restore:** a CPU's terminal reopens where and at the size it was, also after reloading the save (upstream: always at 50,60 with the configured default size). The configured default width, height and font size now apply only to values with nothing saved. `TERMINAL:WIDTH`/`HEIGHT`/`CHARHEIGHT` read before any script sets them give the saved values; a boot script that sets them still wins.
- **Kept on screen:** every time the window is drawn it is moved, as little as possible, wholly onto the game's screen (after a restore, a resolution or UI-scale change, a resize by a script, or while it is moved by its header or strip, which stops it at the screen's edges). A window larger than the screen keeps its cells and is put at the left/top edge along that axis, so its header stays on screen; a smaller size is up to the user (resize) or the script.
- **Resizing by an edge or a corner:** a band 4 px wide inside the frame (scaled with the UI) resizes from that edge; within 12 px of a corner, both edges. The grip is the bottom-right corner. The header buttons keep their clicks (the top-right corner does not resize), and while the scroll bar shows, the right edge beside its track is the scroll bar's. The edge under the pointer is drawn in the accent color (no OS cursor change). The window follows the pointer snapped to the nearest whole cells, the opposite edge staying put (the left and top edges move the window), between the script minimum 15 × 3 cells and the most that fit between that edge and the screen's (the script maximum, 255 × 160, without a screen); a window already larger than the screen can shrink but not grow. A `columns`x`rows` box shows in the middle of the body during the drag; **the screen takes the new size once, on release** (as the step 1 grip; a release without a change of cells is no resize at all). The characters stay clipped to the body meanwhile.
- **Scroll bar:** a thin bar in the gutter, shown only while the view is scrolled back (by the wheel, PageUp or a selection's edge drag) or its thumb is held. Its thumb's length is the share of the lines in view (at least 16 px scaled, never longer than the track) and its position the view's place in the scrollback (at the bottom: the screen). Dragging the thumb moves the shared view (telnet clients follow it, as with the wheel); a press above or below the thumb moves the view by the screen's rows. Back at the bottom it goes away once released; `New output` is unchanged.
- Changed from step 1: the grip's own resize code is gone (it is the bottom-right corner of the edge code); `CommandHistoryFile` uses `CpuTextFile` (no change in behavior or messages).

### Terminal look and settings (Phase 4, step 3b)

`kOS.Safe.Screen.TerminalPalette` (colors as data, contrast-tested), `TerminalChromeLayout` (header buttons), `TermWindow`, `KOSStyle`, `kOS.Suffixed.Config`. Not yet checked in game.

- **One modern green look** (no theme choice, no script API for it): text `#7EE08F` on `#0F1311` (upstream `#80FF80` on black), unpowered text `#ABB3AE` (upstream `#CCCCCC`), chrome greys `#1A1F1C` (header, strip), `#151A17` (panels), `#2A322D` (chips), chrome text `#DCE3DE`, dim `#98A29C`, frame `#353D38`; accent (focus border, edges, notices) the text's green; new state-dot colors. The selection is the text's color at alpha **0.25** × brightness (was 0.35), so the text under it stays readable. Tests (`TerminalPaletteTest`, WCAG 2 ratios in sRGB): text ≥ 7:1 and ≥ 4.5:1 at the default brightness 0.7; unpowered text, the cursor's character and selected text ≥ 4.5:1 at brightness 1 and 0.7; chrome text ≥ 7:1, dim and chip text ≥ 4.5:1 (also hovered); focus border, state dots and scroll thumb ≥ 3:1.
- **Header:** title, then **smaller text** and **larger text** buttons (a small and a large bold `A` in the chrome font; plain ASCII, which KSP's font has), then close. Each changes `CHARHEIGHT` by 2 within 4–48 px (as the old −/+), applied between passes, and is dim and inert at its end of the range; the size is saved with the step 2 geometry. Resize edges exclude all three buttons (the top-right corner still does not resize). The minimum window width (150 px at UI scale 1) is unchanged.
- **The settings popover is removed**, and with it the window's controls for reverse screen, visual beep, key click and brightness. Scripts keep `TERMINAL:REVERSE`, `VISUALBEEP`, `BRIGHTNESS`, `CHARHEIGHT` unchanged; reverse screen has no control at all now.
- **New mod options** (`config.xml`, listed by the toolbar panel as toggles, and as `CONFIG:VISUALBEEP`/`CONFIG:KEYCLICK` and in `LIST CONFIG`): `TerminalVisualBeep` (alias `VISUALBEEP`, false) sets a new terminal's `TERMINAL:VISUALBEEP` when its window attaches to the CPU, as `TerminalBrightness` does for brightness; `TerminalKeyClick` (alias `KEYCLICK`, false) makes every GUI terminal click on each key typed, read on each key (it was a per-window toggle, not saved). An older `config.xml` without them loads with the defaults; no key renamed or removed. `IConfig` (a public interface) is unchanged: the window reads them from `kOS.Suffixed.Config`.
- Removed code: `TermWindow.DrawSettings`/`DrawSettingsContents`/`CloseSettingsOnClickOutside` and the `ContainsPoint` override (`KOSManagedWindow.ContainsPoint` stays virtual), `KOSStyle.Icon.Settings` (gear), `KOSStyle.IconButton`'s `active` parameter. `KOSStyle.Toggle`/`Slider`/`Button` are kept for steps 4–5 (unused now).
- Unchanged: the grid rendering, telnet output, the paste box (step 5), focus and locks, the font (3c, below).

### Terminal font fallback order (Phase 4, step 3c)

`kOS.Safe.Screen.TerminalFontChoice` (tests `TerminalFontChoiceTest`), used by `TermWindow.GetFontIfChanged`. Not yet checked in game.

- The GUI terminal tries the font picked in the mod options (`TerminalFontName`) first, unless it is a placeholder (`_not_chosen_yet_`, `Choose Font`) or blank, then upstream's fallbacks in upstream's order with **`Lucida Console` inserted before the Courier entries**; the first the OS has is used, as before (not checked for monospacing, as upstream).
- **Under Proton the default font is now Lucida Console** (regular weight; the prefix has no bold Lucida) instead of Courier New Bold. Native Windows (Consolas), macOS (Monaco) and Linux (Liberation Mono) find what they found before; only a system without those but with Lucida Console changes.
- A picked font is kept, Courier New Bold included; the font picker, `TerminalFontName`, the chrome's font (KSP's UI font) and the editor's font list (step 5) are unchanged.
- To check in game: Lucida Console's taller ascent (17 against 14 at 16 px, line height 21 against 18) puts its glyphs lower in each row, so the bottom row's descenders and underscores against the padding (at least 4 px) at `CHARHEIGHT` 8–24, `█` and box drawing between rows, and the lighter weight's readability.

### Telnet

- **Parsing and sessions** (NET-01/02; `kOS.Safe/UserIO`): an incremental RFC 854 parser with a persistent UTF-8 decoder (upstream leaked option bytes as Ctrl-X/Ctrl-C, lost escaped IAC data, failed on split commands and split characters); cooperative, bounded shutdown without `Thread.Abort`; serialized writes with a 5 s timeout; Unity objects destroyed on the game thread. NAWS values clamp to 255×160 (0 = unchanged). Size, type and negotiation messages inside input discarded by a break are still processed. The refusal warning no longer ends in a literal `DIE`.
- **Key decoding and bounded queues** (review NET-03): keys split across reads decode as one key; a lone Escape arrives after 100 ms; each CR or LF is one Enter (blank lines in pastes kept). Interrupts, resizes and repaint requests overtake waiting text; an interrupt discards text typed before it; only the latest unread resize applies. Input and output backlogs are bounded (limits below): excess typed text is discarded with a notice; a stalled client's unsent output is replaced by a full repaint (title resent, modes restated). Large pastes are typed over several updates. Unrecognized sequences are still typed as text and SS3 is not decoded (user decision).
- **Short terminal types** (`ANSI`, `VT52`, `dumb`, empty) get the plain mapper instead of ending the session (fixture NET-03).
- **Screen diffs** compare rows by content (VIEW-05): rows of a wrapped input line moved by a print are now sent. A full repaint uses a fresh snapshot (VIEW-03: rows of the old width after a resize are no longer sent).
- **Lifecycle** (P2-3, LIFE-02): clients get the usual `{Detaching from …}` message and return to the menu when their CPU goes (vessel unloaded or destroyed, scene change); picking a CPU that disappeared gives `That CPU is no longer available.` The welcome menu detaches when a CPU is chosen, reads a bounded number of keys per update, and no longer echoes kOS key codes.

### Input ownership and mouse (INPUT-01)

- **Keyboard** (policy: [KOSIm-roadmap.md](KOSIm-roadmap.md), "INPUT-01 design"; `InputOwnership`, `KOSInputOwnership`): one kOS window owns the keyboard; it holds `ControlTypes.All` under the name `kOSTerminal` (kept for other mods and `VesselUtils`). Action groups, `DOEVENT "Open Terminal"`, `DOACTION` and `GUI:SHOW` no longer take the keyboard. Hidden UI (F2 with `ObeyHideUI`) and the flight-results dialog release it and give it back; power loss releases it for good, and an unpowered terminal cannot be focused. The editor popup and the in-flight name-tag window now lock while focused. Escape does not reach the pause menu while a terminal has the keyboard (the lock holds `PAUSE`). Limit: an event delayed by RemoteTech still takes focus.
- **Mouse** (`MouseBlocking`, `KOSMouseBlocking`): while the pointer is over any drawn kOS window, the scene's mouse controls are locked under `kOSMouseOver` (flight `CAMERACONTROLS | MAP_UI | TARGETING`; VAB/SPH `EDITOR_SOFT_LOCK | CAMERACONTROLS`; space center `CAMERACONTROLS | KSC_UI`; tracking station `CAMERACONTROLS | TRACKINGSTATION_UI | MAP_UI`), kept for a press that started over the window and released 2 frames later (a click that closes a window does not reach the scene, #2759). Never keys or flight controls; `CAMERACONTROLS` also pauses the camera keys while hovering. No ClickThroughBlocker integration needed (checked with CTB 2.1.10).
- **Ownership on paths:** `KOSInputOwnership` releases everything on scene change; textures are loaded once per path and shared, window-owned skins/textures destroyed (LIFE-01); threaded compile events and telnet worker handles closed (LIFE-03).

### Files and volumes

- **Raw I/O** (IO-01/02, policy "Preserve text APIs": [file-io-compatibility.md](file-io-compatibility.md)): `READRAW`/`WRITERAW` and `VolumeFile.ReadAllRaw`/`WriteRaw`, `Volume.SaveFileRaw` keep exact bytes; copy, move and boot-file seeding are byte-exact. Legacy text APIs are unchanged except that the Windows newline conversion no longer adds CRs on repeated saves (CRLF stays CRLF). Under Proton text saves use CRLF (Windows branch), by design.
- **Atomic archive saves** (IO-03): written to `.kosim-save-<GUID>.tmp` beside the destination, then `File.Replace`/`File.Move`; no destructive fallback. A crash can leave visible temporary files (no sweep).
- **Volume identity** (IO-05, PATH-01): path functions use a volume or file object's own volume, a `PATH` value keeps the volume it was made from, and paths without a volume id use the current volume itself, so two disks with the same name are no longer confused. A detached disk's object or path fails with "Volume not found". `LOG … TO` and `COMPILE … TO` take objects and paths the same way. Strings with a volume id resolve as before; printing and saving a `PATH` is unchanged.
- **Move onto itself** (MOVE-01): `MOVEPATH` onto the same entry (same path, its own directory, an alias, a default extension, another case on a disk or a case-insensitive archive) fails instead of deleting the file.
- **Containment, links, images** (IO-04): archive and GUI image paths are checked by directory boundary. Links in the archive are followed to read, write and list; a delete never enters a link and removes the link itself. **Under Wine a delete or move of an entry that is or contains a link is refused** (deleting a link there deletes its target). GUI images over 16 MiB or 4096 px a side are refused with a script error.

### Editor, script GUI, settings, formatting

- **Editor transactions** (EDIT-01/02; `EditorDocument`): dirty state compares text with the last load/save; a load replaces the document only after it succeeds; save/discard/cancel dialogs name both files; failed saves keep the draft and the request; the popup is frozen under its dialog.
- **Script GUI**: `POPUPMENU:VALUE` sets the index once (one `ONCHANGE`) and a missing value clears the label (WIDGET-01). Widget callbacks keep their GUI origin under comm delay or `EXTRADELAY` and are delivered once, in order (WIDGET-02).
- **Settings panel**: integer fields allow the digits their range needs, so a 5-digit telnet port can be typed (PANEL-02).
- **Display formatting** (FMT-01): `PRINT`/`:DUMP` of lists and lexicons show nested primitives inline again (an upstream regression since 1.3.0.0, #2939). JSON, messages and saved queues are unchanged.

## Limits and constants

| What | Value |
| --- | --- |
| Telnet input backlog per client | 262,144 characters; after overflow, input resumes at half |
| Telnet output backlog per client | 262,144 characters, plus at most one write |
| Telnet keys moved per client per update | 4,096 and 4 ms (the first key always) |
| Type-ahead below which telnet text is moved | 4,096 keys |
| Type-ahead keys fed to the prompt per update | 2,048 and 4 ms |
| Unfinished key sequence timeout | 100 ms |
| Welcome menu keys per update | 4,096 |
| Telnet subnegotiation / write timeout | 1,024 bytes / 5 s |
| GUI paste | 65,536 characters (refused above) |
| Command history | 500 commands; saved file read up to 16 MiB |
| Scrollback | 2,000 lines |
| Screen model size | 1–160 rows, 1–255 columns (script minimum 3 × 15) |
| PageUp/PageDown, wheel | 10 lines; 3 lines per notch |
| D2 state | refresh 0.25 s; waiting after 0.5 s held, kept 1.5 s |
| Render recovery | retry 2 s; one log batch per 10 s |
| Terminal geometry file | read up to 4 KiB; checked every 0.5 s, saved at most every 2 s; position within ±100,000 px |
| GUI terminal resize band, corner, scroll thumb | 4 px, 12 px, at least 16 px (at UI scale 1, scaled with it) |
| GUI images | 16 MiB file; 4,096 px a side |

All are code constants (user decision), not `CONFIG` settings.

## Known issues and open findings

Open or recorded, not fixed (most likely upstream unless noted):

- **BAR-01** (kOSIM layout): the "N telnet clients attached" label overlapped "Keyclicker" and the D2 state text. Fixed by the Phase 4 status strip (headless-tested; not yet checked in game).
- **POWER-01:** a CPU powering itself off with `DOEVENT("Toggle Power")` logs `KOSBadJumpException`; no other effect. Compare with an upstream build first.
- **LIFE-04** (left as is, user decision): a disposed script GUI keeps its window, texture and skin until power-off or a scene change (about +50 MB per 300 GUIs).
- **T4** (known edge case): two telnet clients, one resized to 1×1 and typing, made the game run away (memory grows by GBs, ~0.1 fps); sizes at the script minimum and above are fine. Related: at 1×1 telnet output is only scroll codes (T4-DISP); kOS answers every `DO ECHO`/`DO SGA` with `WILL`, so non-RFC 1143 clients loop (T4-NEG).
- **Telnet:** a full repaint sends empty cells as NUL (G3-NUL; text after them shifts in a real terminal); the welcome header shows `INITIAL_UNSET` for an 80×24 client (MENU-TYPE); the menu wraps below 50 columns; `ANSI` mapping is not implemented (`TerminalAnsiMapper` unused); a raw send to a client that stopped reading can block the game thread up to 5 s; malformed-input log lines are not rate-limited; several clients pasting at once can interleave within a line; typed private-use characters act as kOS key codes.
- **GUI:** controls overlapped at 20 columns (G3-UI; the Phase 4 chrome has no such controls, to check in game); the "Paste" box is translucent with a tiny hint (PASTE-UI-01, left as is); after a narrowing, the prompt's empty input line covers rows below it with blanks until a key is typed; a selection made before `CLEARSCREEN` may highlight new text at the same rows; AltGr+V on the French Mac layout types `?` (Wine maps the `diamond` keysym).
- **Files:** a save onto a file link replaces the link with a regular file (net48 cannot read link targets); legacy archive `READALL` can still corrupt short binary content (use the raw API); IO-03 commit safety relies on the platform's Replace/Move, without power-loss durability or external-change detection.
- **Script GUI:** #3168 (`POPUPMENU:VALUE` from a string fails with an invalid cast) not addressed. The texture cache never evicts.
- **Performance:** the worst update of a 3,000-line paste grows with the scrollback (not investigated).
- **TEST-FLAKE-01** (headless, kOSIM test): `TerminalStatusTest.TheCpuNamesTheMainProgramAndAHeldWait` failed once in about 20 gate runs on 2026-10-20 ("held by wait 2 for over half a second"): it steps fixed game time while `runpath` compiles on a thread in wall-clock time. Rerun the gate; fix proposed in `HANDOFF.md`.

## In-game coverage

Every implemented item has passed in game (KSP 1.12.5.3190, GE-Proton11-7): the telnet, prompt, editor, GUI, boot, render-recovery and file checks on 2026-10-12/15; the Phase 1 fixes, Phase 2 and Phase 3 P3-1 to P3-4 on 2026-10-19; the rest of Phase 3 on 2026-10-20. **Not yet checked in game:** the STATUS-01 fix (`WaitingDisplay`): `slow2.ks` should show `Running` then `Waiting in` once and stay until `Ready`; **Phase 4 steps 1, 2, 3b and 3c** (terminal chrome, geometry and scroll bar, look and settings, font order; checklists in `KOSIm-roadmap.md`, "Step 1 plan", "Step 2 plan" and "Step 3 plan", and in `HANDOFF.md`).

Never performed (still worth doing when relevant):

- Vessel destruction or unload with telnet attached; picking a just-destroyed CPU; editor/list picker/popup open-close object counts.
- D2 states `No power` (starved), `Delay …` (stock CommNet has no delay; needs a delay-providing mod), `No connection`, `Starting`; the status strip at a narrow width (Phase 4 step 1 pass).
- Saved history across a full game restart (accepted as covered by quickload and scene change).
- INPUT-01: ClickThroughBlocker focus-mode settings; name tag in the SPH; flight-results dialog; F2 with `ObeyHideUI` off; editor confirm dialog with typing; the editor-popup key leak baseline on upstream.
- Files: a volume object kept across undocking; `EDIT` of a file object; an externally locked archive destination (needs a Windows-side lock); third-party volume providers.
- Telnet: real PuTTY and Microsoft Telnet clients; a stalled real client's repaint after a resume; RemoteTech progress overlay (not installed).

## Checklist for user-facing changes

Before a change reaches the user, cover with tests and a Proton pass as relevant:

- Boot and representative flight scripts, source and compiled KSM.
- Fixed-position dashboards (`PRINT AT`, cursor, `CLEARSCREEN`, dimensions, resize watchers).
- Prompt versus script `GETCHAR`, communications delay or loss, paste, interrupts, several viewers (GUI and telnet together).
- Focus, hide, modal, close and scene transitions; no flight-key leakage or stale locks (Alt+F12).
- Save/craft loading, power loss and recovery, mod-facing terminal injection (`ProcessOneInputChar` signatures).
- Fonts, clipboard and input under Wine, UI scales, file paths and case under Proton, native resource cleanup, slow receivers, large-paste frame time.

Record KSP, Proton and mod versions, the build's commit and hashes, steps, and the Player.log copy. Steam or Proton updates can change behavior with the mod unchanged.
