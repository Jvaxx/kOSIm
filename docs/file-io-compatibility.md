# File I/O compatibility policy (IO-01/02)

## Approved direction

The user selected **Preserve text APIs**: add explicit raw I/O, use it for built-in copies/moves, keep legacy text calls and UTF-8/BOM behavior, and fix repeated CRLF expansion. This is implemented locally, not deployed or Proton-verified. No runtime/compiler/serialization migration is included.

## APIs and intentional behavior changes

| Operation | Behavior |
| --- | --- |
| `VolumeFile.ReadAll()` / script `READALL` | Legacy view. Archive reads still apply the existing prefix classification and text conversion; local disks return stored bytes. |
| `VolumeFile.Write(byte[])`, `Write(string)`, `WriteLn(string)` / `WRITE`, `WRITELN` | Legacy append. Archive text conversion is retained; Windows conversion is now idempotent. |
| `Volume.SaveFile(path, content, verifyFreeSpace)` | Legacy save/text conversion; built-in archive saves use the prepared replacement writer. |
| **`VolumeFile.ReadAllRaw()` / `READRAW`** | Complete byte-exact read for built-in ArchiveFile/HarddiskFile, into independent FileContent bytes. No classification, decode or newline conversion. |
| **`VolumeFile.WriteRaw(byte[])` / `WRITERAW(FileContent)`** | Byte-exact append for built-in files. No automatic newline. Script strings/lists are rejected rather than implicitly encoded. |
| **`Volume.SaveFileRaw(path, content, verifyFreeSpace)`** | Byte-exact save for built-in volumes. Archive uses the same prepared replacement path as legacy saves, without text conversion. |
| `VolumeManager.Copy/Move`, including files inside directory operations | Use raw read/save methods. Built-in transfers now preserve CRLF, malformed UTF-8 and arbitrary bytes instead of normalizing/mangling them. |
| `Volume.SaveFile(VolumeFile)` copy helper and new-disk boot-file seeding | Also use raw read/save methods. No CPU/compiler execution behavior was intentionally changed. |

These transfer changes are intentional bug fixes: a copied local-disk text file may now contain the original CRLF rather than the LF previously produced by an archive read. Text helpers do not silently normalize a raw copy afterward. Existing flags/capacity checks, destination resolution, source deletion after successful copy and directory traversal rules remain unchanged. Capacity applies to actual transferred bytes (CRLF occupies two bytes), not the shorter legacy-converted text view; this is not a whole-directory transaction or new move-conflict policy.

Script example for a **new** destination:

```kerboscript
SET CONTENTS TO OPEN("0:/payload.bin"):READRAW.
SET NEWFILE TO CREATE("1:/payload.bin").
IF NOT NEWFILE:WRITERAW(CONTENTS) { PRINT "Write failed.". }
```

`WRITERAW` appends; it is not prepared atomic replacement. For ordinary transfers, use `COPYPATH("0:/payload.bin", "1:/payload.bin")` or `MOVEPATH` instead. Raw writing cannot recover bytes already changed by a legacy `READALL`.

## Text, newline and encoding policy

- `FileContent` still stores bytes. Its `STRING`/`DecodeString` use the existing UTF-8 replacement fallback: malformed sequences produce U+FFFD in the string, without modifying stored bytes. No strict decoder or automatic UTF-16/other encoding detection was added.
- UTF-8 BOM bytes are retained by raw I/O. Decoding retains U+FEFF; encoding still does not automatically prepend a BOM. No automatic BOM stripping was added.
- `FileContent.BINARY`, `LENGTH`, base64 serialization and KSM packing/unpacking are unchanged. On `READRAW` they describe actual bytes; on legacy `READALL` they describe the returned legacy-converted bytes, not necessarily physical file bytes.
- The legacy four-byte `TYPE` classification remains heuristic, not proof of text/binary safety. Raw operations never use it to decide whether to transform data.
- On the Windows branch, legacy text conversion first changes CRLF to LF, then LF to CRLF. Repeating that conversion no longer adds CRs. Lone CRs and already doubled CRs are not guessed away by this converter. Off that branch, legacy save conversion remains disabled.
- Legacy archive read conversion still changes CRLF to LF, retaining lone CRs. Its UTF-8 decode/re-encode can still change short invalid bytes or ASCII-prefix binary. **Use READRAW/raw transfer APIs for byte integrity.** Preserving this legacy path was an explicit compatibility choice, not a claim that it is safe for arbitrary binary data.
- Legacy prefix classification still bypasses conversion for KSM/BINARY, including some UTF-8/BOM text. Editor, RUN/compile text loading, JSON and logging callers retain their existing text API behavior (except the idempotent Windows newline fix).

## Third-party provider compatibility

No new abstract members or interface requirements were added. The new virtual methods on Volume/VolumeFile default to the corresponding legacy methods. Existing subclasses therefore retain their legacy behavior and do **not** automatically receive a byte-exact guarantee.

Built-in Archive/ArchiveFile raw overrides explicitly fall back to those legacy methods for derived provider types, so newly routed copies do not bypass a provider's existing SaveFile/ReadAll/Write policy hooks. A derived provider must explicitly override the new raw methods to opt in. Harddisk remains sealed; its file's defaults already use byte-exact local-disk methods. Custom providers are responsible for their own raw implementation and persistence guarantees. Source compatibility and fallback dispatch are headless-tested; loading external precompiled mods in Unity is unperformed.

## Verification and limits

62 new NUnit cases (46 raw-I/O/provider/compile cases, 16 newline/legacy-policy cases) cover both values of the managed Windows flag on host Mono, restoring it after each test through a test-only private-setter scope. These are managed branch tests, **not Windows/Proton execution**.

Coverage includes all 256 single-byte values, empty/short/malformed/ASCII-prefix binary, valid non-ASCII UTF-8, UTF-8/UTF-16 BOM bytes, mixed newlines, all-byte arrays, opaque KSM/gzip header payloads, same/cross built-in volume transfers, directory/file-to-directory operations, failed commit/full-disk-model saves without source deletion, real suffix invocation, legacy views, serialization, independent read buffers and provider hooks. A real CRLF source copied with boot-style model calls compiles headlessly; generated KSM content is packed/saved/read/unpacked. This does not execute the processor's Unity initialization or run the compiled program.

Before the fix, six temporary characterization probes executed with the full suite (529 executed, 526 passed, 3 known failures). They observed FF → EF-BF-BD on archive read, 128 changed single-byte values, ASCII-prefix corruption, CRLF expansion, and successful copy/move with corrupted data followed by source deletion. Probes removed. Temporary legacy copy and newline mutations made the new full gate fail: 583 executed/14 failures and 583 executed/13 failures respectively, including the same 3 known failures. Mutations removed.

Final local results are recorded in HANDOFF/testing docs. All prepared-save guarantees/limits from IO-03 still apply: no power-loss durability, external-change detection, new link/metadata policy, or guaranteed native partial-commit rollback. Raw append is still nontransactional. Actual Proton/Unity raw suffixes, copies, editor/log/JSON text behavior, boot seeding, compiled execution, permissions/locks and third-party mod loading remain deferred. Use only disposable files in the approved dev instance when those checks resume.
