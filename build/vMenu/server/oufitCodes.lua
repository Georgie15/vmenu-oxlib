-- Refuse requests while the feature is switched off (the callbacks are always registered).
local function outfitCodesEnabled()
    return GetConvarBool("vmenu_outfitcodes", false)
end

-- An outfit code is clothes and props: { Clothes = { ["3"] = { Item = 4, Texture = 0 } }, Props = { ... } }.
-- Codes made since collection references (version 2) also carry the ped model, the layout the
-- numbers were written under and the references themselves:
--   { v = 2, Model = 1885233650, Layout = "<print>", Clothes = ..., Props = ...,
--     Refs = { v = 2, model = 1885233650, clothes = { ["11"] = { col = "mp_m_deptmerge2", drw = 40, tex = 2, rev = "...", pp = "..." } },
--              props = { ["0"] = { empty = true } } } }
-- Anything else is refused, so an importer never receives data vMenu can't apply.
local MAX_OUTFIT_BYTES = 8192

local function isWholeNumber(value, min, max)
    return type(value) == "number" and value == math.floor(value) and value >= min and value <= max
end

local function isText(value, max, pattern)
    return type(value) == "string" and #value <= max and value:match(pattern) ~= nil
end

local function validSlots(slots)
    if type(slots) ~= "table" then return false end
    local count = 0
    for key, item in pairs(slots) do
        count = count + 1
        if count > 16 or not isWholeNumber(tonumber(key), 0, 15) or type(item) ~= "table"
                or not isWholeNumber(item.Item, -1, 4096) or not isWholeNumber(item.Texture, -1, 255) then
            return false
        end
    end
    return true
end

-- One collection reference. A shared code never carries pieces still waiting for review.
local REF_FIELDS = { col = true, drw = true, tex = true, empty = true, rev = true, pp = true, lp = true }
local function validRef(item)
    if type(item) ~= "table" then return false end
    for key in pairs(item) do
        if not REF_FIELDS[key] then return false end
    end
    if item.empty ~= nil then
        return item.empty == true and item.col == nil and item.drw == nil and item.tex == nil
    end
    return isText(item.col, 64, "^[%w_%-%.]*$") and isWholeNumber(item.drw, 0, 4096) and isWholeNumber(item.tex, 0, 255)
        and (item.rev == nil or isText(item.rev, 64, "^[%w_%-%.]+$"))
        and (item.pp == nil or isText(item.pp, 32, "^%x+$"))
        and (item.lp == nil or isText(item.lp, 32, "^%x+$"))
end

local function validRefSlots(slots)
    if type(slots) ~= "table" then return false end
    local count = 0
    for key, item in pairs(slots) do
        count = count + 1
        if count > 16 or not isWholeNumber(tonumber(key), 0, 15) or not validRef(item) then
            return false
        end
    end
    return true
end

local function validRefs(refs, model)
    if type(refs) ~= "table" or refs.v ~= 2 or refs.model ~= model then return false end
    for key in pairs(refs) do
        if key ~= "v" and key ~= "model" and key ~= "clothes" and key ~= "props" then return false end
    end
    return validRefSlots(refs.clothes) and validRefSlots(refs.props)
end

local OUTFIT_FIELDS = { Clothes = true, Props = true, v = true, Model = true, Layout = true, Refs = true }
local function validOutfit(data)
    if type(data) ~= "table" or not validSlots(data.Clothes) or not validSlots(data.Props) then return false end
    for key in pairs(data) do
        if not OUTFIT_FIELDS[key] then return false end
    end
    if data.v ~= nil or data.Model ~= nil or data.Layout ~= nil or data.Refs ~= nil then
        -- Version 2: the model and references come together.
        if data.v ~= 2 or not isWholeNumber(data.Model, 1, 4294967295) or not validRefs(data.Refs, data.Model) then return false end
        if data.Layout ~= nil and not isText(data.Layout, 32, "^%x+$") then return false end
    end
    return #json.encode(data) <= MAX_OUTFIT_BYTES
end

-- Limits are per Rockstar license, so reconnecting doesn't reset them.
local function playerKey(source)
    return GetPlayerIdentifierByType(source, "license") or ("source:" .. source)
end

-- Both callbacks go through the request guard (server/guard/requestGuard.lua): an id must be a
-- number and an outfit a table before the checks below run.
local userRequestCooldowns = {}
VMenuGuard.callback(
    "vMenu:Outfits:Request",
    {
        args = { VMenuGuard.number },
        reject = function(_, why)
            if why == "rate-limited" then return false, "You are requesting outfits too quickly. Please wait a moment." end
            return false
        end,
    },
    function(source, id)

        if not outfitCodesEnabled() then
            return false, "Outfit codes are disabled on this server."
        end

        local key = playerKey(source)
        if userRequestCooldowns[key] then
            -- shouldnt be possible with the client side cooldowns but this is to protect against event spam
            lib.print.error(string.format("%s [%s] tried to request outfit too quickly.", GetPlayerName(source), source))
            return false, "You are requesting outfits too quickly. Please wait a moment."
        end

        if not id then
            lib.print.error(string.format("%s [%s] tried to load outfit with no id?", GetPlayerName(source), source))
            return false
        end

        if type(id) ~= "number" then
            lib.print.error(string.format("%s [%s] tried to load outfit with invalid id?", GetPlayerName(source), source))
            return false
        end

        userRequestCooldowns[key] = true

        -- The creation time tells the client whether an old code predates the captured clothing layout.
        local response = exports.oxmysql:query_async('SELECT `data`, UNIX_TIMESTAMP(`created`) AS `created` FROM `vmenu_outfits` WHERE `id` = ?', {
            id
        })

        SetTimeout(3500, function()
            userRequestCooldowns[key] = nil
        end)

        if response then
            local row = response[1]
            if not row or not row.data then
                return false
            end
            return row.data, tonumber(row.created) or 0
        end

        return false, "Failed to load outfit from database."
    end
)

local userGenerateCooldowns = {}
VMenuGuard.callback(
    "vMenu:Outfits:Generate",
    {
        args = { VMenuGuard.table },
        reject = function(_, why)
            if why == "rate-limited" then return false, "You are generating outfits too quickly. Please wait a moment." end
            return false, "This outfit can't be shared."
        end,
    },
    function(source, data)

        if not outfitCodesEnabled() then
            return false, "Outfit codes are disabled on this server."
        end

        local key = playerKey(source)
        if userGenerateCooldowns[key] then
            -- shouldnt be possible with the client side cooldowns but this is to protect against event spam
            lib.print.error(string.format("%s [%s] tried to generate outfit too quickly.", GetPlayerName(source), source))
            return false, "You are generating outfits too quickly. Please wait a moment."
        end

        if not data then
            lib.print.error(string.format("%s [%s] tried to save outfit with no data?", GetPlayerName(source), source))
            return false
        end

        if not validOutfit(data) then
            lib.print.error(string.format("%s [%s] tried to save outfit with invalid data?", GetPlayerName(source), source))
            return false, "This outfit can't be shared."
        end

        local discord = GetPlayerIdentifierByType(source, "discord") and
            GetPlayerIdentifierByType(source, "discord"):gsub("discord:", "") or false
        if not discord then
            return false, "You need to have Discord linked to generate outfit codes."
        end

        userGenerateCooldowns[key] = true

        local id = exports.oxmysql:insert_async('INSERT INTO `vmenu_outfits` (`discord_id`, `data`) VALUES (?, ?)', {
            discord, json.encode(data)
        })

        SetTimeout(12500, function()
            userGenerateCooldowns[key] = nil
        end)

        return id or false
    end
)

CreateThread(function()
    if GetConvarBool("vmenu_outfitcodes", false) then
        if GetResourceState("oxmysql") ~= "started" then
            for i = 1, 10 do
                lib.print.error("Outfit Code System is enabled but oxmysql is not started..")
            end
            return
        else
            lib.print.info("Outfit Code System is enabled.")
        end
    end
end)
