# Clothing saved by collection (FIB revamp groundwork)

vMenu used to save clothes as global drawable numbers. A global number is a
position in one long list made of every clothing collection the server streams:
the base game, then each DLC or addon collection in load order. When an EUP
pack adds, removes or resizes anything, every later number moves and saved
uniforms change into other garments. From November 9, 2026 vMenu saves each
piece as a **collection reference**: the collection name (`""` for the base
game), the drawable's index inside that collection, and the texture. Other
collections changing can't move it. Old saves are read with a **captured
baseline** of the pre-change layout, never with whatever layout is running.

Code: `vMenu/ClothingRefs.cs` (format and the one resolution policy, no game
calls), `vMenu/ClothingSaves.cs` (every save type and outfit codes, on the
stored JSON), `vMenu/ClothingService.cs` (game calls, join-time upgrade,
exports, capture command). Data: `build/vMenu/config/clothing-layouts.json`,
made only with `tools/clothing-layouts/clothing_layouts.py`.

## What uses it

One policy (`ClothingResolver.Resolve`) for everything that saves or restores
clothing:

| Feature | Storage | Notes |
| --- | --- | --- |
| Saved Outfits | `vmenu_outfit_*` | `refs`, `layout` |
| MP characters: spawn, edit, default and last character | `mp_ped_*` | `ClothingRefs`, `ClothingLayout`. Head, hair, face, tattoos unchanged |
| Last-appearance snapshot (after death) | `vmenu_last_mp_character_snapshot` | keeps unmatched pieces as saved |
| Character editor | in memory, then `mp_ped_*` | each edited slot is re-captured; untouched unmatched pieces are kept |
| Saved Peds (generic) | `ped_*` | **needed the fix** for freemode peds: it saved global numbers for all 12 components and props. Now `clothingRefs`, `clothingLayout`; head and hair included as that feature always did |
| Death restore `vMenu_tmp_saved_ped` | temporary | stamped with the layout at death; dpclothing's death patch still wins |
| Outfit codes: generate, import into a character, apply directly | `vmenu_outfits.data` | v2 payload; old codes still load |

The global numbers stay in every save (and in codes) next to the references, so
an older vMenu can still read them after a rollback. The regular menus still
show global numbers.

Pieces go on with `SET_PED_COLLECTION_COMPONENT_VARIATION` /
`SET_PED_COLLECTION_PROP_INDEX` after checking the model, that the collection
drawable exists (`GET_PED_DRAWABLE_GLOBAL_INDEX_FROM_COLLECTION`) and that the
texture is in range.

## Format

```json
"ClothingRefs": { "v": 2, "model": 1885233650,
  "clothes": { "11": { "col": "mp_m_deptmerge2", "drw": 40, "tex": 2, "rev": "psrp-eup-2026-11-06", "pp": "<pack print>", "lp": "<layout print>" },
               "3":  { "col": "", "drw": 5, "tex": 0, "lp": "<layout print>" } },
  "props":   { "0":  { "empty": true },
               "1":  { "legacy": [7, 1], "why": "not-in-baseline" } } }
```

- `col`/`drw`/`tex`: collection, collection-local drawable, texture. `""` is the base game.
- `empty`: a prop slot explicitly worn empty.
- `legacy` + `why`: a piece that couldn't be matched yet. It keeps the exact
  old numbers and is retried each time it is worn.
- `rev`/`pp`: PSRP pack collections only. The pack revision and the pack print
  (texture counts of the PSRP collections) when saved.
- `lp`: the global layout print when saved.

Outfit code v2: `{ v = 2, Model, Layout, Clothes, Props, Refs }`. `Clothes` and
`Props` are the old `{ Item, Texture }` maps. `server/oufitCodes.lua` validates
every field and refuses codes with pending pieces. The request callback now also
returns the code's `created` time.

## Resolution policy

For each slot of a save:

1. **Reference.** For a PSRP collection, check the running pack. A matching
   pack print means the same pack, so put it on. A known revision is followed
   through the verified remap chain: a move, a texture reorder, `retired` or
   `review`. Unknown either way: wait for review (`pack.strict`, the default).
   Then validate, then put it on.
2. **Old global numbers** (no reference):
   - stamped with the running layout: current, placed with live natives.
   - stamped with another layout: `layout-changed`, never guessed.
   - not stamped (a save made before this format): read with the **baseline**.
     Outside the baseline, or a texture the baseline says didn't exist, means
     `not-in-baseline` or `bad-texture`.
   - not stamped and no baseline: as before, but only while the old pack is
     running, or before a legacy pack is declared. Marked unverified, never
     rewritten.
3. Anything unresolved is **left off**. The player is told which piece, and the
   save keeps the original entry. Nothing is substituted by nearest number.

Models other than `mp_m_freemode_01` and `mp_f_freemode_01` keep their numbers,
because the clothing packs don't touch them.

## Upgrading old saves on join

`ClothingService.Migrate` runs once per join, about 3 s after the session starts.
For every `vmenu_outfit_*`, `mp_ped_*`, `ped_*` and the snapshot made before
this format, it converts with the baseline alone:

- A save is rewritten only when every piece converts. Only the references are
  added; all other JSON is untouched.
- The original is copied to `vmenu_clothing_backup_<key>` first, once; a later
  run never overwrites it. If the backup can't be written, the save is left alone.
- Saves already upgraded are skipped, so it is safe on every join, and for
  players who missed the rollout.
- Saves that can't convert are listed in `vmenu_clothing_review`, and the
  player gets one notice. Backups go into the server backup (`vmenu_clothing_backup_`).
- Without a baseline nothing is rewritten.

Player/staff console commands (F8):

- `vmenu_clothing_review`: what needs review and why.
- `vmenu_clothing_restore list`: upgraded saves with a kept original.
- `vmenu_clothing_restore <key>`: put that save back exactly as before. It is
  upgraded again on the next join if it still converts.
- `vmenu_clothing_capture check`: this server's layout against the baseline and
  pack revision, printed only.

## Pack revisions and remaps (inside PSRP collections)

Collection references survive other collections changing. A repack of PSRP's own
collections can still move a drawable or reorder textures. For example,
removing FIB from `mp_m_deptmerge2` moves 285 other-department references.
Each pack build is a **revision** in `clothing-layouts.json`: texture counts
per drawable of each PSRP collection, and a fingerprint of each collection's
YMT. Steps between revisions come from the EUP builder's reports. Recommended:
both builds' `BUILD_REPORT/original-to-new.csv`, joined on the original pack
choice. That ID is stable across builds; the builder keeps every original
choice. Each row is checked against that build's `Numbering/current-addon-registry.csv`.

```
python -I tools/clothing-layouts/clothing_layouts.py revision --layouts build/vMenu/config/clothing-layouts.json --id <new-id> --current --ymt <each PSRP collection's YMT exported to XML>
python -I tools/clothing-layouts/clothing_layouts.py remap --layouts build/vMenu/config/clothing-layouts.json --from <old-id> --to <new-id> --originals <old build>/BUILD_REPORT/original-to-new.csv <new build>/BUILD_REPORT/original-to-new.csv --registries <old build>/BUILD_REPORT/Numbering/current-addon-registry.csv <new build>/BUILD_REPORT/Numbering/current-addon-registry.csv [--review <FIB_Revamp_Review>/comparison.json]
python -I tools/clothing-layouts/clothing_layouts.py check --layouts build/vMenu/config/clothing-layouts.json
```

Export YMTs with the EUP tool, into a fresh folder:
`Tools/bin/Release/net9.0/EupTool.exe export <ymt> <out.xml> <out folder>`.
`--map <new build>/BUILD_REPORT/current-to-new.csv` also works, but that report
lists department choices only. For the current pack it leaves 99 live textures
out (NG, Paleto, gang, armor), and the tool turns them into `review`.

Rules:

- Every texture of every drawable in the old revision's PSRP collections must
  be accounted for. Anything no report covers becomes a `review` entry, never
  "unchanged". The exception is a collection no report mentions whose YMT is
  byte-identical in both builds; that is proven unchanged.
- A report row whose department doesn't own the model at the place it claims
  is rejected and counted. Today all 96 `STAFF_EUP` rows in `original-to-new.csv`
  claim `mp_m_deptmerge2`; the items really live in `mp_m_leoaccessories`. The
  builder should be fixed to write the right collection.
- A `comparison.json` from the FIB review adds moves into the revamp's own
  collection for exact or moved matches, `retired`, and `review` for
  replacements. These apply only where no report has a surviving target.
- Collection names are the shop meta's `dlcName`: `mp_m_deptmerge2`, `fibeup`,
  `bm_fib`. Report names like `mp_m_freemode_01_fibeup` or `..._p_bm_fib` are
  normalized.

The running pack is identified by its texture counts. An exact match, or only
appended drawables and textures, counts as that revision. Anything else is
unknown, and PSRP pieces wait for a declared revision (`pack.strict`). Counts
cannot detect a count-preserving swap, so the EUP builder's locked-layout
protection and its maps remain the source of truth for moves.

The shipped revision `psrp-eup-2026-11-06` was read from the Desktop
`PSRP_EUP` build of November 6. It is a **candidate** until a live capture
confirms the server runs it.

**Permanent IDs.** The original pack choice that the builder records in
`original-to-new.csv` (source pack, sex, original collection, component,
drawable, texture, file) is already a permanent ID for every managed choice. It
stayed fixed through the October 29 reorder, the staff move and the simulated
FIB removal, and the composed remap above uses it. It was checked against the
current build and the FIB-disabled simulation. Its gaps:

- The builder's STAFF rows record the wrong collection.
- The native replacements in `Misc Civ-Orgs EUP` and the LEO accessories'
  original items aren't builder choices.
- New revamp items need new choices.

A catalog of these IDs, carried into vMenu saves instead of collection
references, would need those gaps closed first. For this rollout the explicit
versioned remap is generated from the IDs, which gives the same result without
changing what players' saves contain.

## Pre-swap capture (needs a live, pre-change server)

The baseline must come from the **live server before any clothing change**:
same resources, same game build. Local EUP files only give PSRP's own
collection-local layout. Global numbers also depend on the game build and
every other clothing resource and its start order.

1. Deploy this vMenu build (below). With no baseline, behaviour for old saves
   is unchanged, nothing is rewritten, and new saves already get references.
2. Grant the capture ace to whoever captures, temporarily, e.g.
   `add_ace identifier.license:<theirs> vMenu.ClothingCapture allow`.
3. In game, F8: `vmenu_clothing_capture check`. The F8 output shows each
   model's layout print and pack revision (`psrp-eup-2026-11-06 (exact)` if the
   candidate is right).
4. F8: `vmenu_clothing_capture`. The server writes
   `<vMenu folder>/clothing-capture-<UTC time>.json`, which includes its
   resources and game build.
5. Copy that file into the repo (not under `build/`), then:
   ```
   python -I tools/clothing-layouts/clothing_layouts.py check --layouts build/vMenu/config/clothing-layouts.json --capture <capture>
   python -I tools/clothing-layouts/clothing_layouts.py baseline --layouts build/vMenu/config/clothing-layouts.json --capture <capture> --id live-<date> --valid-from <date the current pack went live, UTC>
   ```
   `baseline` refuses a capture with anomalies, a non-continuous list, a print
   that doesn't add up, or PSRP collections that don't exactly match a declared
   revision. If the live pack isn't the candidate, export the live pack's YMTs
   and add that revision first. The matching revision becomes `legacy.rev`.
   `--valid-from` is informational: older outfit codes get a "made before"
   notice, and are still read with the baseline, which is what they show today.
6. Run the tests, commit, and deploy the new `clothing-layouts.json` before the
   FIB pack changes. Players' old saves upgrade on their next join.
7. Remove the capture ace.

**Do not change the clothing packs before step 6 is live.** The shipped file
has no baseline and no `legacy.rev` on purpose. The candidate pack isn't
verified against live, and claiming it would refuse old saves at rollout if it
were wrong. Until step 6, old saves behave exactly as they do today. That
includes being misread after a pack change, which is the case this work
exists to prevent.

## FIB revamp findings

Full report: `PSRP_EUP_Source_MiscMerged/FIB_Revamp_Review/` in the EUP workspace
(`README.md`, `comparison.json`, `vmenu-remap-preview.json`, evidence). The
revamp is a separate resource (male collection `fibeup`, female `bm_fib`).
Textures were compared by decoded pixels, because the revamp re-encodes every texture.

| Current FIB choices (197) | |
| --- | --- |
| exact (female: same files and order) | 76 |
| moved (male: same garment, new collection or drawable) | 38 |
| retired (not in the revamp) | 50 |
| needs a decision | 33 |

The revamp adds 24 new textures. FIB shares 32 of its 44 models with other
departments; the revamp re-ships 18 and drops 14.

Needs an owner decision:

1. Male shirts `m:jbib:3#1–16` and `m:jbib:8#1–16` (original jbib 7 and 8,
   32 choices). The revamp drops both meshes; the same textures exist on
   `fibeup` jbib 1, a different shirt cut. `m:jbib:3#6` is only a near match.
2. `m:jbib:23#25`: present in the revamp, but its YMT declares 15 textures, so
   it can't be selected. Fix the YMT or retire it.

If FIB is removed from `PSRP_EUP` and the revamp ships separately:

- 285 references from other departments change slot: Paleto 109, SAHP 76,
  NG 63, HSB 11, SASM 9, gangs 11, BCSO 4, LSPD 1, SAST 1.
- The remap preview maps all of them, follows the 114 kept FIB choices into
  `fibeup`/`bm_fib`, retires 50, and leaves exactly the 33 decisions for review.

Other notes:

- The revamp's male meta has `eCharacter` `SCR_CHAR_MULTIPLAYER_` and names a
  creature file it doesn't ship.
- 89 of its textures are over 2048 px.
- Its female files and collection are identical to the old `PSRP_FIB`, so the
  two must never run together. Start the revamp after `PSRP_EUP`.

## FIB rollout

1. Pre-swap capture (above) deployed. Optionally give players a few days to
   join and upgrade.
2. Build the new EUP pack to a fresh folder. Decide the FIB review's open items
   first; see `FIB_Revamp_Review` in the EUP source and the summary below.
3. Add the new revision and the remap from both builds' `original-to-new.csv`,
   their registries and the FIB `comparison.json` with its decisions applied.
   Set it `--current`, run `check`, and run the tests.
4. Deploy vMenu's `clothing-layouts.json` and the new pack together. Restart
   vMenu and the pack (owner).
5. In game: `vmenu_clothing_capture check` shows the new revision `(exact)`.

## Verification

Local (all pass):

- `dotnet run --project tests/clothing-refs`: 144 checks against a pretend
  game (both freemode models, clothes and props, empty props, base-game items).
  - Survival: a global shift, a drawable moved inside its collection, a
    texture reorder, a retired item (reported, not put on, reference kept), a
    replacement (needs a decision).
  - Old saves and codes: both code load paths and generation, the model check,
    old codes with no model, codes older than the baseline. Ambiguous numbers
    are never put on or rewritten.
  - Storage: no baseline and unknown packs, stamped death saves, migration of
    every save type (rewrite only when complete, backup once, retry, restore).
  - Wiring of every feature, and the shipped config.
- `python tests/clothing-layouts/test_clothing_layouts.py`: 36 checks.
  - The baseline command refuses bad or mismatched captures.
  - Remaps cover every old item; conflicting maps are refused.
  - Composed original-to-new maps are checked against the registries.
  - FIB review moves, retirements and decisions are applied.
  - The prints match the C# ones.
- `dotnet run --project tests/saved-outfits` and
  `lua tests/share-codes/test_share_codes.lua`: updated for the new flow.
  The server accepts and validates v2 codes; Lua hands codes to C#.

Needs a FiveM test server (not done):

- The collection natives on b3258 return what the docs say. The capture's own
  checks show this: no anomalies, and the drawable-by-drawable print equals the
  collection-count print.
- Saved Outfits, characters (spawn, edit, save, preview), Saved Peds,
  after-death restore with and without dpclothing's patch, and both outfit-code
  paths on male and female.
- A pre-change save upgrades on join, `vmenu_clothing_review` and
  `vmenu_clothing_restore` work, and a server backup restore.
- After the FIB pack change, an upgraded uniform still shows the same garments,
  and a retired FIB piece is reported and left off.
- The capture file is written by the server (`SaveResourceFile` into the vMenu
  folder).

## Rollback

- vMenu: put back the previous client DLL and Lua (tag `build-2026-11-05`).
  Saves keep their old global numbers next to the references, so the old build
  reads them as before. Codes made since keep their `Clothes`/`Props` too.
- A single save: `vmenu_clothing_restore <key>`.
- `clothing-layouts.json` alone can be reverted; vMenu then treats saves as
  before it had that data.

## Known limits

- An old save last written before an earlier EUP renumbering (e.g. October 29)
  already shows a different garment today. The baseline keeps what it shows
  today; it can't know when the save was made.
- Hair is still saved as a global index (`PedAppearance.hairStyle`); EUP adds
  no hair.
- The capture works for both freemode models only. Other peds keep numbers.
- A count-preserving swap inside a PSRP collection is only caught through the
  builder's maps (see above).
