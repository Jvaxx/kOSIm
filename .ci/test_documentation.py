"""Check local links in maintained developer docs without network or Sphinx.

Historical research and the inherited changelog are intentionally excluded: their
links and source line references describe older revisions, not current guidance.
"""

from pathlib import Path
import re
import unittest
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parent.parent
FRONT_DOOR = (
    "README.md", "CONTRIBUTING.md", "RELEASECHECKLIST.md", "STYLEGUIDE.md",
    "AGENTS.md", "THIRD_PARTY_NOTICES.md", "doc/README.md",
    ".github/upstream-workflows/README.md",
)
LINK = re.compile(r"\[[^\]\n]+\]\(([^\s)]+)\)")


def heading_ids(text):
    """GitHub-style IDs for the simple ATX headings used in these docs."""
    counts = {}
    for heading in re.findall(r"^#{1,6}\s+(.+?)\s*#*\s*$", text, re.MULTILINE):
        slug = re.sub(r"[^\w\- ]", "", heading.lower()).replace(" ", "-")
        count = counts.get(slug, 0)
        counts[slug] = count + 1
        yield slug if count == 0 else slug + "-" + str(count)


class DocumentationLinkTest(unittest.TestCase):
    def test_local_links_in_maintained_markdown_resolve(self):
        sources = [ROOT / path for path in FRONT_DOOR]
        sources.extend(path for path in sorted((ROOT / "docs").glob("*.md"))
                       if path.name != "improvements-overview.md")
        checked = 0
        for source in sources:
            for target in LINK.findall(source.read_text(encoding="utf-8")):
                url = urlsplit(target)
                if url.scheme or url.netloc:
                    continue
                checked += 1
                with self.subTest(source=str(source.relative_to(ROOT)), target=target):
                    path = (source.parent / unquote(url.path)).resolve() if url.path else source
                    self.assertTrue(path.is_relative_to(ROOT), "link escapes repository")
                    self.assertTrue(path.exists(), "missing local link target")
                    if url.fragment and path.suffix == ".md" and path.is_file():
                        self.assertIn(unquote(url.fragment),
                                      set(heading_ids(path.read_text(encoding="utf-8"))),
                                      "missing heading anchor")
        self.assertGreater(checked, 40, "maintained documentation was not checked")


if __name__ == "__main__":
    unittest.main()
