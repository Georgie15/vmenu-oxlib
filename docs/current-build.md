# Current build and branch handoff

## Current package

`main` is the only live branch, and its `build/vMenu/` folder is the latest
PSRP vMenu package. Start every change from an up-to-date `main`. Agent and
build rules are in [AGENTS.md](../AGENTS.md).

| | |
| --- | --- |
| Latest change | October 21, 2026: client fixes release A (tag `build-2026-10-21`) |
| `build/vMenu/vMenuClient.net.dll` | `e30cc8d4769933f45d7b4f7df815ba494d01dc7f152535518bbafbc41bebe0cf` |
| `build/vMenu/vMenuServer.net.dll` | `e17419b8231fc020b955753299dbf2d1785ba30051ad17f28c2c8be8aede9c8a` (`main`'s server source reproduces it method-for-method; see October 21, server source) |

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
3. In the live `permissions.cfg`, change only the line
   `setr keep_player_head_props true` to `false`. Deploy it together with
   `client/exports.lua`: the new Lua with the old `true` would stop hats falling
   off.
4. Restart the resource.

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
