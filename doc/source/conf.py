# kOS Improved manual; inherited kerboscript domain, lexer and styling.
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

# Resolve paths relative to this file so sphinx-build need not run from doc/.
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

extensions = ['sphinx.ext.mathjax', 'ksdomain', 'KerboscriptLexer']
primary_domain = 'ks'
master_doc = 'contents'
source_suffix = '.rst'
exclude_patterns = []

project = 'kOS Improved'
copyright = '2013-2021, kOS Team (originally Nivekk); kOS Improved contributors'

# Single version source shared with the mod build.
_version_props = ET.parse(Path(__file__).resolve().parents[2] / 'kOS.props')
release = _version_props.findtext('./PropertyGroup/KSPBT_ModVersion')
version = '.'.join(release.split('.')[:3])
_upstream_release = _version_props.findtext('./PropertyGroup/KOSIM_UpstreamVersion')
rst_epilog = '\n.. |upstream_release| replace:: ' + _upstream_release + '\n'

pygments_style = 'sphinx'
highlight_language = 'kerboscript'
html_theme = 'sphinx_rtd_theme'
html_style = 'css/kos_theme.css'
html_logo = '_images/kos_logo_small.png'
html_favicon = '_images/favicon.ico'
html_static_path = ['_images', '_static']
htmlhelp_basename = 'kOSIMdoc'

# Keep the original authors credited in offline formats as well as the HTML manual.
_authors = 'kOS Improved contributors; kOS Team, Dunbaratu, erendrake; originally Nivekk'
latex_elements = {}
latex_documents = [
    ('index', 'kOSIM.tex', 'kOS Improved Documentation', _authors, 'manual'),
]
man_pages = [
    ('index', 'kosim', 'kOS Improved Documentation', [_authors], 1),
]
texinfo_documents = [
    ('index', 'kOSIM', 'kOS Improved Documentation', _authors, 'kOSIM',
     'An independent kOS scripting and terminal fork for Kerbal Space Program.',
     'Miscellaneous'),
]
