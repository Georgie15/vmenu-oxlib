# vMenu-ox (PSRP build)

Instructions for any AI agent or developer working in this repository.

## Which build is current

- `main` is the only live branch. Its source and the package in `build/vMenu/`
  are the latest PSRP vMenu build. Start every change from an up-to-date `main`:
  `git fetch origin && git switch main && git pull --ff-only`.
- `origin` (`Georgie15/vmenu-oxlib`) is the only remote, and `main` is its only
  branch. The fork this came from, `Gravxd/vmenu-oxlib`, is not this build. Don't
  add it as a remote, base work on it or merge from it unless the owner asks.
- The "Current package" table at the top of
  [docs/current-build.md](docs/current-build.md) records the SHA-256 of both
  packaged DLLs. Before starting, check them with
  `sha256sum build/vMenu/vMenuClient.net.dll build/vMenu/vMenuServer.net.dll`.
  If they don't match, stop and report it.

## Building

- Builds go to each project's `bin/Release/` and never touch the package:
  `dotnet build vMenu/vMenuClient.csproj -c Release --nologo`, or
  `vMenuServer/vMenuServer.csproj` for the server. Copy only the DLL you changed
  into `build/vMenu/`.
- The committed server DLL is built from this source (release B). Before
  changing the server, compare a build with
  `dotnet run --project tools/ilcompare -c Release -- build/vMenu/vMenuServer.net.dll vMenuServer/bin/Release/vMenuServer.net.dll`.
  Afterwards the report must show only your intended changes. The same works for
  the client DLL. Shipping a new server DLL needs the owner's OK.
- After any build, run `git status` and restore every file you did not mean to
  change.
- Lua, config, `storage.html` and every other deployed file exist only in
  `build/vMenu/`. Edit them there.

## Tests

```
python tests/vehicle-spawn-placement/test_spawn_placement.py
python tests/vehicle-spawn-cooldown/test_spawn_cooldown.py
dotnet run --project tests/permission-bootstrap-retry
dotnet run --project tests/face-selection/FaceSelection.Tests.csproj -c Release
python tests/face-selection/test_editor_errors.py
python integrations/psrp-cad-vehicle-identity/test_saved_tokens.py
dotnet run --project tests/override-latch
python tests/permission-table/test_permission_table.py
python tests/saved-vehicle-colours/test_custom_colours.py
dotnet run --project tests/personal-vehicle-registry
dotnet run --project tests/overhead-name-tags
python tests/client-fixes/test_client_fixes.py
lua tests/spawn-provenance/test_spawn_provenance.lua
dotnet run --project tests/vehicle-presets
lua tests/save-backups/test_backups.lua
python tests/client-features/test_client_features.py
dotnet run --project tests/model-shortlists
dotnet run --project tests/clothing-toggles
lua tests/share-codes/test_share_codes.lua
python tests/client-guards/test_client_guards.py
dotnet run --project tests/vehicle-conveniences
dotnet run --project tests/saved-outfits
lua tests/photo-mode/test_photo_mode.lua
lua tests/whats-new/test_whats_new.lua
```

They need Python, the .NET 9 SDK and Lua 5.4. None of them run FiveM, so
runtime checks remain manual.

## Shipping a change

- Commit the source, the rebuilt DLL, tests and docs together on `main`, then
  push to `origin main`. Never force-push `main`. Use a short-lived branch only
  if the owner asks for a PR, and merge it back into `main`.
- In docs/current-build.md, add a dated section (what changed, new hashes,
  deploy steps) and update the "Current package" table.
- Tag each new package commit `build-YYYY-MM-DD` and push the tag.
- Deploy only the files that changed. First hash the installed copy and
  confirm it matches the previous package recorded in current-build.md. If it
  doesn't, stop: the server runs a build this repo doesn't record.
