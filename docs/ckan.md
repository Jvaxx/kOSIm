# CKAN packaging

kOS Improved has its own CKAN identifier, **`kOSImproved`**. It installs the same
`GameData/kOS` folder as upstream kOS and stands in for it: it **provides** `kOS`, so
add-ons that depend on `kOS` accept it, and **conflicts** with `kOS`, so the two are
never installed together. The [CKAN spec](https://github.com/KSP-CKAN/CKAN/blob/master/Spec.md#provides)
describes this provide-and-conflict pattern for interchangeable mods.

**Status:** 1.6.0.103 is the first release with a standalone `.ckan`. The fork is
**not yet in the CKAN index**; the NetKAN file was submitted as
[KSP-CKAN/NetKAN#11747](https://github.com/KSP-CKAN/NetKAN/pull/11747).

## Files

| Path | Purpose |
| --- | --- |
| [`ckan/kOSImproved.netkan`](../ckan/kOSImproved.netkan) | NetKAN input, as submitted to `KSP-CKAN/NetKAN` (JSON, a valid YAML subset) |
| [`.ci/package.py`](../.ci/package.py) | Deterministic release ZIP with license and source materials |
| [`.ci/ckan.py`](../.ci/ckan.py) | Standalone `.ckan` from the netkan file and a release ZIP |
| [`.ci/ckan-solver-check.py`](../.ci/ckan-solver-check.py) | Manual check with the real CKAN client on fake game instances |

`.ci/test_ckan.py` and `.ci/test_package.py` run in the headless gate.

## Metadata decisions

- **Identifier `kOSImproved`.** Upstream's netkan owns `kOS` and indexes `KSP-KOS/KOS`
  releases with epoch `1:`. A separate identifier needs no epoch. Fork versions only
  compare with each other. Identifiers are permanent once indexed; do not rename it.
- **Relationships:** `provides: kOS`; `conflicts: kOS, kOS-Classic` (upstream also
  conflicts with `kOS-Classic`); `depends: ModuleManager`; `suggests: RemoteTech`.
  `provides` carries no version, so an add-on requiring a minimum kOS version would not
  be satisfied by the fork. The add-ons checked so far (`kOS-MechJeb2`, `kOS-AFBW`,
  `kOS-Computer`) have no version constraint.
- **Versions and KSP range** come from the packaged `kOS.version` (`$vref: #/ckan/ksp-avc`).
  The range is **KSP 1.12 only**, set explicitly in `src/kOS/kOS.csproj` and checked by
  `check-version.py --built`. Tags are `v<version>`; `x_netkan_version_edit` strips the `v`.
- **Release assets:** the GitHub reference only matches `kOS-v<four numeric fields>.zip`.
  `-dirty` test packages, checksum files, `.ckan` files and the SharpZipLib source
  archive are excluded. `x_netkan_github.prereleases: false` keeps prereleases out of
  the index (NetKAN's default would include them). Draft releases are not visible.
- **Released assets are immutable.** Each `.ckan` pins the ZIP's size and SHA-1/SHA-256.
  Replacing an asset breaks installation for that version; publish a new version instead.
- Historical `1.5.1.100`–`.102` packages are not listed: they lack license materials,
  and their versions would sort below 1.6.0.103 anyway.
- `license: GPL-3.0` is the mod's license. GitHub reports the repository license as
  `NOASSERTION`, so it is set explicitly. The bundled SharpZipLib terms are in the
  packaged notices.

## Client behavior

Verified with CKAN **1.36.4** command line on fake KSP 1.12.5 instances, using a local
package and a test-only next version (`.ci/ckan-solver-check.py`). The CKAN GUI was
not exercised; it uses the same relationship resolver but its dialogs were not checked.

- Installing `kOSImproved` also installs ModuleManager. Upgrades from a CKAN repository
  keep `GameData/kOS/Plugins/PluginData/`. Removal deletes only the files CKAN
  installed, so PluginData stays.
- An add-on that depends on `kOS`, installed while kOSImproved is present, uses the fork
  and does not pull in upstream kOS. `ckan install kOS` then reports "Nothing to install".
- When neither is installed, CKAN asks which `kOS` provider to use, listing upstream
  kOS first. **Interactive users of kOS add-ons will see this question once the fork is
  indexed.** With `--headless`, CKAN 1.36.4 skips the question and installs the add-on
  without either provider, leaving an unsatisfied dependency. That comes from CKAN's
  headless mode, which also skips consistency checks. Name the provider explicitly,
  e.g. `ckan install kOSImproved kOS-MechJeb2`.
- **Replacing CKAN-installed upstream kOS:** a plain `ckan install kOSImproved` prints
  "Nothing to install", because CKAN hides conflicting modules. With an explicit version it
  reports the conflict and changes nothing. Remove `kOS` first. CKAN also removes add-ons
  that depend on it (and auto-installed dependencies); then install `kOSImproved` and
  those add-ons again. PluginData stays.
- **Standalone `.ckan` installs do not update in place.** Installing or upgrading with a
  newer `.ckan` reports the module as already installed or up to date. Remove
  `kOSImproved` and install the newer `.ckan`. Dependent add-ons are removed with it and
  must be reinstalled. This limitation ends once the fork is in the CKAN index.
- Not tested: replacing a **manual** (non-CKAN) kOS or kOSIM install, where CKAN may
  auto-detect `kOS.dll` as upstream kOS, and older CKAN clients.

## Release steps

The [release checklist](../RELEASECHECKLIST.md) is authoritative; CKAN-specific parts:

```bash
python3 .ci/package.py                                   # artifacts/kOS-v<version>.zip
python3 .ci/ckan.py artifacts/kOS-v<version>.zip         # artifacts/kOSImproved-<version>.ckan
python3 .ci/ckan-solver-check.py artifacts/kOS-v<version>.zip
```

The solver check needs the `ckan` command (or `--ckan 'mono /path/ckan.exe'`) and
network access to CKAN-meta, ModuleManager, MechJeb2, kOS-MechJeb2 and upstream kOS.
CKAN runs with HOME/XDG directories in its ignored work folder
(`artifacts/tests/ckan-solver/`). The script fails if the real
`~/.local/share/CKAN/config.json` changes; no real game folder is used.

After the GitHub release is published, download the release ZIP and `.ckan` again
and confirm the ZIP's SHA-256 matches the `.ckan`'s `download_hash`.

## NetKAN submission (maintainer action, after the first release)

1. Publish a non-prerelease GitHub release whose assets include `kOS-v<version>.zip`.
2. Fork `KSP-CKAN/NetKAN`, add `NetKAN/kOSImproved.netkan` identical to
   `ckan/kOSImproved.netkan`, and open a pull request. Explain that it is an independent
   fork that replaces kOS through `provides`/`conflicts`, KSP 1.12 only. NetKAN's own
   checks inflate it against the published release.
3. After merge, CKAN's bot polls the fork's releases and indexes each new
   non-prerelease release. That bot is CKAN's service, not automation in this repository,
   but it means **publishing a GitHub release also publishes it to CKAN users.**
4. Check `ckan update` + `ckan show kOSImproved` against the default repository, then
   update README, the packaged readme and this status. Keep attaching the `.ckan` file.
   Metadata changes after indexing go through NetKAN pull requests. Keep the repository
   copy in sync.
