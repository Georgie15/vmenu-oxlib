"""Release J: vehicle loops poll every 100 ms in a vehicle, as vMenu Enhanced does.

vMenu Enhanced polls the seat every 250 ms and vehicle state every 100-500 ms, and runs only the
options that must be reapplied each frame (such as torque) every frame. Release J:

- GeneralTasks (the vehicle switch check) rests 100 ms, seated or not;
- VehicleOptions rests 100 ms in a vehicle too, unless VehicleOptionsEveryFrame() says an option is
  on that the game needs reapplied each frame, or a vehicle switch is waiting to be handled.

This test checks that every option VehicleOptions applies on its every-frame path is covered by
VehicleOptionsEveryFrame() (with the same permission), so turning any of them on still gets
every-frame treatment, and that the once-per-switch settings are covered by SwitchedVehicle. It
mutation-tests itself: dropping any option from the helper must make the coverage check fail.

Release M: vehicle god mode, freeze, keep clean, no helmet and infinite fuel set things the game
keeps, so they ride on the 100 ms pass; the test checks they no longer force every frame.

Requires Python; no FiveM runtime.
"""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[2]
fc = (root / 'vMenu' / 'FunctionsController.cs').read_text(encoding='utf-8-sig')


def member(source, pattern):
    match = re.search(pattern, source, re.M | re.S)
    assert match, f'not found: {pattern}'
    return match[0]


def strip_block(source, header):
    """Remove `header { ... }` (balanced braces) from source."""
    start = source.index(header)
    i = source.index('{', start)
    depth = 0
    while True:
        if source[i] == '{':
            depth += 1
        elif source[i] == '}':
            depth -= 1
            if depth == 0:
                return source[:start] + source[i + 1:]
        i += 1


vehicle = member(fc, r'^        private async Task VehicleOptions\(\).*?^        }$')
helper = member(fc, r'^        private bool VehicleOptionsEveryFrame\(\).*?^        }$')
general = member(fc, r'^        private async Task GeneralTasks\(\).*?^        }$')

in_vehicle = vehicle[vehicle.index('if (inVehicle)'):vehicle.index('// When the player is not inside a vehicle:')]
assert 'if (SwitchedVehicle)' in in_vehicle
switch_block = in_vehicle[in_vehicle.index('if (SwitchedVehicle)'):]
every_frame_path = strip_block(in_vehicle, 'if (SwitchedVehicle)')

GOD_SUB_OPTIONS = {'VehicleGodVisual', 'VehicleGodEngine', 'VehicleGodAutoRepair'}  # all ANDed with VehicleGodMode
VALUES = {'VehicleTorqueMultiplierAmount', 'VehiclePowerMultiplierAmount'}


def options_used(block):
    used = set(re.findall(r'MainMenu\.VehicleOptionsMenu\.(\w+)', block))
    if 'noHelmet' in block:
        used.add('VehicleNoBikeHelemet')
    return used


def permission_for(option):
    """The permission VehicleOptions checks next to an option (same line)."""
    for line in vehicle.splitlines():
        if f'MainMenu.VehicleOptionsMenu.{option}' in line:
            found = re.search(r'IsAllowed\(Permission\.(\w+)\)', line)
            if found:
                return found[1]
    return None


# Release M: these set things the game keeps, or put right what already happened, so the pass 10
# times a second in a vehicle is enough for them (vMenu Enhanced uses no loop, or a slower one, for
# each). Only options that last a single frame, such as torque, keep the loop every frame.
AT_100MS = {'VehicleGodMode', 'VehicleFrozen', 'VehicleNeverDirty', 'VehicleNoBikeHelemet', 'VehicleInfiniteFuel'}


def coverage_errors(helper_source):
    covered = dict(re.findall(r'options\.(\w+) && IsAllowed\(Permission\.(\w+)\)', helper_source))
    errors = []
    if 'SwitchedVehicle' not in helper_source:
        errors.append('a waiting vehicle switch does not keep the loop every frame')
    for option in sorted(set(covered) & AT_100MS):
        errors.append(f'{option} is fine on the 100 ms pass but still forces every frame')
    for option in sorted(options_used(every_frame_path) - VALUES - AT_100MS):
        if option in GOD_SUB_OPTIONS:
            # They only act with vehicle god mode on, so they follow its rate.
            if 'VehicleGodMode' not in covered and 'VehicleGodMode' not in AT_100MS:
                errors.append(f'{option} needs VehicleGodMode covered')
            continue
        if option not in covered:
            errors.append(f'{option} is applied every frame in a vehicle but does not keep the loop every frame')
            continue
        expected = permission_for(option)
        if expected and covered[option] != expected:
            errors.append(f'{option}: helper checks {covered[option]}, VehicleOptions checks {expected}')
    return errors


errors = coverage_errors(helper)
assert not errors, errors
assert AT_100MS <= options_used(every_frame_path), 'every option allowed on the 100 ms pass is still applied there'
print(f'PASS: every per-frame vehicle option keeps VehicleOptions every frame '
      f'({len(options_used(every_frame_path) - VALUES - GOD_SUB_OPTIONS - AT_100MS)} options, same permissions); '
      f'{len(AT_100MS)} that the game keeps are applied 10 times a second')

# Once-per-switch settings are applied on the pass after a switch, which SwitchedVehicle covers.
for once in ('VehiclePowerMultiplier', 'VehicleNoSiren', 'DisablePlaneTurbulence', 'DisableHelicopterTurbulence', 'AnchorBoat'):
    assert once in switch_block, once
print('PASS: power multiplier, no siren, turbulence and anchor still apply on the pass after a switch')

# The loop rests in a vehicle when nothing needs every frame.
assert re.search(r'if \(AnyMenuOpen \|\| engineAlwaysOn \|\| \(inVehicle && VehicleOptionsEveryFrame\(\)\)\)\s*\{\s*await Delay\(1\);\s*\}\s*else\s*\{\s*await Rest\(StateRestMs\);', vehicle)
assert general.rstrip().endswith('await Rest(StateRestMs);\n        }') and 'Delay(1)' not in general, \
    'the vehicle switch check rests 100 ms, seated or not'
print('PASS: VehicleOptions and the vehicle switch check rest 100 ms in a vehicle when idle')

# Mutation self-test: dropping any line from the helper must be caught.
lines = [l for l in helper.splitlines() if re.search(r'options\.\w+ && IsAllowed|SwitchedVehicle', l)]
for line in lines:
    mutated = helper.replace(line, '                || false', 1)
    assert coverage_errors(mutated), f'the coverage check misses a dropped line: {line.strip()}'
print(f'PASS: mutation self-test ({len(lines)} helper lines, each one caught when dropped)')
