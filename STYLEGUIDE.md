# Coding style

Use the surrounding code and `.editorconfig` as the starting point. Keep style-only
changes separate from functional fixes; do not reformat inherited code wholesale.

## C#

- Allman braces; `using` directives outside the namespace.
- PascalCase for types, methods, properties and events; camelCase for fields,
  parameters and locals; `I` prefix for interfaces. No Hungarian notation or
  new `_`/`m_`/`s_` field prefixes.
- Prefer private fields and properties over new public fields. Use `internal`
  test seams rather than expanding the public mod API unnecessarily.
- Use `var` when the type is evident from the initializer or is anonymous.
- Prefer one top-level public type per file; narrowly scoped helpers may be nested.
  A kerboscript constructor function may be nested in the structure it returns.
- Avoid `#region` and commented-out code. Preserve existing copyright/license notices.
- Keep Unity/KSP adapters thin. Model/policy code belongs in `kOS.Safe` when it can
  be independent of game types.
- Respect `net48` and KSP's shipped Unity runtime. A newer build SDK is not permission
  to introduce APIs unavailable in game.

## Behavior and documentation

- Preserve existing public signatures used by scripts or other mods; do not apply
  style rules by renaming a compatibility API.
- Document ownership, thread boundaries and non-obvious compatibility decisions,
  not a narration of each statement.
- Use invariant formats for persisted machine-readable state and tolerant parsing
  consistent with the relevant file contract.
- Tests should describe observable behavior and use deterministic clocks/inputs
  where possible. Do not replace real-model regression coverage with mocks alone.
- User documentation lives in `doc/source/` (reStructuredText); maintained developer
  guidance lives in `docs/` (Markdown). Keep session transcripts and local paths out
  of both. Historical snapshots are explicitly marked as such.
