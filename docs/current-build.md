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
