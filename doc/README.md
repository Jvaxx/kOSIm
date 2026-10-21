# kOS Improved user manual

`doc/source/` contains the kerboscript reference, tutorials and user help inherited
from kOS, with fork-specific updates. `docs/` (plural) contains
[developer guidance](../docs/README.md); it is not the Sphinx source directory.

The [upstream online manual](https://ksp-kos.github.io/KOS_DOC/) remains useful for
the language, but does not document this fork's UI or releases. No fork-hosted
manual deployment is configured. Do not push generated pages to upstream's repositories.

## Build HTML locally

Use Python 3 in an isolated environment, from the repository root:

```bash
python3 -m venv artifacts/docs-venv
. artifacts/docs-venv/bin/activate
python -m pip install -r doc/requirements.txt
python -m sphinx -W --keep-going -b html doc/source artifacts/manual
python -m http.server --bind 127.0.0.1 --directory artifacts/manual 8000
```

On Windows, activate with `artifacts\docs-venv\Scripts\activate` instead.
Open `http://127.0.0.1:8000/contents.html`. Review warnings and the changed pages;
a successful HTML build is not a check of every external URL or game behavior.
Generated output and the environment stay ignored under `artifacts/`.

The legacy `doc/Makefile` and `make.bat` also build HTML into `doc/gh-pages/`;
that name is a **local output directory**, not an instruction to deploy GitHub Pages.

## Editing

- Edit `.rst` sources, not rendered HTML. Preserve reference labels and public API anchors.
- Keep upstream attribution and historical changelog entries. Mark historical screenshots
  or inherited behavior where they do not match the fork.
- Update fork download/help/contribution links without replacing historical upstream
  issue links or third-party community resources indiscriminately.
- `conf.py` reads fork and exact upstream versions from `kOS.props`. Do not duplicate
  those version numbers in generated configuration.
- The custom `ksdomain.py` and `KerboscriptLexer.py` supply kerboscript roles/highlighting.
  The pinned dependencies below are for documentation tooling only, not the mod runtime.

Original Sphinx setup credit: [Johann Goetz](https://github.com/theodoregoetz).
See [the documentation license](LICENSE.md) and [contribution guide](../CONTRIBUTING.md).
