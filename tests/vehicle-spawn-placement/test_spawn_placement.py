"""Compile the production placement helper and spawn core against a simulated world.

Requires Python and .NET 9. No FiveM runtime or external test packages.
"""
from pathlib import Path
import argparse
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--baseline-ref', help='Run the spawn core from a Git ref instead of the working tree (pre-fix refs should fail).')
args = parser.parse_args()
common = (subprocess.check_output(['git', 'show', args.baseline_ref + ':vMenu/CommonFunctions.cs'], cwd=root).decode('utf-8-sig')
          if args.baseline_ref else (root / 'vMenu/CommonFunctions.cs').read_text(encoding='utf-8-sig'))
core = re.search(r'^        private static async Task<int> SpawnVehicleCore\(.*?^        }$',
                 common, re.M | re.S)[0]

with tempfile.TemporaryDirectory(prefix='vmenu-spawn-placement-') as temp:
    work = Path(temp)
    (work / 'Tests.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup>
</Project>''', encoding='utf-8')
    (work / 'VehicleSpawnPlacement.cs').write_bytes((root / 'vMenu/VehicleSpawnPlacement.cs').read_bytes())
    (work / 'Harness.cs').write_bytes((Path(__file__).parent / 'Harness.cs').read_bytes())
    (work / 'SpawnCore.cs').write_text('''using System;
using System.Linq;
using System.Threading.Tasks;
using CitizenFX.Core;
using static CitizenFX.Core.Native.API;
namespace vMenuClient { partial class Program {
''' + core + '\n} }', encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(work / 'Tests.csproj'),
                    '--configuration', 'Release', '--verbosity', 'quiet'], check=True)
