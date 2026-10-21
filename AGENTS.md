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

- Build only the client, into a temporary folder, then copy just the DLL in:
  `dotnet build vMenu/vMenuClient.csproj -c Release --nologo --output <temp dir>`,
  then copy `<temp dir>/vMenuClient.net.dll` to `build/vMenu/`.
- Do not build the solution or the server project into `build/vMenu/`. Both
  projects output there. A server build copies the older `vMenuServer/config/*`
  over the curated `build/vMenu/config/*`, and the committed server DLL is not
  built from `main`'s server source (see the September 30 section of
  current-build.md). Rebuilding the server needs the owner's decision first.
- After any build, run `git status` and restore every file you did not mean to
  change.
- Lua and config in `build/vMenu/` are the deployed copies. Edit them there. If
  the same file also exists under `vMenuServer/`, update both.

## Tests

```
python tests/vehicle-spawn-placement/test_spawn_placement.py
python tests/vehicle-spawn-cooldown/test_spawn_cooldown.py
dotnet run --project tests/permission-bootstrap-retry
dotnet run --project tests/face-selection/FaceSelection.Tests.csproj -c Release
python tests/face-selection/test_editor_errors.py
python integrations/psrp-cad-vehicle-identity/test_saved_tokens.py
```

They need Python and the .NET 9 SDK. None of them run FiveM, so runtime checks
remain manual.

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
