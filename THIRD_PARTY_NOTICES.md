# Third-party provenance and notices

## Upstream kOS

kOS Improved derives from [KSP-KOS/KOS](https://github.com/KSP-KOS/KOS), originally
created by Kevin Laity (Nivekk) and developed by the kOS team and contributors.
Exact fork provenance is in `kOS.props`; the inherited source uses
[GNU GPL v3](LICENSE.md). Original notices, release credits, art/model source and
sound source are retained. See `src/gimp/README.md` and `src/sound_sources/README.md`.

## Bundled SharpZipLib

The inherited runtime dependency is
`Resources/GameData/kOS/Plugins/ICSharpCode.SharpZipLib.dll`:

- Assembly version **0.86.0.518**, informational version **0.86.0** (read from the DLL).
- Embedded copyright: **Copyright 2001-2010 Mike Krueger, John Reilly**.
- SHA-256: `40b3d590f95191f3e33e5d00e534fa40f823d9b1bb2a9afe05f139c4e0a3af8d`.
- Verified **byte-for-byte match** to `lib/20/ICSharpCode.SharpZipLib.dll` in
  [NuGet SharpZipLib 0.86.0](https://www.nuget.org/packages/SharpZipLib/0.86.0).
- Matching-version source:
  [SharpZipLib tag 0.86.0.518](https://github.com/icsharpcode/SharpZipLib/tree/0.86.0.518),
  commit `cfc69a68fefbc5858fe70b35f7b69fc505b8c2d6`. A source rebuild was not performed;
  stock-distribution identity is verified, not reproducible source-to-binary equivalence.

This version's [source license header](https://github.com/icsharpcode/SharpZipLib/blob/0.86.0.518/src/Zip/ZipFile.cs)
uses **GPL version 2 or later with a linking exception**. Do not apply the current
SharpZipLib project's newer license retroactively. The header also credits GNU
Classpath / Free Software Foundation for translated code. The exception states:

> As a special exception, the copyright holders of this library give you
> permission to link this library with independent modules to produce an
> executable, regardless of the license terms of these independent
> modules, and to copy and distribute the resulting executable under
> terms of your choice, provided that you also meet, for each linked
> independent module, the terms and conditions of the license of that
> module. An independent module is a module which is not derived from
> or based on this library. If you modify this library, you may extend
> this exception to your version of the library, but you are not
> obligated to do so. If you do not wish to do so, delete this
> exception statement from your version.

The [package audit](docs/binary-distribution.md) verified all three existing release
ZIPs against GitHub's asset digests, but found missing license/notice materials and
incorrect packaged source directions. Before public binary distribution, supply
applicable complete license texts/notices and appropriate corresponding-source
arrangements. This inventory is not legal certification or a dependency security assessment.
No dependency was upgraded or removed by the public-documentation cleanup.

## Game references and tooling

KSP/Unity references are proprietary build inputs from the developer's own game
installation, **not redistributable components of this repository**. NuGet and
Python documentation/test dependencies have their own licenses and are restored
locally; do not copy their caches into release packages. ModuleManager is a separate
installation dependency, not bundled by the fork's documented packaging command.
