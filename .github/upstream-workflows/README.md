# Inactive upstream workflows

Reference copies of upstream's build and release workflows at fork point
`8f281a459b6ea0ba917c91bbbc117e68bb948199`. They are not fork instructions.
GitHub does not execute files here or with the `.disabled` suffix.
Do not restore them to `.github/workflows/` without explicit approval.

kOSIM uses **local tests and manual releases only**. Run `bash .ci/test.sh`;
see [build/testing](../../docs/testing-and-build.md) and the
[fork release checklist](../../RELEASECHECKLIST.md). The `.ci/` name does not
imply hosted CI. Obsolete Travis configuration/scripts were removed; their
upstream history remains in git, not as active setup guidance.
