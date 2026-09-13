# Vehicle spawn cooldown fix

September 12, 2026.

## Behavior

Both public `CommonFunctions.SpawnVehicle` overloads now enter one shared
`VehicleSpawnGate` before the main spawn routine does any asynchronous work.
The gate remains occupied through model loading, entity creation, saved mods,
CAD identity calls (where present), and final setup. Repeated requests return
zero and show the existing cooldown notification without entering that routine.

After a completed spawn returns a nonzero handle, the gate enforces
`vmenu_vehicle_spawner_cooldown` milliseconds from completion (default 1000).
Rejected clicks do not extend this interval. An elapsed game-timer check replaces
the detached timers; older attempts cannot clear a newer spawn's lock.
Timer arithmetic handles the 32-bit game timer wrapping.

Validation failures returning zero and exceptions release the gate without a
post-spawn cooldown. Exceptions still propagate to the caller. Zero or negative
configured durations disable only the interval after completion; they do not
allow overlapping spawn operations. A custom model-name input dialog does not
hold the gate; submitting it goes through the same gate as saved vehicles.

This remains client-side protection for the vMenu entry points. It does not
change vehicle replacement rules or suppress repeated cooldown notifications.
The duration is still read at static initialization; restart the resource after
changing the replicated convar to reliably load the new value.

## Build and verification

From the repository root:

```powershell
python tests/vehicle-spawn-cooldown/test_spawn_cooldown.py
dotnet build vMenu/vMenuClient.csproj -c Release --nologo
```

The regression harness compiles the actual gate and both public spawn wrappers
with a controlled clock and simulated async spawn routine. It passed overlapping
saved/model requests, a long-running spawn, expiry boundaries, rejected clicks,
validation failure, exceptions, zero/negative durations, timer wrap, and custom
input cancellation/submission races. It requires Python and the .NET 9 SDK.

The combined local client Release build passed with three existing CS8321
warnings in `MpPedCustomization.cs`. Its output is
`build/vMenu/vMenuClient.net.dll`, SHA-256:
`b1ab138e362e775b0d5f568d4af8e80d293788cefbee4f6ce86de3690b5885eb`.

FiveM runtime/multiplayer testing has not been performed. On a test server,
rapidly select Spawn Vehicle on a saved entry during loading and CAD latency:
only one spawn should enter setup, and the next should be allowed one configured
interval after completion. Repeat across saved and model-name menus. Confirm an
invalid model can be followed immediately by a valid one and replacement still
respects the existing settings/permissions.

## Source and deployment handoff

This checkout already contained extensive uncommitted PSRP source and built-DLL
changes. Only the cooldown source delta, new gate, regression harness, and this
note are committed. The rebuilt local DLL includes the existing PSRP changes
and is intentionally left unstaged with those changes. The published source
commit therefore does not contain the complete local PSRP build.

The fix is already applied to this working tree. Preserve the remaining local
changes; do not replace them with the older committed source or bulk-stage them.
The prior identity package's source hashes describe its historical build, not
this newly rebuilt client. See `integrations/psrp-cad-vehicle-identity/README.md`
for that baseline's prerequisites. Build from the complete intended PSRP source
before deployment; this task does not deploy to the live server.

Pre-fix local source and client DLL backups are under
`C:/Users/Georgie/.codex/backups/vehicle-spawn-cooldown-20260912`.
