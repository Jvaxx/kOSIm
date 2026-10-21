# Contributing to kOS Improved

This repository is an independent fork of [KSP-KOS/KOS](https://github.com/KSP-KOS/KOS).
Send fork issues and pull requests to **[Jvaxx/kOSIm](https://github.com/Jvaxx/kOSIm)**,
not to upstream. Base changes on this repository's **`master`** branch.

## Before you start

- Read the [roadmap and known issues](docs/KOSIm-roadmap.md) and
  [compatibility contracts](docs/compatibility.md).
- Discuss larger UI, language, compiler/VM, runtime or architecture changes in an
  issue first. Small fixes and documentation corrections can go straight to a PR.
- Keep changes focused. Do not mix formatting sweeps, generated outputs or unrelated refactors into a fix.
- Preserve upstream attribution and license notices. Source contributions use the
  project's [GPL v3 license](LICENSE.md); identify the license and provenance of any new asset.
- Use a draft PR for unfinished work. There is no promise of immediate review or merge.

## Work on a branch

Fork **this repository** on GitHub, clone your fork, then:

```bash
git remote add kosim https://github.com/Jvaxx/kOSIm.git
git fetch kosim
git switch -c fix-description kosim/master
# edit, test, commit, then push your branch to your own origin
```

Open a PR against `Jvaxx/kOSIm:master`. Upstream integration is a separate,
reviewed task; do not pull upstream `develop` into an unrelated contribution.
Avoid rewriting commits other contributors are already using.

## Build and test

Follow [the local setup and commands](docs/testing-and-build.md).
The headless suite needs no KSP installation. The full mod build needs a legally
owned game copy, configured through ignored **`kOS.props.user`** (case-sensitive).
Never commit game/Unity assemblies, local paths, credentials or build products.

- Put Unity-independent logic in `src/kOS.Safe/` and regression tests in
  `src/kOS.Safe.Test/`. Use real screen/session models where practical.
- Run **`bash .ci/test.sh`**; report actual discovered/executed/passed counts.
  `dotnet test` does not execute this NUnit 2 suite.
- Keep `.ci/known-test-failures.json` honest. Do not add failures to mask regressions;
  raise its minimum count when adding coverage.
- A full build and game checks are separate from headless tests. For game-dependent
  behavior, record KSP/Proton versions and exact steps, or explicitly say it is untested.
- Automation is **local only**. Do not add hosted CI or release jobs; archived upstream
  workflows are references, not templates to reactivate.

## What a PR should include

- The problem, reproduction, intended behavior and any compatibility impact.
- Regression coverage; for non-trivial fixes, demonstrate that the test fails without the fix.
- Actual test/build results and remaining manual checks, not just “builds successfully”.
- A changelog entry for user-facing fixes/features, and corresponding user-manual
  changes in `doc/source/` when behavior or script APIs change.
- Updates to developer contracts/roadmap only where relevant. Do not add session
  transcripts, machine-specific deployment logs or copied test output to maintained docs.

Follow [STYLEGUIDE.md](STYLEGUIDE.md) and `.editorconfig`. Retain existing script,
KSM, save/craft and public mod API compatibility unless an intentional change is
explicitly reviewed and documented. Keep telnet loopback/opt-in defaults and
predictable flight-control ownership.

## Bug reports

Use [fork issues](https://github.com/Jvaxx/kOSIm/issues). Include the fork version,
KSP/OS/Proton versions, relevant mods, minimal script/steps, expected and actual
results, and relevant log excerpts. Remove personal paths and sensitive material
before sharing logs or saves. If comparing upstream, identify the exact upstream
build and results; do not assume a fork failure is an upstream bug.
