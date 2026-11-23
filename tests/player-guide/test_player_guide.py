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

mp_ped_cs = read('vMenu/menus/MpPedCustomization.cs')
MENUS['mpCreator'] = (mp_ped_cs, 'createCharacterMenu')
dressing = guide.get('dressingRoom', {})
dressing_cards = [('dressing ' + s['id'], s) for s in dressing.get('steps', [])] + \
    ([('dressing finish', dressing['finish'])] if 'finish' in dressing else [])
cards = [('welcome', guide['welcome'])] + [(s['id'], s) for s in guide['steps']] + \
    [(q['question'], q) for q in guide.get('questions', []) if 'step' not in q] + dressing_cards
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
NOT_ROWS = {'Pause → Settings → Key Bindings → FiveM', '/duty', '/dv', 'discord.gg/paradisestaterp'}
all_cards = [guide['welcome']] + guide['steps'] + guide.get('questions', []) + [c for _, c in dressing_cards]
for card in all_cards:
    texts = card.get('body', []) + card.get('inside', []) + [card.get('tip') or '', card.get('foot') or '']
    for bold in re.findall(r'\*\*(.+?)\*\*', ' '.join(texts)):
        if bold in NOT_ROWS:
            continue
        for part in bold.split(' → '):
            check(f'"{part}"' in client_source, f'"{part}" (in **{bold}**) is not a row label anywhere in the client')

# The card and the menu use GTA's own font (menuapi/fonts/chalet-london.woff), which swaps some
# characters for game icons: › draws as a blank button and ‹ as the Rockstar "CREATED" badge. Text
# may use printable ASCII and these, each checked by drawing it from that font.
FONT_SAFE = set('←→↑↓…•–—“”‘’·×')
guide_text = read('build/vMenu/config/player-guide.json')
dressing_cs = read('vMenu/DressingRoomGuide.cs')
dressing_flow_cs = read('vMenu/DressingRoomFlow.cs')
guide_strings = ''.join(re.findall(r'"(?:[^"\\\n]|\\.)*"', guide_cs + content_cs + dressing_cs + dressing_flow_cs))
for where, text in (('player-guide.json', guide_text), ('PlayerGuide strings', guide_strings)):
    for ch in sorted({c for c in text if not (' ' <= c <= '~') and c not in '\r\n\t'}):
        check(ch in FONT_SAFE, f'{where} uses U+{ord(ch):04X} {ch!r}, which GTA\'s font may draw as an icon; use one of {"".join(sorted(FONT_SAFE))}')

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
tick = method_body(guide_cs, 'private static void InputFrame()')
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
begin = method_body(guide_cs, 'private static bool Begin(GuideView view)')
check('MenuController.DisableMenuButtons = true;' in begin, 'the menu buttons are off while a card is up')

# ---- Fail safe: the first build froze the menu when showing a card threw (Regex on FiveM) ----
check(begin.index('inputSleeper?.Wake();') < begin.index('return TryShow(view);'),
      'the input loop is woken before a card is shown, so it can always hand the buttons back')
try_show = method_body(guide_cs, 'private static bool TryShow(GuideView view)')
check(re.search(r'try\s*\{\s*Show\(view\);\s*return true;\s*\}\s*catch \(Exception e\)\s*\{\s*Fail\(e\);', try_show), 'showing a card is guarded')
check(len(re.findall(r'(?<![A-Za-z])Show\(view\);', guide_cs)) == 1, 'cards are only shown through TryShow')
opened = method_body(guide_cs, 'internal static bool MenuOpened()')
check(re.search(r'try\s*\{.*return Begin\(flow\.Welcome\(\)\);\s*\}\s*catch \(Exception e\)\s*\{\s*Fail\(e\);\s*return false;', opened, re.S),
      "the menu watcher can't be broken by the guide")
check('if (broken ||' in opened, 'after an error the guide stays off')
fail = method_body(guide_cs, 'private static void Fail(Exception e)')
check('broken = true;' in fail and 'flow?.MenuClosed();' in fail and '"visible\\":false' in fail and 'inputSleeper.Wake();' in fail and 'ReleaseButtonsNow();' in fail,
      'an error stops the guide, hides the card and gets the buttons back')
input_tick = method_body(guide_cs, 'private static async Task InputTick()')
check(re.search(r'try\s*\{\s*InputFrame\(\);\s*\}\s*catch \(Exception e\)\s*\{[^{}]*?Fail\(e\);\s*ReleaseButtonsNow\(\);', input_tick),
      'an error in the input loop hands the buttons back at once')
check('if (broken || flow == null || flow.Mode != GuideMode.Off)' in guide_cs, 'Start the guide and the questions do nothing after an error')

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

# The card's text matches the menu's (themes/psrp-neon.css): GTA grey, trimmed to the game's weight
# with crisp edges by the vmenu-text filter, and bold drawn as an own-colour outline, because the
# font has no bold and the browser's fake one smears. The filter only changes transparency, so it
# goes on elements holding text alone: the key labels and the tip text get their own spans for it.
guide_rules = {}
for sel, body in re.findall(r'([^{}]+)\{([^{}]*)\}', re.sub(r'/\*.*?\*/', '', style, flags=re.S)):
    sel = ' '.join(sel.split())
    if sel.startswith('#vmenu-guide'):
        guide_rules[sel] = guide_rules.get(sel, '') + body
card_rule = guide_rules.get('#vmenu-guide', '')
check('color: rgb(225 225 225);' in card_rule and '-webkit-text-stroke' not in card_rule,
      'the card\'s text is GTA grey, with no dark hairline (it widened the edges)')
CARD_TEXT = ('#vmenu-guide h3, #vmenu-guide p:not(.g-foot), #vmenu-guide .g-head > span:first-child, '
             '#vmenu-guide .g-tag, #vmenu-guide .g-text, #vmenu-guide .g-label')
check('filter: url(#vmenu-text);' in guide_rules.get(CARD_TEXT, ''), 'the card\'s text-only elements take the vmenu-text filter')
for sel, body in guide_rules.items():
    if 'filter:' in body:
        check(sel == CARD_TEXT, f'{sel} takes a filter, but only text-only elements may')
check('el("span", "g-label", text)' in script, 'key labels have their own span (the main key has a solid background)')
check('el("span", "g-text", rich(d.tip))' in script, 'the tip text has its own span (the tip box has a see-through background)')
check(re.search(r'<filter id="vmenu-text"[^>]*>\s*<feComponentTransfer>\s*<feFuncA type="gamma" amplitude="1" exponent="2.5" offset="0" />', page),
      'the page defines the vmenu-text filter')
for sel in ('#vmenu-guide .g-head', '#vmenu-guide h3', '#vmenu-guide b', '#vmenu-guide .g-tag'):
    check('-webkit-text-stroke: 0.4px currentColor;' in guide_rules.get(sel, ''), f'{sel} is bold by an own-colour outline')
for sel in ('#vmenu-guide h3', '#vmenu-guide b'):
    check('font-weight: normal;' in guide_rules.get(sel, ''), f'{sel} is bold by default, so it turns the browser\'s fake bold off')
for sel, body in guide_rules.items():
    if re.search(r'font-weight:\s*(bold|[6-9]00)', body):
        check(sel == '#vmenu-guide kbd', f'{sel} asks GTA\'s font for a bold it doesn\'t have (only the Arial key caps may be bold)')
check('-webkit-text-stroke: 0;' in guide_rules.get('#vmenu-guide .g-head span + span', ''), 'the muted step counter isn\'t bold')

# ---- The dressing room (DressingRoomGuide, for psrp_dressing_room) ----
check(dressing.get('steps') and dressing.get('finish'), 'the packaged guide has the dressing room steps and finish card')
save_at = mp_ped_cs.find('if (await SavePed())')
check(save_at >= 0 and re.match(r'if \(await SavePed\(\)\)\s*\{\s*(//[^\n]*\n\s*)*DressingRoomGuide\.CharacterSaved\(\);', mp_ped_cs[save_at:]),
      'Save Character tells the dressing room, right after a successful save')
check('internal void RememberCurrentCharacter()' in mp_ped_cs, 'a dressing room save makes the character the one the next join restores')
remember = method_body(mp_ped_cs, 'internal void RememberCurrentCharacter()')
check('SetResourceKvp("vmenu_last_character"' in remember and 'SaveMpCharacterSnapshot(currentCharacter);' in remember,
      'as spawning it from Saved Characters does (last character and snapshot)')
check(re.search(r'internal static bool MenuOpened\(\)\s*\{\s*(//[^\n]*\n\s*)*if \(DressingRoomGuide\.Active\)\s*\{\s*return true;', guide_cs),
      'while the dressing room runs, the Player Guide stays off and What\'s New waits')
for event in ('vMenu:DressingRoom:Start', 'vMenu:DressingRoom:Stop'):
    check(f'EventHandlers.Add("{event}"' in dressing_cs, f'the dressing room listens for {event}')
for event in ('vMenu:DressingRoom:Started', 'vMenu:DressingRoom:Unavailable', 'vMenu:DressingRoom:Saved',
              'vMenu:DressingRoom:Leave', 'vMenu:DressingRoom:Ended'):
    check(f'TriggerEvent("{event}"' in dressing_cs, f'the dressing room tells psrp_dressing_room {event}')
check('new TickSleeper(FrameTick' in dressing_cs and 'sleeper.Sleep();' in dressing_cs, 'its loop sleeps while it is off')
frame_tick = method_body(dressing_cs, 'private static async Task FrameTick()')
check(re.search(r'try\s*\{\s*Frame\(\);\s*\}\s*catch \(Exception e\)\s*\{\s*Fail\(e\);\s*ReleaseButtonsNow\(\);', frame_tick),
      'an error in its loop stops it and hands the menu its buttons at once')
frame = method_body(dressing_cs, 'private static void Frame()')
check('hold.ReadyToRelease(' in frame and frame.index('hold.ReadyToRelease(') < frame.index('MenuController.DisableMenuButtons = hold.ButtonsBefore;'),
      'after the finish card the buttons come back only once its key is let go')
finish_keys = method_body(dressing_cs, 'private static void FinishKeys(bool pad)')
check('MenuController.DisableMenuButtons = true;' in finish_keys and 'left: false' in finish_keys and 'right: false' in finish_keys,
      'the finish card keeps the menu\'s buttons off every frame, and the arrows don\'t send anyone off')
saved = method_body(dressing_cs, 'internal static void CharacterSaved()')
check('hold.Start(buttonsAtStart);' in saved, 'the buttons go back to what they were before the dressing room, not what a keyboard left')
check('MenuController.DisableMenuButtons' not in method_body(dressing_cs, 'private static void Start(string destination)').replace('buttonsAtStart = MenuController.DisableMenuButtons;', ''),
      'during the steps the menu is the player\'s: Start never turns its buttons off')
check('"dressing"' in script and '"dressingDone"' in script, 'the page draws the dressing room card')
check('el("span", "g-text", String(c.title || ""))' in script, 'checklist text has its own span (the text filter is for text-only elements)')

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
