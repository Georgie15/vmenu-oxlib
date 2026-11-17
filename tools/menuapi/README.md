# MenuAPI patches

## MenuAPI 4.1.3 (release I)

- `checkbox-left-toggle-4.1.3.patch`: left toggles a checkbox again, as in
  MenuAPI 3.x. Upstream commit `095af43` (January 2021) moved left and right
  handling into the item classes and only kept right for checkboxes, so in
  4.x left on a checkbox goes back a menu, and holding it can close vMenu.

MenuAPI is LGPL-3.0-or-later. This patch is a change to MenuAPI, so it is under
the same license; `build/vMenu/menuapi/MenuAPI-NOTICE.txt` says so to players.

## MenuAPI 3.2.2 (up to release H)

These two patches are for MenuAPI 3.2.2, which PSRP vMenu used up to release H
(`build-2026-11-17-3` and the `perf/closed-menu` test build):

- `skip-disabled-legacy-toggle.patch`: the V-key toggle guard.
- `idle-while-closed.patch`: menu ticks sleep while every menu is closed.

From release I, PSRP vMenu uses MenuAPI 4.1.3. 4.x opens the menu with a key
binding instead of polling controls, and stops its own ticks while every menu
is closed, so neither of these is needed. See the MenuAPI 4.1.3 section
of [docs/current-build.md](../../docs/current-build.md) for how it is built.
