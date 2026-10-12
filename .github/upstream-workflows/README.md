# Inactive upstream workflows

These are reference copies of upstream's build and release workflows from commit `8f281a459b6ea0ba917c91bbbc117e68bb948199`, not active automation. GitHub does not execute them from this directory or with the `.disabled` suffix.

KOS Improved currently uses **local tests only**. Do not restore hosted CI or release workflows without an explicit change in the user's requirements. The previously added fork-specific hosted test matrix was removed, not archived here.

Run `bash .ci/test.sh` locally; see `docs/testing-and-build.md` for the Proton-only target and validation policy. The `.ci` directory name is retained for the existing local test tooling and does not imply hosted CI.
