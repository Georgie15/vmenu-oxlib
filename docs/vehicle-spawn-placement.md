# Vehicle spawning inside garages

October 20, 2026.

## Cause and behavior

The shared spawn core previously added one metre to the player's spawn position,
seated the player, and called `Vehicle.PlaceOnGround()` without checking its
result or resulting position. That wrapper calls `SET_VEHICLE_ON_GROUND_PROPERLY`.
If GTA chose an upper deck, roof or outdoor ground surface, the occupied vehicle
could move to that level. This is the suspected cause from code inspection; the
affected map locations have not been reproduced in FiveM.

Normal menu vehicles, spawn-by-name and saved vehicles now use
`VehicleSpawnPlacement` through the same `CommonFunctions.SpawnVehicleCore`:

- Remove the unconditional one-metre lift.
- Request collision and trace downward at the chosen spawn X/Y, from 0.25 metres
  above its original Z to 3 metres below. Only world and object collision is
  eligible; peds, vehicles and water do not become the floor. Ignore steep hits
  whose surface normal Z is below 0.5.
- Poll pending shape tests across frames and retry missing/invalid collision for
  at most one second, using rollover-safe game-timer arithmetic.
- Use the detected floor and model minimum Z to position the vehicle's bottom
  0.05 metres above that floor.
- Before seating the player, attempt GTA's normal wheel/slope alignment. If it
  fails or moves the vehicle more than 0.75 metres on any axis, restore the local
  position and original rotation immediately, without yielding a frame. Physics
  can then settle the vehicle locally.
- If no nearby floor is found, keep the original requested altitude and skip
  ground alignment. There is no global ground-height fallback.

Placement also applies with Spawn Inside Vehicle disabled. Explicit coordinates
used by the developer entity-placement tool retain their requested position;
that tool's subsequent manual placement logic is separate. Boats and trains
skip terrain placement to preserve their water/track altitude. Airborne
helicopters retain the existing full-speed blade behavior. Vehicle replacement,
saved mods, CAD identity and the shared spawn gate remain in the same core.

The custom vehicle menu executes the external `openspawner` command. Its spawning
implementation is outside this repository and is not covered by this fix.
This change does not check whether a vehicle fits in a parking space or prevent
physics from moving a vehicle spawned beside an edge. Accurate streamed map
collision is still required.

## Verification

```powershell
python tests/vehicle-spawn-placement/test_spawn_placement.py
python tests/vehicle-spawn-cooldown/test_spawn_cooldown.py
dotnet build vMenu/vMenuClient.csproj -c Release --nologo --output <temporary-directory>
```

The placement harness compiles the production placement helper and extracts the
actual production spawn core. Simulated native calls exercise underground,
ground-level and upper-deck garages; incorrect roof/upper/lower placement;
rollback before seating; nearby ramp alignment; failed placement and rotation
rollback; collision streaming, pending/invalid probes, walls and out-of-range
hits; timer wrap; traffic and object floors; vehicle model dimensions; outside
spawns; replacement; saved setup; explicit coordinates; boats/trains; and an
airborne helicopter. The existing cooldown/model/CAD timeout regressions pass.

To demonstrate the pre-fix failure using the same simulated world:

```powershell
python tests/vehicle-spawn-placement/test_spawn_placement.py --baseline-ref 16fbd80
```

That command is expected to fail at the first wrong-floor case. The fixed
working tree passes. These checks do not replace FiveM runtime testing.

Runtime acceptance: spawn ordinary and saved cars in the affected underground
garage, under a roof and on each parking deck; repeat with Spawn Inside Vehicle
enabled/disabled and Replace Previous Vehicle enabled/disabled. Check a ramp and
open road, then an airborne helicopter and boat. Confirm correct floor, normal
settling, saved mods, CAD identity and cooldown. Test both GTA and streamed cars.

## Package and deployment

Built from the latest `main` baseline (`16fbd80`, also `georgie/main` when fetched
on October 20) plus this fix. The client Release build passed with zero warnings
and errors. Only the resulting client DLL was copied into `build/vMenu`;
all other packaged files, including server/configuration, were preserved.

Current DLL hashes and deployment instructions are recorded in
[`current-build.md`](current-build.md). No live server deployment or FiveM
runtime testing was performed. No new convars or dependencies are required.
