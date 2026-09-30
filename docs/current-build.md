# Current build and branch handoff

September 27, 2026.

`main` records the combined local vMenu source and the `build/vMenu` package
that was in the primary checkout when this branch was created. This includes
the earlier unpublished PSRP work and the local ONX face selector integration.
Use `main` as the base for future changes to this build.

The packaged DLLs were committed as found, without rebuilding them during
the branch change:

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `cacfbdbec147b5c70269dda5a9c121c643a7416f2a85a8a4f0af61f9428ba600` |
| `build/vMenu/vMenuServer.net.dll` | `e17419b8231fc020b955753299dbf2d1785ba30051ad17f28c2c8be8aede9c8a` |

The fork's older `dev` and `master` branches remain available. `master` has a
separate RVF vehicle repair event change at `52b59cb`; that source change was
not folded into this snapshot, so the packaged build stayed as it was. The
isolated `codex/onx-face-selector` branch contains a different DLL built from
an earlier baseline; use the combined build on `main` for this project.

This Git snapshot identifies the local package. It does not establish which
copy is installed on a server. For future edits, make changes from `main`,
build the intended source, and compare the resulting package before deployment.

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
