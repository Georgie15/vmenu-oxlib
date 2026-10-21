"""Check the client's precomputed permission table against the old per-call IsAllowed logic.

Compiles the production Permission enum, GetPermissionAndParentPermissions and
BuildEffectivePermissions from SharedClasses/PermissionsManager.cs and compares every
permission, with staff-only on and off, for fixed and random permission sets.

Requires Python and the .NET 9 SDK; no FiveM runtime or external test packages.
"""
from pathlib import Path
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
source = (root / 'SharedClasses/PermissionsManager.cs').read_text(encoding='utf-8-sig')


def block(pattern):
    return re.search(pattern, source, re.M | re.S)[0]


enum = block(r'^        public enum Permission\s*\{.*?^        \}')
parents_field = block(r'^        private static readonly Dictionary<Permission, List<Permission>> parentPermissions = new\(\);$')
parents = block(r'^        public static List<Permission> GetPermissionAndParentPermissions\(.*?^        }$')
build = block(r'^        internal static bool\[\] BuildEffectivePermissions\(.*?^        }$')

# Wiring: the table is built when permissions arrive, and IsAllowed only reads it.
client_is_allowed = block(r'^        private static bool IsAllowedClient\(.*?^        }$')
assert 'GetSettingsBool' not in client_is_allowed, 'IsAllowed must not read a convar per call'
assert 'GetPermissionAndParentPermissions' not in client_is_allowed, 'IsAllowed must not re-check parents per call'

# The packaged permissions.cfg: every grant says allow or deny (anything else isn't a valid
# grant, like the old `... God" true`), and none is repeated.
aces = [line.strip() for line in (root / 'build/vMenu/config/permissions.cfg').read_text(encoding='utf-8').splitlines()
        if line.strip().startswith('add_ace ')]
invalid = [line for line in aces if line.split('#')[0].split()[-1] not in ('allow', 'deny')]
assert not invalid, f'add_ace without allow/deny: {invalid}'
repeated = sorted({line for line in aces if aces.count(line) > 1})
assert not repeated, f'repeated grants: {repeated}'
print(f'PASS: permissions.cfg has {len(aces)} grants, each allow or deny, none repeated')
assert 'effectivePermissions = BuildEffectivePermissions(Permissions, ConfigManager.GetSettingsBool(ConfigManager.Setting.vmenu_menu_staff_only));' in source

program = r'''
using System;
using System.Collections.Generic;
using System.Linq;

static class PermissionsManager
{
''' + enum + '\n' + parents_field + '\n' + parents + '\n' + build + r'''

    // Reference: the client IsAllowed logic before the table (minus its true-only cache,
    // which only mattered when SetPermissions arrived twice).
    static bool OldIsAllowed(Dictionary<Permission, bool> Permissions, bool staffOnly, Permission permission)
    {
        var staffPermissionAllowed = (
            Permissions.ContainsKey(Permission.Staff) && Permissions[Permission.Staff]
        ) || (
            Permissions.ContainsKey(Permission.Everything) && Permissions[Permission.Everything]
        );
        if (staffOnly && !staffPermissionAllowed)
        {
            return false;
        }
        var permissionsToCheck = GetPermissionAndParentPermissions(permission);
        return permissionsToCheck.Any(p => Permissions.ContainsKey(p) && Permissions[p]);
    }

    static int compared;

    static void Compare(Dictionary<Permission, bool> perms, string label)
    {
        foreach (var staffOnly in new[] { false, true })
        {
            var table = BuildEffectivePermissions(perms, staffOnly);
            foreach (Permission p in Enum.GetValues(typeof(Permission)))
            {
                var expected = OldIsAllowed(perms, staffOnly, p);
                if (table[(int)p] != expected)
                {
                    throw new Exception($"FAIL: {label}, staffOnly={staffOnly}, {p}: table={table[(int)p]} old={expected}");
                }
                compared++;
            }
        }
    }

    static int Main()
    {
        var all = (Permission[])Enum.GetValues(typeof(Permission));
        Compare(new Dictionary<Permission, bool>(), "no permissions");
        Compare(all.ToDictionary(p => p, p => false), "everything explicitly false");
        Compare(new Dictionary<Permission, bool> { [Permission.Everything] = true }, "Everything");
        Compare(new Dictionary<Permission, bool> { [Permission.Staff] = true }, "Staff");
        foreach (var p in all)
        {
            Compare(new Dictionary<Permission, bool> { [p] = true }, "only " + p);
            Compare(new Dictionary<Permission, bool> { [p] = true, [Permission.Staff] = true }, "staff + " + p);
        }

        var random = new Random(20261021);
        for (var i = 0; i < 500; i++)
        {
            var perms = new Dictionary<Permission, bool>();
            foreach (var p in all)
            {
                var roll = random.Next(10);
                if (roll == 0) perms[p] = true;
                else if (roll == 1) perms[p] = false;
            }
            Compare(perms, "random set " + i);
        }

        Check(BuildEffectivePermissions(null, false).All(v => !v), "a missing permission list allows nothing");
        Console.WriteLine($"PASS: permission table matches the old IsAllowed for {compared} checks");
        return 0;
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }
}
'''

with tempfile.TemporaryDirectory(prefix='vmenu-permission-table-') as temp:
    work = Path(temp).resolve()
    work.relative_to(Path(tempfile.gettempdir()).resolve())
    (work / 'Tests.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup>
</Project>''', encoding='utf-8')
    (work / 'Program.cs').write_text(program, encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(work / 'Tests.csproj'),
                    '--configuration', 'Release', '--verbosity', 'quiet'], check=True)
