# KOS Improved — session handoff

## Resume here

**All agreed work is done.** Phases 0–4 are complete (status in `docs/KOSIm-roadmap.md`), and **1.5.1.100** was released on 2026-10-20 (tag `v1.5.1.100` on commit `5bd2afab`; <https://github.com/Jvaxx/kOSIm/releases/tag/v1.5.1.100>). Every implemented item is headless-tested and passed in game under Proton.

**Next action: ask the user what to do next.** Candidates:

- **Terminal geometry presets** (user feature, decisions agreed 2026-10-20: `docs/KOSIm-roadmap.md`, "Planned: terminal geometry presets"). **Step plan written 2026-10-20** (roadmap, "Step plan" under that section), awaiting the user's approval and answers to Q1 (a partial slot counts as empty) and Q2 (no notice on apply). Implement only after approval.
- The rest of EDIT-05 (find/replace, line numbers, highlighting) or the deferred editor conveniences E1/E3/E4 (roadmap, "Workflow").
- POWER-01: a CPU powering itself off with `DOEVENT("Toggle Power")` logs `KOSBadJumpException`; compare with an upstream build first.
- Open issues and never-performed checks: `docs/testing-and-build.md`, "Known issues and open findings" and "In-game coverage" (T4, telnet G3-NUL/MENU-TYPE, #3168, …).
- A later release: raise the fourth version field and follow `docs/testing-and-build.md`, "Versions and releases".

## Workspace state (2026-10-20)

- **Repository:** the user's private GitHub repo `Jvaxx/kOSIm` is `origin`; branch `master` tracks `origin/master`. Upstream kOS is the remote `upstream`, fetch only (push URL `no_push`); local `develop` tracks `upstream/develop`. No GitHub Actions (no `.github/workflows/`). kOS is GPL-3.0: keep the license and notices if builds are shared.
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

Last sessions (2026-10-20, docs only): wrote the geometry-preset step plan; recorded the geometry-preset decisions; trimmed the roadmap and this handoff; folded `phase2-contracts.md` and `file-io-compatibility.md` into the roadmap and `testing-and-build.md` and removed them. Gate and build not rerun (no code change).
