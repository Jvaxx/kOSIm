# kOS Improved — coding-agent guidance

## Start and finish

1. Read `docs/HANDOFF.md` and `docs/KOSIm-roadmap.md` completely.
2. Inspect `git status` and relevant source/tests; do not overwrite existing work.
3. Read relevant contracts in `docs/compatibility.md` and setup in `docs/testing-and-build.md`.
4. `docs/improvements-overview.md` is the **unchanged original review**, not current
   status. It reviewed upstream `d4388439`, not the actual fork point. Verify claims
   against code; preserve the snapshot verbatim.
5. After meaningful work, replace stale state in `docs/HANDOFF.md` with a concise,
   self-contained resume point: changed paths/decisions, exact commands/results,
   blockers/unperformed checks and next steps. Do not append session transcripts,
   personal paths or duplicate release/build receipts. Historical detail belongs in git
   history and ignored local artifacts, not maintained docs.

## Project constraints

- Independent fork of kOS; use fork destinations and attribution. Preserve GPL/license,
  contributor and asset notices; do not erase upstream history as “cleanup”.
- Workflow: external editor + optional kerboscript LSP; game terminal/telnet for
  execution/debugging; embedded editor is a safe fallback, not an IDE.
- Order: baseline/tests → safety → incremental extraction → QoL → UI.
  Focused, tested changes; no wholesale terminal/compiler rewrite, steering/language
  changes, embedded IDE or UI redesign without an agreed plan.
- Preserve scripts, character-grid semantics, KSM/save/craft formats and public mod
  APIs. Document intentional changes. Keep the in-game `net48`/Unity runtime.
- Preserve loopback/opt-in telnet defaults and predictable input/flight-control ownership.
- **Local tests/manual releases only:** no hosted CI, remote test jobs or hosted release
  automation. Reference workflows remain inert in `.github/upstream-workflows/`.
- Push, tag, publish, deploy and launch only when requested; never push to upstream.
  Use `RELEASECHECKLIST.md` for releases and `docs/publication.md` for public preparation.

## Local game safety

- Validation target: Windows Steam **KSP 1.12.5.3190 under GE-Proton11-7** on Linux;
  native Linux/Windows/macOS and other versions are outside validation scope. Do not
  gratuitously break their code paths or claim they were tested.
- On the maintainer's machine, the only approved reference/deploy/test copy is
  **`$HOME/Games/KSP-kOSIM-dev/`**, configured by ignored `kOS.props.user`.
  Build outputs remain in the repository until explicitly deployed.
- Use that copy's `launch-dev.sh` (`--check` for preflight), installed UMU and
  **GE-Proton11-7**, isolated `.proton-prefix` and recorded launch settings. Never
  silently substitute Proton versions, host Wine or the vanilla Steam prefix.
- Protect the vanilla Steam install, original prefix, personal saves and Steam settings.
  No deployment there, save imports or game/system setting changes without approval.
- Ensure the dev game is closed before deployment; back up the mod and preserve
  PluginData/settings/presets. Never close or kill a game unasked.
- Detailed local setup/probes/backups/logs live with the dev copy; do not commit
  proprietary assemblies, credentials, local configuration or unrelated artifacts.

## Verification

- Run **`bash .ci/test.sh`** for headless checks (`PYTHON=python` if needed).
  Compilation is not test execution: report actual discovery/execution/outcomes.
- Add regression tests for fixes; use real models where possible. Keep
  `.ci/known-test-failures.json` honest (currently empty; minimum 1,460).
  Do not expand it to hide regressions; raise the count as coverage grows.
- Run the local gate before calling a change complete. Record the full build and
  game checks separately; Mono headless tests do not validate Unity/Proton behavior.
- Focus, flight controls, scenes, power, fonts and native resources need manual game
  checks. State unperformed checks/blockers explicitly; never invent a reproduction,
  passing test, leak fix or performance gain.
- Headless runs replace safe-project outputs. Full-build again before any deployment;
  for releases verify committed DLL metadata/hash, not only generated attributes.
