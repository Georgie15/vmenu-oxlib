# vMenu / PSRP CAD vehicle identity integration

September 9, 2026. Coordinated implementation with [PSRP_cad's deployment guide](https://github.com/Georgie15/PSRP-Development/blob/main/FiveM%20Scripts/PSRP_cad/VEHICLE_IDENTITY.md).

## Source delivery and existing work

The working checkout is `C:/Users/Georgie/source/repos/vMenu-ox`, originally at `cd866b8`. Before this task it already had extensive uncommitted PSRP changes, including Save to CAD, vehicle catalogs, telemetry and UI work. Those unrelated changes were preserved. The new identity source edits are applied locally, and both DLLs were rebuilt successfully from that combined working tree.

This package publishes the **scoped delta** against that pre-task working baseline. The package commit does not claim to commit all modified C# files or the combined DLLs. Its patch cannot be applied directly to the old committed `cd866b8` source, which lacks required pre-existing integrations. Do not deploy a fresh checkout of the package branch as a replacement for the current PSRP vMenu build.

`baseline-and-builds.json` records normalized before/after hashes for each touched source file and the rebuilt local DLL hashes. The pre-task snapshots remain on this workstation under `C:/Users/Georgie/.codex/backups/vehicle-identity-20260909`. Another workstation must first obtain/publish the matching PSRP source baseline, then apply this patch. Do not bulk-stage unrelated working-tree edits to work around that prerequisite.

## Apply to the matching baseline

1. Verify the four source preimage hashes against `baseline-and-builds.json` (UTF-8, no BOM, LF). Preserve the existing Save to CAD and other PSRP integrations.
2. Run `git apply --check integrations/psrp-cad-vehicle-identity/working-tree-integration.patch`, then apply it. On the current workstation it is **already applied**; `git apply --check --reverse` verifies this without modifying anything.
3. Include `vMenuServer/server/vehicleIdentity.lua` and `vMenuServer/config/vehicleIdentityModels.json`. The PSRP-Development copy of this package contains these same two files as `vehicleIdentity.lua` and `vehicleIdentityModels.json`. The csproj patch copies them into build output. The resource must load `@ox_lib/init.lua` and `server/*.lua`; the existing local manifest already does.
4. Build and test from the vMenu repository root:

```powershell
dotnet build vMenu/vMenuClient.csproj -c Release --nologo
dotnet build vMenuServer/vMenuServer.csproj -c Release --nologo
python integrations/psrp-cad-vehicle-identity/test_saved_tokens.py
```

5. Deploy the complete `build/vMenu` directory from the verified combined source. Do not copy only a DLL or only the Lua adapter. Keep CAD's `psrp_cad_vehicle_alpr` convar false until the coordinated multiplayer acceptance tests pass.

## Contracts and checks

The client begins a model/save-token ticket before entity creation, then claims it after the spawn/modification setup. The server adapter validates the existing vMenu category/model ACEs and delegates to CAD's server-owned ticket service. No CAD identity error blocks normal spawning. Stock model/category lookup comes from the local VehicleData catalog; configured addon models use the existing Addon permission and unknown spawn-by-name models require that ACE.

`VehicleInfo.cadSaveToken` is persisted with saved KVP data. Legacy saves acquire a token once on read. Rename and customization preserve it; re-saving the same bound live vehicle associates its saved token with that vehicle identity. A fresh setup on a different active character gets a separate server association. Save to CAD sends `save_token` and `model_hash` in addition to the existing display name/plate/color payload.

Both Release builds pass. The client has three pre-existing unused-function warnings in MpPedCustomization. `test_saved_tokens.py` extracts and compiles the actual VehicleInfo and StorageManager save/read methods against in-memory KVP adapters and the resource's Newtonsoft.Json DLL. It verifies legacy upgrade idempotence, rename, customization, distinct new saves and failed overwrite preservation. It requires Python and the .NET 9 SDK. These checks do not execute FiveM exports, network creation or multiplayer spawn behavior; those remain acceptance requirements in the CAD guide.
