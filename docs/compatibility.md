# Changes from upstream and compatibility contracts

Baseline: upstream **1.6.0.1**, fork point `8f281a459b6ea0ba917c91bbbc117e68bb948199`
(the tag's tracked tree differs only in `CHANGELOG.md`). This is the maintained summary
of intentional differences, not a transcript of implementation sessions.
See the [changelog](../CHANGELOG.md) for releases and the [roadmap](KOSIm-roadmap.md)
for unresolved findings. The [manual](../doc/README.md) documents user/script operations.

## Installation and versioning

- kOSIM **replaces**, not co-installs with, upstream kOS. Keep assembly names `kOS` and
  `kOS.Safe`, `GameData/kOS`, part identities, script syntax, KSM/save/craft formats and
  existing public mod entry points (notably `TermWindow.ProcessOneInputChar` overloads).
  Added logic sits behind those APIs; intentional behavior changes are listed below.
- `KSPBT_ModVersion` in `kOS.props` drives assembly/file/informational versions,
  kerboscript `CORE:VERSION`, generated `kOS.version` and Sphinx's release value.
  First three fields follow upstream major/minor/patch; fourth is a **continuous fork
  counter**, e.g. `1.6.0.103`, `.104`. Do not reset it on upstream integration.
  KSP's assembly attribute has only three fields (`1, 6, 0` on this line).
- Exact incorporated upstream release and actual fork point are separate properties
  (`KOSIM_UpstreamVersion`, `KOSIM_UpstreamCommit`). Verify source/history when updating them.
  Tag `1.6.0.1` is `d4388439c712994e47258ed7514e863f79da632b`.
- Historical `1.5.1.100`–`.102` already inherited upstream 1.6.0.1; their incorrect
  prefixes, tags and artifacts remain history. Correcting to `1.6.0.103` changes exact
  version comparisons/pins and KSP identity, but requires no data migration.
- Generated `kOS.version` deliberately has no AVC `URL`/`DOWNLOAD`, so KSP-AVC never
  offers upstream releases as updates.
- **CKAN:** the fork is packaged as `kOSImproved`, which provides and conflicts with `kOS`
  (see [CKAN packaging](ckan.md)). Upstream's `kOS` entry uses epoch `1:`
  (`1:1.6.0.1`); fork versions never compete with it. Manually overwriting a
  CKAN-managed upstream install still leaves upstream's registry/file ownership
  intact, and a manually installed DLL can be auto-detected as upstream `kOS`. Use
  CKAN to remove upstream `kOS`, then install `kOSImproved`.
- **Intentional change (1.6.0.103):** `kOS.version` claims **KSP 1.12 only**
  (`KSP_VERSION`, `_MIN` and `_MAX`), not the inherited 1.8–1.12. CKAN therefore offers
  the fork only to 1.12 instances, and KSP-AVC may report it as incompatible with older
  games. The 1.8–1.11 code paths were not removed, only no longer claimed. Even 1.12
  is a metadata claim; runtime validation is Windows KSP 1.12.5.3190 under GE-Proton11-7 only.
- Release ZIPs come from `.ci/package.py`: tracked `GameData/kOS` files, built DLLs and
  `kOS.version`, plus `LICENSE.md`, `THIRD_PARTY_NOTICES.md`, `Licenses/` and a generated
  `SOURCE.txt`. CKAN installs and removes those files and leaves `Plugins/PluginData/` alone.

## Screen, session and threading

- One logical character grid per CPU; GUI and telnet share **one view** and dimensions.
  `PRINT AT`, cursor, `CLEARSCREEN`, prompt and resize watchers act on the output screen.
  Reading old output never redirects writes. `ScrollVertical` now moves the view;
  `CursorRowShow` is view-relative. Out-of-view cursors are hidden in GUI and clamped
  to an edge for telnet.
- Scrollback is bounded at **2,000 lines**, excluding the live screen. `TopRow` counts
  dropped lines. Output follows only when already at the bottom; otherwise the view
  stays on its text and shows `New output`. Typing, Enter, prompt Ctrl+C and clear return
  to the live screen. PageUp/PageDown move 10 lines; GUI wheel moves 3 per notch.
- Last resize wins, applied on the main thread. Model sizes clamp to 1–160 rows and
  1–255 columns; zero keeps that axis. Script minima remain 3 rows / 15 columns.
  An unchanged effective size does nothing, including no resize notification.
  Shortening keeps the bottom around the cursor; width changes cut/pad, not reflow.
- Overlay composition uses the visible intersection and cursor reads are bounds checked.
  Render faults show Retry/Close/Resize, retry every 2 s and batch logs every 10 s;
  CPU execution continues. Not every input/snapshot/window-creation error is inside
  that recovery boundary; fatal errors and `ExitGUIException` propagate.
- Processor owns CPU/window; window owns `TerminalSession`; closing a view does not stop
  the CPU. Session disposal detaches telnet clients with a message. Stop script, power
  off and release flight controls are separate actions.
- Reader/writer threads parse/map bytes, use locked queues and own connection state only.
  Session, screen, interpreter and Unity operations stay on the main thread. Only the
  connection writer writes sockets. Shutdown waits at most 250 ms per client for output,
  then closes the transport; normal writes time out after 5 s.
- Snapshots expose live row references unless deep-copied; do not treat them as immutable.
  Refreshes are consumer-gated. Row strings, strip layout and fitted titles are cached;
  headless allocation measurements are not claims of improved in-game frame time.

## Prompt, clipboard and status

- History retains **500 commands**, including a draft/caret when navigating Up/Down.
  Empty/BREAK lines and consecutive duplicates are omitted. A **leading space** runs
  a command without recording it. Interpreter error source lines count multiline input.
- In flight, **one history per save** is shared by every CPU's GUI and telnet prompt;
  each prompt keeps its own draft, Up/Down position, search and unrecorded command.
  A recalled entry or search match is kept by identity while other prompts add or
  trim commands. Line numbers come from one shared counter, so error source lines stay
  correct; with more CPUs, a prompt-defined function's source can be trimmed sooner.
  Outside flight, a CPU's history is in memory only.
- Ctrl+R searches history in GUI and telnet. Enter runs the match; Esc/Ctrl+C cancels
  search without breaking; navigation takes the match for editing. Telnet Ctrl+L repaints;
  Ctrl+R still repaints in the welcome menu, not the attached prompt.
- GUI paste: Ctrl+V / Shift+Insert without Alt; any line break requires confirmation.
  CRLF/CR/LF become Enter, tabs survive; other controls/private-use key codes are filtered.
  Over **65,536 characters** is refused whole. Pending paste drops on close/power loss.
  Queued input preserves order. Ctrl+C retains a reliable break/cancel path.
  GUI Ctrl+D/Ctrl+X still act as Enter; telnet's detach keys remain transport-specific.
- GUI Ctrl+Shift+C copies selection, else visible output; Ctrl+Shift+E copies last error.
  Plain Ctrl+C remains BREAK. Trailing whitespace/blank rows are trimmed for view copy;
  wrapped rows copy as separate lines. Copy filtering drops C0 except tab/LF, **not DEL/C1**.
- Mouse selection follows cells in reading order and auto-scrolls at the edges.
  CPU/program/power/link/input state appears in the strip, toolbar and telnet title,
  never injected into output. Refresh ≤ every 0.25 s while observed (toolbar counts).
  Waiting appears after 0.5 s held and persists until 1.5 s without a held wait.

## Input safety and UI

- One keyboard owner across kOS windows. Focused terminal/editor/in-flight name tag locks
  `ControlTypes.All` under the compatibility name `kOSTerminal`; a click outside releases.
  Only explicit user opens take focus. Scripts/action groups/`GUI:SHOW` do not.
  F2 with `ObeyHideUI` and flight results suspend/restore; power loss releases permanently.
  Close, destroy, vessel loss and scene change must release idempotently.
- Mouse hover locks scene mouse/camera interactions under `kOSMouseOver`, not staging
  or flight controls (camera keys are included in camera controls). A press begun inside
  retains ownership through release plus two frames; a scene press is not taken over.
  Transparent Unity UI raycast blockers prevent part/UI click-through; no extra CTB dependency.
- Scaled IMGUI chrome with one green palette; no themes API or embedded IDE. KSP UI scale
  controls chrome (0.5–3), `CHARHEIGHT` controls cells (4–48). Script suffixes remain.
  Font choice is honored; Lucida Console precedes Courier in fallback order for Proton.
  Runtime-loaded bundled TTF approaches did not work in the tested Unity/Proton setup.
- Terminal moves by header/strip and resizes by snapped edges/corners. After open/restore,
  script/preset resize or screen/scale changes it is fitted wholly on screen. Header drags
  may leave it partly off-screen with 48 scaled pixels of draggable header recoverable;
  an oversize window starts at the top/left without silently reducing its grid.
- Three shared preset buttons: **right-save, left-apply**, hidden when title room <60
  scaled pixels (198 px window threshold at scale 1). Applying resizes once and fits on
  screen; saving does not broadcast. Empty/failed saves give strip notices.
- Right-click unused header space for **background-only opacity 0–100%, default 100%**.
  Text/cursor/selection/controls/focus border and brightness stay independent; no click-through
  even at zero. Menu/dialog/recovery backgrounds stay opaque. Esc/outside press dismisses;
  typing is suppressed while open. Focus/power loss, hide/dialog/fault/close dismiss too.
  This is per window, not a script suffix, and does not affect telnet or other windows.
- Toolbar has CPUs/Settings tabs, read-only power state and descriptive setting labels.
  In flight, **right-click** on the stock or Blizzy button opens every terminal of the
  active vessel when any is closed, else closes them all. Opening this way does not take
  focus. Windows still at the default position (no saved geometry, not moved) are
  stepped down/right by one header height each, after any already open there; the
  resulting positions are saved as moves. Left-click/hover behavior is unchanged.
  Numeric fields commit on Enter, panel click-away, tab change or close. Global visual-beep
  and key-click defaults are additive config options; reverse-screen remains script-only.
- Popup editor is independent/movable/resizable, follows caret only after edits/navigation,
  and preserves dirty drafts across failed save/open transactions. Dialogs name actions and
  Esc cancels. Script GUIs retain their own appearance and keyboard opt-out.

## Persisted UI state

| State | Location and policy |
| --- | --- |
| History | `saves/<save>/kOS/history.txt`, shared by the save's CPUs; UTF-8 no BOM, escaped lines, 16 MiB read cap; read when the first CPU of a flight scene uses it; save at most every 2 s and when any CPU is destroyed; forgotten when the last CPU goes, so the next flight reads it again |
| Geometry | `saves/<save>/kOS/terminal/<persistentId>[-n].txt`; integer `x`, `y`, `columns`, `rows`, `charheight`, optional `opacity`; 4 KiB read cap; checked every 0.5 s, save at most every 2 s, not while dragging, final save on destroy |
| Shared presets | `GameData/kOS/Plugins/PluginData/kOS/terminal-presets.txt`; 3 slots (`1.x`, etc.), 4 KiB read cap, read once; all five geometry fields required, opacity optional |

History and per-CPU files are flight-only. Multiple modules on one part get the `-n`
suffix. Quickload/revert do not roll history/geometry back; reboot keeps history. A game crash
can lose the last 2 s of commands. Delete a file to restore defaults. Missing/invalid
opacity means **100%**, including old presets; no forced migration or write on load.
Files use the atomic writer, tolerate unknown keys and log failures once.

After a save loads **from the main menu**, a worker prunes geometry of parts absent
from the loaded game and all `.sfs` files in that save folder and its `Backup`.
Recent files (<1 minute), links and unknown names are kept. Any unreadable/incomplete
save blocks pruning. Craft-only parts are not retained. No prune on quickload/revert.
The same worker **deletes** the old per-CPU histories (`kOS/history/<persistentId>[-n].txt`,
from 1.6.0.103 and earlier) whatever the saves hold, without merging them, then the
folder when empty. It keeps links, unknown names and anything under a linked folder.
Old temporary save files beside `history.txt` are swept too.

## Files and volumes

- **Preserve legacy text APIs:** `READALL`, `WRITE`, editor/compile/JSON/logs keep text
  semantics, with idempotent Windows newline conversion. Proton text saves use CRLF.
  `READRAW`/`WRITERAW`, `ReadAllRaw`/`WriteRaw` and `SaveFileRaw` preserve bytes/BOMs;
  `WRITERAW` appends a `FileContent`. Copies, moves and boot seeding are byte-exact.
  Third-party volumes must override raw methods to guarantee byte preservation; defaults
  fall back to legacy APIs for compatibility.
- Whole archive saves write beside the destination to `.kosim-save-<32 hex>.tmp`, then
  Replace/Move, with no destructive fallback. A listing sweeps exact lowercase-pattern
  leftovers ≥1 minute old, never links. No power-loss durability guarantee.
- Object/path endpoints retain their actual volume identity; detached objects fail
  rather than resolve a same-named disk. String paths still resolve through the manager.
  Moving onto the same entry, including aliases/default extensions, fails without deletion.
- Containment is directory-boundary-aware. Reads/listing follow links; directory links
  are followed for writes. Appends and `:CLEAR` write through file links, but **whole-file
  atomic saves replace the link**, leaving its target unchanged (verified on Linux Mono;
  whole-file replacement of links not verified under Proton).
- Deletes never enter links. Under Wine, delete/move of an entry containing a link is
  refused because Wine can delete the target. The check walks once; concurrent link
  creation after that walk is not covered. This is not an adversarial filesystem sandbox.
- Widget images are limited to 16 MiB and 4,096 pixels per side.
- Editor dirty state compares content, not length; active and pending documents are
  separate. Popup selection commits once, and delayed widget callbacks retain GUI origin.
  Nested primitive display formatting is fixed without changing serialization formats.

## Telnet limits and behavior

Incremental protocol/UTF-8 parsing preserves fragmented input. CR, LF, CRLF, LFCR and
CR-NUL each form one line break; unknown sequences remain text, SS3 is not decoded.
A lone Escape waits 100 ms. Resize/type/interrupt/repaint control events bypass queued
text; BREAK discards earlier input and dispatcher text until the socket drains.

| Limit | Value |
| --- | --- |
| Input/output backlog per client | 262,144 characters each; input resumes at half, overflow is reported; output overflow requests full repaint |
| Client input moved per update | 4,096 keys / 4 ms; first key always allowed |
| Type-ahead threshold / prompt budget | 4,096 keys / 2,048 keys and 4 ms per update |
| Welcome menu budget | 4,096 keys per update |
| Subnegotiation | 1,024 bytes |
| Protocol output waiting / shutdown drain | 64 KiB (disconnect above) / 250 ms per client |

These are code constants, not settings. Telnet remains **unauthenticated and unencrypted**:
keep loopback/opt-in defaults; do not expose it directly to untrusted networks.
