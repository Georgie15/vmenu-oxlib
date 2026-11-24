# Current build and branch handoff

## Current package

`main` is the only live branch, and its `build/vMenu/` folder is the latest
PSRP vMenu package. Start every change from an up-to-date `main`. Agent and
build rules are in [AGENTS.md](../AGENTS.md).

| | |
| --- | --- |
| Latest change | November 24, 2026 — release O, second build: the dressing room guide opens the creator on the character's body and says in F8 why it can't start (client DLL), on top of release O's first build and releases N, M, L, K, J, I and H. **Testing on branch `feature/release-o-dressing-room`; `main` keeps `build-2026-11-17-3`** |
| `build/vMenu/vMenuClient.net.dll` | `0572ace734e6b5f61adeb3bfc1cb69486411fe56d22d489eb32361d00868d9e9` |
| `build/vMenu/vMenuServer.net.dll` | `fd9433560d3e28fbf31c820eb8787bc6bc44179b5bf445c6dac682e7b8769a90` (built from this source) |
| `build/vMenu/MenuAPI.dll` | `43442a16c92e22bbc37e9fe4db2470de2272bbd92ed760cc75b741888d05b722` |

Check a checkout with
`sha256sum build/vMenu/vMenuClient.net.dll build/vMenu/vMenuServer.net.dll build/vMenu/MenuAPI.dll`.
When the package changes, update this table, add a dated section below and tag
the commit `build-YYYY-MM-DD`.

This repository identifies the package. It does not establish which copy is
installed on a server; hash the installed files before deploying.

## November 24, 2026 — release O, second build: the creator on the character's body (client DLL) — TESTING, branch `feature/release-o-dressing-room`

**Status: test build, not verified in game, not on `main`.** Client DLL
`0572ace734e6b5f61adeb3bfc1cb69486411fe56d22d489eb32361d00868d9e9`
(first build: `5464a38a…`). `storage.html` and `config/player-guide.json` are
the first build's.

- **The first try in game** (testing mode, November 23) fell back to
  psrp_dressing_room's text prompt: vMenu answered Unavailable. Release O's DLL
  was loaded (F8 listed `DressingRoomGuide`), and the F8 log gave no reason.
  The likely one is a `config/player-guide.json` on the server without the
  `"dressingRoom"` section (release N's copy), since an invalid file would have
  logged a `[Player Guide]` line. vMenu now prints the reason,
  `[vMenu] [Dressing Room] not starting: …`, and psrp_dressing_room prints
  which answer it got.
- **The creator on the character's body.** The owner wants the menu to open on
  the creator for the gender picked in the spawn selector. `Start` now takes
  `(destination, body)`; psrp_dressing_room sends `male`/`female` from the
  selector's `gender` state bag. With a body, the guide does what Create
  Male / Female Character does (`MpPedCustomization.StartNewCharacter`, now
  shared by both rows) and opens the creator; the pick-a-body step ticks as it
  opens, so the card starts on Face & Skin. Exit Without Saving still goes back
  to picking a body. With no body it opens MP Ped Customization as before.
- psrp_dressing_room's side (PSRP-Development, November 24): the body, the F8
  reasons, and a fade into the store instead of the cloud (the selector's
  client and testing mode's "now").

Reviewed with `tools/ilcompare` against the first build's DLL: `DressingRoomGuide`
(`Start(String, String)`, `Unavailable()` replacing `Available()`, `Frame`,
`OpenCreator`), `DressingRoomFlow` (`BodyFrom`, `StartsInCreator`) and
`MpPedCustomization` (`StartNewCharacter`, the two Create rows calling it). The
other `MpPedCustomization` entries are compiler-numbered local functions
renumbered by the new method.

Tests: `tests/player-guide` adds the body (`BodyFrom`, `StartsInCreator`,
opening straight on the creator, Exit Without Saving after it) and the wiring
(both Create rows and the guide share `StartNewCharacter`, the F8 reason, no
page tracking while the model loads). `tests/telemetry-leases` now expects 3
`SetPlayerModel` sites, not 4 (the two Create rows share one), and still
checks the vitals after each. All 47 tests in AGENTS.md pass.

Deploy: on a server with release O's first build, copy `vMenuClient.net.dll`
from `deploy/release-o-test-build/`, **check that `config/player-guide.json`
contains `"dressingRoom"`** (or copy it from there too), then `restart vMenu`.
With psrp_dressing_room's `client/main.lua` and character-spawn-selector's
`client/main.lua` from PSRP-Development.

Not checked in game: the creator opening on the body, the cursor on Face &
Skin after it opens, and everything in the first build's list below.

## November 23, 2026 — release O: the dressing room guide (client DLL, `storage.html`, `config/player-guide.json`) — TESTING, branch `feature/release-o-dressing-room`

**Status: test build, not verified in game, not on `main`.** Built on release N
(`feature/release-n-player-guide`). Only the client DLL and two client-side
files change; the server DLL and MenuAPI are release N's.

PSRP-Development's new `psrp_dressing_room` resource sends a brand-new player
(and anyone in its testing mode, `dressingroom test <id>` in the server
console) to a clothing store in a private lobby before their first spawn. The
owner tried it with only a text prompt and wanted vMenu itself to open and walk
the player through making a character. This is that half.

- **Start.** After the welcome, psrp_dressing_room fires the local event
  `vMenu:DressingRoom:Start` (where the player goes next). vMenu answers
  `vMenu:DressingRoom:Started`, or `vMenu:DressingRoom:Unavailable` when the
  player has no MP Ped Customization or the guide file has no dressing room;
  the resource then falls back to its own prompt. An older vMenu answers
  nothing, which also means fall back.
- **The steps** (`config/player-guide.json`, `dressingRoom`): pick a body
  (done when the creator opens), Face & Skin, Character Appearance, Character
  Clothes (each done by visiting the page and coming back), Save Character.
  `DressingRoomFlow` (no game calls, tested) decides from the open page; it
  ticks steps done out of order, shows a page's own help while it is open, and
  starts over after Exit Without Saving. `DressingRoomGuide` opens MP Ped
  Customization, reads the open menu every frame while on (a `TickSleeper`
  otherwise), moves the cursor to the next step's row and draws the card.
- **The menu stays the player's.** Unlike the Player Guide's tour, the card
  takes no keys and never turns the menu's buttons off during the steps. If
  the player closes the menu, the card hides; opening the menu again goes
  straight back to the step's page.
- **Finish.** A successful Save Character calls
  `DressingRoomGuide.CharacterSaved()` (`MpPedCustomization`). The finish card
  has keys, the Player Guide's way (menu buttons off until the key is let go,
  `GuideButtonHold`, restored to their value from before the dressing room):
  Enter fires `vMenu:DressingRoom:Leave` and closes the menu; Backspace, Esc
  or closing the menu fires `vMenu:DressingRoom:Ended` ("stay"). No arrow keys,
  so one slip can't send anyone off.
- **The next join.** A new character used to be worn only until the player
  left: vMenu restores the last character spawned from Saved Characters, not
  one just made. `RememberCurrentCharacter()` sets `vmenu_last_character` and
  the snapshot after a dressing room save, as spawning it would. Other saves
  are unchanged.
- **The rest of vMenu.** `PlayerGuide.MenuOpened()` returns true while the
  dressing room runs, so the Player Guide's welcome stays off and What's New
  waits. Player Guide > Start the guide says to finish the dressing room first.
  `vMenu:DressingRoom:Stop` (the resource sending the player off, or stopping)
  hides the card and closes the menu.
- **The card** (`storage.html`): kinds `dressing` and `dressingDone`, the same
  card with "Dressing room" and the step in its head, a checklist (ticks drawn
  with borders: GTA's font may draw a tick character as an icon) and, on the
  finish card only, keys. Key labels no longer wrap. Checked at 2560x1440 in
  headless Edge with the packaged page and mock messages.

Reviewed with `tools/ilcompare` against release N's DLL: the new
`DressingRoom*`/`Dressing*` types, the guide file reader (`ReadDressingRoom`,
`ReadCard` and `Lines` taking their keys), `PlayerGuide` (`MenuFor` creator
pages, `MenuOpened`, the Start the guide guard, `Content`/`Busy`/`Controls`)
and `MpPedCustomization` (the save hook and `RememberCurrentCharacter`). The
other `MpPedCustomization` entries are compiler-numbered local functions
renumbered by the new method, with no change.

Tests: `tests/player-guide` (both halves) now also cover the dressing room:
the packaged steps, refused broken ones, the flow (pages, out of order,
extras, Exit Without Saving, save, finish keys, stop), every lit row being a
row of its menu, and the wiring. All 47 tests in AGENTS.md pass.

Deploy, on top of release N (`deploy/README.txt`, "RELEASE O"): copy
`vMenuClient.net.dll`, `storage.html` and `config/player-guide.json`, then
`restart vMenu`. Deploy psrp_dressing_room's `client/main.lua` with it.
`deploy/release-o-full-from-main/` brings main or any of H to N up to O;
`deploy/rollback-to-release-n/` has N's three files.

Not checked in game: everything above, in particular MenuAPI keeping the
cursor on the row the guide picks after the creator opens, the finish card's
Enter not also picking a row after the save name keyboard, and the next join
restoring the saved character.

## November 18, 2026 — release N: the Player Guide (client DLL, `storage.html`, `fxmanifest.lua`, `client/whatsNew.lua`, `config/player-guide.json`, `themes/psrp-neon.css`) — TESTING, branch `feature/release-n-player-guide`

**Status: test build, not verified in game, not on `main`.** It is built on
release M (`perf/release-m`). Only the client DLL and client-side files change;
the server DLL and MenuAPI are release M's.

**Fixed after the first in-game try (November 18).** The owner's first test
froze the menu: F1 opened and closed it, but no other key did anything, and the
welcome card never showed.
- **The cause:** FiveM's client log showed `System.MethodAccessException: Error
  verifying ... Regex:ValidateMatchTimeout is not accessible`. FiveM's Mono
  refuses .NET's `Regex`, which the guide used to strip `~colour~` codes from row
  names. The .NET 9 tests can't see that.
  - The guide had already switched the menu's buttons off. The error stopped it
    before it showed the card or woke its key loop, so nothing turned them back on.
  - The error also escaped into the menu watcher, so that open was never
    announced to the rest of vMenu.
- **The fix:**
  - The codes are stripped by hand.
  - The card's message is built with Newtonsoft's `JObject`, which the guide's
    file reader proves works in game, rather than serialising an anonymous type.
- **Fail safe from now on:**
  - The key loop is woken before a card is shown.
  - Showing a card, offering the welcome and every frame of the key loop are
    each guarded. Any error stops the guide for the session, hides the card,
    gives the menu its buttons back, and writes `[vMenu] [Player Guide] stopped
    for this session after an error: …` to F8.
  - The menu watcher can no longer be broken by the guide.
- **New guards:**
  - `tests/client-guards` fails if anything compiled into the client uses
    `Regex`.
  - `tests/player-guide` checks the fail-safe wiring, and the stripping by hand
    with codes anywhere, stray tildes and unclosed codes.
  - 7 more deliberate breakages each fail a test, 28 in all.
- **First build's client DLL:** `5a66bf81…`. Don't deploy it.

**Second in-game fix (November 18): white blocks in two tips.** The card and the
menu use GTA's own font (`menuapi/fonts/chalet-london.woff`), which swaps some
characters for game icons: `›` (U+203A) is a blank button and `‹` (U+2039) the
Rockstar "CREATED" badge. "Pause › Settings › Key Bindings › FiveM" and "Player
Appearance › Clothing Quick Toggles" showed a white block for each `›`.
- `config/player-guide.json` now uses `→`, which that font draws as a chevron.
  Only this file changed.
- `tests/player-guide` now fails on any character in the guide's text, or in
  PlayerGuide's strings, outside printable ASCII and `←→↑↓…•–—“”‘’·×`. Each of
  those was drawn from the font to check it.
- The guide's `version` stays 1: the text fix doesn't need to show the guide to
  everyone again.

**Sharper text (November 18).** The owner found the menu's text a little soft
since MenuAPI 4 (release I).
- **Not the owner's setup.** They run ReShade with QuantV's effects, but their
  in-game screenshot shows no fringing, halos or grain on the menu or card text,
  and the same page drawn by Edge at 2560×1440 matches it almost pixel for pixel.
  FiveM's browser is Chromium 144 (`FiveM.app/bin/libcef.dll`), so Edge previews
  hold. Scaling the 1080p layout up (`transform: scale`) isn't the cause either:
  CSS `zoom` gives identical text pixels.
- **The cause.** The browser draws GTA's font heavier than the game did. In one of
  the owner's September screenshots, the old GTA-drawn "Player Related Options"
  has 4.0 px strokes at 1440p; the new menu has 5.1 px at the same size and width,
  so the letters crowd together. GTA's font also has no bold, so bold is the
  browser's fake one, about half again as heavy (7.6 px), which smears.
- **First try, a dark hairline** (commit `bc4eba0`): a
  `-webkit-text-stroke: 0.45px rgb(0 0 0 / 55%)` over each letter's edge. It got
  the weight right (3.93 px in the owner's next screenshot) but still looked out
  of focus: the hairline brings its own soft border, so the edge from background
  to text widened from 1.30 px to 1.78 px. A solid hairline in the surface's
  colour (1.94 px), a black one and a sharpening filter didn't fix it.
- **The change** (`themes/psrp-neon.css` for the menu and key hints,
  `storage.html` for the guide card and the filter itself):
  - Text is GTA's menu grey, `rgb(225 225 225)`, rather than the skin's
    lilac-white.
  - The `vmenu-text` SVG filter in `storage.html` raises each pixel's coverage to
    the power 2.5 (`feFuncA type="gamma" exponent="2.5"`). The faint outer pixels
    fade, which thins the letters and steepens their edges in one step. At 1440p:
    rows 4.01 px with a 1.12 px edge, against the old GTA menu's 3.95 px and
    1.30 px. It is the same at 1080p (3.02 px, 1.12 px); at 900p strokes are
    2.15 px, a little under proportional, and still read well.
  - The filter changes only transparency, so it is only on elements that hold
    text alone: row text and labels, separators, subtitle, description,
    overflow arrows, stat and colour-panel labels, key-hint labels, and on the
    card the title, paragraphs, header, TIP tag, tip text and key labels. The
    tip text and key labels got their own spans for this, since the tip box is
    see-through and the main key is solid.
  - Greyed-out rows, muted card text, the banner title and the key caps are left
    at full weight.
  - Bold (MenuAPI's inline `font-weight` for `~h~`, and the card's title, bold
    words, header and TIP tag) is the regular letters with a 0.4 px outline in
    their own colour, which the filter then sharpens: 1.34× regular in the menu
    and 1.42× on the card, with 1.35–1.41 px edges. The browser's fake bold is
    switched off.
  - The filter only runs when the menu's content changes, not every frame.
- **Considered and not used:** MenuAPI's `geometric` text mode draws identical
  pixels; its `supersampled` mode only reaches 4.6 px, needs a client DLL change,
  and on Chromium 144 its `zoom: 2` also doubles the menu's position.
- **Guards:** `tests/psrp-skin` and `tests/player-guide` check every part of the
  above: the filter's exact list of text-only elements, its definition, no dark
  hairline, the bold outline, that MenuAPI still marks `~h~` with an inline
  `font-weight`, and that nothing asks GTA's font for a bold. 15 deliberate
  breakages each fail a test.
- **Undo just this:** `deploy/release-n-undo-text-change/` holds `storage.html`
  and `themes/psrp-neon.css` from before both text changes (commit `ae93c9b`).
  The hairline version is commit `bc4eba0`.

**Why.** The owner asked for a guide new players can go through on their first
F1, like the ones in PSRP_CAD and EasyAdmin, because players keep asking staff
how to get an outfit or a car. A clickable mockup was approved first.

**What players see:**
- **The first time they open the menu,** a card beside it asks "New to the
  menu?". <kbd>Enter</kbd> starts the guide; <kbd>Esc</kbd> skips it.
- **The guide** is eight short steps. Each one opens the real menu page, selects
  and lights up the rows it talks about, and dims the rest:
  1. Find your way around (keys, mouse, where to rebind).
  2. Make your character (MP Ped Customization).
  3. Change clothes and save outfits.
  4. Department uniforms: only while `vmenu_dept_outfits` is on, so it's left
     out until release K's Department Outfits are switched on.
  5. Get a car (Vehicle Spawner; Custom Vehicles and F9; the 5-vehicle cap, 3
     in a purge, from PSRP_Framework's `sv_spawnlimiter.lua`; `/dv`).
     PSRP_welcome and PSRP_loading still say 3; that text is out of date.
  6. Keep your car (Saved Vehicles, Save to CAD, vehicle codes).
  7. Your personal vehicle.
  8. Back up your saves (only while `vmenu_backups` is on).
  9. You're all set (where to find it again; the Discord).
- **Keys while it's up:** <kbd>Enter</kbd> or <kbd>→</kbd> next,
  <kbd>Backspace</kbd> or <kbd>←</kbd> back, <kbd>Esc</kbd> skip. On a controller:
  A, B, and X to skip (without also jumping). These match EasyAdmin's staff
  guide and the CAD tour. The menu's own keys do nothing while a card is up,
  and Esc doesn't open the pause menu. Over a fade, a player switch or the pause
  menu, MenuAPI hides the menu; the card hides with it, and keys don't move it on.
- **Main Menu > Player Guide**, the new first row: "Start the guide", then
  "How do I…?" questions. A question opens just that answer on the right page;
  Enter leaves the player there to use it, Backspace returns to the questions.
- **Seen:** finishing or skipping counts; closing the menu partway doesn't, so
  the welcome comes back next time. It's saved on the player's PC
  (`vmenu_player_guide_seen`), like the CAD tour's flag. Every current player
  gets the welcome once too, since the flag is new.
- **What's New** waits while the guide's card is up: the menu watcher now sends
  `vMenu:MenuStateChanged` with a second value, whether the card is up. A new
  player (no note seen yet) never gets the note on top of the guide; anyone
  else gets a newer note on their next open. It stays in Misc Settings > What's
  New In vMenu.

**How it's built:**
- `vMenu/PlayerGuide.cs`: the menu rows, the first-open check, and a loop that
  runs only while a card is up (`TickSleeper`). It reads the game's frontend
  controls itself while `MenuController.DisableMenuButtons` keeps the menu still.
  MenuAPI 4 acts on a key's release, so after the guide ends it keeps the
  buttons off until the key is let go (plus 150 ms). Otherwise the Enter that
  finished the guide would also pick the selected row. It also waits 400 ms
  after another resource's on-screen keyboard closes. MenuAPI saves the buttons
  setting when a keyboard opens and writes it back 300 ms after it closes.
  Handing the buttons back first would leave them off for good.
- `vMenu/PlayerGuideContent.cs`: no game calls. It holds the JSON reader, the
  step flow, key routing and the button hold, so tests run them directly.
- `config/player-guide.json`: all the wording. `**bold**` and `[KEY]` are the
  only marks; the page shows everything else as plain text. A card names its
  menu page and the rows to light up, and can depend on a convar
  (`requiresConvar`). A card whose menu the player doesn't have is left out, and
  the step count follows. A broken file turns the guide off with an F8 line
  saying why, rather than showing half a guide. Raise `version` to show the
  guide to everyone again.
- `storage.html`: the card (`#vmenu-guide`), placed beside the menu at its
  scale (on the left of a right-aligned menu). It lights up rows by matching
  MenuAPI's row text, and re-applies after each MenuAPI redraw.
- `client/whatsNew.lua`: the What's New skip above.

**Validation:**
- **New tests:**
  - `dotnet run --project tests/player-guide` (425 checks):
    - the packaged file reads, and broken copies are refused with a reason
      (misspelt setting, unknown menu, missing title or body, duplicate ids, a
      question on a missing step, version 0 or in quotes);
    - every path through the flow: welcome, next, back, skip, finish, menu
      closed, started from the menu, questions;
    - cards left out when their convar or menu is missing, with the count
      following;
    - key routing (Esc also counts as the game's cancel, X skips only on a
      controller);
    - row scrolling;
    - the button hold (held, released, pressed again, an on-screen keyboard).
  - `python tests/player-guide/test_player_guide.py`:
    - every row a card lights up is made and added to that card's menu;
    - every **bold** name in the text is a real row label;
    - every convar a card needs is set in `permissions.cfg`;
    - the wiring in MainMenu, the input loop, the page, the manifest and What's
      New.

    It caught the outfit-code question pointing at rows that live in the
    character editor, not on the MP Ped Customization page.
- **Updated tests:**
  - `tests/whats-new`: the note waits while the card is up.
  - `tests/sleeping-loops`: the watcher offers the guide once per open.
- **Each behaviour, broken on purpose, fails a test (28 cases):**
  - the button hold, the keyboard wait and the key order;
  - Esc, jump and the hidden-menu guard;
  - seen only on finish or skip;
  - unavailable steps left out, and unknown settings refused;
  - the watcher's offer and What's New's wait;
  - a renamed row in the highlights or the text;
  - the card's text shown as HTML.
- **Browser check:** `storage.html` with a stand-in menu and a guide message. The
  card sits beside the menu, the two rows are outlined, the others are dimmed,
  and the keys and progress bar show.
- **Independent review** against MenuAPI 4.1.3's source. Nothing serious:
  - presses during the guide are dropped;
  - the release can't pick a row;
  - page changes don't re-announce the menu;
  - a menu closing under the guide ends it cleanly;
  - every lit row exists for a regular player.

  Fixed from it:
  - **The card stayed on screen over a fade.** It stayed during a fade or player
    switch from another resource while the menu was hidden, and Enter still
    moved it on. The card now hides with the menu, and keys are ignored then.
  - **What's New could disagree with the guide.** It now goes by whether the
    card is actually up, not by its own guess. With a broken guide file, or a
    guide version bump, both could have shown at once or the note been lost.
  - **X on a controller also jumped.** Jump is now held off while a card is up.
  - **The menu buttons could stay off for good.** That happened when another
    resource's on-screen keyboard opened during the guide. The hold now waits
    out MenuAPI's keyboard check.
  - **The car step said 3 vehicles.** PSRP_Framework's cap is 5 (3 in a purge).
- **All 47 tests in AGENTS.md pass.**

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `d34b825ebdcdc5755edc8fa0b52b6fb1af778525c3d6ed18872ec4e821c4fc4d` |
| `build/vMenu/vMenuServer.net.dll` | `fd943356…` (unchanged from release L) |
| `build/vMenu/MenuAPI.dll` | `43442a16…` (unchanged) |

**Deploy (test):** on top of release M. The files are in
`deploy/release-n-test-build/`. Someone skipping releases uses
`deploy/release-n-full-from-main/`.
1. Check the installed client DLL is release M's (`2fc34daa…`).
2. Copy `vMenuClient.net.dll`, `storage.html`, `fxmanifest.lua`,
   `client/whatsNew.lua`, `config/player-guide.json` and `themes/psrp-neon.css`.
3. Run `refresh`, then `restart vMenu` (the manifest changed). Have players
   reconnect.

**In-game checks:**
- **First F1:** the welcome card shows beside the menu. Esc skips it, and the
  pause menu does not open. The next F1 doesn't show it again. To see it again,
  delete the `vmenu_player_guide_seen` KVP, or raise `version` in the file.
- **The guide:** Enter goes through every step; each opens the right page with
  the rows lit. Backspace goes back. Finishing leaves the main menu on Player
  Guide, and that Enter does not open the Player Guide menu.
- **Closing mid-guide:** F1 closes the menu, and the welcome comes back next
  open.
- **Questions:** Main Menu > Player Guide > a question. Enter leaves you on that
  page, and the menu works normally. Backspace returns to the questions.
- **Controller:** A, B and X work, and the card shows A, B and X.
- **Right Align Menu** (Misc Settings): the card moves to the left of the menu.
- **What's New** doesn't pop up for a new player alongside the guide.
- **F8:** no `[vMenu] [Player Guide]` line (that line means the file couldn't be
  used).
- **Text:** grey and crisp, with the letters not crowding. Greyed-out rows are
  still easy to read, and bold words on the card (and the version in About
  vMenu) are bold without smearing.
- **Everything in release M's list.**

**Roll back to release M:** copy back the files from
`deploy/rollback-to-release-m/`, `refresh`, and `restart vMenu`.
`config/player-guide.json` can stay; release M doesn't read it.

## November 17, 2026 — release M: loops fully off while idle, options set once (client DLL) — TESTING, branch `perf/release-m`

**Status: test build, not verified in game, not on `main`.** It is built on
release L (`perf/release-l`). Only the client DLL changes.

**Why.** The owner's profile of release L showed vMenu at 0.148 ms a frame:
- about 0.11 ms of it was the C# runtime's bookkeeping for vMenu's resting
  loops;
- the rest was four loops that ran every frame because of options on the
  owner's account.

The owner asked for the rest of vMenu Enhanced's optimizations. A check of all
74 of Enhanced's loops found two things releases H to L hadn't brought over:
- Enhanced's loops stop completely while their feature is off.
- Enhanced sets most player and weapon options once and re-applies them
  occasionally, rather than every frame.

**What changed:**
- **Loops fully off while idle.** 17 loops have nothing to watch but menu
  options:
  - player, weapon and misc options; weather; the HUD and location; the
    recording keys; death notifications; overhead names; entity outlines;
  - the character editor camera; the snowball hint;
  - the personal vehicle loops (without a personal vehicle);
  - the shared player and vehicle protections.

  With every menu closed and their options off, they take themselves off
  FiveM's tick list (`TickSleeper`), so they cost nothing, not even a wait for
  the runtime to check. They come back:
  - **all at once,** on anything that ended release L's rests
    (`IdleRest.Woken`): a menu opening, new permissions, the player blips and
    names integration events, a new personal vehicle, the minimap key, or the
    pause menu's expanded radar;
  - **each on its own,** when `GeneralTasks` (every 100 ms) runs that loop's own
    "wanted" test (cheap reads of options and permissions) and it says there is
    work. This catches anything that changes without one of those events,
    within 0.1 s; release L's fallback took 2 s.
- **Options set once, then held 4 times a second,** as Enhanced does. These set
  something the game keeps:
  - player: god mode, no ragdoll, invisible, never wanted;
  - weapons: no reload, which now uses the game's infinite clip flag (as
    Enhanced) instead of refilling the clip every frame, and the auto-equip and
    unlimited parachutes. With the flag, ammo doesn't go down at all (minigun
    and probably throwables included), so No Reload now also acts like
    unlimited ammo, as in Enhanced. In PSRP only `group.owner` has either
    permission;
  - the pause menu's expanded radar.

  So they are re-applied every 250 ms rather than every frame. That puts them
  back after a respawn or another script resetting them. Enhanced holds god
  mode once a second; this is 4 times. These stay every frame because they
  only last a frame: super jump, unlimited ammo (it follows the weapon in hand),
  hide radar (it has to win against HUD scripts that show the radar every
  frame), the camera locks, and the street name hide in a vehicle with both
  speeds on.
- **Vehicle options:** only the torque multiplier keeps the vehicle loop every
  frame. These now ride on the 100 ms in-vehicle pass: vehicle god mode
  (including repairs), freeze, keep clean, no helmet and infinite fuel.
- **Player blips, when on:** they still refresh 10 times a second, but between
  refreshes they turn with their players 20 times a second instead of every
  frame. Enhanced updates them 4 times a second.
- **`vmenu_ticks`** in F8 lists the loops that sleep while idle and whether each
  is asleep, as Enhanced's command of the same name does.

**Expected effect.** Not measured; the owner's next profile will tell.
- **Menu closed, options off:** the 17 option-only loops leave the runtime's
  list, which takes away most of the bookkeeping cost. vMenu should drop from
  about 0.15 ms to roughly 0.08–0.10 ms a frame.
- **Staff with god mode, blips and weapon options on:**
  - PlayerOptions and WeaponOptions run 4 times a second instead of every frame
    (unless super jump or unlimited ammo is on);
  - blips run about 20 times a second instead of every frame.

**Validation:**
- **`tests/idle-rests`** still replays the live build (`build-2026-11-17-3`)
  beside this one through a long random session, comparing what each version
  does every frame. The every-frame options must match call for call. The
  options now held 4 times a second are compared as the state the game keeps:
  - invincible, visible, ragdoll, wanted level, no reload, parachutes, map,
    autopilot;
  - after a 300 ms settle following anything outside vMenu that resets them (a
    respawn, a wanted level, a lost parachute).

  It compared 18,225 frames, the kept state on 14,943 of them. It also checks:
  - while idle, the four option loops it runs don't run at all;
  - each option runs at its new rate: every frame, or 4 times a second.
- **`tests/sleeping-loops`:**
  - each of the 17 loops is registered with its own "wanted" test and sleeps
    only with every menu closed (except the snowball hint and the personal
    vehicle blip, which have no menu option);
  - every wake path is wired;
  - the real registry, with the real `TickSleeper` and `IdleRest`, sleeps and
    wakes by event and by its own test, and `vmenu_ticks` lists it.
- **`tests/vehicle-rests`:** only the torque multiplier keeps every frame, and
  the five options above no longer force it.
- **`tests/frame-costs`:** blips match the every-frame version at every refresh
  and turn 20 times a second in between (32 turns in 260 frames).
- **Updated:** `tests/hud-overlay`, `tests/client-guards`,
  `tests/vehicle-conveniences`.
- **Each change, undone on purpose, fails a test:**
  - super jump, unlimited ammo or torque not every frame;
  - god mode or no reload never applied;
  - expanded radar dropped;
  - a loop sleeping with the menu open;
  - GeneralTasks or `IdleRest.Woken` not waking;
  - vehicle god mode forcing every frame;
  - blips turning every frame.
- **Independent review.** No high-severity bugs. For all 17 loops, the
  condition a loop sleeps under matches its own "wanted" test, so none can stay
  asleep or bounce awake, and every latch is released before a loop sleeps.
  Fixed from it:
  - **Leaving noclip turned god mode off and showed an Invisible player.** That
    lasted up to 250 ms now that PlayerOptions holds those 4 times a second;
    before, the next frame put them back. Now:
    - noclip leaves invincibility on while vMenu's god mode is on;
    - its visibility reset matches its comment, so an Invisible player on foot
      stays invisible (unless vMenu doesn't handle invisibility,
      `vmenu_handle_invisibility`);
    - noclip ending wakes the option loops for the next frame.
  - **`SetupTickFunctions` runs once.** A second run would have put every loop
    on the tick list twice, and a sleeping loop only takes one copy off.
  - **The weather and character editor camera loops count an open menu as
    work.** If the menu watcher ever missed a menu opening, the 100 ms check
    still brings them back.

  Accepted as designed, worth noticing in game:
  - after a ped or model change (department outfit, character load), god mode,
    no ragdoll and invisibility can be missing for up to 250 ms, and no helmet
    for up to 100 ms;
  - vehicle engine god and auto-repair act within 100 ms;
  - with both speeds on, the street name can show for up to 100 ms on getting
    in.
- **All 45 tests in AGENTS.md pass.**

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `2fc34daa2e0245c7ffa4d40bb5126b9f914883791980ab1ba89bc5ab94fb0c07` |
| `build/vMenu/vMenuServer.net.dll` | `fd943356…` (unchanged from release L) |
| `build/vMenu/MenuAPI.dll` | `43442a16…` (unchanged) |

**Deploy (test):** on top of release L. Only `vMenuClient.net.dll` changes; it's
in `deploy/release-m-test-build/`. Someone skipping releases uses
`deploy/release-m-full-from-main/`.
1. Check the installed client DLL is release L's (`8d5a4c47…`).
2. Copy it, then run `restart vMenu` and have players reconnect.

**In-game checks:**
- **God mode:** survives a respawn and a PSRP_DeathScript revive. Another
  script turning invincibility off is put back within a quarter second.
- **Noclip:** with god mode and Invisible on, turn noclip off near someone. You
  stay invincible and invisible.
- **Invisible, no ragdoll, never wanted:** still hold. Getting a wanted level
  clears within a quarter second.
- **No reload:** no reloading. Note whether ammo now still goes down, because
  the game's infinite clip flag may differ from the old refill. Also check the
  parachute options.
- **Expanded radar:** with the pause menu setting on, it stays expanded.
- **Vehicle options:** vehicle god mode (damage repaired, engine kept), freeze,
  keep clean, no helmet on a bike and infinite fuel. Torque still pulls.
- **Blips:** turn smoothly with their players.
- **`vmenu_ticks`** in F8: most loops show asleep with the menu closed, and
  running while it's open.
- **Outside switches with the menu closed:** a Discord role change with
  `/refreshperms`, PSRP_Clockin's blips and names, the minimap key. Each takes
  effect within a moment.
- **Everything in release L's list.**

**Roll back to release L:** copy back release L's `vMenuClient.net.dll` from
`deploy/rollback-to-release-l/`.

## November 17, 2026 — release L: upstream bug fixes, sleeping loops, refined skin (client DLL, server DLL, `themes/psrp-neon.css`) — TESTING, branch `perf/release-l`

**Status: test build, not verified in game, not on `main`.** It is built on
release K (`perf/release-k`). Until it passes an in-game check, `main` and
`build-2026-11-17-3` stay the live build and the fallback. It changes the
server DLL (one range check), so client and server still go together, as with
release K.

**Why.** The owner asked for the remaining bug fixes and performance work from
the comparison with vMenu Enhanced v1.0.22.

**Bug fixes (client):**
- **License Plate Type.** Four styles were under the wrong names: choosing "Blue
  on White 2" gave Yellow on Black, and so on. One table
  (`VehicleOptions.PlateStyles`, in upstream 9e4055ba's order) now sets the
  style and reads it back, where three hand-written switches used to.
- **Randomize Character** saved the eyebrow style as the colour and the colour
  as the style.
- **Garbled arrows.** The "↓ Characters ↓" and "↓ Vehicles ↓" headings and the
  "→→→" labels in Saved Characters and Saved Vehicles were stored double-encoded
  and showed as `â†“`.
- **Toggle all doors** (Space in the mod menu) looked only at the driver's door
  (upstream 2c7b9ab5). A vehicle without one, or with only the boot open,
  always opened everything. It now checks every door that isn't broken, and
  uses the doors menu's all-doors cooldown.
- **Saved weapon loadouts:**
  - A save that couldn't be read stopped the loadouts menu or a respawn. A
    missing "weapons before respawn" save was read as `{}`, which throws. Every
    read now goes through `ReadWeaponLoadout`, which treats an unreadable save
    as empty and logs it.
  - Each saved weapon's permission is looked up by its spawn name (upstream
    a6349406). The number saved with it moves when a weapon is added to the
    middle of the list. Release K's newspaper permission came after every
    weapon, so it moved none.
  - Unchanged: which loadout actions skip the permission check. In PSRP,
    regular players have no vMenu weapon permissions, so the respawn restore
    still skips it.

**Bug fix (server):** Kick Passengers works only within 500 m of the owner.
The menu can only reach the personal vehicle while the player's game has it
loaded (OneSync's vehicle range, 424 m by default), so 500 m never stops the
menu. A request from further away is logged as malformed. Upstream doesn't
limit this; it was this repo's own audit finding.

**Performance (client):**
- **The menu watcher sleeps** (`MainMenu.OnTick`). It ran every frame with
  every menu closed. Now:
  - It takes itself off the tick list once every menu is closed (`TickSleeper`).
  - MenuAPI 4's `MenuTicks.Changed` wakes it. The event fires when MenuAPI's
    own menu loops start, on the frame after a menu opens and before they read
    any input.
  - The wake-up does that frame's work immediately: it ends the idle rests,
    announces `vMenu:MenuStateChanged`, and sets the character creator's back
    button guard.
  - A 100 ms safety net in `GeneralTasks` wakes it if the event was ever
    missed.
- **The death save starts from the game's damage event**
  (`CEventNetworkEntityDamage`). The appearance and weapons are saved as the
  player dies, so the weapon in hand is not lost.
  - A check 10 times a second, vMenu Enhanced's rate, catches deaths no damage
    event reports, such as a script setting health to 0.
  - Started while the ped is still dying, it waits for death, and restores
    nothing if death never comes.
  - Only one save runs per death.
- **Loops sleep while their options are off.** Seventeen loops have nothing to
  watch but menu options. With every menu closed and those options all off,
  they rest for 2 s instead of 0.1–0.5 s:
  - Player, weapon and misc options;
  - Weather;
  - HUD text and location;
  - Recording keys;
  - Death notifications;
  - Overhead names;
  - Entity outlines;
  - The character editor camera;
  - The snowball hint;
  - The personal vehicle loops (while there is no personal vehicle);
  - The shared player and vehicle checks, which also lost six 100 ms waits.

  These events end the rest at once:
  - a menu opening;
  - new permissions;
  - the player blips or names integration events;
  - a new personal vehicle;
  - the minimap key;
  - the pause menu's expanded radar.

  The loops that watch the game itself keep their rates: the radar preference,
  voice chat, player blips, clothing glow, the vehicle loops and the time and
  weather sync.

**PSRP Neon skin, refined** (`themes/psrp-neon.css`). The owner asked for a skin
that looks good without being messy with colours. It was busy because:
- each block (subtitle, scroll bar, description, stats) had its own purple ring
  and glow, under the banner's own purple frame;
- the stats, scroll bar and colour panel were MenuAPI's black, and the sliders
  GTA blue;
- the selected row and subtitle had gradients, and the key hints had purple
  keys and a purple border.

Now:
- **One dark glass for every block**, with a faint neutral edge: rows,
  subtitle, description, scroll bar, stats, the colour and opacity panels (now
  rounded too), and the key hints.
- **Purple only for what is active:**
  - the selected row, solid instead of a purple-to-blue gradient;
  - the subtitle text;
  - the description rule, which fades out;
  - the filled part of sliders and stats.
- **vMenu's 17 sliders** (Paint Fade, the face shape sliders and others) use
  the skin's colours instead of GTA blue. They keep MenuAPI's default colours on
  each slider, which no token reaches, so the skin overrides them.
- **Selected-row icons:** the icons and checkboxes on the selected row are white,
  like its text. MenuAPI drew them black, for GTA's white highlight.
- **Key hints:** light keys on the dark glass.
- **New test** `tests/psrp-skin`. Every MenuAPI setting the skin sets (31, up
  from 19) and every part of the menu it styles must be a real one, so a typo
  can't silently fall back to the GTA look. It checks that every rule stays
  inside the menu and its key hints, and that every colour it uses is defined.
  Each kind of mistake, made on purpose, fails it.
- **Preview:** the skin was compared before and after in a browser, on
  MenuAPI's own page, with the PSRP banner. It hasn't been seen in game yet.

**Expected effect.** Not measured. With every menu closed, on foot, and every
option off:
- vMenu's own every-frame loops go from 2 to none. MenuAPI's toggle tick
  remains, as in Enhanced.
- The other loops wake about 65 times a second instead of about 125. This is an
  estimate from their rest times, not a measurement. Most of what is left is
  the loops that watch the game, and the 10-a-second death check.

**Validation:**
- **New tests:**
  - `tests/sleeping-loops` runs the production watcher and death code against
    a fake game. It checks:
    - asleep, nothing runs;
    - MenuAPI's event does the open frame, and the creator's guard is set
      before any input;
    - the close frame is handled before sleeping;
    - the safety net;
    - one save per death, from the damage event or the fallback check, and
      never for another entity.

    Source checks cover which loops take the long rest (never with a menu
    open) and every wake-up.
  - `tests/upstream-bug-fixes`:
    - plate labels against styles, through the one table;
    - eyebrows;
    - no double-encoded text anywhere in the client;
    - the door toggle against fake doors;
    - loadout reading (empty, `{}`, cut off, `null`, entries without a weapon)
      and permissions by name;
    - the Kick Passengers range.
  - Each fix, undone, fails one of them.
- **Updated tests:**
  - `tests/idle-rests` still matches `build-2026-11-17-3` frame by frame with
    the longer rests.
  - `tests/key-bindings` checks that the minimap key wakes the loops.
  - `tests/hud-overlay` checks the HUD's long rest.
  - `tests/client-fixes` and `tests/client-guards` follow the moves.
- **`tools/ilcompare` against release K:** the server changes only in
  `GetOutOfCar` and its new range constant. The client changes are the
  methods above, plus local functions the compiler renumbered.
- **All 45 tests in AGENTS.md pass.**

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `2fc34daa2e0245c7ffa4d40bb5126b9f914883791980ab1ba89bc5ab94fb0c07` |
| `build/vMenu/vMenuServer.net.dll` | `fd9433560d3e28fbf31c820eb8787bc6bc44179b5bf445c6dac682e7b8769a90` |
| `build/vMenu/MenuAPI.dll` | `43442a16…` (unchanged) |

**Deploy (test):** on top of release K. The two DLLs and `themes/psrp-neon.css`
change. They are in `deploy/release-l-test-build/`.
1. Check that the installed DLLs are release K's (`4d002d53…` and `03147fa7…`).
2. Copy both DLLs and `themes/psrp-neon.css`, then run `restart vMenu` (or
   restart the server) and have players reconnect.

**In-game checks:**
- **Plates:** each License Plate Type name matches the plate.
- **Randomize Character:** saves the eyebrows it shows.
- **Arrows:** the Saved Characters and Saved Vehicles headings and arrows look
  right.
- **Toggle all doors:** Space in the mod menu, on a bike and on a car with only
  the boot open.
- **Death and respawn:** with Restore Weapons on, die with a gun in hand, by a
  shot and by `/die` (or another script). The gun comes back after the
  respawn, and so does the appearance. Do the same with PSRP_DeathScript's
  revive.
- **Menu:**
  - F1 opens at once.
  - The character creator's back button still shows "save first", including
    after "Exit Without Saving" is cancelled.
  - Closing the menu still offers to update a changed saved vehicle.
- **Options switched with the menu closed:** a Discord role change with
  `/refreshperms`, PSRP_Clockin's blips and names, the minimap key. Each takes
  effect at once.
- **Kick Passengers** works on a personal vehicle nearby.
- **Skin:**
  - The menu is dark glass with purple only on the selected row, the subtitle,
    the description's top edge, and slider and stat fills.
  - Sliders (Paint Fade, the face shape sliders) are purple, not blue.
  - On the selected row, checkboxes and lock icons are white. This check
    matters: the preview used stand-in checkbox images, not the game's.
  - The paint colour panel and the key hints match.
- **F8:** no new errors.

**Roll back to release K:** copy back release K's two DLLs and
`themes/psrp-neon.css` from `deploy/rollback-to-release-k/`.

## November 17, 2026 — release K: department outfits, character wardrobe, data refresh, vehicle options, request guard (client DLL, server DLL, Lua, config, SQL) — TESTING, branch `perf/release-k`

**Status: test build, not verified in game, not on `main`.** It is built on
release J (`perf/release-j`). Until it passes an in-game check, `main` and
`build-2026-11-17-3` stay the live build and the fallback. **It changes the
server DLL** (the owner asked for the request guard and the live role refresh).

**Why.** The owner picked these from the gap list against vMenu Enhanced v1.0.21,
leaving out what PSRP already has elsewhere: EasyAdmin handles staff tools,
PSRP_Framework restricts vehicles and weapons, and vMenu's Custom Vehicles
button already opens PSRP_spawner.

**What changed:**
- **Department Outfits** (MP Ped Customization > Department Outfits, after
  Saved Outfits). Off until `setr vmenu_dept_outfits true`.
  - Anyone on duty with a department (PoliceEMSActivity's `/duty`) lists and
    wears its outfits for their body type. Nobody needs an ace to wear them.
  - **Managers** publish, replace, rename and delete them. There are three
    ways to manage:
    - `vMenu.DeptOutfits.ManageOwn` manages each department whose duty
      permission the player has. That is the org's `member` in
      `Config.DepartmentOutfitOrgs`, copied from PoliceEMSActivity's
      `Config.Orgs`, e.g. `group.lspdblip`. PSRP grants it to
      `group.depthead`, so a department head manages exactly their own
      department(s).
    - `vMenu.DeptOutfits.Manage.<org>` manages one department.
    - `vMenu.DeptOutfits.ManageAll` manages every department. PSRP grants it to
      `group.owner`.
  - Stored in the `vmenu_dept_outfits` table (`database.sql`). The server
    (`server/deptOutfits.lua`) checks every request against duty and aces, and
    checks every outfit (a whole MP outfit, every piece matched to a clothing
    collection, under 8 KB).
  - **Limits:**
    - names are unique per department and body type;
    - at most 60 outfits per department and body type;
    - one publish per 10 seconds per player;
    - who manages what is read once per player per 30 seconds; duty is read
      fresh every time.
- **Character Outfits and Hair & Makeup Styles** (Manage Saved Character).
  Each MP character has its own outfits and styles, kept on the player's PC
  (KVP `vmenu_character_outfit_*` / `vmenu_character_style_*`) and included in
  backups. Renaming or cloning a character takes them along, and deleting one
  removes them.
- **Base-game data refresh:**
  - 29 vehicles, now 927 in the spawner;
  - 21 peds;
  - `weapon_newspaper` (`vMenu.WeaponOptions.Newspaper`).
  - Tattoos: the list (`overlays.json`, 3,429) is checked against the game, and
    tattoos only the game's shops know are added. The F8 console line
    `[vMenu] [Tattoos] … Game checked: True, shops used: True` shows it worked.
  - The spawner and ped menus hide models this game build doesn't have.
  - `config/vehicleIdentityModels.json` is regenerated for the new vehicles.
- **Vehicle Options:**
  - Hex paint (`#RRGGBB`) for primary and secondary colours.
  - "Paint Fade" (was "Vehicle Enveff Scale"), with its slider now showing the
    vehicle's current value.
  - Preset factory colours clear custom paint first.
  - Roof livery.
  - Radio: a station to tune when you get in as the driver (or leave it
    alone, the default), and stations to block. The old forced LS Rock Radio
    on spawn is gone, and an old "Default radio station" setting carries over.
  - New aces `vMenu.VehicleOptions.HexColors`, `.RoofLivery` and `.Radio`,
    granted to everyone in the packaged `permissions.cfg`. A server without
    them falls back to Vehicle Options' own permission.
- **Request guard and security log:**
  - `vMenuServer/RequestGuard.cs` and `server/guard/requestGuard.lua` check
    every request a client sends to vMenu's server side: argument types and
    sizes, the sender, and a per-player, per-event rate limit (far above what
    the menu sends).
  - Refusals are printed to the server console as `[vMenu] [SECURITY]`. They
    can also go to a Discord webhook (`set vmenu_security_webhook`, never
    `setr`).
  - The convars are in `permissions.cfg`, commented out at their defaults.
  - The time update also refuses an hour outside 0–23 or a minute outside 0–59.
- **Live role refresh.** DiscordAcePerms already fires
  `TriggerEvent('vMenu:RequestPermissions', src)` after `/refreshperms` and the
  bot's auto-refresh. vMenu now answers it from the server (it ignored calls
  with no client source) and sends that player their new permissions. Options
  turn on or off at once. Menus are built once per session, though, so a menu
  the player didn't have at join only appears after they reconnect.
- **Permission names.** The client skips a permission name it doesn't know
  instead of failing. Before, a client older than its server never built the
  menu. This only helps from release K on: release J's client still fails
  against release K's server, so **deploy and roll back client and server
  together.**

**Validation:**
- **New tests:**
  - `tests/dept-outfits` (Lua): the whole server against an in-memory table,
    covering:
    - access by duty, Manage, ManageOwn and ManageAll;
    - names and payloads;
    - limits;
    - two publishes sent together;
    - the 30-second access cache, and forgetting it when a player leaves;
    - org spelling (`LSPD` and `certcivt2` stored as the config writes them);
    - player names cut on a character boundary;
    - database errors;
    - the wiring.
  - `tests/character-wardrobe`: keys, rename, clone and delete, styles, the
    shared outfit path, request pacing, and the Department Outfits wiring.
  - `tests/game-data`: the new vehicles, peds and weapon.
  - `tests/tattoo-catalog`: hashing, the game check, shop tattoos, the
    fallbacks, and tattoos saved by hash that a later list names.
  - `tests/vehicle-colours-radio`: hex paint, Paint Fade, roof livery, radio
    choices and their wiring.
  - `tests/request-guard` (C# and Lua): rate limits, argument checks, sources,
    and every handler's wiring.
- **Updated tests:** `tests/permission-table` (unknown permission names are
  skipped), `tests/vehicle-rests`, `tests/idle-rests` and others follow the
  moves.
- `tools/ilcompare` of the server against the first release K build: only
  `RequestPermissions` (live refresh) and `UpdateTime` (bounds) changed.
- **Two independent reviews** (server and client). Fixed from them:
  - a department access check costing up to ~190 ace checks per request (now
    cached per player for 30 s);
  - two publishes sent together both passing the limit;
  - org casing;
  - names cut mid-character;
  - the list size cap (300 → 2,000 names);
  - the time bounds;
  - the client failing on unknown permission names;
  - the tattoo shop read failing the zone check with it;
  - tattoos saved by hash;
  - the radio choice not applying when moving to the driver's seat or getting
    back into the same vehicle;
  - the request loop polling every frame;
  - reopening the menu quickly showing "Couldn't load";
  - an unclear "other body type" error.
- **Left as is (rare):**
  - A tuned radio station can re-tune over another resource that switched the
    radio off within 3 s of getting in.
  - Blocked stations stay blocked for the session if the Radio ace is
    removed.
  - Rolling back to J forgets the old radio setting, so J tunes LS Rock again.
  - Wardrobe entries can be orphaned after a rollback to J and a character
    delete there.
  - The tattoo catalog is built in one go the first time the character
    creator opens (estimated tens of ms, once).
- All 42 tests in AGENTS.md pass.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `2fc34daa2e0245c7ffa4d40bb5126b9f914883791980ab1ba89bc5ab94fb0c07` |
| `build/vMenu/vMenuServer.net.dll` | `03147fa748dcee86dba985a99edb08fda6a58a9fca0b8cb8958409f80a70d39d` |
| `build/vMenu/MenuAPI.dll` | `43442a16…` (unchanged) |

**Deploy (test):** on top of release J. `deploy/release-k-test-build/` has every
changed file, with `SHA256SUMS.txt`.
1. Check the installed `vMenuClient.net.dll` is release J's (`dece98b4…`) and
   `vMenuServer.net.dll` is `8827cd4c…`.
2. Run the `vmenu_dept_outfits` `CREATE TABLE` from `database.sql` on the
   server's database. It's `IF NOT EXISTS`, so running it twice is safe.
3. Copy both DLLs together, plus:
   - `fxmanifest.lua`, `database.sql`;
   - `server/deptOutfits.lua`, `server/guard/requestGuard.lua`;
   - the changed `server/*.lua` (`backups`, `clothingCapture`, `oufitCodes`,
     `spawnProvenance`, `staminaTelemetry`, `vehicleCodes`, `vehicleIdentity`);
   - `client/backups.lua`;
   - `config/vehicleIdentityModels.json`.
4. **`config/config_server.lua`:** if the installed one matches release J's,
   copy it. Otherwise add the `DepartmentOutfitOrgs` block by hand.
5. **`config/permissions.cfg`:** the live file has hand edits, so add these by
   hand:
   - `setr vmenu_dept_outfits true`;
   - the two `add_ace` lines for `group.depthead` (ManageOwn) and
     `group.owner` (ManageAll);
   - the three `vMenu.VehicleOptions` aces (HexColors, RoofLivery, Radio);
   - optionally, the commented `vmenu_guard_*` and `vmenu_security_webhook`
     lines.
6. Restart the server (or `refresh` + `restart vMenu`), and have players
   reconnect.

**In-game checks:**
- **Department Outfits:**
  - A department head on duty publishes for their own department, and is
    refused for another.
  - A member on duty sees and wears it.
  - A civilian sees nothing.
  - Reopening the menu quickly still loads.
- **Live role refresh:** `/refreshperms` after a Discord role change turns an
  option on or off without a reconnect.
- **Joining and leaving:** no `[vMenu] [SECURITY]` lines from normal play,
  joining or leaving. The server's `[FromSource] string` source has only been
  checked offline.
- **Radio:** tune on entry, block a station, move from passenger to driver.
- **Vehicle paint:** roof livery and hex paint.
- **Data:** the F8 tattoo line; the new vehicles and peds spawn; models the
  game lacks are hidden.
- **Release J's checks:** key bindings and the HUD text size.

**Roll back to release J:** copy back release J's two DLLs and the files above
from `deploy/rollback-to-release-j/`, then restart. Both DLLs go back together.
The `vmenu_dept_outfits` table, the extra aces and the convars can stay; J
ignores them.

## November 17, 2026 — release J: the rest of Enhanced's performance patterns, empty teleport fields (client DLL, `storage.html`, `client/exports.lua`) — TESTING, branch `perf/release-j`

**Status: test build, not verified in game, not on `main`.** It is built on
release I (`perf/menuapi4`), which is built on release H. Until it passes an
in-game check, `main` and `build-2026-11-17-3` stay the live build and the
fallback.

**Why.** The owner asked for the remaining performance patterns that vMenu
Enhanced uses, plus one teleport fix. After releases H and I, vMenu still did
some work every frame that Enhanced does not:
- a loop for the visor and snowball keys, for every player;
- the player options loop for every regular player, because Never Wanted
  defaults to on even without its permission;
- the vehicle options and vehicle switch loops, every frame in any vehicle;
- polling the point, minimap, drift and teleport keys while they are switched
  on;
- drawing coordinates, location, time and speed with game calls while they
  are switched on.

**What changed (client DLL):**
- **Never Wanted** keeps the player options loop running every frame only for
  players allowed to use it (`vMenu.PlayerOptions.NeverWanted`). For everyone
  else it never did anything.
- **Vehicle loops:**
  - The vehicle switch check (`GeneralTasks`) checks every 100 ms, seated or
    not. Enhanced checks every 250 ms.
  - Vehicle options rest 100 ms in a vehicle too, unless an option is on that
    the game needs every frame: vehicle god mode, freeze, keep clean, torque,
    no helmet or infinite fuel. `VehicleOptionsEveryFrame()` lists them, with
    the same permissions. A waiting vehicle switch also keeps it every frame,
    so the switch settings apply on the next pass. Those are the power
    multiplier, no siren, turbulence and boat anchor.
- **Key bindings instead of reading keys every frame** (as Enhanced). Players
  can change them in Settings > Key Bindings > FiveM. The names follow
  vMenu's existing `vMenu:<keymapping id>:…` bindings:

  | Key | Default | Was |
  | --- | --- | --- |
  | Helmet visor (hold 400 ms) | F11 / D-pad right | the game's visor control |
  | Pick up snowball (on release) | G / D-pad left | the game's detonate control |
  | Finger point toggle | B / double tap right stick | the game's special ability control |
  | Expanded minimap (on release) | Z / D-pad down | the game's multiplayer info control |
  | Drift mode (hold) | Left Shift / X (Square) | sprint / jump |
  | Teleport to waypoint | from `vmenu_teleport_to_wp_keybind_key` (168 = F7) | that control |

  - Each handler keeps the conditions the old check had: the option switched
    on in Misc Settings, permission, menu or pause menu closed, faded in, not
    dead, on foot or in a vehicle as before, and not typing for the point key.
  - The point and minimap keys also check that no other script has disabled
    the game control, as when vMenu read the control itself. Key bindings run
    outside the game's control handling, so that check is best effort.
  - Pointing steers its animation every frame only while pointing (a
    `TickSleeper` loop). It waits half a second for the pointing task to
    start before going back to sleep. The old visor/snowball loop is gone.
  - The minimap and drift keys do nothing in noclip, which uses Z and Shift.
    Point, drift and teleport do nothing over the pause menu, and teleport
    does nothing with a menu open.
  - The recording keys stay as they were. They are the game's own replay
    controls, with a hold-and-press combination on controller, and they
    already rest while switched off.
- **Coordinates, location, time and speed** are shown on the web page
  (`storage.html`, `#vmenu-hud`), as Enhanced does, instead of being drawn with
  game calls every frame.
  - `HudOverlayTick` sends what to show at most 10 times a second, and only
    when it changed. It does nothing while every one of these options is off.
  - The text uses the same screen positions as before and the game's own text
    heights, in GTA Chalet (Antonio for the speedometer).
  - It is hidden over the pause menu, fades, player switches and a hidden HUD.
  - Coordinates now always show a decimal point, whatever the player's Windows
    number format.
  - The text is aligned to the top of the game's safe zone, as the old
    `SetScriptGfxAlign` did, so a safe zone under 100% moves it inside, as
    before.
  - With both speeds shown, the game's street name is hidden every frame (in
    `MiscSettings`), as before, because MPH sits where it is.

**Teleport To Coords** (`client/exports.lua`): the X, Y and Z fields start
empty, so pasted coordinates don't have to replace your current position. Your
position only shows as a faint hint.

**What players will notice.**
- New entries in Settings > Key Bindings > FiveM ("vMenu Helmet Visor (hold)",
  "vMenu Finger Point Toggle"…) that can be rebound.
- The visor key on keyboard is F11 now. Before it was the game's own visor
  control.
- **Timing:**
  - Getting into a vehicle applies the switch settings within about 0.2 s.
  - Switching an in-vehicle option off takes up to 0.1 s to undo with the menu
    closed.
- On-screen text looks slightly different: drawn by the browser, not the game.
  Its size may need a small tweak after seeing it in game.

**Expected effect.** Not measured. For a player on foot with every option off,
vMenu's every-frame loops go from 4–5 (release I) to 2: the menu watcher and
the first-frame death check. MenuAPI's toggle tick remains, as in Enhanced. In
a vehicle with every option off, two fewer loops run every frame.

**Validation:**
- **New tests:**
  - `tests/key-bindings`: binding names, default keys and press or release
    timing, and that nothing polls these keys any more. It also runs the
    production point, steering, minimap, drift and teleport handlers against
    fake natives: 26 checks covering every old condition.
  - `tests/hud-overlay`: MiscSettings no longer draws text. It runs the
    production `HudOverlayTick`: rests and hides once while off, sends once per
    change, hidden over the pause menu, permissions, speed only in a vehicle,
    both speeds.
  - `tests/vehicle-rests`: every option the vehicle loop applies every frame
    keeps it every frame, with the same permission. The switch settings ride
    on `SwitchedVehicle`. A mutation self-test drops each helper line in turn.
- **Updated tests:**
  - `tests/frame-costs`: the visor and snowball handlers run against the same
    fakes as before. Nothing runs on frames with no key used, and the pickup,
    snow, walking, menu and visor-hold rules are unchanged.
  - `tests/idle-rests` still compares this build against `build-2026-11-17-3`
    frame by frame for everything left in those loops, Never Wanted included.
  - `tests/client-guards`, `tests/client-fixes` and `tests/client-features`
    follow the moves. The last one checks that the teleport fields start
    empty, and it fails on the old dialog.
- **Browser and runtime checks:**
  - `storage.html` in a browser: the HUD parts land beside the minimap, top
    centre and bottom right, and hide when switched off. The storage page's
    elements are unchanged.
  - `tests/menuapi-compat`: every member the client uses exists in FiveM's
    runtime.
- `tools/ilcompare` against release I's client: only `FunctionsController`
  changed, in the methods above, plus compiler-generated helpers. One local
  function was only renumbered.
- **Independent review.** It found these, all fixed:
  - the coordinates and compass blocks not hiding when switched off (their
    CSS `display` beat `hidden`);
  - the snowball's controller key, which is D-pad left, not right;
  - pointing that could go unsteered;
  - Z in noclip toggling the minimap;
  - point and teleport working over the pause menu;
  - the safe-zone alignment;
  - the street-name hide.

  It also noted that the overlay hides when another resource hides the HUD
  (phone, tablet, Photo Mode). Game-drawn text used to stay.
- All 35 tests in AGENTS.md pass.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `dece98b426477274f47302c1ad2038b2d5a57ba0378b3636c8e6edaa1caf25bd` |
| `build/vMenu/MenuAPI.dll` | `43442a16c92e22bbc37e9fe4db2470de2272bbd92ed760cc75b741888d05b722` (unchanged) |
| `build/vMenu/vMenuServer.net.dll` | `8827cd4c…` (unchanged) |

**Deploy (test):** on top of release I.
1. Check the installed `vMenuClient.net.dll` is release I's (`8219b71e…`).
2. Copy `vMenuClient.net.dll`, `storage.html` and `client/exports.lua`.
3. Run `restart vMenu` and have players reconnect.

**Roll back to release I:** copy back release I's `vMenuClient.net.dll`,
`storage.html` and `client/exports.lua`.

## November 17, 2026 — release I: MenuAPI 4.1.3 and the PSRP Neon skin (client DLL, MenuAPI DLL, `storage.html`, `fxmanifest.lua`, `menuapi/`, `themes/`) — TESTING, branch `perf/menuapi4`

**Status: test build, not verified in game, not on `main`.** It is built on top
of release H (`perf/closed-menu`), so testing it tests release H too. Until it
passes an in-game check, `main` and `build-2026-11-17-3` stay the live build
and the fallback.

**Why.** With a menu open, MenuAPI 3.2.2 draws every visible row, label, banner
and description with game calls every frame, inside vMenu's script time.
MenuAPI 4 draws the menu on the resource's web page (NUI), so vMenu's C# only
reads input. 4.1.3 is the version vMenu Enhanced uses. It also has built in
what this build patched into 3.2.2: its loops stop while every menu is closed,
and its toggle key is a key binding, so the V-key bug can't happen.

**What changed.**
- **MenuAPI 4.1.3**, built from the v4.1.3 source without the `RepositoryUrl`
  attribute (command below), plus one local patch,
  [`tools/menuapi/checkbox-left-toggle-4.1.3.patch`](../tools/menuapi/checkbox-left-toggle-4.1.3.patch):
  left toggles a checkbox, as in 3.2.2. An upstream refactor (`095af43`, 2021)
  dropped that, so in stock 4.x left on a checkbox goes back a menu, and holding
  it can close vMenu. vMenu has about 100 checkboxes.
  - The NuGet package's DLL carries `AssemblyMetadataAttribute`, which FiveM's
    client `mscorlib` doesn't have: the November 17 crash. So that DLL is
    never shipped.
  - `tools/ilcompare` finds every type, field and method identical to the
    NuGet DLL, plus the patch's one new method, `MenuCheckboxItem.GoLeft`. The
    version strings and that attribute are the only other differences.
  - MenuAPI 4 is LGPL-3.0-or-later. It ships as its own DLL, with the license
    texts and a notice in `menuapi/`. The notice names the patch, which is under
    the same license and kept in this repository.
- **`storage.html` is now also MenuAPI's page.**
  - It loads MenuAPI's stylesheets and scripts, and has `<div id="menuapi">`.
  - The old page (ped pack store prompt, import/export) lives in
    `#vmenu-storage`, and every one of its style rules is scoped to it. Before,
    its `*` rule set the font and centring of every element on the page, which
    would have restyled the menu.
  - It only reacts to its own two messages, `showPedPackPrompt` and
    `showImportExport`. Before, any other message opened the import/export
    screen, and MenuAPI messages this page all the time. Nothing sends
    `showImportExport` (that screen was unreachable before as well).
- **`fxmanifest.lua`** adds `menuapi/**/*` and `themes/**/*` to `files`.
- **`menuapi/`**: MenuAPI's web files from the MenuAPI.FiveM 4.1.3 package, and
  its license texts.
- **The PSRP Neon skin**, `themes/psrp-neon.css`, chosen by the owner from four
  previews. It sets MenuAPI 4's theme tokens:
  - a purple-to-blue highlight (`#8a2be2` to `#4a5cff`, the ped pack prompt's
    colours) with white text;
  - dark violet rows, subtitle and description;
  - a light purple subtitle;
  - 10 px corners and a faint purple edge;
  - a matching key hints bar.

  `storage.html` links it after MenuAPI's own stylesheets. Removing that one
  `<link>` line brings back the plain GTA look; no DLL change is needed either
  way. It is written for PSRP and copies none of Enhanced's GPL themes.
- **Client**, only what 4.x no longer has:
  - Menus close when the ped dies, in `MainMenu.OnTick`, as 3.2.2 did. 4.x's
    own close-on-death sits behind its "player is dead" draw gate and never
    runs, so a menu open at death came back after respawn.
  - Spacer headings ("↓ Vehicle Categories ↓" and five others) pad with
    non-breaking spaces. The web page collapses ordinary spaces, which put them
    against the left edge. In a browser they now centre at 47–49% of the row.
  - The menu toggle: `MenuController.EnableMenuToggleKey = false`, set in
    `MainMenu`'s constructor. vMenu opens the menu with its own key mapping, as
    before. `EnableManualGCs` is gone because 4.x has no forced GC.
  - The three in-menu keys, used in five menus, are now FiveM key bindings with
    the same keys (`vMenu/MenuKeys.cs`):
    - Space filters the male, female and other ped lists.
    - Delete deletes an unavailable saved vehicle.
    - Space opens or closes every door in the vehicle mod menu.

    On a controller all three are X / Square, as before. Players can rebind
    them in Settings > Key Bindings > FiveM. The ped filter and the delete key
    still disable the game control on that key while their menu is open, as
    3.2.2 did, so Space doesn't also jump.
  - The character creator's camera hints (turn head, turn character, turn
    camera) are button hints. They are rebuilt when their menu opens and when
    the player switches between keyboard and controller.

**What players will notice.**
- Menus use the PSRP Neon skin. Layout, fonts and the banner (the game's
  `commonmenu` texture, as before) are unchanged. The browser draws the menu,
  so text may be slightly sharper.
- Menu keys are FiveM key bindings, listed in Settings > Key Bindings > FiveM
  ("Menu up", "Menu select"…), and can be rebound.
  - By default: arrow keys, Enter, Backspace or Escape, and the mouse wheel.
    The left and right mouse buttons now select and go back.
  - Other resources that disable game controls no longer block menu
    navigation. vMenu's own blocks (`DontOpenMenus`, `DisableMenuButtons`)
    still apply.
- Take Photo no longer captures an open menu, because the web page isn't part
  of the game image.

**Expected effect.** Not measured.
- **Menu closed:** the same as release H. MenuAPI 4 stops its loops just as
  release H's patch did, and both keep one toggle tick every frame.
- **Menu open:** vMenu no longer draws the rows itself, so `resmon` should show
  noticeably less. Part of that work moves to the browser layer, which
  `resmon` doesn't count.

**Validation:**
- `tests/menuapi-compat` is rewritten for 4.1.3. It checks:
  - the packaged MenuAPI is 4.1.3, and the client was built against 4.1.3;
  - assembly attributes are only types FiveM's `mscorlib` has, and there is no
    `AssemblyMetadataAttribute`;
  - through `tools/ilcompare`, the code is the official release;
  - the only code difference from the official release is the patch's
    `MenuCheckboxItem.GoLeft`, and that method selects an enabled checkbox;
  - it uses the same outside types and members as the official release, apart
    from that attribute;
  - on a machine with FiveM installed, every type and member that MenuAPI and
    the client use exists in FiveM's runtime: 237 and 1,085 members.

  It fails on the NuGet DLL. The MenuAPI build is reproducible: two checkouts
  in different folders give the same hash.
- `tests/frame-costs`: the MenuAPI GC check now requires MenuAPI 4.1.3, which
  has no forced GC.
- Checked by reading the code:
  - **List values.** 4.x copies a list item's values instead of keeping the
    list vMenu passed in. Every list vMenu mutates is filled before its item is
    built.
  - **Text codes.** Every text code vMenu puts in menu text renders the same:
    colours, `~h~`, `~n~`, `~italic~`, `~s~`. The `~INPUT_…~` codes appear only
    in game-drawn help and notifications.
- **The merged page in a browser:**
  - all 43 menu elements keep MenuAPI's font and alignment;
  - with Font Awesome loaded, all 48 elements of the ped pack prompt and the
    import/export panel have the same computed styles as before, and the lock
    icon keeps its icon font;
  - MenuAPI's messages don't open the import/export panel.
- All 32 tests in AGENTS.md pass.
- **Two independent reviews**, one of the change and one of how 4.1.3 behaves
  differently from 3.2.2 everywhere vMenu uses MenuAPI. Fixed from them:
  - the lock icon losing its font to the scoped reset (now `:where()`, zero
    specificity like the old `*`);
  - left on checkboxes;
  - menus left open at death;
  - spacer centring;
  - the controller buttons for the three in-menu keys;
  - hints following a switch between keyboard and controller.

  Found and left as they are:
  - **Another resource disabling every control freezes the menu.** MenuAPI 4
    treats control 360 being disabled as "F8 console open" and ignores keys.
    `DisableAllControlActions` does that; vMenu's Photo Mode does so after
    closing the menus, but the toggle key can reopen them during Photo Mode.
  - **The game's M / pad Select control (244) is disabled while a menu is
    open**, so other resources that read it ignore it then.
  - **vMenu's own back/select calls do nothing while the weapon wheel is
    open**, for example closing a vehicle-only menu when you leave the vehicle.
  - **"Disable Controller Support" does nothing.** It already didn't in
    `build-2026-11-17-3`.
  - **The unreachable import/export panel can't scroll.**
  - **The `↓` arrows in two spacers are garbled in the source** (`â†“`), as
    before.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `8219b71e57a0ce169eced482a4391b50d0fb39e661ef952efef073477bc1142f` |
| `build/vMenu/MenuAPI.dll` | `43442a16c92e22bbc37e9fe4db2470de2272bbd92ed760cc75b741888d05b722` |
| `build/vMenu/vMenuServer.net.dll` | `8827cd4c…` (unchanged) |

`storage.html`, `fxmanifest.lua` and `menuapi/` are text and fonts. Git
normalises text line endings, so compare those by content, not hash.

**Build MenuAPI** from a v4.1.3 checkout
(`51f745da63cc7b98844d5331e5f4317197783b7e`), without Node, which only re-minifies
the web files. Apply `tools/menuapi/checkbox-left-toggle-4.1.3.patch` first
(`git apply`), then:

```
dotnet build MenuAPI/MenuAPI.csproj -c Release -p:MenuApiFlavor=Legacy --nologo -p:Version=4.1.3 -p:IncludeSourceRevisionInInformationalVersion=false -p:GenerateRepositoryUrlAttribute=false "-p:PathMap=<checkout>=/_/MenuAPI"
```

1. Copy `MenuAPI/bin/Release/net452/MenuAPI.dll` to `build/vMenu/`.
2. Copy the web files from the NuGet package
   (`~/.nuget/packages/menuapi.fivem/4.1.3/contentFiles/any/any/menuapi/`) to
   `build/vMenu/menuapi/`.
3. Copy `LICENSE.md` and `LICENSE.GPL.md` from the package to
   `menuapi/MenuAPI-LICENSE.md` and `menuapi/MenuAPI-LICENSE.GPL.md`, and keep
   `menuapi/MenuAPI-NOTICE.txt` up to date.
4. Run `dotnet run --project tests/menuapi-compat`.

**Deploy (test):**
1. Hash the installed `vMenuClient.net.dll` and `MenuAPI.dll`. They should be
   `4fdd0413…` and `7493926e…` (`build-2026-11-17-3`). If not, stop.
2. Check that the installed `storage.html` and `fxmanifest.lua` match
   `build-2026-11-17-3`'s by content. If not, stop.
3. Copy `vMenuClient.net.dll`, `MenuAPI.dll`, `storage.html`,
   `fxmanifest.lua`, and the `menuapi` and `themes` folders.
4. Run `refresh`, then `restart vMenu`, because the manifest changed. Have
   players reconnect.

**In-game check:** everything in release H's list, plus:
- F1 opens and closes the menu in the PSRP Neon skin, including the key hints
  in the bottom right. It navigates with the arrows, Enter, Backspace, Escape
  and the mouse wheel.
- Left and right both toggle checkboxes; left on a plain button goes back.
- Spacer headings ("↓ Vehicle Categories ↓") are centred.
- Dying with a menu open closes it.
- Long lists scroll, list values change with left and right, and checkboxes
  and sliders work.
- The colour and opacity panels work in MP ped customization, and so do the
  weapon and vehicle stats.
- The character creator's camera hints show, its back-button guard works,
  and "Exit Without Saving" works.
- Space filters the ped lists without jumping, Delete removes an unavailable
  saved vehicle, and Space toggles the doors in the mod menu.
- The right-aligned menu option works.
- The ped pack store prompt shows and closes, and nothing else pops up on the
  page.
- Typing in chat or an ox_lib dialog doesn't move the menu.
- `resmon` with the menu open is lower than on release H.

**Roll back:** copy back all four of `build-2026-11-17-3`'s files
(`vMenuClient.net.dll`, `MenuAPI.dll`, `storage.html`, `fxmanifest.lua`),
delete `menuapi/` and `themes/`, `refresh` and `restart vMenu`. These go
back together:
unlike release H, this client doesn't run with MenuAPI 3.2.2, and MenuAPI 4
needs the new page. To keep release H only, use release H's two DLLs with
`build-2026-11-17-3`'s `storage.html` and `fxmanifest.lua`.

## November 17, 2026 — release H: idle rests (client DLL, MenuAPI DLL) — TESTING, branch `perf/closed-menu`

**Status: test build, not verified in game, not on `main`.** It lives on the
`perf/closed-menu` branch until it passes an in-game check. Until then, `main`
and the `build-2026-11-17-3` package stay the live build and the fallback.

**Why.** vMenu Enhanced (upstream's rewrite) uses 2–3 times less client CPU
than this build. It runs on the same C# runtime, so the difference is in how
its loops are organised:
- A loop only exists while its feature is on, and most of them check every
  100–500 ms instead of every frame.
- MenuAPI 4.x stops its own loops while every menu is closed.

FiveM's C# runtime starts every registered tick handler again each frame once
its last run has finished. October's profile put that start at about 3 µs per
handler, more than most of these handlers' own checks. With the menu closed and
every option off, this build still started about 20 handlers a frame: about 15
of vMenu's and 5 of MenuAPI's.

**What changed.** Client DLL and MenuAPI DLL only; the server DLL and every Lua,
config and HTML file are unchanged.

- **Idle rests.** A loop with nothing that must happen every frame ends its run
  with a 250 ms wait instead of being started again next frame, or 100 ms for
  what follows getting into a vehicle or dying. While a loop's feature is on,
  or any menu is open, it runs every frame exactly as before, so switching an
  option takes effect on the next frame. These loops rest:
  - player options;
  - weapon options;
  - vehicle options (on foot);
  - voice chat (off);
  - the MP editor camera;
  - the entity outlines;
  - the shared per-frame checks;
  - the misc settings loop (with every display, key and camera option off);
  - the recording keys (when off).

  A rest ends early when a menu opens (`vMenu/IdleRest.cs`). The menu watcher
  in `MainMenu.OnTick` still runs every frame and signals it, so on the frame
  after any menu appears every resting loop is back to every frame. That
  includes the character creator returning from its "exit without saving"
  prompt: its back button guard, camera and movement lock are back on the next
  frame, as before.

  These stay every frame:
  - the respawn restore, which saves your appearance and weapons on the first
    frame of death;
  - the menu watcher;
  - the vehicle switch check while you are getting in or seated.
- **Noclip and prop placement** take their loops off the tick list while
  they're off. `SetNoclipActive` and `SpawnEntity` put them back, so they start
  on the next frame, as before (`vMenu/TickSleeper.cs`).
- **Vehicle switch check** (`GeneralTasks`): every frame while you are
  getting in or seated, as before. On foot it rests 100 ms, which is shorter
  than getting in.
- **Radar preference and expanded radar.** A new `RadarSettings` loop applies
  the pause menu's radar preference and reads its expanded-radar option 4 times
  a second with every menu closed, and every frame while one is open;
  `MiscSettings` used to do both every frame. Hide Radar is still enforced
  every frame.
- **Recording keys:** the three keys are read first. The pause, fade, menu and
  input-mode checks only run when one is used.
- **Clothing glow:** the pass over every player in scope (about five game calls
  each) runs once a second instead of every 25 ms. Animated glows are still
  updated every 25 ms, and your own decorator is still checked every 25 ms, so
  the join re-sync's 100 ms style flip still gets through. A change to your own
  style or ped scans on that same pass, so your glow follows at once.
- **MenuAPI** gets a second local patch,
  [`tools/menuapi/idle-while-closed.patch`](../tools/menuapi/idle-while-closed.patch),
  on top of the toggle guard:
  - When every menu is closed, its draw and button ticks unload the menu
    textures and the button scaleform once, then take themselves off the tick
    list. `Menu.Visible` puts them back when a menu opens, so the menu draws on
    the next frame, as before.
  - The onscreen-keyboard watcher checks 10 times a second while every menu is
    closed.
  - The vMenu client uses no new MenuAPI API, so either DLL can be rolled back
    on its own.

**What players may notice: nothing, by design.** The only timing changes:
- Being warped straight into a vehicle by another script starts the in-vehicle
  options (vehicle god mode, torque) and the vehicle switch settings (power
  multiplier, no siren, turbulence, anchor, personal vehicle) up to 100 ms
  later. Getting in normally starts them while the door opens.
- Becoming an animal ped with every menu closed (the default-character
  restore) blocks first person up to 100 ms later.
- With every menu closed, the radar follows a change to the pause menu's
  radar options, or another resource showing it, within 250 ms.
- Another player's glow style, or a new player's glow, shows within a second.

**Not changed.**
- The snowball and visor keys: two key reads a frame, already cut in release G.
- Player blips: they already rest at 2 Hz when off.
- Time and weather sync: both are off on this server.
- Keys stay as polled controls, not FiveM key mappings. Key mappings ignore
  other scripts that block those controls (for example while cuffed), and they
  would save only a few microseconds more.

**Expected effect.** Not measured; estimated from the October profile:
- With the menu closed, roughly 3–5 handlers start per frame instead of about
  20.
- vMenu should land around 0.1 ms per frame instead of about 0.2 ms.

Measure with `resmon 1`, or `profiler record 1000` and `profiler view`, standing
still with the menu closed, before and after.

**Validation:**
- New test `python tests/idle-rests/test_idle_rests.py`. It compiles this
  build's MiscSettings, RadarSettings, MiscRecordingKeybinds, PlayerOptions,
  WeaponOptions and VoiceChat, and `build-2026-11-17-3`'s versions of the same
  methods (read from git). It runs both against one fake game for 19,178 frames:
  - each option switched on two frames after the menu opens, then used with
    the menu closed;
  - then 120 random cycles of option sets, keys, vehicle and heli changes,
    weapon swaps, wanted levels, respawns, the waypoint, auto pilot cancels,
    voice, controller and keyboard modes, and radar changes by other resources.

  Everything the two versions do to the game matches on every frame. Persistent
  settings re-applied 4 times a second (radar preference, expanded radar, voice
  off) may lag by up to 600 ms after another resource changes them, and must
  then match again. The test also checks that each loop rests while idle and
  still runs as often as before while its option is on. It fails if any of
  these is removed:
  - the every-frame condition for the point keys, super jump or no-reload;
  - the early end of a rest when a menu opens.

  It runs the real `IdleRest` and checks that it never lists more than one
  rest per loop. The rests are tracked in its own list rather than through
  `Task.WhenAny`, so nothing piles up on a long stretch with the menu closed. It also tests `TickSleeper` against a model of the runtime's tick list
  (`code/client/clrcore/BaseScript.cs`): asleep costs nothing, a woken handler
  starts on the next frame, and it never runs twice at once.
- `tests/frame-costs` is extended:
  - the shared per-frame checks rest when idle, and still run every frame in a
    vehicle, as an animal (noticed within 100 ms) and in the MP editor (from
    the frame after it reopens, mid-rest);
  - the glow scans every player once a second, picks up a new glowing player
    within a second, skips one who has gone, applies your own new style on the
    next pass, and still rewrites your decorator through the join re-sync;
  - `RadarSettings` is wired.
- `tests/menuapi-compat` now:
  - allows exactly one new runtime member, `BaseScript::remove_Tick`, and checks
    that it exists in this machine's FiveM `CitizenFX.Core` runtime;
  - checks that `Menu.Visible` wakes the menu ticks and `ProcessMenus` puts
    them to sleep;
  - fails on the previous MenuAPI.
- MenuAPI rebuilt from a clean v3.2.2 checkout (`8787b79`) with both patches is
  IL-identical to the packaged DLL. Its hash differs only by the checkout path.
- `tools/ilcompare` against `build-2026-11-17-3`:
  - client: only the methods above, plus compiler-renumbered local functions;
  - MenuAPI: `Menu.set_Visible`, `MenuController` (constructor,
    `SleepMenuTicks`, `WakeMenuTicks`, `ProcessMenus`,
    `MenuButtonsDisableChecks`) and two new fields.
- All 32 tests in AGENTS.md pass.
- Problems found and fixed before this build:
  - **By an independent review of the change:**
    - the creator's back button guard returning late after the exit prompt;
    - a 100 ms late death snapshot;
    - the radar showing for up to 250 ms after Hide Radar was unticked;
    - your own glow change taking up to a second.

    It found nothing wrong with the MenuAPI patch or `TickSleeper`.
  - **By the side-by-side test:** recording keys and entity outlines starting
    up to 250 ms after being switched on.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `ca395987150938132a901f4024e182d2709e3fc0003ceaddb7f446ab26814b99` |
| `build/vMenu/MenuAPI.dll` | `67336ff29bfa804e63d1807f1ad930ba9fe9a2b09ef363af648ac896244c3d86` |
| `build/vMenu/vMenuServer.net.dll` | `8827cd4c…` (unchanged) |

**Build MenuAPI** from a v3.2.2 checkout (`8787b7916204e605ed0827da7a93c4462282fa09`):
1. Apply `tools/menuapi/skip-disabled-legacy-toggle.patch`.
2. Apply `tools/menuapi/idle-while-closed.patch`.
3. Run:

   ```
   dotnet build MenuAPI/MenuAPI.csproj -c "Release FiveM" --nologo -p:IncludeSourceRevisionInInformationalVersion=false -p:GenerateRepositoryUrlAttribute=false "-p:PathMap=<checkout>=/_/MenuAPI"
   ```

4. Run `dotnet run --project tests/menuapi-compat`.

**Deploy (test):**
1. Hash the installed `vMenuClient.net.dll` and `MenuAPI.dll`. They should be
   `4fdd0413…` and `7493926e…` (`build-2026-11-17-3`). If not, stop.
2. Copy only those two files.
3. Restart `vMenu`, and have players reconnect.

**In-game check:**
- F1 opens and closes vMenu instantly, and the menu draws and navigates
  normally;
- noclip toggles instantly and moves as before;
- prop placement follows the camera;
- player options (god mode, super jump, invisible, never wanted), weapon
  options (no reload, unlimited ammo, parachutes) and vehicle options (god
  mode, torque, health text, high beams on honk) work;
- coordinates, location, time and speedometer show;
- the point (B), teleport-to-waypoint, radar (Z), drift and recording keys
  work;
- respawning restores your appearance and loadout;
- clothing glow animates for you and others;
- `resmon` shows lower vMenu ms with the menu closed.

**Roll back:** copy back `build-2026-11-17-3`'s two DLLs (`4fdd0413…`,
`7493926e…`), or just the one at fault. Each works with the other build's
version of the other file.

## November 17, 2026 — MenuAPI rebuild crashed clients (MenuAPI DLL)

**Do not deploy `build-2026-11-17-2`'s MenuAPI.dll (`c153b57b…`).** It has the
right V-key guard, but it crashes FiveM clients.

**What broke.** The .NET 9 SDK turns MenuAPI's `RepositoryUrl` project property
into an `AssemblyMetadataAttribute("RepositoryUrl", …)` on the assembly. Stock
MenuAPI 3.2.2 doesn't have it. FiveM's client `mscorlib` doesn't define that
type.
- vMenu's `dynamic` calls go through `Microsoft.CSharp`'s binder, and that
  binder reads the attributes of every loaded assembly.
- So every such call threw `TypeLoadException: Could not resolve type with token
  0100000e` (TypeRef `0x0100000e` in that DLL is the missing attribute type).
  The first one seen is `CanDoInteraction` (CommonFunctions.cs:46).
- Seen on live after deploying it:
  - The ped was invisible after spawn: the first-spawn appearance restore stopped
    at the weapon loadout's `CanDoInteraction`.
  - Noclip and Saved Characters failed.
  - Then FiveM crashed at `mono-2.0-sgen.dll+2660F5`.
- The client logs show no such error in any session before the swap.
- The earlier `ilcompare` review saw this attribute and treated it as harmless.

**Fix.** The same MenuAPI v3.2.2 source and
[the legacy toggle patch](../tools/menuapi/skip-disabled-legacy-toggle.patch),
built with `-p:GenerateRepositoryUrlAttribute=false`:

```
dotnet build MenuAPI/MenuAPI.csproj -c "Release FiveM" --nologo -p:IncludeSourceRevisionInInformationalVersion=false -p:GenerateRepositoryUrlAttribute=false "-p:PathMap=<checkout>=/_/MenuAPI"
```

A rebuild without the new flag reproduced `c153b57b…`'s IL exactly, so the build
setup matches the earlier one. The new flag is the only difference.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/MenuAPI.dll` | `7493926e44724b2f7983e6dd1bc07106dfbd3c403acd5d5afdedbabd1f69db0a` |
| `build/vMenu/vMenuClient.net.dll` | `4fdd0413…` (unchanged) |
| `build/vMenu/vMenuServer.net.dll` | `8827cd4c…` (unchanged) |

**Validation:**
- `tools/ilcompare` against `c153b57b…`: only the `RepositoryUrl` attribute is
  removed.
- Against stock 3.2.2 (`8851e2ad…`): the assembly attributes are identical, and
  the only code change is the guard in `ProcessToggleMenuButton`. The compiler
  also orders the state-machine setup in `Menu.Draw` and
  `MenuButtonsDisableChecks` differently; vMenuClient builds with the same SDK
  and runs fine on live.
- **New test**, `dotnet run --project tests/menuapi-compat`. It checks that:
  - the packaged MenuAPI references no external type or member that stock 3.2.2
    doesn't;
  - its assembly attributes are stock's;
  - the guard is present;
  - when FiveM is installed on the machine, every type that MenuAPI.dll and
    vMenuClient.net.dll reference exists in FiveM's own runtime assemblies.

  It fails on `c153b57b…` with exactly `AssemblyMetadataAttribute`, and passes
  on this build.
- **Verified in game on November 17, 2026**, with `7493926e…` deployed on live:
  - the ped is visible after spawn;
  - F1 opens and closes vMenu, and V only switches the camera;
  - no crashes.

**Deploy:**
1. Hash the installed `MenuAPI.dll`. It should be `c153b57b…` (the crashing
   build) or `8851e2ad…` (stock, if it was rolled back). If it is neither, stop.
2. Copy only `MenuAPI.dll`.
3. Restart `vMenu`, and have players reconnect.
4. Check in game:
   - your ped is visible after spawning;
   - F1 opens and closes vMenu, and V only switches the camera;
   - noclip and Saved Characters work;
   - F8 shows no `TypeLoadException`.

This package is tagged `build-2026-11-17-3`.

## November 17, 2026 — backport the full legacy menu toggle guard (MenuAPI DLL)

> Superseded: this build's `MenuAPI.dll` (`c153b57b…`) crashes clients. Use
> `build-2026-11-17-3` (see above).

Stock vMenu 3.8.7 uses [MenuAPI 3.2.4](https://github.com/TomGrobbe/vMenu/pull/584).
With the legacy toggle set to `-1`, it
[returns before polling either input path](https://github.com/TomGrobbe/MenuAPI/blob/v3.2.4/MenuAPI/MenuController.cs#L450-L464).
The earlier PSRP patch guarded only keyboard input and the `V` report persisted.
This backport applies the same early exit to MenuAPI 3.2.2. The explicit vMenu
key mappings, including the `F1` default, are still registered by vMenu. The
legacy controller hold is skipped with the rest of the legacy handler.

Rebuild from [MenuAPI v3.2.2](https://github.com/TomGrobbe/MenuAPI/tree/v3.2.2)
at `8787b7916204e605ed0827da7a93c4462282fa09` with .NET SDK 9.0.321 and
[the legacy toggle patch](../tools/menuapi/skip-disabled-legacy-toggle.patch).
Apply it with `git apply` inside that checkout. Build with
`dotnet build MenuAPI/MenuAPI.csproj -c "Release FiveM" --nologo -p:IncludeSourceRevisionInInformationalVersion=false`
and map the checkout path to `/_/MenuAPI` with `-p:PathMap`. The output is
`MenuAPI/bin/Release FiveM/net452/MenuAPI.dll`.

IL comparison against an unpatched build with the same SDK limits the change
to `ProcessToggleMenuButton`. Local tests cover skipping both input paths for
unset or out-of-range controls and preserving both paths for valid controls.
The frame-cost, client-feature, vehicle-convenience and face-selection tests
passed. In-game verification is pending.

Verify the installed `MenuAPI.dll` against the earlier November 17 release
(`6ac2d121fc226bbb1df8d46535484b71849b20db661d6758522ef79cca918a59`).
If the keyboard-only patch was rolled back, the recorded stock hash is
`8851e2ad462e2a7b8149802fd4a56522067e3ac6b122fcac4641d61800f1a472`.
Replace only `MenuAPI.dll` and restart `vMenu`. On the affected artifact,
verify `V` switches the camera, `F1` opens and closes vMenu, and any configured
controller binding works. This package is tagged `build-2026-11-17-2` to
preserve the first same-day release.

## November 17, 2026 — camera key opening vMenu (MenuAPI DLL)

Players reported that `V` opened vMenu alongside `F1`. The saved vMenu
binding on the affected client was `F1`. vMenu already sets MenuAPI's old
keyboard control to `-1`, but MenuAPI 3.2.2 passed that value to the game
when checking whether to open or close a menu. The patch skips those checks
and skips disabling the input when the configured control is negative. The
explicit vMenu binding and MenuAPI's controller handling remain available.

Build from the [MenuAPI v3.2.2 tag](https://github.com/TomGrobbe/MenuAPI/tree/v3.2.2)
at `8787b7916204e605ed0827da7a93c4462282fa09` with .NET SDK 9.0.321. Apply
[the keyboard toggle patch from the first November 17 release](https://github.com/Georgie15/vmenu-oxlib/blob/build-2026-11-17/tools/menuapi/disable-legacy-keyboard-toggle.patch)
with `git apply` inside that checkout, then run
`dotnet build MenuAPI/MenuAPI.csproj -c "Release FiveM" --nologo -p:IncludeSourceRevisionInInformationalVersion=false`.
The output is `MenuAPI/bin/Release FiveM/net452/MenuAPI.dll`. The shipped
build maps the checkout path to `/_/MenuAPI` with `-p:PathMap`.

`tools/ilcompare` against an unpatched build with the same SDK shows only
changes in `ProcessToggleMenuButton`'s state machine. Local checks exercised
ignored negative controls, ordinary keyboard toggles and controller holds.
The frame-cost, client-feature, vehicle-convenience and face-selection tests
also passed. The in-game check is pending.

Verify the installed `MenuAPI.dll` SHA-256 is
`8851e2ad462e2a7b8149802fd4a56522067e3ac6b122fcac4641d61800f1a472`, then
replace only `MenuAPI.dll` with the package copy and restart `vMenu`. In
FiveM, verify `V` changes the camera with the menu closed, `F1` opens and
closes vMenu, and the controller opens it with the configured shortcut.

## November 16, 2026 — double-tap vehicle locking ignores tackle input (client DLL)

The horn shortcut keeps its 500 ms double-tap window. The configurable
`vMenu PV Lock/Unlock` binding now uses the same window. Each shortcut counts
its own taps, and a shared `E` binding toggles the car only once per pair.
`Shift+E` takes priority even on a miss or during cooldown. It invalidates any
pending first tap. After releasing Shift, two fresh `E` taps can toggle the
lock immediately, including during recovery. The shortcuts use the horn and
lock status message for feedback.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `4fdd0413a9c59e9748246273f2b4a6adc338be1e8511601597faab214cd134f0` |
| `PSRP_Framework/client/cl_tackle.lua` (LF as committed) | `cb96f45a3bde87642c5c2152683b4b86bd64d3c215abc7390ff7defb6b928dd1` |

This package uses `build-2026-11-16-3` to preserve the earlier same-day tags.
The client build, IL comparison, vehicle convenience, client feature, client
guard, client fix, and both Framework tackle tests passed locally.

Deploy this client DLL with `PSRP_Framework/client/cl_tackle.lua` from
PSRP-Development commit `fc6b47598b7793d407d951ab43c94e14c36c077c`. Check the
installed files against the previous November 16 entry below, then replace
those two files and restart both resources.

In FiveM, check one tap and then a double-tap with `E` as the horn shortcut.
Bind the mapped lock shortcut to `E` too and verify a double-tap still toggles
the car once. Try a single `E`, then `Shift+E`, then another single `E`.
The car should stay in its original lock state until two fresh plain taps.
Repeat when the tackle misses and while it is on cooldown. The in-game check
is pending.

## November 16, 2026 — tackle takes priority over vehicle locking (client DLL)

When the tackle chord and vMenu lock share `E`, the Framework records the input
frame even on a miss or during cooldown. vMenu waits one frame for that input
to resolve and checks it before toggling. The horn shortcut also checks the
original press, so a tackle input cannot count toward a double-tap. Both
shortcuts are suppressed during a tackle and recovery. A fresh `E` press
can lock or unlock after recovery, including during the remaining cooldown.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `75af8685c13ed4c950de61239963a1b5b7806e0a1c3ecaf1a2ea7e9bbe940edb` |
| `PSRP_Framework/client/cl_tackle.lua` (LF as committed) | `5ef47bea629c61512b60eef708f01dfbad3a6b42a7bbf2c3113fb3d8e25b3793` |

This package uses `build-2026-11-16-2` to preserve the original same-day tag.
Built with .NET SDK 9.0.321. The client IL comparison shows only the lock
paths and optional Framework bridge, plus assembly metadata. The vMenu
client feature, vehicle convenience, client guard, and client fix tests passed,
as did both Framework tackle Lua specs.

Deploy this client DLL with `PSRP_Framework/client/cl_tackle.lua` from
PSRP-Development commit `6928c525509d4bd6c5b46c822bdc3d7cfbe52104`. Check the
installed DLL against the previous November 16 entry below. The previous
Framework script SHA-256 is
`fd1f47664ba7a26b2226596f4ea9c7dfc1013f4bc39bc9c45b4ea87a58eaf1e6`
(LF as committed). Restart both resources after replacing those files.

In FiveM, bind lock to `E` and test beside a personal vehicle. Try `Shift+E`
with a target, with no target, and during cooldown. Spam the key through the
dive and get-up and verify the car stays in its original lock state. Verify a
fresh `E` press works after recovery, then repeat with the horn double-tap.
The in-game check is pending.

## November 16, 2026 — personal vehicle lock shortcuts (client DLL)

The `vMenu PV Lock/Unlock` binding and horn double-tap both used the key-fob
animation, which clears the ped's tasks, forces a turn, and attaches a prop.
Those actions can interrupt the PSRP_Framework `Shift+E` tackle dive and
ragdoll. Both shortcuts now use the vehicle horn and lock status message for
feedback. They share one lock operation, so presses received while it is
running are ignored. A shared `E` binding can still toggle the car during a
tackle. This fix ships in vMenu alone.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `543003acae292219c9e3667d2ebe06cc93961faecff3370af5e48188efd0da79` |

Built with .NET SDK 9.0.321. `tools/ilcompare` shows the lock shortcut and
its callers plus the assembly version metadata. The vMenu client feature,
vehicle convenience, client guard, and client fix tests passed, as did both
PSRP_Framework tackle Lua specs.

Before deploying, verify the installed `vMenuClient.net.dll` against the
November 9 entry below. Replace only that DLL and restart `vMenu`.
In FiveM, bind `vMenu PV Lock/Unlock` to `E`, select a
personal vehicle, and verify lock/unlock and horn feedback on foot. Then
tackle another player beside the vehicle with `Shift+E` and repeatedly press
`E` through the dive and recovery. Check that both players recover normally
and that the car still toggles once per completed operation. Repeat with the
horn double-tap shortcut. The in-game check is pending.

## November 9, 2026 — clothing saved by collection (client DLL, Lua, config)

Saved uniforms used to be global drawable numbers, which move whenever any
clothing pack is added, removed or repacked (the proposed FIB revamp would do
all three). Every vMenu feature that saves clothing now stores collection
references and resolves them through one policy. That covers Saved Outfits,
MP characters (spawn, edit, default and last character, the after-death
snapshot), Saved Peds and all three outfit-code paths. Old saves are read with
a captured pre-change baseline and upgraded on join, with backups. Ambiguous,
retired or replaced pieces are reported and left off, never substituted. The
design, capture, migration, FIB rollout and rollback steps are in
[clothing-refs.md](clothing-refs.md).

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `6d3e33fff3252b3916163c1853dd0088a8b440198d80fdcea18a7164ce458f27` |
| `build/vMenu/vMenuServer.net.dll` | `8827cd4c4bd6e7862322458a3ed07ccb86b2bad89f51c41ae8b25037c3bdca2f` (unchanged) |
| `build/vMenu/config/clothing-layouts.json` (new, LF as committed) | `b8384ce6589b1c5772d74e17483ef3bf18ed964f3c9f37585d9aa05095f53c3f` |
| `build/vMenu/server/clothingCapture.lua` (new, LF as committed) | `47c66ec2f9c47d548239ade169b15b094af702d65da73e11c71cd624221cc63f` |

Also changed: `fxmanifest.lua` (ships the layouts file), `client/outfitCodes.lua`
(codes resolved by the C# side), `server/oufitCodes.lua` (validates v2 codes,
returns the creation time), and `client/backups.lua` and `server/backups.lua`
(back up upgrade originals). `config/permissions.cfg` gains a commented
`vMenu.ClothingCapture` example. The `vmenu_outfits` table and existing code ids
are unchanged.

The client DLL was built with SDK 9.0.320, because 9.0.123 is no longer
installed. `tools/ilcompare` against the package also shows newer-compiler
output in seven untouched methods: iterator `Dispose`, branch offsets and
`GetAllVehicleMods`. Building unchanged `HEAD` with 9.0.320 gives the same
seven. Against that same-SDK `HEAD` build, the only differences are the new
clothing types, `MpPedCustomization`'s clothing paths, `SavedOutfit`, Saved
Peds in `CommonFunctions`, and the new fields on `MultiplayerPedData` and
`PedInfo`. All 30 suites in `AGENTS.md` pass. Two are new:
`tests/clothing-refs` (144 checks) and `tests/clothing-layouts` (36 checks).

**Deploy:** check each live file against
`git show build-2026-11-05:<path>` (ignoring line endings) first. Then copy the
DLL, `fxmanifest.lua`, the four changed Lua files, `clothingCapture.lua`,
`clothing-layouts.json` and `permissions.cfg`, and restart vMenu. With the
shipped layouts file (no baseline yet), old saves behave as before and nothing
is rewritten; new saves get references. Then do the pre-swap capture in
clothing-refs.md and deploy the resulting `clothing-layouts.json` **before any
clothing pack changes**.

**Runtime acceptance (not done):** see "Needs a FiveM test server" in
clothing-refs.md.

## November 5, 2026 — Take Photo crash (client DLL)

Recording > Take Photo used to start capture, ask GTA to save the image and
free the photo buffer in the same frame. Capture and save both complete on
later frames. vMenu now waits for a successful capture before requesting the
save and keeps the buffer allocated until the save finishes. The button accepts
one request at a time and reports whether it saved to the Pause Menu gallery.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `23a83de485ae760cc8630f809d6f659232644300a1c5f60c33dce22d9fef09ad` |
| `build/vMenu/vMenuServer.net.dll` | `8827cd4c4bd6e7862322458a3ed07ccb86b2bad89f51c41ae8b25037c3bdca2f` |

The client DLL was built with the Windows .NET SDK 9.0.123. `tools/ilcompare`
found changes confined to `Recording` and the assembly version's commit hash.
All 28 suites in `AGENTS.md` passed. The Take Photo regression runs the menu
with stubbed natives and checks frame order, repeat presses and failed capture
or save requests. It fails against the previous same-frame implementation.

1. Hash the live `vMenuClient.net.dll`. Proceed only if it matches
   `650000eb17e8f6e477df8d6bb8d9d5d889fa38d641cd0831638b100a53b51e30`.
2. Copy the packaged `build/vMenu/vMenuClient.net.dll` over the live DLL and
   restart vMenu.
3. In FiveM, use Recording > Take Photo, then Open Gallery and verify the new
   image appears. Try again with quick repeat presses. In-game verification
   is pending.

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
| `build-2026-10-21-f` | `625fada` | F: saved-car prompt, outfits, ped favourites and preview, auto-lock, car keys, photo mode, what's new |
| `build-2026-10-21-g` | the release G commit | G: frame-cost cuts (client DLL only; see its section) |

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

## October 21, 2026: frame-cost release G (client DLL)

The owner's FiveM profile (`profiler record`, 1,000 frames) was taken on a
client running release E, not the pre-edit build: the pre-edit DLLs all run a
once-a-second `GcTick`, which never appears; `DeathNotifications` runs 4 times a
second (release E); and F's auto-lock tick is absent. It showed vMenu at
0.33 ms per frame:
- 0.164 ms: vMenu's own tick code
- 0.031 ms: MenuAPI's tick code
- 0.071 ms: the C# scheduler starting or resuming about 25 tick functions a frame
- 0.055 ms: the C# runtime's fixed cost

Release G cuts the per-frame work in vMenu's code without changing what players
see. Client only: the server DLL is release B's.

**Changes:**
- **Player blips** (when blips are on): the full refresh (creating and removing
  blips, sprite, colour, name, category, heading indicator, display and your own
  sprite decorator) runs 10 times a second instead of every frame. Rotation is
  still set every frame for the blips the last refresh rotated, so a turning
  player's blip turns smoothly. Before, the refresh made about 20 game calls per
  player in scope and two per player elsewhere on the server, every frame; the
  test scenario goes from about 120 calls a frame to about 10 between refreshes.
- **Snowball and visor keys:** the two keys are read first. The six state checks
  and the `vmenu_enable_snow` convar read now happen only when one is used, not
  every frame.
- **Clothing glow:** waits its 25 ms with one `Delay(25)` instead of waking every
  frame until 25 ms had passed. The glow is the same.
- **One shared per-frame tick** (`PerFrameChecks`) replaces four ticks: vehicle
  health text, flash high beams on honk, the MP editor movement lock and the
  animal-ped first-person block. Each keeps its permission or setting, and each
  is caught separately so one failing doesn't stop the others. The movement lock
  and the animal block now also apply on the odd frame the old ticks skipped while
  restarting.
- **Fewer game calls:**
  - `PlayerPedId()` (one call) replaces `Game.PlayerPed` (two calls) in general
    tasks, player options, vehicle options, weapon options, the MP editor camera
    and the death check.
  - On foot, your last vehicle is looked up only while vMenu has a vehicle
    hidden.
  - Unlimited parachutes use the parachute's hash (`WeaponHash.Parachute`,
    0xFBAB5776) instead of hashing `"gadget_parachute"` every frame.
- **MenuAPI's forced garbage collection is off.** MenuAPI called `GC.Collect()`
  every 60 s while no menu was open (`EnableManualGCs`, on by default), a likely
  periodic hitch. Release A removed vMenu's own one; this is the second.

**Not changed:**
- MenuAPI's own per-frame work (about 0.03 ms: texture-dictionary checks and
  key handling while the menu is closed). It needs a fork of the library.
- The duty-blip heartbeat (every 5 s) keeps its timing.
- The C# runtime's fixed 0.055 ms.

Expected effect: about 0.2 ms per frame instead of 0.33 under the profiler. This
has not been measured; profile again to confirm.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `2d0178c6cdea02dfbf88378b0336d382fe43a0150679821b796073f808d3a02e` |
| `build/vMenu/vMenuServer.net.dll` | `003e0932bed3ee2195f052e292b8f5d63e2f30fd8beace5ce46e728daa5c66e9` (unchanged) |

**Validation:**
- `tools/ilcompare` against release F's client DLL shows only the members above:
  `PlayerBlipsControl` and `RotatePlayerBlips`, `AnimationsAndInteractions`,
  `PlayerClothingAnimationsController`, `PerFrameChecks` with the four checks it
  calls, `SetupTickFunctions`, `GeneralTasks`, `PlayerOptions`, `VehicleOptions`,
  `WeaponOptions`, `ManageCamera`, `RestorePlayerAfterBeingDead` and the
  `MainMenu` constructor. Everything else is renumbered compiler-generated local
  functions.
- New test `python tests/frame-costs/test_frame_costs.py`. It runs the production
  code against fake natives, frame by frame, on a scheduler that restarts and
  resumes ticks the way FiveM's C# runtime does:
  - Blips: the throttled code against the same code with the throttle off (the
    old every-frame behaviour), through players joining, leaving, entering a
    heli, coming into range, the map opening, the duty list changing and blips
    going off and on. They match at every refresh, refreshes come every 100 ms,
    rotation follows every frame in between, and other resources' blips are never
    touched.
  - Snowball and visor keys, clothing glow (one wake-up per pass), and the shared
    tick (every check every frame, errors isolated).
  - Twelve deliberate breakages of these behaviours are each caught.
- A one-off run of release F's code against G's on the same fakes:
  - identical blips: every frame with the throttle off, and at every refresh with
    it on
  - identical snowball and visor results over 20,000 random frames
  - identical glow writes at 7, 16 and 33 ms frames, with a quarter of the
    wake-ups at 7 ms frames, half at 16 ms and the same at 33 ms
  - identical health text and high beams
  - the movement lock and the animal block identical, or applied on about 45
    more of 20,000 frames, never fewer
- `tests/client-guards`: its weapon-options fake gains `PlayerPedId`.
- All twenty-five tests pass. The client builds with 0 warnings.
- No FiveM runtime testing was done.

**Deploy** (on top of release F, or with the A-F steps in "releases B-E merged"):
1. Hash the live client DLL. It must be release F's
   (`38bac5d98528d7a5663fd7adee888af6ef9fb62b9cb1d7fc61ada6d48f1e635d`). A server
   still on release E (`8b05f1f4…`) needs release F's files and steps too.
2. Copy `vMenuClient.net.dll`.
3. Restart the resource.

**Runtime acceptance:**
- Blips on, with other players around: their blips appear, switch to a vehicle
  sprite when they get in a car, turn smoothly as they turn, leave the minimap
  when they're far and show on the pause-menu map. On-duty players keep the duty
  blip. Turn blips off: vMenu's blips go.
- With snow on, pick up a snowball (G while standing still). Hold the visor key
  on a bike helmet. Neither works with the menu open.
- Illuminated clothing: fade and flash look the same as before.
- Vehicle health text, high beams on honk, no walking in the MP character editor,
  and no first person as an animal.
- Run `profiler record 1000` in the same spot as before, then `profiler saveJSON`,
  and compare vMenu's time.

## October 23, 2026: vehicle spawner "on cooldown" while loading (client DLL)

Players got "Vehicle spawner is on cooldown." on every click, on foot, with no
vehicle appearing. The cooldown interval wasn't the cause. The spawn gate showed
that message for two different states: a request still in progress, and the
interval after a vehicle was created. A request is in progress before any
vehicle exists while it streams the model (up to 15 s), asks CAD for an identity
ticket (up to 10 s) and probes the ground (up to 1 s). Add-on models download on
first use and can take seconds; a broken one never loads. Nothing showed it was
loading, so every further click said "on cooldown", and a model that never
loaded blocked every other vehicle for 15 s per attempt. This dates from the
September 12 gate, not release G. Details are in
[vehicle-spawn-cooldown.md](vehicle-spawn-cooldown.md). Client only: the server
DLL is release B's.

**Changes:**
- A blocked click says why: "Still loading *name*. Choosing a different vehicle
  cancels it.", "*name* is still spawning.", or "Vehicle spawner is on cooldown."
  only after a vehicle was created.
- Choosing a different vehicle (another model or another saved vehicle) while
  one is still loading replaces it. Nothing has been deleted or created then, and
  the replaced request stops before the old vehicle is removed, CAD is asked or a
  vehicle is created.
- GTA's loading spinner shows "Loading *name*" while a model streams.
- The 15 s model timeout stays; its message names the vehicle.
- If `CreateVehicle` returns no vehicle, the player is told, and the model isn't
  added to Recent Vehicles or claimed with CAD. Before, this failed silently.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `1e29abeef567056226a69209e4aa5a3b1389bd5ea22b33f961c12ed118aa6f17` |
| `build/vMenu/vMenuServer.net.dll` | `003e0932bed3ee2195f052e292b8f5d63e2f30fd8beace5ce46e728daa5c66e9` (unchanged) |

**Validation:**
- `tools/ilcompare` against release G's client DLL shows only `VehicleSpawnGate`,
  the new `VehicleSpawnAttempt` and `VehicleSpawnBlock`, both `SpawnVehicle`
  overloads, `SpawnVehicleCore`, `LoadModel`, the new `NotifySpawnBlocked`, the
  removed `VehicleSpawnerCooldownEnabled` and `VehicleSpawner.GetVehicleName`
  (now internal). `TeleportToCoordsCore` differs only by a renumbered
  compiler-generated local function with an identical body.
- `tests/vehicle-spawn-cooldown` gains a frame-by-frame scheduler and covers
  the loading message, the spinner, replacing a loading vehicle (cached or
  streaming), saved vehicles of the same model, a request past loading not being
  replaced, and the cooldown starting only after a vehicle is created.
- `tests/vehicle-spawn-placement` runs the real spawn core: a request replaced
  after its model loaded deletes, creates and claims nothing and releases its
  model unless the replacement uses it; a `CreateVehicle` failure is reported.
- Removing the gate's replacement, or the core's post-load check, makes these
  tests fail.
- `tests/spawn-provenance` follows the split `CreateVehicle` line and checks a
  failed create hands telemetry 0.
- All twenty-five tests pass. The client builds with 0 warnings.
- No FiveM runtime testing was done.

**Deploy** (on top of release G):
1. Hash the live client DLL. It must be release G's
   (`2d0178c6cdea02dfbf88378b0336d382fe43a0150679821b796073f808d3a02e`). If it
   isn't, stop: the server runs a build this file doesn't record.
2. Copy `vMenuClient.net.dll`.
3. Restart the resource.

**Runtime acceptance:**
- Spawn an add-on car you haven't used this session. The spinner shows
  "Loading *name*" until it appears. Click it again while loading: "Still
  loading *name*", not "on cooldown".
- While it loads, pick a different car: the new one spawns and the first never
  appears.
- Once a car appears, click again within a second: "Vehicle spawner is on
  cooldown." A second later it spawns.
- If a player reports a car that never appears, their F8 shows which case it
  was: `did not load within 15000ms` (the model never streamed) or
  `CreateVehicle returned no vehicle` (the game refused the entity).

## October 23, 2026: after-death restore waits for PSRP_DeathScript (client DLL)

PSRP_DeathScript's fake death (PSRP-Development `24267e5e`) brings a dead
player's ped back to life about 1.5 s after the fall, invincible and held in a
lying clip, so bodies sync between clients. `RestorePlayerAfterBeingDead` waited
only for `Game.PlayerPed.IsDead` to clear, then swapped the model
(`SetPlayerModel` via `RestorePreferredMpAppearance`) and reapplied clothing and
weapons. Under fake death that swap would run under the lying body, so others
would see it stand up in default clothes and lie down again. The same swap is
likely what PSRP_DeathScript's README records as an unexplained "ped replaced"
during the hospital bed scene and mid-drag. Client only: the server DLL is
release B's.

**Change:** the wait also holds while the `dead` state bag (true or 1) is set,
via the new `IsLogicallyDead()`. The restore now runs after the real revive or
respawn, as it did before fake death.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `129a407f03d79ce2132a1a9675c9bad8562367e6a1fada591e2bc556f7f6b934` |
| `build/vMenu/vMenuServer.net.dll` | `003e0932bed3ee2195f052e292b8f5d63e2f30fd8beace5ce46e728daa5c66e9` (unchanged) |

**Validation:**
- `tools/ilcompare` against the previous client DLL shows only
  `RestorePlayerAfterBeingDead` (one added call in its wait condition) and the
  new `IsLogicallyDead`. `SnowballPickupHelpMessageTask` differs only by a
  renumbered compiler-generated local function with an identical body.
- `tests/client-fixes` checks the wait condition and the state bag read.
  `client-guards`, `client-features`, `frame-costs` and `permission-table` pass.
  The client builds with 0 warnings.
- No FiveM runtime testing was done.

**Deploy** (with the PSRP-Development fake death deploy):
1. Hash the live client DLL. It must be the October 23 vehicle spawner build
   (`1e29abeef567056226a69209e4aa5a3b1389bd5ea22b33f961c12ed118aa6f17`). If it
   isn't, stop: the server runs a build this file doesn't record.
2. Copy `vMenuClient.net.dll`.
3. Restart the resource.

**Runtime acceptance:**
- With a saved MP character loaded, die and wait a few seconds: the body stays
  lying in the same clothes. Revive or respawn: the appearance and weapons
  restore then, as before.

## October 27, 2026: noclip telemetry lease and vitals after a model swap (client DLL, server DLL)

Two fixes for psrp_telemetry false alerts. The owner approved shipping a new client
and a new server DLL.

**Noclip lease.** vMenu noclip (`vMenu/Noclip.cs`) sets the noclip entity
invincible every frame. On foot that entity is the player's ped, so OneSync's
invincibility bit is set and psrp_telemetry raised god mode alerts for staff in
noclip. Telemetry rightly ignores the `vmenu_noclip_active` state bag, because the
client writes it about itself. Noclip now gets the same server-owned lease as vMenu
god mode:

| | God mode (August 17, 2026) | Noclip (this release) |
| --- | --- | --- |
| Client event | `vMenu:PSRP:GodmodeState` | `vMenu:PSRP:NoclipState` |
| Client reports active when | permissions set up, Player Options god mode on, `POGod` allowed | permissions set up, noclip on (`NoClip.IsNoclipActive()`), `NoClip` allowed |
| Server ACE (any one) | `vMenu.PlayerOptions.God`, `vMenu.PlayerOptions.All`, `vMenu.Everything` | `vMenu.NoClip`, `vMenu.Everything` |
| Lease | `GrantInvincibilityGrace(src, 4, "vMenu god mode")` | `GrantInvincibilityGrace(src, 4, "vMenu noclip")` |

Both work the same way:
- The client (`vMenu/GodmodeTelemetry.cs`) polls every 500 ms in one tick. Each
  state is sent on the first poll, whenever it changes and every 2 s while active.
- The server (`vMenuServer/GodmodeTelemetry.cs`) renews leases every 1 s in one
  tick, only while the heartbeat is at most 6 s old, the player holds one of the
  ACEs above and `GetPlayerInvincible(src)` is true. A failed check, an inactive
  report or `playerDropped` clears the request; the client's next heartbeat starts
  it again. With nothing requested the tick does no work. psrp_telemetry must be
  started.
- `vMenu.NoClip` is a global permission: its only parent is `vMenu.Everything`
  (`PermissionsManager.GetPermissionAndParentPermissions`).
- Noclip in a vehicle makes the vehicle invincible, not the player, so the bit
  check fails and no noclip lease is issued.
- The client still writes `vmenu_noclip_active`. It grants nothing for
  invincibility.
- A player in both modes gets both leases. An older client sends only the god
  mode event, which the server still handles as before.

**Vitals after a model swap.** Create Male / Create Female (MP Characters) and
`SpawnSavedPed` called `SetPlayerModel`, awaited the weapon restore, and only then
put back max armour, max health, health and armour. During that await the new ped
had the model's default full health: telemetry could read it as a refill, and an
injured player was healed for a moment. The vitals are now restored straight after
`SetPlayerModel`, before any await, as `CommonFunctions.SetPlayerSkin` already did.
These are the only `SetPlayerModel` calls in `vMenu/`. `vMenu:PedChange:Before`,
the weapon save and restore and `vMenu:PedChange:After` keep their order;
`PedChange:After` handlers now see the restored vitals.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `650000eb17e8f6e477df8d6bb8d9d5d889fa38d641cd0831638b100a53b51e30` |
| `build/vMenu/vMenuServer.net.dll` | `39e47fa503dcc0ff8eb92d50cc234ed523f9e12094f3c45f67695e3a24cfb3d2` |

**Validation:**
- `tools/ilcompare` client, against the October 23 client DLL: the intended
  changes are `GodmodeTelemetry` (state fields moved into the new nested
  `Heartbeat` type with its constructor and `Publish`, the tick's state machine
  and the constructor), the MP Characters menu lambda (vitals moved before the
  await; its four hoisted vitals fields are gone, as they no longer live across an
  await) and `SpawnSavedPed`. The rest is Mac build noise (below): the version
  attribute and seven methods.
- `tools/ilcompare` server, against release B's server DLL: only
  `GodmodeTelemetry` changed (new `Heartbeats` type, `OnNoclipState`, `Entry`,
  `ForgetIfIdle`, `GrantLease`, `IsFresh`, `HasNoclipPermission`, the two reason
  constants, and the changed handlers, constructor and renewal tick), plus the
  version attribute.
- New test `python tests/telemetry-leases/test_telemetry_leases.py` compiles both
  production files against fake natives. The real `Permission` enum,
  `GetPermissionAndParentPermissions` and `GetAceName` decide which ACEs count. It
  checks each ACE alone for each event, heartbeat age (6 s renews, 7 s doesn't),
  the invincibility bit, clearing (inactive, revoked ACE, dropped, missing player),
  a telemetry restart, both modes together, the older client's event, the client
  heartbeat timing, one tick each side and the exact reasons. It also checks that
  every `SetPlayerModel` in `vMenu/` restores the vitals once, before its first
  await.
- All twenty-six tests pass on macOS (Lua 5.5), and all twenty-five passed there
  before the change. Both DLLs build with 0 warnings.
- No FiveM runtime testing was done.

**Building on macOS.** With .NET 9 from Homebrew (keg-only):
```
export DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec PATH=/opt/homebrew/opt/dotnet@9/bin:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1
printf '<Project><Target Name="PostBuild" /></Project>\n' > /tmp/skip-postbuild.targets
dotnet build vMenu/vMenuClient.csproj -c Release --nologo -p:CustomAfterMicrosoftCommonTargets=/tmp/skip-postbuild.targets
dotnet build vMenuServer/vMenuServer.csproj -c Release --nologo
```
The client project's `PostBuild` target runs `postbuild.cmd` through cmd syntax
(`if exist ...`), which fails on macOS (MSB3073). `-p:PostBuildEvent=` doesn't
help, because the target is a custom `Exec`, not the `PostBuildEvent` property.
The targets file above is imported after the project and replaces that target with
an empty one. The project file is unchanged. A Mac build of unchanged source,
compared with the Windows-built package, differs only in:
- the `AssemblyInformationalVersion` git hash (both DLLs)
- client only, seven methods where the compiler emits a slightly different but
  equivalent body: `CommonFunctions.GetAllVehicleMods`,
  `EntitySpawner.FinishPlacement`, `FunctionsController.ManageCamera`, the
  `Dispose` of the `InfinityPlayerList` and two `NativePlayerList` enumerators,
  and an async lambda in `PlayerOptions.CreateMenu`
- client only, the embedded `overlays` resource (`vMenu/data/overlays.json`,
  tattoo data) has LF line endings instead of CRLF, because git checks it out
  with the platform's line endings. The text is otherwise identical, and
  `TattoosData` only parses it as JSON. This is why a Mac-built client DLL is
  about 23 KB smaller. `ilcompare` doesn't compare resources.

Treat those as noise when comparing a Mac build.

**Known issue, not changed:** when noclip ends it calls
`SetEntityInvincible(entity, false)`. That doesn't turn vMenu god mode off for
long: the god mode latch re-applies invincibility every frame while the option is
on, so it is back by the next frame. It does clear invincibility that another
resource set on the ped or vehicle and doesn't re-apply every frame.

**Deploy** (both DLLs, together with or after the psrp_telemetry change):
1. Hash the live DLLs. They must be the previous package: client
   `129a407f03d79ce2132a1a9675c9bad8562367e6a1fada591e2bc556f7f6b934` (October 23),
   server `003e0932bed3ee2195f052e292b8f5d63e2f30fd8beace5ce46e728daa5c66e9`
   (release B). If either differs, stop: the server runs a build this file doesn't
   record.
2. Copy `vMenuClient.net.dll` and `vMenuServer.net.dll`.
3. Restart the resource.

**Runtime acceptance:**
- A staff member with `vMenu.NoClip` noclips on foot for a minute: no god mode
  alert in psrp_telemetry. Noclip in a vehicle also raises none.
- A player without the ACE who forces the state bag or the event gets no lease.
- vMenu god mode alone still raises no alert.
- Injured (for example 50 health), create a male or female MP character, or load a
  saved one: health stays at 50 throughout, with no refill alert.

## October 28, 2026: purge mode noclip drop (Lua only)

PSRP-Development added a console-switched purge mode (`purge on` / `purge off` in
PSRP_Framework, read as `GlobalState.purgeActive`). While it is on, clock-in is
blocked, so only `psrp.clockin.bypass` holders keep staff powers. vMenu already
refuses noclip, names and blips to staff who are not clocked in, but leaving
noclip never depends on clock-in, so a staff member flying when the purge
started could stay in it.

`build/vMenu/client/integrations.lua` now drops a non-bypass player out of
vMenu noclip when they are clocked out or the purge state bag changes, but only
while purge mode is on. It runs vMenu's own `vMenu:<keymapping id>:NoClip`
command, which `canDoInteraction` already allows while noclip is active, and
tells the player why. The clock-out events and the state bag can arrive in
either order, so a queued drop runs once. Outside the purge nothing changes.

No DLL changed; the hashes in the table above are the October 27 package.
Test: `lua tests/purge-noclip/test_purge_noclip.lua` (runs the real
`integrations.lua` with stubbed natives).

**Deploy:** copy `build/vMenu/client/integrations.lua` over the live file (check
the live copy matches `git show build-2026-10-27:build/vMenu/client/integrations.lua`,
ignoring line endings, first), then restart vMenu. Deploy with the PSRP-Development
purge mode change.

**Runtime acceptance:** with purge mode on, a clocked-in moderator in vMenu
noclip is taken out of it with a notice (they fall if airborne); a `psrp.clockin.bypass` holder
keeps noclip. Not checked in game yet.

## October 28, 2026: lease renewal player lookup (server DLL)

The owner asked for this fix after two server profiles taken on October 28 (217
players). `RenewValidatedLeases` in `vMenuServer/GodmodeTelemetry.cs` checked
`Players[handle] != null` with the handle string. The CitizenFX server
`PlayerList[string]` indexer looks a player up by **name**: it calls
`GetPlayerName` for every online player. A server id never matches a name, so:

- each renewal cost about 18 ms per requesting staff member at 217 players. The
  profiles showed 18/36/55/74 ms ticks every ~1 s (36–44% of all server script
  time, and every one of the worst ticks over 60 ms)
- the lookup always returned null, so the request was dropped and **no god mode
  or noclip lease ever reached psrp_telemetry** (since August 17). The client's
  next heartbeat re-added the request, so the scan repeated forever.

The check is now `DoesPlayerExist(handle)`, as `MainServer.GetPlayerFromServerId`
already does. No other `Players[...]` use in `vMenuServer/` takes a string.

**Behaviour change:** psrp_telemetry now receives `"vMenu god mode"` and
`"vMenu noclip"` leases as designed in the October 27 section. Staff in vMenu
god mode or on-foot noclip get the 4 s grace that was always intended.

| File | SHA-256 |
| --- | --- |
| `build/vMenu/vMenuClient.net.dll` | `650000eb17e8f6e477df8d6bb8d9d5d889fa38d641cd0831638b100a53b51e30` (unchanged) |
| `build/vMenu/vMenuServer.net.dll` | `8827cd4c4bd6e7862322458a3ed07ccb86b2bad89f51c41ae8b25037c3bdca2f` |

**Validation:**
- `tools/ilcompare` server, against the October 27 server DLL: only
  `RenewValidatedLeases` changed (`get_Players` + `PlayerList::get_Item(String)`
  replaced by `API::DoesPlayerExist(String)`), plus the version attribute's git hash.
- `python tests/telemetry-leases/test_telemetry_leases.py` passes. Its fake
  `PlayerList` used to treat the string as a server id, which is why it passed with
  the bug. The fake now matches by name like the runtime, `DoesPlayerExist` is
  faked, and a source check refuses any `Players[` in the server file. With the old
  lookup and the new fake, the behaviour checks fail (no leases granted).

**Deploy** (server DLL only):
1. Hash the live `vMenuServer.net.dll`. It must be the October 27 package
   `39e47fa503dcc0ff8eb92d50cc234ed523f9e12094f3c45f67695e3a24cfb3d2`. If it
   differs, stop: the server runs a build this file doesn't record.
2. Copy `build/vMenu/vMenuServer.net.dll`.
3. Restart vMenu.

**Runtime acceptance:**
- With a staff member in vMenu god mode, a server profile shows no repeating
  18 ms+ `c# tasks → task iteration` spike every ~1 s.
- That staff member raises no god mode alert in psrp_telemetry; noclip on foot
  raises none either.
- Not checked in game yet.
