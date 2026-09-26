# Direct face and skin selection

September 25, 2026.

## Use and installation

In MP Character Customization, create or edit a character and open **Face & Skin**.
Use left/right on **Face** to pick a whole face and **Skin Tone** to pick a skin
independently. You no longer need to choose parents or adjust inheritance to use
a face. The optional Blend Face A/B and Blend Skin A/B controls allow mixing;
the direct selector displays **Custom blend (keep current)** when appropriate.
Face feature adjustments, hair, overlays, clothing, and the normal Save Character
flow remain available. Addon entries are labeled **Custom #46** through **Custom #97**.
Face IDs are zero-based and match ONX's documentation.

Install the licensed `cfx_onx_mp_faces` resource in the server resources folder
without renaming it, and start it before vMenu:

```cfg
ensure cfx_onx_mp_faces
ensure vMenu
```

Use the rebuilt local `build/vMenu/vMenuClient.net.dll` in the existing vMenu
resource. The server DLL, permissions, and addon-ped configuration need no changes
for this feature. Deploy/restart during maintenance. This task does not install
the resource or deploy to a live server.

The exact ONX addon resource name is detected on the client each time Face & Skin
opens. While it is started, GTA faces 0–45 and ONX faces 46–97 are available for
both freemode sexes. Skin selections remain 0–45. Without it, only vanilla faces
are offered. A stale addon selection is rejected if the resource stops while the
menu is open; reopening refreshes the list. A previously saved unavailable face
is retained for display, not silently converted. Keep ONX running to load/render
characters saved with its faces.

The supplied ONX README requires game build 2802 or higher and valid Cfx asset
entitlement. Do not run the addon pack alongside either ONX replacement variant.
Other resources replacing `mp_m_freemode_01.ymt` or `mp_f_freemode_01.ymt` need a
separate conflict check. ONX reference:
https://github.com/onxgg/rockstar/blob/main/docs/onx-mp-faces/index.md

## Implementation and preservation

`FaceSelectionMenu.cs` owns the controls. Every entry has an explicit native ID;
selection positions are not treated as parent IDs. Direct face selection sets all
three shape inputs to the chosen face while retaining all skin inputs/weights.
Direct skin selection does the reverse. The blend controls are optional two-face
or two-skin editing; using those advanced controls clears a legacy third blend.

Opening the menu only reads the live ped, so saved blends are not rounded or
reapplied just because a menu opened. The existing serialized PedHeadBlendData
format is retained, and the load path now restores its third blend weight too.
The local PSRP character randomizer includes available ONX shapes and chooses
skin IDs independently. New characters reset face/skin state rather than reusing
the previous editor's selections. Opening Character Appearance no longer
reapplies stale inheritance fields.

The existing inheritanceMenu object is retained for camera/control integrations;
only its displayed name and contents change. MenuAPI 3.2.2 updates list indices
before callbacks but increments sliders after callbacks: the handler uses the
new event value and leaves the active slider's increment to MenuAPI.

## Verification

```powershell
dotnet run --project tests/face-selection/FaceSelection.Tests.csproj -c Release
python tests/face-selection/test_editor_errors.py
dotnet build vMenu/vMenuClient.csproj -c Release --nologo
```

The test harness runs the production menu class against simulated MenuAPI and
CitizenFX objects. It checks resource lifecycle, first/last addon IDs, exact ID
mapping, face/skin independence, blend slider event timing, reopening serialized
head data, missing-resource preservation, third blends, male/female defaults,
and randomization. It is not an in-game render or full FiveM save integration test.
Both the isolated committed-source build and the combined local PSRP build pass
with zero warnings/errors.

### Runtime error follow-up

The reported KeyNotFoundException at local MpPedCustomization.cs:1947 came from
refreshing face feature sliders using `shapeFaceValues`, a cache populated only
by randomization. It was also stale after manual changes. This cache has been
removed: sliders now read the character's actual feature dictionary, with neutral
defaults for absent values and bounded positions. Initialization uses the same
conversion, including for legacy saves with no feature dictionary.

The InvalidCastException at local line 1843 came from randomization casting menu
slot 7 to MenuListItem even though it is the Load Shared Outfit button. It now
updates the existing `faceExpressionList` reference directly. Both failing patterns
were present in the pre-ONX local source backup. The published clean baseline has
no randomizer; its matching local fix is included in local-customization.patch.

The new Python regression harness extracts the actual refresh and randomizer UI
statements from the source and runs them with controlled menu state. The pre-fix
source reproduced KeyNotFoundException; the fixed local source passes opening
new/legacy/sparse saves, reopening edited sliders, malformed values, switching
characters, and expression updates with a button at slot 7. The isolated source
runs the shared feature cases. Builds remain successful. Install the newly rebuilt
combined local client DLL and repeat both actions in-game; runtime retesting is
still required. Pre-follow-up source/DLL backups are in
`C:/Users/Georgie/.codex/backups/onx-editor-errors-20260925`.

Remaining FiveM smoke checks: for male and female characters, choose ONX 46 and
97, change skin, save, respawn/reconnect, edit again, then open Character Appearance
and change hair/clothes. Confirm the face and skin remain correct. Reopen an old
mixed-parent save and confirm opening Face & Skin alone changes nothing. Test
vanilla creation with the ONX resource absent on a separate test startup. Confirm
the head camera and turn controls still work. Runtime visual verification has not
been performed in this task.

## Source/build handoff

The main checkout already contains extensive unpublished PSRP source changes.
They were preserved. The feature is published separately on
`codex/onx-face-selector`, based on `6310688`, in the managed worktree
`C:/Users/Georgie/.codex/worktrees/onx-face-selector/vMenu-ox`.
That branch includes the feature's source, tests, documentation and its matching
client DLL built from the committed baseline. It does not include unrelated local
PSRP changes; do not overwrite the PSRP deployment with that branch's DLL.

The ready local combined client is:
`C:/Users/Georgie/source/repos/vMenu-ox/build/vMenu/vMenuClient.net.dll`.
SHA-256: `cacfbdbec147b5c70269dda5a9c121c643a7416f2a85a8a4f0af61f9428ba600`.
The isolated branch client SHA-256 is
`6f81e664a2a550d071c5b336ba54f88b47493a44454f54bde2466ef23b3165b2`.
Its full source still includes the pre-existing unpublished edits. The corresponding
task-only delta against the pre-task local customization source is preserved in
`integrations/onx-face-selector/local-customization.patch`. It is already applied
locally. For another matching PSRP checkout, first use `git apply --check`, then
apply that patch and copy FaceSelectionMenu.cs and FaceFeatureValues.cs from the
published branch.
Do not apply this local patch to the older clean baseline or apply it twice.

Pre-task source/client backups and the pre-existing tracked diff are under
`C:/Users/Georgie/.codex/backups/onx-face-selector-20260925`.
Preserve all other local source, binaries and untracked integration files.
