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

## October 23: "on cooldown" while a vehicle is still loading

Players reported "Vehicle spawner is on cooldown." on every click, on foot, with
no vehicle appearing. The cooldown interval was not the cause. The gate showed
the same message for two different states:

- a request still in progress (`_spawning`)
- the configured interval after a vehicle was created

A request is in progress before any vehicle exists while it streams the model
(up to 15 s), asks CAD for an identity ticket (up to 10 s) and probes the ground
(up to 1 s). Add-on models download from the server the first time they are used
and can take seconds; a broken one never loads. Nothing showed it was loading, so
players clicked again and every click said "on cooldown". A model that never
loaded blocked every other vehicle for 15 s per attempt. Separately, if
`CreateVehicle` returned no vehicle, the spawn carried on with an empty handle:
no vehicle, no message, and the model was still added to Recent Vehicles.

Changes (client only):

- The gate records the request it holds (`VehicleSpawnAttempt`: model, save name)
  and reports why a click was turned away (`VehicleSpawnBlock`):
  - **Loading** the model: "Still loading *name*. Choosing a different vehicle
    cancels it."
  - **Spawning** (past loading): "*name* is still spawning."
  - **Cooldown**, only after a vehicle was created: "Vehicle spawner is on
    cooldown." as before.
- **A different vehicle replaces one that is still loading.** "Different" means
  another model or another saved vehicle; the same request again just says it is
  still loading. Nothing has been deleted or created at that point. The replaced
  request stops at its next frame. If its model had already loaded, the core's
  `attempt.TryBeginCreating()` check stops it. That check comes before the old
  vehicle is deleted, the CAD ticket is requested or anything is created, so at
  most one request ever reaches those steps. A replaced request leaves the gate,
  the cooldown and the spinner to the request that replaced it. It releases its
  model unless the replacement uses the same one.
- While a model streams, GTA's loading spinner shows "Loading *name*". It goes
  when the model loads, times out or is replaced.
- The 15 s timeout stays. Its message now names the vehicle, and it is not shown
  for a replaced request.
- If `CreateVehicle` returns no vehicle, the player is told it couldn't be
  created. The model is released, telemetry gets handle 0, and nothing is added
  to Recent Vehicles or claimed with CAD. No cooldown starts.
- Spawn By Name still opens while a vehicle is loading; it is refused with the
  matching message while one is spawning or on cooldown.

Not changed: the CAD and ground-probe waits keep their timeouts, and repeated
clicks still each show a notification.

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
