# Outfit code hair reset fix

September 27, 2026.

Saved MP characters store their chosen hair in `PedAppearance`. Older saves and
outfit codes can also carry drawable component `2` in the clothing map. The
character loader used to apply that map after `PedAppearance`, so the stale
component replaced the hair selected in the editor when the character spawned.
Component `0` is also outside the MP clothing menu and belongs with appearance.

Outfit code generation, character outfit loading, and direct code loading now
leave components `0` and `2` alone. Old codes remain usable; these two
components are ignored when they are read. Character spawning also ignores
legacy map entries for these components, so an already affected saved character
uses its `PedAppearance` hair. Editing hair removes the stale component `2`
entry from that character's data when it is next saved.

The client build was compiled from this branch with
`dotnet build vMenu/vMenuClient.csproj -c Release --nologo`. It completed with
zero warnings and zero errors. The changed package files are
`build/vMenu/client/outfitCodes.lua` and `build/vMenu/vMenuClient.net.dll`;
deploy them together after an in-game check. The client DLL SHA-256 is
`78b8ac8c8649ed89e629b2a1c1805bebe88c67c3f6d750c2455140ef5af90c94`.
No server DLL change is required.

In-game check: clone a saved character, load an existing outfit code, change
hair style and color, save, then spawn or reconnect as that character. The new
hair should remain while the shared clothing and props remain applied. Repeat
with a previously affected saved character and with a code generated before
this fix. In-game behavior has not been verified from this checkout.
