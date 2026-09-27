-- For exports, they must remain in this file. However, you can add the events into your other scripts like infiniteFuelToggled, licensePlateUpdated, and noclipToggled etc

local dutyAllows
local enforceOffIfDisallowed

-- Mirrors vMenu's own noclip toggle (the "noclip" action below). Leaving noclip
-- must never depend on clock-in; see canDoInteraction.
local noclipActive = false

---@class logAction
---@field action string
---@field data table
AddEventHandler("vMenu:Integrations:Action", function(action, data)
    if action == "infinitefuel" then
        ---@field enabled boolean
        lib.print.debug("Infinite Fuel: " .. tostring(data.enabled))
    elseif action == "licenseplate" then
        ---@field handle integer
        ---@field plate string
        lib.print.debug("License Plate Updated: " .. data.handle .. " - " .. data.plate)
        --[[
            Example Usage:
            if doesTextContainBlacklistedWord(data.plate) then
                SetVehicleNumberPlateText(data.handle, "PLATE")
                TriggerServerEvent("banplayer")
            end
        --]]
    elseif action == "noclip" then
        ---@field enabled boolean
        noclipActive = data and data.enabled == true
        TriggerServerEvent("jd-headtags:server:noclip", data and data.enabled == true)
        lib.print.debug("NoClip: " .. tostring(data.enabled))
    elseif action == "playernames" then
        ---@field enabled boolean
        if data and data.enabled == true and not dutyAllows() then
            lib.print.debug("Blocked Names while off-duty; reverting.")
            enforceOffIfDisallowed()
        else
            lib.print.debug("Player Names: " .. tostring(data.enabled))
        end
    elseif action == "playerblips" then
        ---@field enabled boolean
        if data and data.enabled == true and not dutyAllows() then
            lib.print.debug("Blocked Blips while off-duty; reverting.")
            enforceOffIfDisallowed()
        else
            lib.print.debug("Player Blips: " .. tostring(data.enabled))
        end
    end
end)

RegisterNetEvent("vMenu:Integrations:SetPlayerNames")
AddEventHandler("vMenu:Integrations:SetPlayerNames", function(enabled)
    TriggerEvent("vMenu:Integrations:Action", "playernames", { enabled = enabled })
end)

RegisterNetEvent("vMenu:Integrations:SetPlayerBlips")
AddEventHandler("vMenu:Integrations:SetPlayerBlips", function(enabled)
    TriggerEvent("vMenu:Integrations:Action", "playerblips", { enabled = enabled })
end)

local lastVehicleSpawnBlockedNotify = 0
local vehicleSpawnBlockedNotifyCooldown = 1500
local isEasyAdminFrozen = false
local isStaffClockedIn = false
local hasClockinBypass = false
local hasDutySync = false

local function getClockinExport(name)
    local ok, result = pcall(function()
        return exports["chimera-staff-clockin"][name]()
    end)

    if not ok then
        return false
    end

    return result == true
end

dutyAllows = function()
    if hasClockinBypass then
        return true
    end

    if hasDutySync then
        return isStaffClockedIn
    end

    if getClockinExport("HasBypass") then
        return true
    end

    return getClockinExport("IsClockedIn")
end

enforceOffIfDisallowed = function()
    if dutyAllows() then
        return
    end

    TriggerEvent("vMenu:Integrations:SetPlayerNames", false)
    TriggerEvent("vMenu:Integrations:SetPlayerBlips", false)
end

local function isTruthy(value)
    if value == true then
        return true
    end

    if type(value) == "number" then
        return value ~= 0
    end

    if type(value) == "string" then
        local normalizedValue = value:lower()
        return normalizedValue == "true" or normalizedValue == "1"
    end

    return false
end

local function getPlayerStateValue(key)
    local state = LocalPlayer and LocalPlayer.state
    if not state then
        return nil
    end

    return state[key]
end

local function notifyVehicleSpawnBlocked(reason)
    local now = GetGameTimer()
    if now < lastVehicleSpawnBlockedNotify then
        return
    end

    lastVehicleSpawnBlockedNotify = now + vehicleSpawnBlockedNotifyCooldown
    Config.Notify("Cannot Spawn Vehicle", reason, "error", 6500)
end

local function getVMenuBlockContext()
    if isTruthy(getPlayerStateValue("dead")) then
        return "death"
    end

    if isTruthy(getPlayerStateValue("isInHospitalize")) then
        return "hospital"
    end

    if isTruthy(getPlayerStateValue("isInCrowJail")) then
        return "crow_jail"
    end

    if isTruthy(getPlayerStateValue("isInPrison")) then
        return "prison"
    end

    if isEasyAdminFrozen then
        return "easyadmin"
    end

    return ""
end

local function getVMenuBlockDebugData()
    local deadState = getPlayerStateValue("dead")
    local hospitalState = getPlayerStateValue("isInHospitalize")
    local prisonState = getPlayerStateValue("isInPrison")
    local crowJailState = getPlayerStateValue("isInCrowJail")
    local blockContext = getVMenuBlockContext()

    return {
        deadState = deadState,
        hospitalState = hospitalState,
        prisonState = prisonState,
        crowJailState = crowJailState,
        isEasyAdminFrozen = isEasyAdminFrozen,
        blockContext = blockContext,
        staffClockedIn = isStaffClockedIn,
        hasClockinBypass = hasClockinBypass,
        hasDutySync = hasDutySync,
    }
end

local blockMessages = {
    death = "You cannot spawn vehicles while incapacitated.",
    hospital = "You cannot spawn vehicles while hospitalized.",
    crow_jail = "You cannot spawn vehicles while in staff jail.",
    prison = "You cannot spawn vehicles while in prison.",
    easyadmin = "You cannot spawn vehicles while frozen by staff.",
}

local function getVehicleSpawnBlockReason()
    local context = getVMenuBlockContext()
    return blockMessages[context] or nil
end

RegisterNetEvent("EasyAdmin:FreezePlayer")
AddEventHandler("EasyAdmin:FreezePlayer", function(toggle)
    isEasyAdminFrozen = toggle == true
end)

AddEventHandler("onResourceStart", function(resourceName)
    if resourceName ~= GetCurrentResourceName() then
        return
    end

    CreateThread(function()
        TriggerServerEvent("chimera-staff-clockin:syncMe")
        Wait(1000)
        enforceOffIfDisallowed()
    end)
end)

CreateThread(function()
    TriggerServerEvent("chimera-staff-clockin:syncMe")
    Wait(1000)
    enforceOffIfDisallowed()
end)

RegisterNetEvent("chimera-staff")
AddEventHandler("chimera-staff", function(data)
    if type(data) ~= "table" then
        return
    end

    hasDutySync = true

    if data.staff ~= nil then
        isStaffClockedIn = data.staff == true
    end

    if data.bypass ~= nil then
        hasClockinBypass = data.bypass == true
    end

    enforceOffIfDisallowed()
end)

RegisterNetEvent("chimera-staff-clockin:clockedIn")
AddEventHandler("chimera-staff-clockin:clockedIn", function(data)
    hasDutySync = true
    isStaffClockedIn = true
    hasClockinBypass = data and data.bypass == true

    if not hasClockinBypass then
        CreateThread(function()
            Wait(100)
            TriggerEvent("vMenu:Integrations:SetPlayerNames", true)
            TriggerEvent("vMenu:Integrations:SetPlayerBlips", true)
        end)
    end
end)

RegisterNetEvent("chimera-staff-clockin:clockedOut")
AddEventHandler("chimera-staff-clockin:clockedOut", function(data)
    hasDutySync = true
    isStaffClockedIn = false
    hasClockinBypass = data and data.bypass == true
    enforceOffIfDisallowed()
end)

RegisterCommand("vmenublockdebug", function()
    local debugData = getVMenuBlockDebugData()
    local description = ("context=%s | dead=%s | hospital=%s | crowJail=%s | prison=%s | easyadmin=%s"):format(
        debugData.blockContext ~= "" and debugData.blockContext or "none",
        tostring(debugData.deadState),
        tostring(debugData.hospitalState),
        tostring(debugData.crowJailState),
        tostring(debugData.prisonState),
        tostring(debugData.isEasyAdminFrozen)
    )

    lib.print.info(("[vMenu debug] %s"):format(description))

    Config.Notify("vMenu Debug", description, "inform", 10000)
end, false)

exports("getVehicleSpawnBlockReason", function()
    return getVehicleSpawnBlockReason() or ""
end)

exports("getVMenuBlockDebugData", function()
    return getVMenuBlockDebugData()
end)

exports("getVMenuBlockContext", function()
    return getVMenuBlockContext()
end)

--#region Example Interaction Checks
--[[
        Example Usage:
        -- Prevent weapon spawning while in vehicles or dead
        if type == "spawnweapon" then

            if cache.vehicle then
                lib.notify({
                    title = "Cannot Spawn Weapon",
                    description = "Exit vehicle first",
                    type = "error"
                })
                return false
            end

            if LocalPlayer.state.isDead then
                lib.notify({
                    title = "Cannot Spawn Weapon",
                    description = "You are dead",
                    type = "error"
                })
                return false
            end

            -- Example integration check
            local isInRestrictedArea = exports.zones:isInRestrictedArea()

            if isInRestrictedArea then
                lib.notify({
                    title = "Restricted Area",
                    description = "Cannot spawn weapons here",
                    type = "error"
                })
                return false
            end
        end

        -- Vehicle spawn restrictions based on player state and location
        if type == "spawnvehicle" then

            if IsEntityInWater(cache.ped) then
                lib.notify({
                    title = "Cannot Spawn Vehicle",
                    description = "Get out of water first",
                    type = "error"
                })
                return false
            end

            -- Example integration check
            local isAtSpawnPoint = exports.locations:isAtVehicleSpawn()

            if not isAtSpawnPoint then
                lib.notify({
                    title = "Invalid Location",
                    description = "Find a vehicle spawn point",
                    type = "error"
                })
                return false
            end
        end

        -- Loadout restrictions based on player state and permissions
        if type == "spawnloadout" then

            if IsPedRagdoll(cache.ped) then
                lib.notify({
                    title = "Cannot Spawn Loadout",
                    description = "Cannot equip while ragdolled",
                    type = "error"
                })
                return false
            end

            -- Example integration check
            local hasPermission = exports.permissions:hasLoadoutAccess()

            if not hasPermission then
                lib.notify({
                    title = "Access Denied",
                    description = "Unauthorized for loadouts",
                    type = "error"
                })
                return false
            end
        end

        -- Ammo refill restrictions based on combat and events
        if type == "refillammo" then
            if IsPedInMeleeCombat(cache.ped) then
                lib.notify({
                    title = "Cannot Refill Ammo",
                    description = "Not while in combat",
                    type = "error"
                })
                return false
            end

            -- Example integration check
            local eventActive = exports.events:isEventRunning()

            if eventActive then
                lib.notify({
                    title = "Event Active",
                    description = "Cannot refill during events",
                    type = "error"
                })
                return false
            end
        end
    --]]
--#endregion

---@class canDoInteraction
---@field action string
---@return boolean Returns true if the player can do the interaction, false otherwise
exports("canDoInteraction", function(action)
    -- vMenu runs this same check when noclip is switched off as well as on, so a
    -- staff member whose clock-in drops mid-flight would be stuck in noclip.
    -- Leaving noclip is always allowed; only entering it needs clock-in.
    if action == "noclip" and noclipActive then
        return true
    end

    if action == "noclip" or action == "playernames" or action == "playerblips" then
        return dutyAllows()
    end

    if action == "spawnvehicle" then
        local blockReason = getVehicleSpawnBlockReason()
        if blockReason then
            notifyVehicleSpawnBlocked(blockReason)
            return false
        end
        TriggerEvent("vMenu:telemetry:queueVehicleSpawn")
    elseif action == "changepedmodel" then
        local context = getVMenuBlockContext()
        if context == "death" then
            Config.Notify("vMenu", "You cannot change your ped while incapacitated.", "error", 6500)
            return false
        end
    end

    return true
end)

---@class customNotify
---@field description string
---@field ntype string
local function stripGtaColorCodes(s)
    if not s then return s end
    return (s:gsub("~[%w_]+~", ""))
end

AddEventHandler("vMenu:CustomNotify", function(description, ntype)
    Config.Notify("vMenu", stripGtaColorCodes(description), ntype, 6500)
end)
