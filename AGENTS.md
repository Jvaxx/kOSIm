# KOS Improved — agent instructions

## Start each session

1. Read `docs/HANDOFF.md` completely before making changes.
2. Read `docs/KOSIm-roadmap.md` for the agreed priorities and constraints.
3. Inspect `git status` and relevant source/tests. Do not overwrite existing user work.
4. Consult `docs/improvements-overview.md` for findings relevant to the task. Its reviewed commit differs from the initial clone; verify claims against current code.

## Project direction

- Primary workflow: external editor + kerboscript LSP; in-game terminal for execution and debugging; telnet remains supported.
- Order: baseline/tests → safety fixes → incremental structural extraction → QoL → UI modernization.
- Local tests only: do not add hosted CI, remote test jobs, or hosted release automation. Inactive upstream workflows are preserved under `.github/upstream-workflows/`; do not reactivate them without approval.
- Current runtime target: the Windows Steam build of KSP running under Proton on this user's Linux machine. Native Linux, native Windows, and macOS game support are outside the current validation scope; avoid gratuitously breaking their code paths.
- Use the approved development instance `/home/jvz/Games/KSP-kOSIM-dev/` for game references, mod deployment, and disposable game tests. Its `KSP_x64_Data/Managed` is configured in ignored `kOS.props.user`; build outputs remain in the repository until explicitly deployed.
- Launch the dev copy with `/home/jvz/Games/KSP-kOSIM-dev/launch-dev.sh` (or `--check` for preflight). It uses installed UMU and GE-Proton11-7 with an isolated `.proton-prefix`; first launch initialized the isolated prefix and opened a game window; terminal/gameplay verification remains pending. Do not reuse the vanilla Steam prefix or silently change Proton versions.
- Protect the vanilla source `/home/jvz/.local/share/Steam/steamapps/common/Kerbal Space Program/`: do not deploy mods there, copy personal saves into the dev copy, or change Steam settings/the original prefix without approval. The dev copy now has stock GameData plus the tested local kOSIM build; consult the handoff and ensure the game is closed before redeploying.
- Preserve upstream scripts, character-grid semantics, KSM/save/craft compatibility, and public mod APIs. Document intentional changes.
- Keep KSP's runtime constraints. Do not upgrade the in-game runtime as incidental cleanup.
- Preserve safe telnet exposure defaults and predictable flight-control/input ownership.
- Favor focused, tested changes. No wholesale terminal/compiler rewrite or embedded IDE without explicit approval.
- Preserve `docs/improvements-overview.md` as the original review; put evolving plans and status in the roadmap and handoff.

## Testing

- Read `docs/testing-and-build.md` for setup and the baseline. Run `bash .ci/test.sh` for local headless checks; use `PYTHON=python` if needed.
- `.ci/known-test-failures.json` records known failing tests so they stay visible without being skipped. It is currently empty: the three upstream lexicon-formatting failures were fixed (see `docs/testing-and-build.md`). Do not expand it to hide regressions; remove entries only after investigating a fix, and maintain its minimum test count as coverage grows.
- Compilation is not proof that tests ran. Report actual discovery/execution and outcomes.
- Add regression coverage for fixes; use real terminal models where possible.
- Local Linux/Mono headless tests are not Proton/Unity runtime validation. Run them before considering a change complete; record full-build and in-game Proton checks separately, including KSP and Proton versions and unperformed checks.
- Headless tests do not replace manual KSP checks for focus, flight controls, power, scenes, fonts, and native resource cleanup.
- Do not claim a reproduction, passing test, or performance improvement without evidence. Record blockers and unperformed checks explicitly.
- Do not commit proprietary KSP assemblies, credentials, or unrelated changes.

## Required session handoff

`docs/HANDOFF.md` is the authoritative cross-session progress record. Update it after meaningful work and before ending a session, including planning-only sessions when decisions change.

Keep it self-contained and current, recording:

- Current phase, branch/commit when useful, and workspace state.
- Completed work and affected paths.
- Decisions and compatibility constraints established this session.
- Exact build/test commands and results, or explicitly that they were not run.
- Incomplete work, known failures, blockers, and required manual verification.
- Prioritized next steps sufficiently concrete for a fresh session to resume.

Do not rely on chat history for continuity. Distinguish planned work from implemented and verified work. Retain useful historical context briefly, but keep the current next action obvious.
