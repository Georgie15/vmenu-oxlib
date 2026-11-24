-- Outfit codes. A character's outfits page (vMenu's C#, MpPedCustomization.CreateOutfitsMenu) loads
-- a code as an outfit and makes codes from outfits; the exports below do its server side
-- (server/oufitCodes.lua). exports.vMenu:loadOutfitFromCode(id) puts a code straight on, for other
-- resources and /loadoutfitfromcode (config_client.lua).

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

-- Fetches a code from the server: its JSON and creation time, or nil and why not. The server's
-- own reason is passed on when it gives one (codes switched off, too quick, a database error).
local function requestCode(id)
    local data, created = lib.callback.await("vMenu:Outfits:Request", false, id)
    Cooldown = true
    SetTimeout(5000, function()
        Cooldown = false
        lib.print.debug("Outfit load cooldown expired.")
    end)
    if not data then
        return nil, type(created) == "string" and created or format("There's no outfit code #%s.", id)
    end
    if type(data) ~= "string" or not readOutfitCode(data) then
        return nil, "This outfit code is damaged and can't be loaded."
    end
    return data, tonumber(created) or 0
end

-- An outfit name as typed: trimmed, up to 30 characters, no | or ~. The same rules as vMenu's
-- CharacterWardrobe.CleanName, which checks it again. Nil and why when it can't be used.
local function cleanName(text)
    local name = tostring(text or ""):match("^%s*(.-)%s*$")
    if (utf8.len(name) or #name) > 30 then
        return nil, "Names can be up to 30 characters."
    end
    if name:find("[|~%c]") then
        return nil, "Names can't contain | or ~."
    end
    return name
end

---Load Outfit Code, on a character's outfits page. Asks for the code and a name, refuses a name the list it goes
---into already has, and fetches the code. vMenu's C# checks it and saves it as an outfit.
---@param takenNamesJson string JSON list of the outfit names already in that list
---@return string JSON { ok = true, id, code, created, name, wear } or { ok = false, message } (no message: the dialog was closed)
exports("requestOutfitCode", function(takenNamesJson)
    Wait(0) -- vMenu awaits this export, and only an export that yields gives it something to await
    if Cooldown then
        return json.encode({ ok = false, message = "Wait a few seconds before loading another outfit code." })
    end

    local input = lib.inputDialog("Load Outfit Code", {
        { type = 'number', label = 'Outfit Code', description = "The number you were given, without the #.", icon = 'hashtag', required = true, min = 1 },
        { type = 'input', label = 'Outfit Name', description = "Leave it empty to name it after the code.", icon = 'tag', max = 30 },
        { type = 'checkbox', label = 'Wear it now', checked = true },
    })
    if not input then
        return json.encode({ ok = false })
    end

    local id = tonumber(input[1])
    if not id or id < 1 or id ~= math.floor(id) then
        return json.encode({ ok = false, message = "Outfit codes are whole numbers, like 1234." })
    end
    id = math.floor(id)

    local name, why = cleanName(input[2])
    if not name then
        return json.encode({ ok = false, message = why })
    end
    if name == "" then
        name = format("Code %d", id)
    end
    local read, taken = pcall(json.decode, takenNamesJson or "[]")
    for _, used in ipairs(read and type(taken) == "table" and taken or {}) do
        if used == name then
            return json.encode({ ok = false, message = format("An outfit called %s is already saved there. Pick another name.", name) })
        end
    end

    local code, created = requestCode(id)
    if not code then
        return json.encode({ ok = false, message = created })
    end
    return json.encode({ ok = true, id = id, code = code, created = created, name = name, wear = input[3] == true })
end)

---Share and Share What I'm Wearing, on a character's outfits page. Stores the code vMenu built and copies its number.
---@param codeJson string the code vMenu built (ClothingSaves.BuildCode)
---@return string JSON { ok = true, id } or { ok = false, message }
exports("shareOutfitCode", function(codeJson)
    Wait(0) -- see requestOutfitCode
    if GenerateCooldown then
        return json.encode({ ok = false, message = "You can make one outfit code every 15 seconds. Try again in a moment." })
    end

    local read, code = pcall(json.decode, codeJson or "")
    if not read or type(code) ~= "table" then
        return json.encode({ ok = false, message = "This outfit can't be shared." })
    end

    local id, why = lib.callback.await("vMenu:Outfits:Generate", false, code)
    if not id then
        return json.encode({ ok = false, message = why or "The outfit code couldn't be made. Try again in a moment." })
    end
    GenerateCooldown = true
    SetTimeout(15000, function()
        GenerateCooldown = false
        lib.print.debug("Outfit generate cooldown expired.")
    end)

    print(format("Outfit code created: %s", id))
    lib.setClipboard(tostring(id))
    return json.encode({ ok = true, id = id })
end)

---Puts an outfit code straight on the player, without saving it. For other resources and
---/loadoutfitfromcode; players load codes on a character's outfits page (Saved Characters).
---@param outfitCode number
---@return boolean
exports("loadOutfitFromCode", function(outfitCode)
    if type(outfitCode) ~= "number" then
        lib.print.error("export: loadOutfitFromCode needs the outfit code as a number.")
        return false
    end

    local code, created = requestCode(outfitCode)
    if not code then
        Config.Notify("vMenu", created, "error", 6500)
        return false
    end

    -- Resolved and validated before anything is changed; pieces that can't be matched are left off.
    local reply = clothing("vMenuApplyOutfitCode", code, created)
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
