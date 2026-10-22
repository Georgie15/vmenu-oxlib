-- Refuse requests while the feature is switched off (the callbacks are always registered).
local function outfitCodesEnabled()
    return GetConvarBool("vmenu_outfitcodes", false)
end

-- An outfit code is only clothes and props: { Clothes = { ["3"] = { Item = 4, Texture = 0 } }, Props = { ... } }.
-- Anything else is refused, so an importer never receives data vMenu can't apply.
local MAX_OUTFIT_BYTES = 8192

local function isWholeNumber(value, min, max)
    return type(value) == "number" and value == math.floor(value) and value >= min and value <= max
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

local function validOutfit(data)
    if type(data) ~= "table" or not validSlots(data.Clothes) or not validSlots(data.Props) then return false end
    for key in pairs(data) do
        if key ~= "Clothes" and key ~= "Props" then return false end
    end
    return #json.encode(data) <= MAX_OUTFIT_BYTES
end

-- Limits are per Rockstar license, so reconnecting doesn't reset them.
local function playerKey(source)
    return GetPlayerIdentifierByType(source, "license") or ("source:" .. source)
end

local userRequestCooldowns = {}
lib.callback.register(
    "vMenu:Outfits:Request",
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

        local response = exports.oxmysql:query_async('SELECT `data` FROM `vmenu_outfits` WHERE `id` = ?', {
            id
        })

        SetTimeout(3500, function()
            userRequestCooldowns[key] = nil
        end)

        if response then
            return response[1] and response[1].data or false
        end

        return false, "Failed to load outfit from database."
    end
)

local userGenerateCooldowns = {}
lib.callback.register(
    "vMenu:Outfits:Generate",
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
