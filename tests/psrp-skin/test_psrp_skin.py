"""The PSRP Neon skin (build/vMenu/themes/psrp-neon.css) only styles the menu, and only with names
MenuAPI 4.1.3 actually reads.

A misspelt token or class does nothing at all, so the skin would quietly fall back to MenuAPI's GTA
look in that spot. This checks every --menuapi-* token the skin sets against MenuAPI's packaged
stylesheets, every class it targets against MenuAPI's markup classes, every --psrp-* colour it uses
against the ones it defines, and that every rule stays inside the menu (#menuapi) or its key hints
(#menuapi-buttons), so it can never restyle the storage page or the HUD.

Requires Python only.
"""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[2]
skin = (root / 'build' / 'vMenu' / 'themes' / 'psrp-neon.css').read_text(encoding='utf-8-sig')
menuapi_css = (root / 'build' / 'vMenu' / 'menuapi' / 'menuapi.min.css').read_text(encoding='utf-8-sig')
colour_list_css = (root / 'build' / 'vMenu' / 'menuapi' / 'colour-list.min.css').read_text(encoding='utf-8-sig')
storage = (root / 'build' / 'vMenu' / 'storage.html').read_text(encoding='utf-8-sig')

code = re.sub(r'/\*.*?\*/', '', skin, flags=re.S)
rules = re.findall(r'([^{}]+)\{([^{}]*)\}', code)
assert rules, 'no rules found'

# Scope: every selector is the menu or its key hints.
for selectors, _ in rules:
    for selector in (s.strip() for s in selectors.split(',')):
        assert re.match(r'^#menuapi(-buttons)?(\s|$)', selector), f'rule outside the menu: {selector}'

# Tokens: every --menuapi-* set here is one MenuAPI's stylesheets read.
read_by_menuapi = set(re.findall(r'var\((--menuapi-[a-z0-9-]+)', menuapi_css + colour_list_css))
set_here = set(re.findall(r'(--menuapi-[a-z0-9-]+)\s*:', code))
unknown = sorted(set_here - read_by_menuapi)
assert not unknown, f'tokens MenuAPI never reads (misspelt?): {unknown}'
assert len(set_here) >= 30, f'only {len(set_here)} tokens set'
# The surfaces that default to black or GTA blue are all themed.
for token in ('--menuapi-overflow-bg', '--menuapi-stats-bg', '--menuapi-panel-bg', '--menuapi-panel-accent',
              '--menuapi-stat-track', '--menuapi-slider-divider', '--menuapi-row-disabled-text'):
    assert token in set_here, f'{token} left at MenuAPI\'s default'

# The skin's own colours: every one used is defined.
defined = set(re.findall(r'(--psrp-[a-z0-9-]+)\s*:', code))
used = set(re.findall(r'var\((--psrp-[a-z0-9-]+)', code))
assert used <= defined, f'undefined colours: {sorted(used - defined)}'

# Classes: every one targeted exists in MenuAPI's stylesheet.
classes = set(re.findall(r'\.(menuapi-[a-z0-9_-]+)', ' '.join(s for s, _ in rules)))
missing = sorted(c for c in classes if '.' + c not in menuapi_css)
assert not missing, f'classes MenuAPI doesn\'t have: {missing}'

# The two things tokens can't reach: vMenu's sliders (colours set on each slider) and the selected
# row's icons (black for GTA's white highlight).
assert re.search(r'#menuapi \.menuapi-slider \{[^}]*background-color:[^;]*!important', code)
assert re.search(r'#menuapi \.menuapi-row--selected \.menuapi-icon \{[^}]*--tint:[^;]*!important', code)
assert re.search(r'#menuapi \.menuapi-row--selected \.menuapi-icon--checkbox \{[^}]*filter: invert\(1\)', code)

# storage.html loads it after MenuAPI's own stylesheets, so it wins.
assert storage.index('menuapi/menuapi.min.css') < storage.index('themes/psrp-neon.css')
print(f'PASS: the skin sets {len(set_here)} MenuAPI tokens, all real, styles only the menu and its key hints, and uses only its own defined colours')
