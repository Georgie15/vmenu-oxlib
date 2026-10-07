# Vehicle spawn cooldown fix

September 12, 2026. Updated October 6, 2026.

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

## October 6: recovery from a stalled spawn

The cooldown notification also represents an occupied spawn gate. The gate's
`finally` only runs when `SpawnVehicleCore` returns or throws. `LoadModel` used
to poll `HasModelLoaded` indefinitely, and both CAD identity exports were awaited
without a deadline. A model that exists in the catalog but never streams, or a
CAD callback that never answers, could therefore block spawning for the rest of
the session even when no vehicle had appeared. The screenshot alone cannot
establish which wait stalled on the affected player's client.

Model validation now rejects non-vehicle/missing models before streaming.
Loading stops after 15 seconds using rollover-safe game-timer arithmetic,
releases the model request and returns zero through the gate. The player sees a
model-loading timeout notification; the next request can proceed immediately.
This happens before the previous vehicle is removed or a new entity is created.

CAD begin/claim each wait at most 10 seconds and then use the bridge's existing
optional-integration fallback (empty ticket/false). A completed vehicle still
consumes the configured cooldown. Timeout stages and model hashes are written
to F8/CitizenFX logs even when vMenu debug mode is disabled. A timed-out CAD
request cannot resume the spawn core, create another vehicle or release another
attempt's gate; the external request itself is not cancelled and may finish
later. CAD remains responsible for validating late identity operations.

The expanded regression harness compiles the production model loader and CAD
wait helper in addition to the gate and wrappers. It covers non-loading models,
timer rollover, immediate recovery, CAD begin/claim stalls, healthy responses,
late responses during a newer spawn, and the prior cooldown cases. Substituting
the original model loader reproduces the unbounded wait and fails the regression;
the updated loader passes. These simulated checks do not identify the affected
client's particular streamed asset or CAD failure.

For the October 6 client DLL and deployment notes, see
[`current-build.md`](current-build.md). Build only the client into a temporary
output directory and copy only its DLL into the package, avoiding the build
cleanup/config-copy traps documented there. No live server was changed.

## Build and verification

From the repository root:

```powershell
python tests/vehicle-spawn-cooldown/test_spawn_cooldown.py
dotnet build vMenu/vMenuClient.csproj -c Release --nologo
```

The regression harness compiles the actual gate, both public spawn wrappers,
model loader and identity wait helper with a controlled clock and simulated
async spawn routine. It passed overlapping
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

This section records the September 12 development state. The combined local
source and packaged build were subsequently saved on `main`; see
[`current-build.md`](current-build.md) for the current branch and build hashes.

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
