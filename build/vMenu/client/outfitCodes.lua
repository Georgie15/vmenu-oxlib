local Cooldown = false
local GenerateCooldown = false
local format = string.format

-- An outfit code's clothes and props, or nil if the code is damaged. Codes made before the
-- server checked them can hold anything; entries that aren't numbers are dropped.
local function readOutfitCode(text)
    local ok, code = pcall(json.decode, text)
    if not ok or type(code) ~= "table" or type(code.Clothes) ~= "table" or type(code.Props) ~= "table" then
        return nil
    end
    local function slots(source)
        local out = {}
        for k, v in pairs(source) do
            if tonumber(k) and type(v) == "table" and type(v.Item) == "number" and type(v.Texture) == "number" then
                out[tostring(k)] = v
            end
        end
        return out
    end
    return { Clothes = slots(code.Clothes), Props = slots(code.Props) }
end

-- The clothing itself (collection references, old codes read with the captured pre-change
-- layout, model checks) is handled by vMenu's C# side; see docs/clothing-refs.md.
local function clothing(name, ...)
    local ok, reply = pcall(function(...)
        local resource = exports[GetCurrentResourceName()]
        return resource[name](resource, ...)
    end, ...)
    local decoded = nil
    if ok and type(reply) == "string" then
        local read, value = pcall(json.decode, reply)
        decoded = read and value or nil
    end
    if type(decoded) ~= "table" then
        lib.print.error(format("clothing export %s failed: %s", name, tostring(reply)))
        return { ok = false, message = "Error loading outfit, please try again!" }
    end
    return decoded
end

---@class loadSharedOutfit
---@param name string
exports("loadSharedOutfit", function(name)

    if not name or type(name) ~= "string" then
        return lib.print.error("export: Tried to load outfit failed, no name provided.")
    end
    
    name = string.sub(name, 8)

    if Cooldown then
        Config.Notify("vMenu", "You must wait before loading another outfit!", "error", 6500)
        return false
    end

    local Existing = GetResourceKvpString(format("mp_ped_%s", name))
    if not Existing then
        lib.print.debug("Outfit data does not exist somehow?")
        Config.Notify("vMenu", "Error loading outfit, please try again!", "error", 6500)
        return false
    end

    local input = lib.inputDialog("Enter Outfit Code", {
        { type = 'number', label = 'Outfit Code', description = "The sharing code you were given.", icon = 'hashtag', required = true },
        { type = 'input', label = 'Save Name', description = "What should we save this outfit as?", icon = 'tag', max = 30, min = 3, required = true },
    })

    if not input then
        Config.Notify("vMenu", "You must enter a valid outfit code!", "error", 6500)
        return false
    end

    local code = input[1]
    local newName = input[2]

    local nameExists = GetResourceKvpString(format("mp_ped_%s", newName))
    if nameExists then
        Config.Notify("vMenu", "You have an outfit saved with that name already!", "error", 6500)
        return false
    end

    local Valid, created = lib.callback.await("vMenu:Outfits:Request", false, code)
    Cooldown = true
    SetTimeout(5000, function()
        Cooldown = false
        lib.print.debug("Outfit load cooldown expired.")
    end)

    if not Valid then
        Config.Notify("vMenu", "The outfit code you entered is invalid!", "error", 6500)
        return false
    end

    if type(Valid) ~= "string" or not readOutfitCode(Valid) then
        Config.Notify("vMenu", "This outfit code is damaged and can't be loaded.", "error", 6500)
        return false
    end

    -- Face, hair, tattoos and everything else come from this character; the code's clothes and
    -- props replace its own. Saved under the new name (and SaveName), never over the source.
    local reply = clothing("vMenuImportOutfitCode", Valid, tonumber(created) or 0, format("mp_ped_%s", name), format("mp_ped_%s", newName))
    if not reply.ok then
        Config.Notify("vMenu", reply.message or "Error loading outfit, please try again!", "error", 6500)
        return false
    end

    Config.Notify("vMenu", reply.message or "Outfit has been successfully loaded!", "success", 6500)

    return true
end)

exports("loadOutfitFromCode", function(outfitCode)
    if not outfitCode then
        return lib.print.error("export: Tried to load outfit failed, no code provided.")
    end
    if type(outfitCode) ~= "number" then
        return lib.print.error("export: Tried to load outfit failed, invalid code provided. (not a number)")
    end

    local Valid, created = lib.callback.await("vMenu:Outfits:Request", false, outfitCode)
    if not Valid then
        lib.print.debug(format("export: Failed to load outfit with code: %s", outfitCode))
        return false
    end

    if type(Valid) ~= "string" or not readOutfitCode(Valid) then
        lib.print.debug(format("export: Outfit code %s is damaged", outfitCode))
        return false
    end

    -- Resolved and validated before anything is changed; pieces that can't be matched are left off.
    local reply = clothing("vMenuApplyOutfitCode", Valid, tonumber(created) or 0)
    if not reply.ok then
        Config.Notify("vMenu", reply.message or "This outfit code can't be loaded.", "error", 6500)
        return false
    end

    return true
end)

if Config.OutfitSharing.CommandEnabled then
    TriggerEvent("chat:addSuggestion", "/loadoutfitfromcode", "Load an outfit from a code", {
        { name = "code", help = "The outfit code you were given." }
    })
    RegisterCommand("loadoutfitfromcode", function(source, args)
        if #args ~= 1 then
            Config.Notify("vMenu", "Invalid Usage! \n\n /loadoutfitfromcode <code>", "error", 6500)
            return
        end
        if not tonumber(args[1]) then
            Config.Notify("vMenu", "You must provide a valid outfit code!", "error", 6500)
            return
        end
        exports[GetCurrentResourceName()]:loadOutfitFromCode(tonumber(args[1]))
    end, false)
end

AddEventHandler("vMenu:Outfits:GenerateCode", function(name)
    name = string.sub(name, 8) -- this removes the prefix of mp_ped_ from the name used to store kvp data

    if GenerateCooldown then
        Config.Notify("vMenu", "You must wait before generating another outfit code!", "error", 6500)
        return
    end

    local Existing = GetResourceKvpString(format("mp_ped_%s", name))
    if not Existing then
        Config.Notify("vMenu", "You do not have a MP ped saved with this name!", "error", 6500)
        return
    end

    -- The character's clothes and props (no head or hair), with its model and collection references.
    local reply = clothing("vMenuBuildOutfitCode", format("mp_ped_%s", name))
    if not reply.ok or type(reply.code) ~= "table" then
        Config.Notify("vMenu", reply.message or "Failed to generate outfit code!", "error", 6500)
        return
    end
    local ToSave = reply.code

    local Generated, errorMessage = lib.callback.await("vMenu:Outfits:Generate", false, ToSave)
    GenerateCooldown = true
    SetTimeout(15000, function()
        GenerateCooldown = false
        lib.print.debug("Outfit load cooldown expired.")
    end)

    if not Generated then
        Config.Notify("vMenu", errorMessage or "Failed to generate outfit code!", "error", 6500)
        return
    end

    print(format("Outfit code created: %s", Generated))
    Config.Notify("vMenu", "Outfit code has been generated - id: #" .. Generated, "success", 10000)
    lib.setClipboard(Generated)
end)
