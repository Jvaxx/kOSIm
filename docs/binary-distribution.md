# Binary distribution audit

**Conclusion: the inspected binaries are not inherently forbidden to redistribute,
but the existing release ZIPs are not ready for a clean public distribution as-is.**
The concrete gaps are missing license materials and inadequate source directions.
This is a package/provenance inspection, not a legal opinion, security audit or
certification of every asset's copyright chain.

## Scope and verified payloads

Inspected local `artifacts/kOS-v1.5.1.100.zip`, `.101.zip` and `.102.zip`, comparing
all three whole-archive SHA-256 hashes with the GitHub release API's asset digests.
All matched. CRC checks passed; each contains **64 files / 75 entries**, with no
duplicate or unsafe paths. No historical package, tag, release note or asset was changed.

| Package | Tag commit | Mod DLL informational versions |
| --- | --- | --- |
| `1.5.1.100` | `5bd2afab` | `1.5.1.100+5bd2afabb2d9962b894d11882d99ee24e56d8e53` |
| `1.5.1.101` | `d6c3c14a` | `1.5.1.101+d6c3c14a263b3e8074be60b3c44ad5f00554ad61` |
| `1.5.1.102` | `c1fb95a9` | `1.5.1.102+c1fb95a900342840254c3f75b9ab2315fe29b330` |

Each ZIP contains exactly three PE executables: `kOS.dll`, `kOS.Safe.dll` and
`ICSharpCode.SharpZipLib.dll`. **No KSP/Unity/framework DLLs, ModuleManager or
KSPAPIExtensions binaries are packaged.** References to game assemblies in `kOS.dll`
are external references, not copies of those DLLs. Manifests declare only the mod's
ordinary `.resources` entry, with no separately declared embedded game-DLL resources.

For each archive, **61 files match the upstream fork-point tree byte-for-byte**,
including SharpZipLib and the shipped art/audio/config/readme. Only the two mod DLLs
and generated version JSON differ. Asset author fields remain intact. That establishes
unchanged upstream provenance, not an independent legal audit of all artwork.

## SharpZipLib provenance and terms

The bundled DLL is **byte-for-byte identical** to
`lib/20/ICSharpCode.SharpZipLib.dll` in
[NuGet SharpZipLib 0.86.0](https://www.nuget.org/packages/SharpZipLib/0.86.0).
It is not an unexplained fork-specific binary. Its assembly version is 0.86.0.518;
DLL SHA-256 and attribution are in [third-party notices](../THIRD_PARTY_NOTICES.md).

Versioned source is available at tag `0.86.0.518`, commit
[`cfc69a68fefbc5858fe70b35f7b69fc505b8c2d6`](https://github.com/icsharpcode/SharpZipLib/tree/cfc69a68fefbc5858fe70b35f7b69fc505b8c2d6).
Its [GPLv2 text](https://github.com/icsharpcode/SharpZipLib/blob/cfc69a68fefbc5858fe70b35f7b69fc505b8c2d6/doc/COPYING.txt)
and [source header with the linking exception](https://github.com/icsharpcode/SharpZipLib/blob/cfc69a68fefbc5858fe70b35f7b69fc505b8c2d6/src/Zip/ZipFile.cs)
were inspected. The grant is **GPLv2-or-later with a linking exception**, not the
modern project's MIT license. The exception permits linking with independent
modules; **it does not waive the library's own redistribution/source obligations**.
A source rebuild was not performed; stock NuGet identity is verified, not reproducible
source-to-binary equivalence. Bit-identical recompilation is not itself a GPL requirement.

## Distribution gaps

1. **All three ZIPs omit the full GPL license, dependency license/exception materials
   and third-party notices.** A reference to a license in a release note is not a
   substitute for supplying the required license copy with the distribution.
2. **Their packaged readme directs source requests to upstream KSP-KOS/KOS**, which
   does not contain the fork's changes. It neither identifies the fork nor its release
   source revision. The working-tree readme is fixed, but existing ZIPs are unchanged.
3. GitHub provides automatic source ZIP/tar links for each tag, and the corresponding
   tags contain the mod source, solution, properties and local build/test inputs.
   Both mod DLLs' embedded commit identifiers match their release tag. **Source is
   not missing from the repository**, but explicit, durable directions beside the
   binaries should name the exact fork source and dependency source. This metadata
   check is not an independent rebuild proving every DLL was compiled from that tag.
4. SharpZipLib source/license arrangements are not supplied by those ZIPs or their
   release notes. Providing only the DLL inside a fork source archive does not provide
   the library's preferred source form. Preserve/offer the exact version's complete
   source/build inputs under a suitable distribution method, not just today's branch.

For network distribution, see [GPLv3 §6(d)](https://www.gnu.org/licenses/gpl-3.0.html#section6)
and the library's [GPLv2 §3](https://www.gnu.org/licenses/old-licenses/gpl-2.0.html#section3).
The specific source-delivery method matters; do not assume every GPLv2 distribution
can be cured by an arbitrary external URL. A conservative approach is to offer the
versioned dependency source archive alongside the mod source and binaries, retaining
its notices and maintaining availability.

## Required before a public binary release

- Include the mod GPLv3 text, fork/third-party attribution, the applicable SharpZipLib
  full license text and linking exception, plus any other required notices.
- Ship a fork-specific readme with version, changed-status/date and exact source
  tag/commit; put corresponding source directions next to the download too.
- Provide the release's complete mod source/build inputs and a maintained, appropriate
  source-delivery arrangement for the verified SharpZipLib version. Public repository
  visibility helps access but does not insert missing files into an existing ZIP.
- Decide how to address earlier releases explicitly: supplemental license/source
  assets and corrected notes, or clearly identified replacement packages. **Do not
  silently overwrite historical ZIPs, move tags or claim this audit repaired them.**
- Follow [the release checklist](../RELEASECHECKLIST.md). The current manual `zip`
  example alone does not add these compliance materials. No `1.6.0.103` package has
  been produced or certified by this audit.

Detailed manifests, API metadata, extracted-DLL metadata and NuGet comparison are
kept locally under ignored `artifacts/tests/package-license-audit/`. No packaged DLL
was executed, upgraded or modified; no game test was necessary for this inspection.
KSP/plugin-linking legal questions and complete asset rights review remain outside
this technical package audit.
