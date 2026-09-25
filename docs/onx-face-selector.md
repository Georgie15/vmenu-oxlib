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
dotnet build vMenu/vMenuClient.csproj -c Release --nologo
```

The test harness runs the production menu class against simulated MenuAPI and
CitizenFX objects. It checks resource lifecycle, first/last addon IDs, exact ID
mapping, face/skin independence, blend slider event timing, reopening serialized
head data, missing-resource preservation, third blends, male/female defaults,
and randomization. It is not an in-game render or full FiveM save integration test.
Both the isolated committed-source build and the combined local PSRP build pass
with zero warnings/errors.

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
SHA-256: `eabdd412ef5aebb7d29538ec61e0cb9a8fe37cf8198bb5bf987c9df4d5ef964f`.
The isolated branch client SHA-256 is
`6000b4765e9dd5a54f656fc2205d9ffc25976a28ede3c029f0bfb620e9685d8a`.
Its full source still includes the pre-existing unpublished edits. The corresponding
task-only delta against the pre-task local customization source is preserved in
`integrations/onx-face-selector/local-customization.patch`. It is already applied
locally. For another matching PSRP checkout, first use `git apply --check`, then
apply that patch and copy the new FaceSelectionMenu.cs from the published branch.
Do not apply this local patch to the older clean baseline or apply it twice.

Pre-task source/client backups and the pre-existing tracked diff are under
`C:/Users/Georgie/.codex/backups/onx-face-selector-20260925`.
Preserve all other local source, binaries and untracked integration files.
