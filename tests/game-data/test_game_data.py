"""Check vMenu's built-in base-game vehicle, ped and weapon lists.

Vehicles:
- Every class list in vMenu/data/VehicleData.cs is free of duplicates and holds the
  base-game models added from classic vMenu legacy v3.9.2, POLCOQUETTE4 included
  (POLCOQUETTE5 was a typo: no such model exists).
- build/vMenu/config/vehicleIdentityModels.json maps every model in those lists to the
  vMenu.VehicleSpawner.<Class> permission the server checks. It is generated from the
  lists, so it can't drift: run this script with --write after changing a list.
- The spawner hides models the running game build doesn't have.

Peds: the legacy v3.9.2 additions are listed once, in the right menu, and the ped menus
hide models the game build doesn't have.

Weapons: weapon_newspaper has a name, a description and its own permission.

Requires Python only; no FiveM runtime.
"""
from pathlib import Path
import json
import re
import sys

root = Path(__file__).resolve().parents[2]
data_cs = (root / 'vMenu/data/VehicleData.cs').read_text(encoding='utf-8-sig')
json_path = root / 'build/vMenu/config/vehicleIdentityModels.json'

# The permission suffix of each class list (vMenu.VehicleSpawner.<suffix>).
PERMISSION = {'SportsClassics': 'SportsClassic'}

lists = {}
for m in re.finditer(r'public static List<string> (\w+) \{ get; \} = new List<string>\(\)\s*\{(.*?)\};', data_cs, re.S):
    names = []
    for line in m.group(2).splitlines():
        s = line.strip()
        if s.startswith('//'):
            continue
        mm = re.match(r'"([^"]+)"\s*,', s)
        if mm:
            names.append(mm.group(1))
    lists[m.group(1)] = names

classes = re.search(r'VehicleClasses \{ get; \} = new Dictionary<string, List<string>>\(\)\s*\{(.*?)\};', data_cs, re.S).group(1)
order = re.findall(r'\[GetLabelText\("VEH_CLASS_(\d+)"\)\] = (\w+),', classes)
assert [int(i) for i, _ in order] == list(range(23)), 'VehicleClasses lists classes 0-22 in order'
assert all(name in lists for _, name in order), 'every class has a list'


def identity_map():
    # A model in two classes gets the later one, as the original generator did.
    out = {}
    for _, name in order:
        for model in lists[name]:
            out[model] = PERMISSION.get(name, name)
    return out


def render(mapping):
    return json.dumps(mapping, indent=2) + '\n'


if '--write' in sys.argv:
    json_path.write_text(render(identity_map()), encoding='utf-8')
    print(f'wrote {json_path.relative_to(root)}')
    sys.exit(0)

for name, models in lists.items():
    dupes = sorted({m for m in models if models.count(m) > 1})
    assert not dupes, f'{name} lists {dupes} twice'
    assert all(m == m.upper() and re.fullmatch(r'[A-Z0-9_]+', m) for m in models), f'{name} has a malformed model name'
total = sum(len(v) for v in lists.values())
unique = len({m for v in lists.values() for m in v})
print(f'PASS: {len(lists)} class lists, {total} entries ({unique} models), no duplicates within a class')

# Base-game models added from classic vMenu legacy v3.9.2 (TomGrobbe/vMenu, branch legacy).
added = {
    'Sedans': ['MERULA', 'SENTINEL6'],
    'SUVs': ['ESTRIDE', 'WARDEN'],
    'Coupes': ['DRIFTSENTINEL2'],
    'Muscle': ['DRIFTDOMINATOR8', 'DRIFTDOMINATOR9'],
    'SportsClassics': ['ASTRALE', 'GT750', 'ITALI2'],
    'Sports': ['CARTUCCIA', 'DRIFTCOQUETTE', 'DRIFTELEGY', 'DRIFTRT3000'],
    'Super': ['FMJ2', 'HORUS', 'LRCGT', 'LUIVA', 'VELENOGT', 'XTREME'],
    'OffRoad': ['CARACARA3'],
    'Utility': ['DRIFTKEITORA', 'KEITORA', 'TRFLAT2'],
    'Vans': ['LAUFER'],
    'Service': ['VIVANITE2'],
    'Emergency': ['POLBUFFALO', 'POLBUFFALO6', 'POLCOQUETTE4', 'POLIGNUS'],
}
for name, models in added.items():
    missing = [m for m in models if m not in lists[name]]
    assert not missing, f'{name} is missing {missing}'
    for m in models:
        line = re.search(r'^\s*"' + m + r'",(.*)$', data_cs, re.M).group(1)
        assert re.search(r'// .*DLC - Requires b\d{4}', line), f'{m} says which DLC and game build it needs'
assert 'POLCOQUETTE5' not in data_cs, 'the POLCOQUETTE5 typo is gone'
print(f'PASS: the {sum(len(v) for v in added.values())} legacy v3.9.2 models are listed in their classes, each with its DLC and build')

stored = json_path.read_text(encoding='utf-8')
expected = render(identity_map())
assert stored.replace('\r\n', '\n') == expected, \
    'vehicleIdentityModels.json is out of date: run python tests/game-data/test_game_data.py --write'
assert 'POLCOQUETTE5' not in stored and json.loads(stored)['POLCOQUETTE4'] == 'Emergency'
print(f'PASS: vehicleIdentityModels.json maps all {unique} models to their spawner permission, in list order')

spawner = (root / 'vMenu/menus/VehicleSpawner.cs').read_text(encoding='utf-8-sig').replace('\r\n', '\n')
populate = spawner.split('void PopulateClass()')[1].split('menu.OnItemSelect')[0]
assert re.search(r'foreach \(var veh in VehicleData\.Vehicles\.VehicleClasses\[className\]\)\s*\{\s*'
                 r'(//[^\n]*\n\s*)*if \(!DoesModelExist\(veh\)\)\s*\{\s*continue;\s*\}', populate), \
    'models the game build lacks are skipped before anything else'
assert 'shownModels.Add(veh);' in populate and 'vehicleClassMenu.AddMenuItem(vehBtn);' in populate
assert populate.count('vehicleClassMenu.AddMenuItem(') == 1, 'one place adds a vehicle, right after recording its model'
assert 'Enabled = false' not in populate and 'MenuItem.Icon.LOCK' not in populate, 'no locked placeholder entries'
assert 'await SpawnVehicle(shownModels[index2], SpawnInVehicle, ReplaceVehicle);' in spawner, \
    'a click spawns the model of the item clicked, not the list entry at that index'
common = (root / 'vMenu/CommonFunctions.cs').read_text(encoding='utf-8-sig')
assert 'public static bool DoesModelExist(uint modelHash) => IsModelInCdimage(modelHash);' in common
print('PASS: the spawner hides models missing from the game build and spawns the clicked model')

# --- Peds -----------------------------------------------------------------------------
appearance = (root / 'vMenu/menus/PlayerAppearance.cs').read_text(encoding='utf-8-sig').replace('\r\n', '\n')
ped_dicts = {}
for name in ['mainModels', 'animalModels', 'maleModels', 'femaleModels', 'otherPeds']:
    body = re.search(r'private readonly Dictionary<string, string> ' + name + r' = new\(\)\s*\{(.*?)\n\s*\};', appearance, re.S).group(1)
    ped_dicts[name] = re.findall(r'\["([^"]+)"\] = "([^"]*)"', body)
    keys = [k.lower() for k, _ in ped_dicts[name]]
    dupes = sorted({k for k in keys if keys.count(k) > 1})
    assert not dupes, f'{name} lists {dupes} twice'

# Base-game peds added from classic vMenu legacy v3.9.2 (A Safehouse in the Hills, mp2025_02,
# and Kortz Center Heist, mp2026_01).
new_peds = {
    'animalModels': ['a_c_cat_02'],
    'otherPeds': ['cs_amandatownley_02', 'csb_avi_04', 'csb_faber_02', 'csb_faber_03', 'csb_rae',
                  'csb_rafdeangelis_02', 'g_m_m_knowway_01', 'ig_alec', 'ig_amandatownley_02', 'ig_avi_04',
                  'ig_faber_02', 'ig_faber_03', 'ig_fibleader_01', 'ig_pernell_moss_02', 'ig_rae',
                  'ig_rafdeangelis_02', 'ig_valencheat', 'p_michael_02', 's_m_m_highsec_06',
                  's_m_m_kortzsecurity_01'],
}
everywhere = [k.lower() for entries in ped_dicts.values() for k, _ in entries]
for name, peds in new_peds.items():
    listed = [k for k, _ in ped_dicts[name]]
    for ped in peds:
        assert ped in listed, f'{ped} is in {name}'
        assert everywhere.count(ped) == 1, f'{ped} is listed once across the ped menus'
        line = re.search(r'^\s*\["' + ped + r'"\][^\n]*$', appearance, re.M).group(0)
        assert re.search(r'// mp20(25_02|26_01)$', line), f'{ped} says which DLC it needs'
models_cs = (root / 'vMenu/data/PedModels.cs').read_text(encoding='utf-8-sig')
animal_hashes = re.findall(r'GetHashKey\("([^"]+)"\)', models_cs)
assert sorted(animal_hashes) == sorted(k for k, _ in ped_dicts['animalModels']), \
    'the animal check (PedModels.AnimalHashes) covers exactly the Animals menu'
print(f'PASS: the {sum(len(v) for v in new_peds.values())} legacy v3.9.2 peds are listed once each; '
      f'AnimalHashes matches the {len(ped_dicts["animalModels"])} animals')

create = appearance.split('MenuController.BindMenuItem(spawnPedsMenu, otherPedsMenu, otherPedsBtn);')[1].split('async void FilterMenu')[0]
assert 'static bool InGameBuild(string model) => IsModelInCdimage((uint)GetHashKey(model));' in create
for menu_dict, var in [('animalModels', 'animal'), ('mainModels', 'ped'), ('maleModels', 'ped'), ('femaleModels', 'ped'), ('otherPeds', 'ped')]:
    assert re.search(r'foreach \(var ' + var + r' in ' + menu_dict + r'\)\s*\{\s*if \(!InGameBuild\(' + var + r'\.Key\)\) continue;', create), \
        f'{menu_dict} skips models the game build lacks'
assert 'var model = (uint)GetHashKey(item.Text);' in appearance, 'a ped is spawned by the clicked item, so skipping items is safe'
print('PASS: the ped menus hide models missing from the game build')

# --- Weapons --------------------------------------------------------------------------
weapons = (root / 'vMenu/data/ValidWeapon.cs').read_text(encoding='utf-8-sig')
perms = (root / 'SharedClasses/PermissionsManager.cs').read_text(encoding='utf-8-sig')
assert weapons.count('{ "weapon_newspaper", GetLabelText("WT_NEWSPAPER") },') == 1
assert weapons.count('{ "weapon_newspaper", GetLabelText("WTD_NEWSPAPER") },') == 1
assert weapons.count('["weapon_newspaper"] = Permission.WPNewspaper,') == 1
enum = re.search(r'public enum Permission\s*\{(.*?)\n        \}', perms, re.S).group(1)
members = re.findall(r'^\s*(\w+),', enum, re.M)
assert members.count('WPNewspaper') == 1 and members.index('WPNewspaper') == members.index('WPStunRod') + 1
for dict_name in ['weaponDescriptions', 'weaponNames']:
    body = re.search(r'public static readonly Dictionary<string, string> ' + dict_name + r' = new\(\)\s*\{(.*?)\};', weapons, re.S).group(1)
    names = re.findall(r'\{ "(weapon_\w+)"', body)
    assert len(names) == len(set(names)), f'{dict_name} has no duplicates'
names_body = re.search(r'public static readonly Dictionary<string, string> weaponNames = new\(\)\s*\{(.*?)\};', weapons, re.S).group(1)
perm_body = re.search(r'public static readonly Dictionary<string, Permission> weaponPermissions = new\(\)\s*\{(.*?)\};', weapons, re.S).group(1)
assert set(re.findall(r'\{ "(weapon_\w+)"', names_body)) <= set(re.findall(r'\["(weapon_\w+)"\]', perm_body)), \
    'every named weapon has a permission (CreateWeaponsList looks each one up)'
options = (root / 'vMenu/menus/WeaponOptions.cs').read_text(encoding='utf-8-sig')
assert 'var cat = (uint)GetWeapontypeGroup(weapon.Hash);' in options, \
    'weapon categories come from the game, so a weapon this build lacks (group 0) gets no menu entry'
print('PASS: weapon_newspaper has a name, a description and the WeaponOptions.Newspaper permission')
