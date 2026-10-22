# Current build and branch handoff

## Current package

`main` is the only live branch, and its `build/vMenu/` folder is the latest
PSRP vMenu package. Start every change from an up-to-date `main`. Agent and
build rules are in [AGENTS.md](../AGENTS.md).

| | |
| --- | --- |
| Latest change | October 21, 2026: player features release F |
| `build/vMenu/vMenuClient.net.dll` | `38bac5d98528d7a5663fd7adee888af6ef9fb62b9cb1d7fc61ada6d48f1e635d` |
| `build/vMenu/vMenuServer.net.dll` | `003e0932bed3ee2195f052e292b8f5d63e2f30fd8beace5ce46e728daa5c66e9` (built from this source; see release B) |

Check a checkout with
`sha256sum build/vMenu/vMenuClient.net.dll build/vMenu/vMenuServer.net.dll`.
When the package changes, update this table, add a dated section below and tag
the commit `build-YYYY-MM-DD`.

This repository identifies the package. It does not establish which copy is
installed on a server; hash the installed files before deploying.

## September 27, 2026: combined snapshot

`main` records the combined local vMenu source and the `build/vMenu` package
that was in the primary checkout when this branch was created. This includes
the earlier unpublished PSRP work and the local ONX face selector integration.

The packaged DLLs were committed as found, without rebuilding them during
the branch change:

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `cacfbdbec147b5c70269dda5a9c121c643a7416f2a85a8a4f0af61f9428ba600` |
| `build/vMenu/vMenuServer.net.dll` | `e17419b8231fc020b955753299dbf2d1785ba30051ad17f28c2c8be8aede9c8a` |

The older `master`, `dev` and `codex/*` branches were retired on October 21,
2026. Their work is in `main`: the ONX face selector, outfit/hair fix, CAD
vehicle identity and the RVF `vehiclerepair` event from `master` (`52b59cb`),
which every client DLL committed on `main` includes. `dev`
was an unmodified copy of the upstream fork's `dev`, which is still available
at `github.com/Gravxd/vmenu-oxlib`.

## September 30, 2026: permission request retry (client only)

Artifact upgrade plan 4.2 (PSRP-Development
`FiveM Scripts/ARTIFACT-UPGRADE-PLAN-2026-09-30.md`). The client asked for its
permissions once. If `vMenu:SetPermissions` / `vMenu:SetAddons` were lost (a known
issue on newer artifacts for players still joining), F1, noclip and all of vMenu
stayed dead for the session. `vMenu/PermissionBootstrapRetry.cs` now re-sends
`vMenu:RequestPermissions` only while the menu is still not built: after 45 s,
90 s, 180 s, then every 5 minutes. The first request goes out at resource start
as before. The long waits are deliberate (see below).

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `52c7b233efa00552684712f401f2d15fd2b05ec527ab0cd0f8973a761322e61e` |
| `build/vMenu/vMenuServer.net.dll` | `e17419b8231fc020b955753299dbf2d1785ba30051ad17f28c2c8be8aede9c8a` (unchanged) |

Test: `dotnet run --project tests/permission-bootstrap-retry`.

**Deploy:** only `vMenuClient.net.dll`. Before deploying, hash the live
`vMenuClient.net.dll` and confirm it is the previous package
(`78b8ac8c8649ed89e629b2a1c1805bebe88c67c3f6d750c2455140ef5af90c94`). If not,
stop: live runs a different client than this repo records.

### What was checked (method-level IL comparison)

Builds are deterministic, but a DLL built from another folder path differs byte
for byte, so hashes can't prove source equality. Comparing every method's IL with
metadata tokens resolved to names showed:

- The previous committed **client** DLL matches `main` source exactly. The new
  client differs only in the `MainMenu` constructor and the added retry code.
- The committed **server** DLL (`e17419b8…`) is **not** `main`'s server source.
  `main` has the per-handle permission queue (`ProcessPermissionQueueAsync`,
  `InvalidatePermissionSession`, session/license checks). The packaged server has an
  earlier version: one global `permissionsBootstrapBusy` lock, a license re-check
  after each chunk, no merging of duplicate requests, and no session invalidation
  on join/drop. This matches the September 20 note that the queued server DLL was
  pulled and a prior build restored.

**Do not build and deploy the server project from `main`** without deciding
first. That would ship the pulled queue. The server-side parts of the upgrade plan
(a request cooldown in `RequestPermissions`, and plan 4.5, removing the
`playerJoining` push) are on hold until someone decides which server source is
the base: `main`'s queue version, or a reconstruction of the packaged one. The
client retry waits are long because the packaged server serialises bootstraps and
does not merge duplicates.

**Build trap:** both projects write to `build/vMenu/`. Building the solution copies
`vMenuServer/config/*` over the curated `build/vMenu/config/*` (older files).
Building only the client project can delete other tracked files in `build/vMenu/`.
After any build, run `git status` and `git checkout --` everything except the DLL
you meant to change.

## October 6, 2026: stalled vehicle spawn recovery (client only)

An unbounded vehicle model load or CAD identity response could keep the shared
spawn gate occupied indefinitely. Further clicks displayed "Vehicle spawner is
on cooldown" even though the cooldown interval was not the cause. Model loading
now cancels after 15 seconds without starting a cooldown, while optional CAD
begin/claim calls each fall back after 10 seconds. The shared gate still prevents
overlapping spawns. See [vehicle-spawn-cooldown.md](vehicle-spawn-cooldown.md)
for behavior, diagnostics and the limitations of late external CAD responses.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `25718294c3ee119f199a7741a7c5aed47c43e72c9653b8d695b0d5f6cd719170` |
| `build/vMenu/vMenuServer.net.dll` | `e17419b8231fc020b955753299dbf2d1785ba30051ad17f28c2c8be8aede9c8a` (unchanged) |

Validation: `python tests/vehicle-spawn-cooldown/test_spawn_cooldown.py` passed
all original cases and the added model/CAD stall regressions. Replacing only the
model loader with the pre-fix source fails on the never-loading model, confirming
the regression detects the bug. Client Release build succeeded with zero
warnings/errors using `dotnet build vMenu/vMenuClient.csproj -c Release --nologo
--output <temporary-directory>`; only the resulting client DLL was copied into
the package. Other packaged files were preserved. Source, regression harness,
notes and client DLL are committed together.

**Deploy:** replace only `vMenuClient.net.dll` after verifying the installed
client matches the previous recorded package (`52c7b233efa00552684712f401f2d15fd2b05ec527ab0cd0f8973a761322e61e`).
If it differs, reconcile that build before replacing it. The existing server
source/package mismatch described above remains; do not rebuild the server for
this fix. No server deployment or FiveM runtime testing was performed.

Runtime acceptance: retry a saved vehicle whose streamed model fails to load;
after 15 seconds it should report a loading timeout and allow a different vehicle
immediately. With CAD responses delayed/lost, spawning should finish using the
optional identity fallback, then allow the next spawn after the configured
cooldown. Confirm normal CAD plate/identity behavior with healthy responses and
check F8 for the timeout stage if a reported stall recurs.

## October 20, 2026: garage vehicle spawn placement (client only)

The shared vehicle spawner now traces nearby floor collision at the original
spawn height, removes the unconditional one-metre lift, and finishes placement
before seating the player. GTA's wheel alignment is retained only if it stays
near the detected floor; failed or distant placement is rolled back immediately.
Missing collision has a one-second deadline and preserves the requested altitude
without a global ground search. Normal menu vehicles, spawn-by-name and saved
vehicles share the fix. See [vehicle-spawn-placement.md](vehicle-spawn-placement.md)
for behavior, exclusions and runtime acceptance checks.

This client was built from the latest fetched `georgie/main` (`16fbd80`) plus
the garage fix, retaining the previous permission retry, outfit/hair, cooldown
and model/CAD timeout work.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `586cff071970fafce2287690ee097a1a7044dd991c255ccfd3b24f5eddb33bc4` |
| `build/vMenu/vMenuServer.net.dll` | `e17419b8231fc020b955753299dbf2d1785ba30051ad17f28c2c8be8aede9c8a` (unchanged) |

Validation: production placement helper and actual spawn core pass the simulated
garage/collision regression harness. The pre-fix core at `16fbd80` fails the first
wrong-floor case in the same harness. Existing spawn cooldown/model/CAD timeout
regressions pass. Client Release build succeeded with zero warnings/errors using
a temporary output directory; only the client DLL was copied into the package.
All other packaged files were preserved.

**Deploy:** replace only `build/vMenu/vMenuClient.net.dll` in the installed
resource after verifying its current client matches the previous recorded
package (`25718294c3ee119f199a7741a7c5aed47c43e72c9653b8d695b0d5f6cd719170`).
If it differs, reconcile the installed build first. Restart the resource and
perform the garage/ramp acceptance checks linked above. The existing server
source/package mismatch remains; do not rebuild the server for this fix.
No new configuration is required. No live server was changed and FiveM runtime
testing remains outstanding.

## October 21, 2026: client fixes release A (client DLL, Lua, config)

First of two releases from the October 21 code review. This one needs no server
rebuild. Release B (server rebuild: make `main`'s server source reproduce the
packaged DLL, then remove the player-identifier and ban-list leaks) comes after
this has run fine on live.

**vMenu no longer overwrites other scripts every frame.** For every player it used
to write player invincibility, ragdoll, drag-out/shot-in-vehicle/knock-off-bike,
the vehicle god-mode proofs (bullet/fire/explosion/collision/melee, visual damage,
engine wear, wheels/axles, doors, ramp damage), the helmet setting and, on each
vehicle switch, power multiplier, siren, plane/heli turbulence and boat anchor.
This undid PSRP scripts that set the same things. Now, through
`vMenu/OverrideLatch.cs`:
- A vMenu option that is on is still enforced exactly as before.
- When it is turned off, or the ped/vehicle changes, vMenu restores the default
  once on the entity it changed, and then leaves it alone.
- If a player never uses the option, vMenu never touches the setting.

Two visible differences for option users:
- Vehicle god mode now comes off the previous vehicle when you enter another one.
  Before, that vehicle stayed protected for whoever drove it next.
- A parked vehicle keeps god mode until you turn the option off or enter another
  vehicle.

"Toggle Vehicle Visibility" only re-shows a vehicle vMenu hid. Before, it made
your last vehicle visible every frame, whatever had hidden it.

**Bug fixes:**
- **Saved custom paint:** custom black, pure red and other colours with a 0
  channel are restored. New saves store an explicit `customPrimary` /
  `customSecondary` flag. Saves made before this keep the old rule, so they spawn
  exactly as before. A custom colour with a 0 channel saved before this release
  needs re-saving once.
- **Server messages** (`vMenu:Notify`) show once in ox_lib instead of ox_lib plus
  the GTA feed.
- **Teleport flag:** `vmenu_teleporting` is cleared 3 s after a teleport
  finishes, instead of 3 s after it starts. Long ground searches are no longer
  outside the window psrp_telemetry checks.
- **Death notifications** no longer stop at the first player already reported
  dead.
- **Hats and glasses:** the `keep_player_head_props` logic was inverted. The
  code now matches the config comment (true = props stay on), and the config is
  set to `false` in both copies. Behaviour is unchanged: hats and glasses can
  still be knocked off.
- **Shared vehicle codes** no longer carry the sharer's CAD `cadSaveToken`. It is
  stripped when a code is generated and when one is imported, so each importer
  gets their own token on first read. Imports also repair an empty `mods` list
  the same way `extras` already was.
- **Player blips** are now removed when toggled off for staff without the Online
  Players menu. A check on that menu skipped the removal.

**Removed (unused):**
- The Online Players and Banned Players menus, with their client code: teleport
  to/summon/spectate/kill/kick/ban, private messages, GPS-to-player, "Disable
  Private Messages" and the ban list.
- The packaged server DLL still has the matching handlers. Release B removes
  them; that is what closes the server-side leaks.

**Performance:**
- The forced `GC.Collect()` every 60 s is gone.
- Client permission checks use a table built once in `SetPermissions`. They no
  longer read the staff-only convar and re-check parent permissions on every
  call; denied answers were never cached.
- One shared `ExternalFunctions` bridge replaces a new script object per
  dialog/notification/interaction check.

**Lua and config:**
- `format` is local in each client file (it was a global defined only in
  outfitCodes.lua), and `rgbToHex` is local.
- Error and alert notifications honour the duration.
- The clock-in sync is sent once at start, not twice.
- `lib.versionCheck` against the upstream Gravxd fork is removed.
- The outfit/vehicle code server callbacks refuse requests while their convar is
  off.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `e30cc8d4769933f45d7b4f7df815ba494d01dc7f152535518bbafbc41bebe0cf` |
| `build/vMenu/vMenuServer.net.dll` | `e17419b8231fc020b955753299dbf2d1785ba30051ad17f28c2c8be8aede9c8a` (unchanged) |

**Validation:**
- All nine tests in AGENTS.md pass. Three are new:
  - `tests/override-latch`: latch behaviour, plus a check that the old
    unconditional writes are gone.
  - `tests/permission-table`: the table matches the old `IsAllowed` logic for
    every permission, with staff-only on and off, over fixed and 500 random
    permission sets. Breaking the staff-only rule makes it fail.
  - `tests/saved-vehicle-colours`: the paint rule for new, -1-marked and oldest
    saves.
- The client Release build had 0 warnings and 0 errors. It was built into a
  temporary folder, and only the client DLL was copied into the package.
- No FiveM runtime testing was done.

**Deploy:**
1. Hash the live `vMenuClient.net.dll` and confirm it is the previous package
   (`586cff071970fafce2287690ee097a1a7044dd991c255ccfd3b24f5eddb33bc4`). If it
   differs, stop and reconcile first.
2. Copy:
   - `vMenuClient.net.dll`
   - `client/exports.lua`, `client/integrations.lua`, `client/outfitCodes.lua`,
     `client/vehicleCodes.lua`
   - `config/config_client.lua`
   - `server/oufitCodes.lua`, `server/vehicleCodes.lua`, `server/versionCheck.lua`
   - `config/vehicleIdentityModels.json`. Live never had it, although live already
     runs `server/vehicleIdentity.lua`, which reads it. With it, CAD identity
     tickets for stock vehicles check the vehicle-category permission instead of
     spawn-by-name. The owner chose to add it.
3. `permissions.cfg`: the repo copy is the live file
   (`c5bcaee2a7abb016ac89aea330c9350fd55a89092354400a31411277a657f864`, checked
   October 21) with one change, `setr keep_player_head_props true` to `false`.
   - Either make that one-line change on live, or copy the repo file. Copying it
     is only safe if the live file still has that hash.
   - Deploy it together with `client/exports.lua`: the new Lua with the old
     `true` would stop hats falling off.
4. Restart the resource.

Correction made before deploy: the repo's `permissions.cfg` had come from the
September 27 snapshot and did not match live.
- It gave the torque multiplier to `group.srmanagement` and `group.film`; live
  gives it to `group.communitymanager`.
- It gave the power multiplier to `group.film`, which live doesn't.
- It lacked live's infinite fuel for `group.supreme`.

It now matches live.

**Runtime acceptance:**
- F1 and noclip open.
- God mode, no-ragdoll and vehicle god mode still work when switched on and off.
- Another script's invincibility or no-ragdoll survives while vMenu god mode is
  off.
- A newly saved vehicle with custom black paint spawns black.
- A server notification shows once.
- A staff teleport-to-waypoint isn't flagged by psrp_telemetry.
- Hats can still be knocked off.
- An imported share code gets its own CAD token.
- Online Players and Banned Players are gone.
- `vmenuclient dump` lists the same permissions as before.

## October 21, 2026: server source reproduces the packaged server DLL (source only)

This is release B step 1. The package does not change: `build/vMenu/vMenuServer.net.dll`
is still `e17419b8…`, and there is nothing to deploy.

**What the packaged server is.** Its `AssemblyInformationalVersion` is
`1.0.0+cd866b83…`, so it was built while `HEAD` was `cd866b8`, from a working tree
with uncommitted changes.

**How it was compared.** `main`'s server project was built into a temporary folder
(never into `build/vMenu/`). `tools/ilcompare` then compared it with the packaged
DLL. The tool renders every type, field, attribute and method body, with operands
resolved to names, and compares them method by method. It found exactly two
differences:
- `PermissionsManager`'s server bootstrap. `main` had the per-handle request queue
  with sessions and merged duplicates. The live build has one
  `permissionsBootstrapBusy` flag: it waits for the previous player, then
  re-checks the player still exists with the same license after each chunk of 25
  ACE checks.
- `MainServer.OnPlayerJoining` and `OnPlayerDropped`, which in `main` also called
  `InvalidatePermissionSession`.

**What changed in the source.** `main` now has the live bootstrap, rebuilt from
the IL, and the join/drop handlers without those calls. The queue version is
still in git history (`a2d9aa8` and earlier) if it is ever wanted.

**Result:**
- The rebuilt server matches the packaged DLL in every type, field and method,
  with 0 differences.
- It also matches with compiler-generated names compared raw
  (`ILCOMPARE_RAW=1`), which proves member order is identical too. Type, method
  and field counts are equal.
- The only difference is the git hash in `AssemblyInformationalVersion`.
- A rebuilt client still matches the packaged client; the shared-file edit is
  server-only.
- All nine tests pass.

Reproduce it with:

```
dotnet build vMenuServer/vMenuServer.csproj -c Release --nologo --output <temp dir>
dotnet run --project tools/ilcompare -c Release -- build/vMenu/vMenuServer.net.dll <temp dir>/vMenuServer.net.dll
```

Exit code 0 means they match. Set `ILCOMPARE_RAW=1` for the strict comparison.
The tool was first checked on the client: a fresh client build matched the
packaged client, and the previous client showed exactly the release A changes.

**Next (release B):** remove the identifier, player-list, coordinates, kick, kill,
summon and PM handlers and the whole ban system. Then restrict Kick Passengers to
your own personal vehicle. After that, the comparison should show only those
intended differences. Release B waits until release A has run fine on live.

## October 21, 2026: server security release B (server DLL, client DLL)

Built on the local `next` branch and merged into `main` with releases C-E on
October 21 (see "Releases B-E merged" below). This is the first server DLL built from
this repository's source.

**Leaks closed.** Any client could trigger these, with no menu needed:
- `vMenu:GetPlayerIdentifiers` returned any online player's license, Discord,
  Steam and FiveM IDs.
- `vMenu:RequestBanList` returned every ban record, including IP addresses.

**Removed** (unused: only the owner had Online Players, and vMenu bans were never
used; EasyAdmin handles bans):
- the player list, player coordinates, kick (and `vmenu.log` kick logging), kill,
  summon and private-message handlers
- `BanManager.cs`: ban and unban events, ban list, connect-time ban check, and the
  `vmenuserver ban/unban` console commands
- the upstream author's dev-identifier check in the permission bootstrap
- the client's `vMenu:GoodBye` handler (crashes the game; only the ban code sent it)

The permission bootstrap is otherwise unchanged.

**Kick Passengers works only on your own vehicle.**
- The client reports each personal vehicle it sets
  (`vMenu:SetPersonalVehicle`).
- The server records it only once it sees that player in the driver seat,
  retrying for about 2 s while the seat syncs. It keeps the net ID, entity and
  model, because net IDs are reused.
- `vMenu:GetOutOfCar` now only works for the sender's recorded vehicle. Before,
  anyone with the permission (everyone) could empty any car by net ID.

**Clear Area works for moderators.** The server required
`vMenu.MiscSettings.All`, while the client shows the option to
`vMenu.MiscSettings.ClearArea` (moderator, tmod, master). It now accepts either.

**Unauthorised attempts are logged.** Weather, time, blackout, cloud,
teleport-location and Kick Passengers requests without permission now log a
`[vMenu] [WARNING] … without permission` line. With auto-ban off they used to do
nothing at all.

**`permissions.cfg` is unchanged.** The plan was to delete the now-unused
`vMenu.OnlinePlayers.*` lines. They are kept: no vMenu code checks them any more,
but psrp_telemetry verifies "a vMenu teleport ACE" before trusting a teleport,
and that may be `vMenu.OnlinePlayers.Teleport`.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `27dcac3a54d0063a49b1fb95a985382e512e977f05f9f21fa7bcd23bd18a4630` |
| `build/vMenu/vMenuServer.net.dll` | `003e0932bed3ee2195f052e292b8f5d63e2f30fd8beace5ce46e728daa5c66e9` |

**Validation:**
- `tools/ilcompare` against the previous live server DLL (`e17419b8…`) shows
  exactly the changes above, and nothing else:
  - removed: `BanManager` and the removed handlers with their lambdas, the
    player-list anonymous type and the PM state-bag call sites
  - changed: `MainServer` constructor, `ServerCommandHandler`, `GetOutOfCar`,
    `ClearAreaNearPos`, the weather/time/teleport-location handlers (log instead
    of `BanCheater`), `OnPlayerDropped`, and the bootstrap state machine (dev
    check removed)
  - added: `SetPersonalVehicle`, `LogUnauthorized`, `PersonalVehicleRegistry`
- New test: `dotnet run --project tests/personal-vehicle-registry`.
- All ten tests pass.
- Both projects build with 0 warnings, into a temporary folder.

**Deploy:**
1. Hash the live DLLs. They must be release A's: client
   `e30cc8d4769933f45d7b4f7df815ba494d01dc7f152535518bbafbc41bebe0cf`, server
   `e17419b8231fc020b955753299dbf2d1785ba30051ad17f28c2c8be8aede9c8a`. If either
   differs, stop.
2. Copy `vMenuClient.net.dll` and `vMenuServer.net.dll`. Copy both: the new
   server needs the client's personal-vehicle report.
3. Restart the resource.

**Runtime acceptance:**
- Players join and get their menus (F1), including on a busy server.
- Drive a car, get out, let a friend sit in it, then use Personal Vehicle > Kick
  Passengers: they are removed. Try it on a car you never drove: you get "You can
  only remove passengers from your own personal vehicle".
- A moderator's Clear Area works.
- No new errors in the server console.

## October 21, 2026: build and repo cleanup (no package change)

Nothing to deploy. The DLLs and every file in `build/vMenu/` are unchanged.

- **The build trap is gone.** Both projects now build into their own
  `bin/Release/`, so a plain `dotnet build` can no longer overwrite the package.
  The server project no longer copies config, Lua, `storage.html` or
  `System.Numerics.dll` anywhere.
- **Stale copies deleted.** These were identical to the package copy, or (for
  `locations.json` and `permissions.cfg`) older versions of it:
  `vMenuServer/config/*`, `vMenuServer/server/*.lua`, `vMenuServer/storage.html`
  and `vMenuServer/System.Numerics.dll`. `build/vMenu/` is now the only copy of
  every deployed file.
- **Dead code deleted.** These were comment-only: the flares and plane bombs
  stub, the old weapon save/restore, `IntToFloat`, and the old noclip key handler.

Proof: `tools/ilcompare` with `ILCOMPARE_RAW=1` shows 0 differences for both
DLLs against the release B package (only the git hash differs).

## October 21, 2026: bugs and performance release C (client DLL, Lua)

Built on the local `next` branch after release B, and merged into `main` with
the other releases on October 21 (see "Releases B-E merged" below). Client only: the server DLL is release B's.

**Bugs fixed:**
- **Vehicle Extras "Go Back".** Every open of the Extras menu added another Go
  Back handler, so after a few opens Go Back jumped several menus. It is now
  registered once.
- **Overhead names.** A tag was re-created every 500 ms for every nearby player
  and never removed when that player left. Tags are now kept per server ID,
  created only when the player has no active tag, and removed when the player
  moves away, leaves, or names are turned off. The distance is unchanged.
- **Teleport black screen.** Finding the ground loaded the scene again at every
  height it tried, up to 3 s each (up to about 2 minutes in the worst case). It
  now loads the area once (at most 3 s), then probes heights.
- **Hangs on assets that never load.** Changing ped (Player Appearance, MP
  characters), walking styles, suicide, the key-fob animation, pointing, the
  helmet visor, snowballs and the watch animation all waited forever for a model
  or animation. They now give up after 15 s (models) or 5 s (animations). A ped
  change that fails shows "could not load" and leaves you as you were, instead
  of stripping your weapons first.
- **Weapon loadout ammo.** Restoring a loadout waited forever for each weapon's
  ammo to apply. It now waits at most 1 s per weapon.
- **Spawn tracking for psrp_telemetry.**
  - The ticket was requested from inside `canDoInteraction`, which runs twice
    per spawn and can be called by other scripts, and the new vehicle was found
    by scanning every vehicle in the world for up to 3 s.
  - `SpawnVehicleCore` now asks for the ticket once, right before
    `CreateVehicle`, and passes the new vehicle straight to
    `client/spawnProvenance.lua`.
  - The events psrp_telemetry receives (`vMenu:telemetry:beginVehicleSpawn`,
    `psrp_telemetry:sv:claimVehicleSpawn`, the version 2 ready handshake) are
    unchanged.

**Performance:**
- **Clothing glow.** Every 25 ms the glow loop re-applied every player's static
  glow and rewrote your own decorator. Static glows are now applied only when a
  player's mode changes, and your decorator only when yours changes.
- **Staff blips.** Sprite, colour, name and category were set on every blip
  every frame. They are now set when the blip's sprite changes; heading and
  display only on change. Rotation is still per frame.
- **Vehicle spawner.** About 800 vehicle entries were built at startup. Each
  class is now filled the first time you open it.
- **Small items.** The misc-settings text (coordinates, time, speed) is no
  longer an `async void` started every frame, and the animal-ped check uses a
  `HashSet`.

**Manifest.** `fxmanifest.lua` now names this PSRP build in its description,
version (`2.0.0-psrp`) and author. `fx_version` is unchanged.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `1bc1f3e6916fbd062665c67befd603c7943febfdb98cec98b7206b859ae37d09` |
| `build/vMenu/vMenuServer.net.dll` | `003e0932bed3ee2195f052e292b8f5d63e2f30fd8beace5ce46e728daa5c66e9` (unchanged) |

**Validation:**
- `tools/ilcompare` against release B's client DLL shows only the members
  above: the load helpers, the changed call sites, `OverheadNameTags`,
  `BlipStyle`, the glow fields, the Extras handler and the spawner's deferred
  build, plus renumbered compiler-generated local functions.
- New tests:
  - `dotnet run --project tests/overhead-name-tags`: tag bookkeeping (no
    re-creation, removal on leave, away and off) and the FunctionsController
    wiring.
  - `python tests/client-fixes/test_client_fixes.py`: compiles the real load
    helper against fake natives (timeout, late load, already loaded) and checks
    each fixed call site.
  - `lua tests/spawn-provenance/test_spawn_provenance.lua`: the new script
    claims the ticket for the vehicle it was given, once, and ignores failed
    spawns and late tickets. It fails against the old script.
- `tests/vehicle-spawn-placement` now also checks that the ticket is requested
  once before `CreateVehicle` and the vehicle reported once after it.
- All thirteen tests pass. The client builds with 0 warnings.
- No FiveM runtime testing was done.

**Deploy:**
1. Hash the live `vMenuClient.net.dll`. It must be release B's
   (`27dcac3a54d0063a49b1fb95a985382e512e977f05f9f21fa7bcd23bd18a4630`). If it
   differs, stop.
2. Copy `vMenuClient.net.dll`, `client/integrations.lua`,
   `client/spawnProvenance.lua` and `fxmanifest.lua`. Copy the DLL and both Lua
   files together: the new DLL reports spawns that only the new
   `spawnProvenance.lua` understands, and the old `integrations.lua` would ask
   for a second ticket per spawn.
3. Restart the resource.

**Runtime acceptance:**
- Open Vehicle Options > Vehicle Extras several times, then Go Back: it goes
  back one menu.
- Overhead names show above nearby players and disappear when they walk away or
  leave.
- Teleport to a waypoint far away: the screen is black for a few seconds at
  most, and you land on the ground.
- Change ped, MP character and walking style; restore a weapon loadout.
- Spawn a car from the spawner and by name: psrp_telemetry records it as a vMenu
  spawn, exactly once.
- Open a few vehicle classes in the spawner: each lists its vehicles.
- Staff blips still show the right sprite, colour, name and heading.
- `resmon`: vMenu's client time is no higher than on release B.

## October 21, 2026: player features release D (client DLL, Lua, database, config)

Built on the local `next` branch after release C, and merged into `main` with
the other releases on October 21 (see "Releases B-E merged" below). Client only: the server DLL is release B's.
The backup server code is Lua.

**Back up and restore saves** (Misc Settings, replacing the hidden
experimental "Export/Import Data" stub):
- "Back Up My Saves" copies your saved vehicles and their categories, saved
  peds, MP characters and their categories, weapon loadouts, default character
  and loadout, and vehicle presets to the server. Menu settings and temporary
  keys are not included. It shows how many saves it will send and which backup it
  replaces, and asks first.
- "Restore My Saves" adds every save from your backup that you don't have. A save
  with the same name is never overwritten. It reports how many were added and
  how many were kept. Reopen the menu to see them.
- One backup per Rockstar `license`, in the new `vmenu_backups` table. Upload and
  download use latent events.
- The server only accepts known save keys with text values, at most 2 MB and
  5000 saves, and logs a refused upload with the player's name. Limits: one
  backup a minute and one restore every 30 s per license.
- `client/backups.lua` and `server/backups.lua` do the work. The items only show
  with `setr vmenu_backups true`.

**Vehicle presets** (Vehicle Options > Vehicle Extras):
- "Save As My Preset" stores the vehicle's extras, livery and mod-kit livery for
  that model. "Clear My Preset" removes it.
- The preset is applied whenever you spawn that model from the spawner or by
  name. Saved vehicles keep their own setup.
- LEO spawn codes (`vMenu:ApplyAllVehicleExtras`) use your preset instead of
  "every extra on". The siren still comes on.
- Extras the vehicle doesn't have, and liveries out of range, are skipped.
- The Extras menu now lists extras 0-19, the range saves already store. It
  stopped at 13, so extras 14-19 could not be changed.

**Noclip:**
- A/D strafe in follow-cam mode. They still turn in the other mode.
- The mouse wheel raises and lowers the speed. Shift still cycles it. The wheel
  no longer switches weapons while noclipping.
- The help panel shows the real key for Toggle NoClip. The old hint left out
  the key-mapping bit, so it didn't show your key.
- The help panel is rebuilt only when the speed or camera mode changes, not
  every frame.

**Personal vehicle:**
- New key bindings "vMenu PV Lock/Unlock" and "vMenu PV Engine", unbound by
  default. Set them in Settings > Key Bindings > FiveM. They need the same
  permissions as the menu items, and work while the vehicle is in range.
- New item "Set Waypoint To My Car". Out of range, it marks where you last saw
  the car.

**Teleport To Coords** asks for X, Y and Z in one dialog, filled in with your
current position (new `coordsDialog` export in `client/exports.lua`).

**Vehicle Liveries menu.** It added another livery handler and another Go Back
handler every time it opened. They are now registered once, like the Extras
menu's in release C. Nothing visible changes.

**`permissions.cfg`** (besides the new convar):
- `add_ace group.communitymanager "vMenu.PlayerOptions.God" true` now says
  `allow`. An ACE is `allow` or `deny`, so the line wasn't a valid grant.
  vMenu already gave them God through `PlayerOptions.All`. Anything that checks
  this exact permission (psrp_telemetry, perhaps) now sees it as allowed.
- A repeated `add_ace group.georgiespecial "vMenu.WeaponOptions.Menu" allow` is
  removed. No effect.
- Left as they are, by the owner's choice: `group.weaponperms` (the Mk2
  whitelist) has a member but no grants here, and `group.mod` has none either.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `c84a9ae894dcde1b851ecc7287b0bb2286a0277f57edcf4302940be48abc9c00` |
| `build/vMenu/vMenuServer.net.dll` | `003e0932bed3ee2195f052e292b8f5d63e2f30fd8beace5ce46e728daa5c66e9` (unchanged) |

**Validation:**
- `tools/ilcompare` against release C's client DLL shows only the members above:
  `VehiclePreset`, `ApplyVehiclePreset`, `GetUserCoords`, the noclip help panel
  and handler, the personal-vehicle keys and waypoint, the Extras menu, and the
  removed export stub. Against the first build of this release, the Liveries fix
  changes only `VehicleOptions.CreateMenu` and its closures.
- New tests:
  - `lua tests/save-backups/test_backups.lua`: runs both backup scripts against
    fake FiveM, ox_lib and oxmysql. Covers what goes into a backup, cancel, size
    and count limits, every refusal, the rate limits, convar, missing license,
    database errors, and that restoring never overwrites a save and ignores
    non-save keys. Breaking the merge or the server key check makes it fail.
  - `dotnet run --project tests/vehicle-presets`: capture, apply (missing extras,
    livery range), storage round trip, and where presets are applied.
  - `python tests/client-features/test_client_features.py`: compiles the noclip
    key hint and the coordinates parser (checked under a comma-decimal locale),
    and checks the noclip, key binding, waypoint, teleport, backup and Liveries
    menu wiring.
- `tests/vehicle-spawn-placement` now also checks that presets apply to fresh
  spawns and not to saved vehicles.
- `tests/permission-table` now also checks that every grant in the packaged
  `permissions.cfg` says allow or deny and none is repeated. It fails on the old
  `true` line.
- All sixteen tests pass. The client builds with 0 warnings.
- No FiveM runtime testing was done.

**Deploy:**
1. Hash the live DLLs. The client must be release C's
   (`1bc1f3e6916fbd062665c67befd603c7943febfdb98cec98b7206b859ae37d09`) and the
   server release B's (`003e0932…`). If either differs, stop.
2. Run the `vmenu_backups` statement from `database.sql` once. It only creates
   the table if it is missing.
3. Make three edits to the live `permissions.cfg` (the repo copy has them):
   - Add, below `setr vmenu_vehiclecodes true`:
     ```
     setr vmenu_backups true
     ```
   - Change `add_ace group.communitymanager "vMenu.PlayerOptions.God" true` to
     end in `allow`.
   - Delete the second `add_ace group.georgiespecial "vMenu.WeaponOptions.Menu" allow`.
4. Copy `vMenuClient.net.dll`, `client/backups.lua`, `client/exports.lua` and
   `server/backups.lua`.
5. Restart the server, or run `setr vmenu_backups true` in the console and then
   restart the resource. The console should print "Save backups are enabled."
   It names the problem instead if oxmysql or the table is missing.

**Runtime acceptance:**
- Misc Settings shows Back Up My Saves and Restore My Saves. Back up, delete a
  saved vehicle, restore: it comes back, and your other saves are unchanged.
- Back up twice within a minute: the second one is refused.
- In a police car, set some extras and a livery, Save As My Preset, then spawn
  the same model from the spawner: it has them. A saved vehicle of that model
  keeps its own setup. A LEO spawn code uses the preset.
- Noclip: A/D strafe in follow-cam, the wheel changes the speed shown in the
  help panel, and the panel shows your Toggle NoClip key.
- Bind the PV keys in Settings > Key Bindings > FiveM: they lock/unlock and start
  or stop your personal vehicle. Set Waypoint To My Car marks it on the map.
- Teleport To Coords opens one dialog with your position filled in.
- Open Vehicle Liveries several times and change the livery: it changes once
  per press, and Go Back on a car without liveries goes back one menu.
- A community manager's god mode still works.
- `resmon`: vMenu's client time is no higher than on release C.

## October 21, 2026: fixes and player features release E (client DLL, Lua, config)

Built on the local `next` branch after release D, and merged into `main` with
the other releases on October 21 (see "Releases B-E merged" below). Client only: the server DLL is release B's.

**Fixes:**
- **Ped pack paywall.** Spawn By Name (everyone has it) accepted any model, so
  typing a `pedpack.json` model such as `pw_ana` gave the paid ped without
  `vMenu.PlayerAppearance.PedPack`. A saved ped of a pack model also kept working
  after the pack expired. Only the Ped Pack menu checked the permission.
  `SetPlayerSkin`, which every ped change goes through, now refuses a pack model
  without the permission and shows the store prompt.
- **Spawn tickets.** Any client could call `vMenu:telemetry:beginVehicleSpawn`
  as often as it liked, and each call got a 12 s wildcard ticket from
  psrp_telemetry. A cheater could keep a stream of tickets so injected vehicles
  looked like vMenu spawns. `server/spawnProvenance.lua` now issues at most one
  ticket per player per half `vmenu_vehicle_spawner_cooldown` (500 ms with
  the live 1000), never closer than 250 ms. Normal spawning is never that fast.
  The status export gains a `throttled` count.
- **Share codes.**
  - The outfit and vehicle code servers stored any table of any size. They now
    store only real outfits (clothes and props, whole numbers in range) and real
    saved vehicles (plain values and flat tables, at most 16 KB).
  - The server also drops the sharer's CAD token and category itself.
  - Cooldowns are per Rockstar license, so reconnecting no longer resets them.
  - Importing a damaged old code shows "damaged" instead of a Lua error. An
    outfit keeps its good entries.
  - Old codes keep working.
- **Unlimited ammo** (owner, master, georgiespecial) was written every frame,
  which switched off any other script's infinite ammo. It now uses release A's
  latch: kept on the weapon in hand while on, and put back once when it goes off
  or you change weapon.
- **Expanded radar.** vMenu closed an expanded radar that another script opened
  on the next frame; its 8 s timer only counted vMenu's own opens. It now only
  closes one it opened.
- **Death notifications** (owner, master, admin) checked every player every
  frame. They now check 4 times a second.
- **Tidy-ups:**
  - The player options tick no longer reads a convar every frame before
    checking the option.
  - Removed from `permissions.cfg` because nothing reads them:
    `set vmenu_disable_ai` (NPCs are controlled by `Config.DisableAI` in
    `config_server.lua`), and the auto-ban, ban-log, kick-log and ban-message
    convars (release B removed their code).

**New for players:**
- **Favourite and Recent Vehicles** (Vehicle Spawner):
  - Recent Vehicles lists the last 10 vehicles you spawned from the spawner, by
    name or from your favourites. Saved vehicles aren't listed.
  - Favourite Vehicles holds up to 50: add the vehicle you're in, or favourite
    one from Recent.
  - Each entry offers Spawn and Remove or (Un)favourite. Spawning goes through
    the normal spawn checks, so restricted classes stay restricted.
  - Both lists are included in save backups.
- **Auto Back Up My Saves** (Misc Settings, a checkbox):
  - Every 10 minutes it uploads your saves if they changed since your last
    successful backup, with no dialog. Success is a short note; only the first
    failure of a session is shown. Turning it on backs up straight away.
  - With it off, a player who has saves but never backed up from this PC (or
    not for 30 days, with changes since) gets a reminder at most once a week.
- **Clothing Quick Toggles** (Player Appearance):
  - Take off and put back your hat, glasses, ear accessory, mask or bag.
    Putting back restores exactly what you took off, and a new ped starts fresh.
  - The mask and bag only work on MP characters, because those slots mean
    something else on other peds.
  - Key bindings "vMenu Toggle Hat", "Glasses", "Ear Accessory", "Mask" and
    "Bag" are unbound by default. Set them in Settings > Key Bindings > FiveM.
  - Gloves are left out. GTA draws gloves as part of the arms piece, and the
    bare-arms piece depends on the top you wear, so it needs a lookup table for
    every top.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `8b05f1f47c1cc660aca62f337d6fe0e6d3ac0b56a8d196e75fdc2ca35164d7c8` |
| `build/vMenu/vMenuServer.net.dll` | `003e0932bed3ee2195f052e292b8f5d63e2f30fd8beace5ce46e728daa5c66e9` (unchanged) |

**Validation:**
- `tools/ilcompare` against release D's client DLL shows only the members
  above: `ClothingToggles`, `VehicleShortlist`, the ped pack check, the spawner,
  Misc Settings and Player Appearance menus, `SetPlayerSkin`, the spawn core and
  the changed ticks. Everything else is renumbered compiler-generated local
  functions.
- New tests:
  - `python tests/client-guards/test_client_guards.py`: compiles the real ped pack
    check and the real unlimited-ammo block (with `OverrideLatch`) against fakes,
    and checks the radar, death notification, convar order and config fixes.
  - `lua tests/share-codes/test_share_codes.lua`: both share-code servers refuse
    every malformed outfit and vehicle, drop the CAD token, and limit per
    license. The clients report damaged codes and keep an outfit's good
    entries.
  - `dotnet run --project tests/vehicle-shortlists`: recent order and limit,
    favourites, damaged storage, and the spawner, spawn core and backup wiring.
  - `dotnet run --project tests/clothing-toggles`: take off and put back per
    slot, the newest item, a new ped, and the menu and key binding wiring.
- Extended tests:
  - `tests/spawn-provenance` now also runs the server script: one ticket per
    interval per player, the 250 ms floor, and the permission check.
  - `tests/save-backups` now covers auto backup (changed saves only, quiet, one
    at a time, letter-case changes, failures shown once) and the reminder (weekly,
    not with auto backup on, after a recent backup, or without saves). It also
    covers favourites and recents in backups.
  - `tests/vehicle-spawn-placement` checks that only fresh spawns go into Recent
    Vehicles.
- The new tests fail against release D's scripts.
- All twenty tests pass. The client builds with 0 warnings.
- No FiveM runtime testing was done.

**Deploy:**
1. Hash the live DLLs. The client must be release D's
   (`c84a9ae894dcde1b851ecc7287b0bb2286a0277f57edcf4302940be48abc9c00`) and the
   server release B's (`003e0932…`). If either differs, stop.
2. Copy `vMenuClient.net.dll`, `client/backups.lua`, `client/outfitCodes.lua`,
   `client/vehicleCodes.lua`, `server/backups.lua`, `server/oufitCodes.lua`,
   `server/vehicleCodes.lua` and `server/spawnProvenance.lua`.
3. Optional: delete the dead convar lines listed above from the live
   `permissions.cfg`. They do nothing, so leaving them is harmless.
4. Restart the resource.

**Runtime acceptance:**
- Without the ped pack, Spawn By Name `pw_ana`: the store prompt opens and your
  ped doesn't change. With the pack it works, and saved pack peds spawn.
- Spawn several vehicles in a row: psrp_telemetry records each as a vMenu
  spawn, and `GetVehicleSpawnProvenanceStatus().throttled` stays 0.
- Generate and load an outfit code and a vehicle code. Codes made before this
  release still load.
- With unlimited ammo off, another script's infinite ammo keeps working. On, it
  still works as before.
- Another script's expanded radar stays open. vMenu's radar key still opens it,
  and it closes after 8 s.
- Spawn a few cars: they appear in Recent Vehicles. Favourite one and spawn it
  from Favourite Vehicles. A restricted class still refuses.
- Turn on Auto Back Up My Saves: "backed up automatically". Change a save and
  within 10 minutes it is backed up again.
- Clothing Quick Toggles take off and put back each item. Bind a key and use
  it.

## October 21, 2026: releases B-E merged, and how to deploy them together

At the owner's request, releases B, cleanup, C, D and E were fast-forwarded onto
`main` together, without waiting for each to be confirmed on live. Release A had
not been checked on live either. Release F followed the same day, at the owner's
request too, and the steps below include it. The table at the top has the
current package's hashes.

| Tag | Commit | Release |
| --- | --- | --- |
| `build-2026-10-21` | `a2d9aa8` | A: client fixes |
| `build-2026-10-21-b` | `29aa7b8` | B: server security (first server DLL built from source) |
| `build-2026-10-21-c` | `76cc1b1` | C: bugs and performance |
| `build-2026-10-21-d` | `4b2f49c` | D: backups, presets, noclip, PV keys, teleport |
| `build-2026-10-21-e` | `103e449` | E: fixes, favourites, auto backup, clothing toggles |
| `build-2026-10-21-f` | this commit | F: saved-car prompt, outfits, ped favourites and preview, auto-lock, car keys, photo mode, what's new |

The cleanup commit (`d9459ba`) changed no package file, so it has no tag.

**Deploy everything at once.** Each release section above says which hashes the
live copy must have before *that* release. When deploying A-F together, use
these steps instead:

1. Hash the live `vMenuClient.net.dll` and `vMenuServer.net.dll`. The server must
   be `e17419b8231fc020b955753299dbf2d1785ba30051ad17f28c2c8be8aede9c8a`. The
   client must be either:
   - before release A: `586cff071970fafce2287690ee097a1a7044dd991c255ccfd3b24f5eddb33bc4`
   - release A: `e30cc8d4769933f45d7b4f7df815ba494d01dc7f152535518bbafbc41bebe0cf`

   If either DLL differs, stop: the server runs a build this repo doesn't record.
2. Back up the live `vMenu` folder.
3. Run the `vmenu_backups` statement from `database.sql` once (it only creates the
   table if it is missing).
4. Edit the live `config/permissions.cfg`. Don't copy the repo file over it:
   staff can change the live one.
   - `setr keep_player_head_props true` becomes `false` (release A; already done
     if A is live).
   - Below `setr vmenu_vehiclecodes true`, add `setr vmenu_backups true` (release D).
   - `add_ace group.communitymanager "vMenu.PlayerOptions.God" true` ends in
     `allow` (release D).
   - Delete the second `add_ace group.georgiespecial "vMenu.WeaponOptions.Menu" allow`
     (release D).
   - Optional: below `setr vmenu_backups true`, add `setr vmenu_photo_mode true`
     to turn on photo mode (release F).
   - Optional: delete `set vmenu_disable_ai`, `vmenu_auto_ban_cheaters`,
     `vmenu_auto_ban_cheaters_ban_message`, `vmenu_log_ban_actions`,
     `vmenu_log_kick_actions` and `vmenu_default_ban_message_information`
     (release E; nothing reads them).
5. Copy these files from `build/vMenu/`. They are every file A-F changed, plus
   `config/vehicleIdentityModels.json`, which release A added to live:
   - `vMenuClient.net.dll`, `vMenuServer.net.dll`, `fxmanifest.lua`
   - `client/`: `backups.lua`, `exports.lua`, `integrations.lua`,
     `outfitCodes.lua`, `photoMode.lua`, `spawnProvenance.lua`, `vehicleCodes.lua`,
     `whatsNew.lua`
   - `server/`: `backups.lua`, `oufitCodes.lua`, `spawnProvenance.lua`,
     `vehicleCodes.lua`, `versionCheck.lua`
   - `config/`: `config_client.lua`, `vehicleIdentityModels.json`
   - `database.sql` (not loaded by the resource; only the record of the tables)
6. Restart the server, so the new convar is read. The console should print
   "Save backups are enabled."
7. Work through the runtime acceptance list of each release, A to F. If something
   breaks, put the backup from step 2 back.

## October 21, 2026: player features release F (client DLL, Lua, config)

Built on the local `next` branch after release E, and merged into `main` the same
day at the owner's request. Client only: the server DLL is release B's.

**New for players:**
- **"Save your changes?" for saved vehicles.**
  - When you spawn a saved vehicle (or save one), vMenu remembers which save it
    is. It records how the vehicle looks when you open Vehicle Options.
  - When you close the menu, if you changed it, vMenu asks whether to save the
    changes to that save.
  - It stays quiet if nothing changed, if you're in another vehicle or on foot,
    if the save was renamed or deleted, or if you already saved.
  - Saving uses the same code as Replace Vehicle (moved into
    `CaptureVehicleInfo`), so the CAD token and category are kept.
- **Saved Outfits** (MP Ped Customization):
  - Save what you're wearing under a name, then Wear, Update or Delete it.
  - Only clothing slots change; face, hair and tattoos stay. Wearing one also
    updates the character you're editing, so saving the character keeps it.
  - Outfits are kept per body type, not per character: your male characters
    share theirs, and so do your female ones. Stored as `vmenu_outfit_<name>`
    and included in backups.
- **Favourite and Recent Peds** (Spawn Peds), like the vehicle ones from
  release E: up to 50 favourites and your last 10 peds. MP characters aren't
  listed.
  - Becoming one goes through `SetPlayerSkin`, so the ped pack check still
    applies. Animals still need animals to be allowed, and water animals still
    need water.
  - Included in backups.
- **Ped pack: browse and preview.**
  - Players without the pack can now open the Ped Pack list. Before, they went
    straight to the store prompt and bought blind.
  - Each ped has Preview, and Get It In The Store instead of Spawn.
  - Preview puts the ped in front of you for 20 seconds as a local,
    non-networked ped: nobody else sees it, and you get nothing to keep. It
    goes when you leave the ped's menu.
- **Auto-Lock When I Walk Away** (Personal Vehicle, needs the lock permission):
  your car locks once you walk more than 15 m from it, if nobody is inside and
  it isn't already locked. It re-arms when you come back within 10 m.
- **Key bindings for the vehicle you're in**, unbound by default:
  - "vMenu Vehicle Engine On/Off", "Hood", "Trunk", "Windows Up/Down" and "Next
    Vehicle Seat".
  - Each needs the same permission as its menu item.
  - Engine, doors and windows need you to be the driver, like the menu.
- **Photo Mode** (Misc Settings, plus an unbound "vMenu Photo Mode" key):
  - A free camera with the HUD hidden: mouse to look, WASD to move, Space/Ctrl
    for up/down, scroll to zoom, Shift for faster, H to hide the help,
    Backspace to leave.
  - The camera stays within 15 m of your ped's head, and only moves where the
    head has a clear line of sight, so it can't look through walls or into
    closed rooms.
  - Your ped can't move meanwhile, and it won't start in a moving vehicle.
  - Needs `setr vmenu_photo_mode true`.
- **What's New In vMenu:** a short list of recent features, shown once the first
  time a player opens the menu after an update. It is also in Misc Settings.
  - It only mentions backups and photo mode when the server has them on.
  - Edit the list in `client/whatsNew.lua`, and change `VERSION` so everyone
    sees it again.

**Fix: locking and unlocking your personal vehicle.**
- The double-tap-horn shortcut and release D's "vMenu PV Lock/Unlock" key
  decided between lock and unlock with `GetVehicleDoorsLockedForPlayer`, but
  passed your ped handle where it takes a player ID. So they most likely always
  saw "unlocked" and could only lock.
- Both now read the car's own lock status (`IsVehicleLocked`). Auto-lock uses
  it too.

**Other changes:**
- `VehicleShortlist` is now `ModelShortlist` (it serves peds too), and its test
  moved from `tests/vehicle-shortlists` to `tests/model-shortlists`. The storage
  keys didn't change.
- `permissions.cfg` gains `setr vmenu_photo_mode true`.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `38bac5d98528d7a5663fd7adee888af6ef9fb62b9cb1d7fc61ada6d48f1e635d` |
| `build/vMenu/vMenuServer.net.dll` | `003e0932bed3ee2195f052e292b8f5d63e2f30fd8beace5ce46e728daa5c66e9` (unchanged) |

**Validation:**
- `tools/ilcompare` against release E's client DLL shows only the members
  above: `SavedVehicleTracker`, `PersonalVehicleAutoLock`, `SavedOutfit`,
  `ModelShortlist` (the renamed class), `CaptureVehicleInfo`,
  `IsVehicleLocked`, `OfferToUpdateSavedVehicle`, the auto-lock tick, the
  vehicle key bindings, and the ped menus and preview. Everything else is
  renumbered compiler-generated local functions.
- New tests:
  - `dotnet run --project tests/vehicle-conveniences`: when the update offer
    appears, auto-lock (once per walk away, never with someone inside), and the
    wiring of both, the lock-state fix and the key binding permissions.
  - `dotnet run --project tests/saved-outfits`: clothing slots only, wearing,
    damaged storage, the menu (MP characters only, current body type, never
    overwrites, delete asks) and backups.
  - `lua tests/photo-mode/test_photo_mode.lua`: the 15 m limit, walls, zoom,
    help, leaving, the convar, the moving-vehicle refusal and the key binding.
  - `lua tests/whats-new/test_whats_new.lua`: once per version, on demand, and
    only switched-on features listed.
- Extended tests:
  - `tests/model-shortlists`: the ped menus, animal rules and recording only
    real model changes.
  - `tests/client-guards`: ped pack browse and preview (local ped, spawn still
    locked).
  - `tests/save-backups`: outfits and ped lists backed up; auto-lock and
    what's-new keys not.
  - `tests/vehicle-spawn-placement`: saved vehicles are tracked for the update
    offer; fresh spawns aren't.
  - `tests/client-features`: release D's lock-key check now expects the
    lock-state fix.
- All twenty-four tests pass. The client builds with 0 warnings.
- No FiveM runtime testing was done.

**Deploy** (on top of release E, or with the A-E steps in "releases B-E merged"):
1. Hash the live client DLL. It must be release E's
   (`8b05f1f47c1cc660aca62f337d6fe0e6d3ac0b56a8d196e75fdc2ca35164d7c8`), unless
   you're deploying A-F together. The server must be release B's
   (`003e0932…`), or `e17419b8…` when deploying with B.
2. Add `setr vmenu_photo_mode true` to the live `permissions.cfg` if you want
   photo mode (below `setr vmenu_backups true`). Without it, photo mode stays
   hidden.
3. Copy `vMenuClient.net.dll`, `client/backups.lua`, `server/backups.lua`, and the
   new `client/photoMode.lua` and `client/whatsNew.lua`.
4. Restart the server (or the resource, after running `setr vmenu_photo_mode true`
   in the console).

**Runtime acceptance:**
- Spawn a saved car, change its colour in Vehicle Options, and close the menu:
  vMenu asks to save the changes. Yes updates the save; No leaves it. Close the
  menu without changes: no question.
- Save an outfit on an MP character, change clothes, then Wear it: the clothes
  come back and your face and hair don't change. Update and Delete work.
- Become a few peds: they appear in Recent Peds. Favourite one and become it
  from Favourite Peds.
- Without the ped pack: open Ped Pack, preview a ped (only you see it, and it
  goes after 20 s), and Get It In The Store opens the store. With the pack,
  Spawn Ped works.
- Turn on Auto-Lock When I Walk Away, leave your car and walk away: it chirps
  and locks. With a friend inside, it doesn't.
- Double-tap the horn near your car, and use the PV Lock/Unlock key: each
  toggles between locked and unlocked.
- Bind the vehicle keys and try each one as the driver.
- Photo Mode: move around, try to pass through a wall (you can't), go far (it
  stops at 15 m), zoom, hide the help, leave with Backspace.
- The first menu open shows What's New once; Misc Settings shows it again.
