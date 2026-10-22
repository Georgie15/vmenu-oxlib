-- Refuse requests while the feature is switched off (the callbacks are always registered).
local function vehicleCodesEnabled()
    return GetConvarBool("vmenu_vehiclecodes", false)
end

-- A vehicle code is a saved vehicle: plain values plus a few flat tables (colors, extras, mods).
-- Anything deeper, larger or of another type is refused, so an importer never receives data
-- vMenu can't read.
local MAX_VEHICLE_BYTES = 16384

local function plainValue(value)
    local kind = type(value)
    if kind == "number" then return value == value and value ~= math.huge and value ~= -math.huge end
    if kind == "string" then return #value <= 128 end
    return kind == "boolean"
end

local function validVehicle(data)
    if type(data) ~= "table" or (type(data.model) ~= "number" and type(data.model) ~= "string") then return false end
    local fields = 0
    for key, value in pairs(data) do
        fields = fields + 1
        if fields > 64 or type(key) ~= "string" or #key > 32 then return false end
        if type(value) == "table" then
            local entries = 0
            for innerKey, innerValue in pairs(value) do
                entries = entries + 1
                if entries > 128 or not plainValue(innerKey) or not plainValue(innerValue) then return false end
            end
        elseif not plainValue(value) then
            return false
        end
    end
    return #json.encode(data) <= MAX_VEHICLE_BYTES
end

-- Limits are per Rockstar license, so reconnecting doesn't reset them.
local function playerKey(source)
    return GetPlayerIdentifierByType(source, "license") or ("source:" .. source)
end

local userRequestCooldowns = {}
lib.callback.register(
    "vMenu:Vehicles:Request",
    function(source, id)

        if not vehicleCodesEnabled() then
            return false, "Vehicle codes are disabled on this server."
        end

        local key = playerKey(source)
        if userRequestCooldowns[key] then
            -- shouldnt be possible with the client side cooldowns but this is to protect against event spam
            lib.print.error(string.format("%s [%s] tried to request vehicle too quickly.", GetPlayerName(source), source))
            return false, "You are requesting vehicles too quickly. Please wait a moment."
        end

        if not id then
            lib.print.error(string.format("%s [%s] tried to load vehicle with no id?", GetPlayerName(source), source))
            return false
        end

        if type(id) ~= "number" then
            lib.print.error(string.format("%s [%s] tried to load vehicle with invalid id?", GetPlayerName(source), source))
            return false
        end

        userRequestCooldowns[key] = true

        local response = exports.oxmysql:query_async('SELECT `data` FROM `vmenu_vehicles` WHERE `id` = ?', {
            id
        })

        SetTimeout(3500, function()
            userRequestCooldowns[key] = nil
        end)

        if response then
            return response[1] and response[1].data or false
        end

        return false, "Failed to load vehicle from database."
    end
)

local userGenerateCooldowns = {}

lib.callback.register(
    "vMenu:Vehicles:Generate",
    function(source, data)

        if not vehicleCodesEnabled() then
            return false, "Vehicle codes are disabled on this server."
        end

        local key = playerKey(source)
        if userGenerateCooldowns[key] then
            -- shouldnt be possible with the client side cooldowns but this is to protect against event spam
            lib.print.error(string.format("%s [%s] tried to generate vehicle too quickly.", GetPlayerName(source), source))
            return false, "You are generating vehicles too quickly. Please wait a moment."
        end

        if not data then
            lib.print.error(string.format("%s [%s] tried to save vehicle with no data?", GetPlayerName(source), source))
            return false, "No data provided."
        end

        if not validVehicle(data) then
            lib.print.error(string.format("%s [%s] tried to save vehicle with invalid data?", GetPlayerName(source), source))
            return false, "This vehicle can't be shared."
        end
        -- Each importer gets their own CAD token and puts it in their own category.
        data.cadSaveToken = nil
        data.Category = "Uncategorized"

        local discord = GetPlayerIdentifierByType(source, "discord") and
            GetPlayerIdentifierByType(source, "discord"):gsub("discord:", "") or false
        if not discord then
            return false, "You need to have Discord linked to generate vehicle codes."
        end

        userGenerateCooldowns[key] = true

        local id = exports.oxmysql:insert_async('INSERT INTO `vmenu_vehicles` (`discord_id`, `data`) VALUES (?, ?)', {
            discord, json.encode(data)
        })

        SetTimeout(12500, function()
            userGenerateCooldowns[key] = nil
        end)

        return id or false
    end
)

CreateThread(function()
    if GetConvarBool("vmenu_vehiclecodes", false) then
        if GetResourceState("oxmysql") ~= "started" then
            for i = 1, 10 do
                lib.print.error("Vehicle Code System is enabled but oxmysql is not started..")
            end
            return
        else
            lib.print.info("Vehicle Code System is enabled.")
        end
    end
end)
