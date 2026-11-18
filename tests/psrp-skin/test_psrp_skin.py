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

# Text weight. The browser draws GTA's font about a quarter heavier than the game did, so a faint
# dark hairline trims every letter back to the game's weight; greyed-out rows, the banner title and
# the key caps keep their full weight. The text is GTA's own menu grey.
def rule(selector_pattern):
    found = [body for sel, body in rules if re.fullmatch(selector_pattern, ' '.join(sel.split()))]
    assert len(found) == 1, f'expected one rule for {selector_pattern}, found {len(found)}'
    return found[0]


edge = rule(r'#menuapi, #menuapi-buttons')
assert re.search(r'-webkit-text-stroke:\s*0\.45px rgb\(0 0 0 / 55%\)', edge), 'the text hairline is missing or changed'
reset = rule(r'#menuapi \.menuapi-row--disabled, #menuapi \.menuapi-header__title, #menuapi-buttons \.menuapi-key')
assert re.search(r'-webkit-text-stroke:\s*0;', reset), 'greyed-out rows, the title and key caps must keep full weight'
assert re.search(r'--psrp-text:\s*rgb\(225 225 225\)', code), 'menu text is GTA grey'

# Bold. MenuAPI draws GTA's ~h~ as an inline font-weight on a span; GTA's font has no bold, so the
# browser would fake one (half again as heavy) and the hairline above would show inside it as a
# grey outline. Bold is the regular letters with a thin outline in their own colour instead.
menuapi_js = (root / 'build' / 'vMenu' / 'menuapi' / 'menuapi.min.js').read_text(encoding='utf-8-sig')
assert 'style.fontWeight="bold"' in menuapi_js, 'MenuAPI no longer marks ~h~ bold with an inline font-weight; update the bold rule'
bold = rule(r'#menuapi span\[style\*="font-weight"\]')
assert re.search(r'font-weight:\s*normal\s*!important', bold), 'bold must not use the browser\'s fake bold'
assert re.search(r'-webkit-text-stroke:\s*0\.3px currentColor', bold), 'bold is drawn with an own-colour outline'
assert not re.search(r'font-weight:\s*(bold|[6-9]00)', code), 'nothing in the skin may ask GTA\'s font for a bold it doesn\'t have'

# storage.html loads it after MenuAPI's own stylesheets, so it wins.
assert storage.index('menuapi/menuapi.min.css') < storage.index('themes/psrp-neon.css')
print(f'PASS: the skin sets {len(set_here)} MenuAPI tokens, all real, styles only the menu and its key hints, and uses only its own defined colours')
