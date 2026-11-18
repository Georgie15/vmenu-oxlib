"""Player Guide wiring: python tests/player-guide/test_player_guide.py

The C# half (dotnet run --project tests/player-guide) runs the content reader, the step flow, the
keys and the button hold. This half checks what that can't reach:
- every row a card lights up is a real row of that card's menu, added to that menu, so a renamed
  row can't silently leave a card pointing at nothing;
- every convar a card depends on is one the packaged permissions.cfg sets;
- the menu, the watcher, the manifest, the page and What's New are wired as described.
"""
import json
import re
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[2]
read = lambda p: (root / p).read_text(encoding='utf-8-sig')

guide = json.loads(read('build/vMenu/config/player-guide.json'))
guide_cs = read('vMenu/PlayerGuide.cs')
content_cs = read('vMenu/PlayerGuideContent.cs')
main_menu = read('vMenu/MainMenu.cs')
page = read('build/vMenu/storage.html')
manifest = read('build/vMenu/fxmanifest.lua')
whats_new = read('build/vMenu/client/whatsNew.lua')
permissions = read('build/vMenu/config/permissions.cfg')
menuapi_js = read('build/vMenu/menuapi/menuapi.min.js')
menuapi_css = read('build/vMenu/menuapi/menuapi.min.css')

failures = []


def check(ok, message):
    if not ok:
        failures.append(message)


def method_body(source, signature):
    start = source.index(signature)
    brace = source.index('{', start)
    depth = 0
    for i in range(brace, len(source)):
        depth += {'{': 1, '}': -1}.get(source[i], 0)
        if depth == 0:
            return source[brace:i + 1]
    raise AssertionError('unbalanced ' + signature)


# Where each menu id's rows are made, and the variable the rows are added to.
spawner = read('vMenu/menus/VehicleSpawner.cs')
MENUS = {
    'main': (guide_cs, 'mainMenu'),
    'mpPed': (read('vMenu/menus/MpPedCustomization.cs'), 'menu'),
    'appearance': (read('vMenu/menus/PlayerAppearance.cs'), 'menu'),
    'vehicleSpawner': (spawner, 'menu'),
    'savedVehicles': (method_body(read('vMenu/menus/SavedVehicles.cs'), 'private void CreateTypeMenu()'), 'savedVehicleTypeMenu'),
    'personalVehicle': (read('vMenu/menus/PersonalVehicle.cs'), 'menu'),
    'misc': (read('vMenu/menus/MiscSettings.cs'), 'menu'),
}

cards = [('welcome', guide['welcome'])] + [(s['id'], s) for s in guide['steps']] + \
    [(q['question'], q) for q in guide.get('questions', []) if 'step' not in q]
for name, card in cards:
    menu_id = card.get('menu', 'main')
    for label in card.get('highlight', []):
        check(menu_id in MENUS, f'{name}: lights up rows on "{menu_id}", which this test has no source for')
        if menu_id not in MENUS:
            continue
        source, menu_var = MENUS[menu_id]
        made = re.findall(r'(\w+)\s*=\s*new(?:\s+Menu(?:Item|CheckboxItem|ListItem|SliderItem))?\(\s*"' + re.escape(label) + '"', source)
        check(made, f'{name}: no "{label}" row is made in the {menu_id} menu source')
        added = [v for v in made if re.search(re.escape(menu_var) + r'\.AddMenuItem\(\s*' + re.escape(v) + r'\s*\)', source)]
        check(added, f'{name}: "{label}" is made but not added to the {menu_id} menu ({menu_var})')

# Every **name** the text uses is a real row (or one of the few non-row phrases below), so renaming a
# row can't leave the guide telling players to pick something that isn't there.
client_source = '\n'.join(p.read_text(encoding='utf-8-sig') for p in (root / 'vMenu').rglob('*.cs') if 'obj' not in p.parts and 'bin' not in p.parts)
NOT_ROWS = {'Pause › Settings › Key Bindings › FiveM', '/duty', '/dv', 'discord.gg/paradisestaterp'}
all_cards = [guide['welcome']] + guide['steps'] + guide.get('questions', [])
for card in all_cards:
    texts = card.get('body', []) + [card.get('tip') or '', card.get('foot') or '']
    for bold in re.findall(r'\*\*(.+?)\*\*', ' '.join(texts)):
        if bold in NOT_ROWS:
            continue
        for part in bold.split(' › '):
            check(f'"{part}"' in client_source, f'"{part}" (in **{bold}**) is not a row label anywhere in the client')

# Every menu id the content may name is one PlayerGuide.MenuFor knows.
menu_ids = re.search(r'MenuIds\s*=\s*\{(.*?)\};', content_cs, re.S).group(1)
for menu_id in re.findall(r'"(\w+)"', menu_ids):
    check(f'case "{menu_id}":' in guide_cs, f'PlayerGuide.MenuFor has no case for "{menu_id}"')

# Convars a card waits for are ones the packaged permissions.cfg sets.
for name, card in [(s['id'], s) for s in guide['steps']] + [(q['question'], q) for q in guide.get('questions', [])]:
    convar = card.get('requiresConvar')
    if convar:
        check(re.search(r'^setr\s+' + re.escape(convar) + r'\s', permissions, re.M), f'{name}: {convar} is not set in permissions.cfg')

# ---- The menu ----
add_at = main_menu.find('PlayerGuide.AddToMainMenu(Menu);')
check(0 <= add_at < main_menu.find('var playerSubmenuBtn = new MenuItem("Player Related Options"'), 'the Player Guide row comes first in the main menu')
about_at = main_menu.find('AddMenu(Menu, sub, btn);')
questions_at = main_menu.find('PlayerGuide.AddQuestions();')
check(0 <= about_at < questions_at < main_menu.find('// Refresh everything.'), 'the questions are added once every menu exists')
watch = method_body(main_menu, 'private static bool WatchMenus()')
check(re.search(r'if \(isMenuOpen\)\s*\{[^}]*guideShown = PlayerGuide\.MenuOpened\(\);', watch), 'the menu watcher offers the guide when a menu opens')
check(watch.index('guideShown = PlayerGuide.MenuOpened();') < watch.index('TriggerEvent("vMenu:MenuStateChanged", isMenuOpen, guideShown);'),
      'before announcing the open, which tells What\'s New whether the card is up')

# ---- PlayerGuide ----
check('LoadResourceFile(GetCurrentResourceName(), ContentFile)' in guide_cs and 'ContentFile = "config/player-guide.json"' in guide_cs, 'reads config/player-guide.json')
check('GetResourceKvpInt(SeenKvp) >= content.Version' in guide_cs, 'offers the guide until this version is seen')
seen = re.findall(r'(.{0,120})SetResourceKvpInt\(SeenKvp', guide_cs, re.S)
check(len(seen) == 1 and 'CountsAsSeen(end)' in seen[0], 'marks it seen only when finished or skipped')
check('new TickSleeper(InputTick' in guide_cs and 'inputSleeper.Sleep();' in guide_cs, 'the input loop sleeps while the guide is off')
tick = method_body(guide_cs, 'private static async Task InputTick()')
check('hold.ReadyToRelease(' in tick and 'MenuController.DisableMenuButtons = hold.ButtonsBefore;' in tick,
      'the menu gets its buttons back only after the key that ended the guide is let go')
check(tick.index('hold.ReadyToRelease(') < tick.index('MenuController.DisableMenuButtons = hold.ButtonsBefore;'), 'and not before')
check('if (!MenuController.IsAnyMenuOpen())' in tick and 'End(flow.MenuClosed());' in tick, 'a closed menu ends the guide')
check('GuideInput.Route(' in tick and 'esc: IsDisabledControlJustPressed(0, ControlPauseAlternate)' in tick, 'keys go through GuideInput.Route')
check('foreach (var control in GuideControls)' in tick and 'DisableControlAction(0, control, true);' in tick, 'the guide keys are kept from the game (Esc would open the pause menu)')
check(re.search(r'ControlPause = 199;', guide_cs) and re.search(r'ControlPauseAlternate = 200;', guide_cs), 'P/Start and Esc are among them')
check(re.search(r'ControlJump = 22;', guide_cs) and 'ControlJump }' in guide_cs, 'and jump, which is X on a controller too')
check('hold.NoteKeyboard(UpdateOnscreenKeyboard() == 0, GetGameTimer());' in tick, 'an on-screen keyboard is noted every frame, for the button hold')
check(tick.index('hold.NoteKeyboard(') < tick.index('if (flow.Mode == GuideMode.Off)'), 'in the hold after the guide too')
check(re.search(r'if \(GetGameTimer\(\) < acceptInputAt \|\| !IsScreenFadedIn\(\) \|\| IsPauseMenuActive\(\) \|\| IsPlayerSwitchInProgress\(\)\)', tick),
      'keys pressed while the menu is hidden (fade, switch, pause menu) do nothing')
check('if (GetGameTimer() < acceptInputAt ||' in tick, 'a press right as a card opens is ignored')
check('MenuController.DisableMenuButtons = true;' in method_body(guide_cs, 'private static void Begin(GuideView view)'), 'the menu buttons are off while a card is up')

# ---- The page ----
check(re.search(r'<div id="vmenu-guide" hidden></div>', page), 'the page has the card')
check(page.index('<div id="vmenu-guide" hidden>') < page.index('<div id="vmenu-storage">'), 'the card is outside the storage page and its reset')
script = page[page.index('// The Player Guide\'s card and lit-up rows'):]
script = script[:script.index('</script>')]
check('d.action !== "vmenuGuide"' in script, 'the card only reacts to its own message')
check(re.search(r'#menuapi\[hidden\] ~ #vmenu-guide \{\s*display: none !important;', page) and page.index('<div id="menuapi">') < page.index('<div id="vmenu-guide"'),
      'the card hides whenever MenuAPI hides the menu')
check('innerHTML' not in script and 'insertAdjacentHTML' not in script, 'the card shows the text as text')
check('.menuapi-row__text' in script and '"menuapi-row__text"' in menuapi_js, 'rows are matched on MenuAPI\'s row text element')
for cls in ('menuapi-row', 'menuapi-row--selected', 'menuapi-row--separator'):
    check(f'"{cls}"' in menuapi_js or f'.{cls}' in menuapi_css, f'MenuAPI has {cls}')
check('--menuapi-scale' in menuapi_css and 'data-align' not in page.split('<script')[0], 'the card follows the menu\'s scale')
check('c.dataset.align=n.align' in menuapi_js and 'menu.dataset.align === "right"' in script, 'and sits on the other side of a right-aligned menu')
style = page[:page.index('</style>')]
for selector in re.findall(r'^\s*([^\n{]*#menuapi[^\n{]*)\{', style, re.M):
    check('vmenu-guide' in selector, f'a #menuapi rule outside the guide: {selector.strip()}')

# ---- Manifest and What's New ----
check("'config/player-guide.json'" in manifest, 'the manifest ships config/player-guide.json')
handler = whats_new[whats_new.index("AddEventHandler('vMenu:MenuStateChanged'"):]
check(re.search(r"function\(open, guideShown\).*if guideShown then.*return\s+end\s+SetResourceKvp\(SEEN_KEY, VERSION\)\s+show\(\)", handler, re.S),
      "What's New waits while the guide's card is up")

if failures:
    for f in failures:
        print('FAIL:', f)
    sys.exit(1)
print('player-guide wiring: all checks passed')
