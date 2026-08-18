-- Correlate vMenu's centralized spawn call with the newly created vehicle.
-- The server issues the source-bound nonce before CommonFunctions.CreateVehicle
-- runs; this watcher can only claim that nonce for a new, nearby local entity.

local INTEGRATION_VERSION = 2
local requestSequence = 0
local pending = {}
local reservedVehicles = {}
local lastBeginAt

local function elapsed(nowMs, thenMs)
    local value = nowMs - thenMs
    if value < 0 then value = value + 0x100000000 end
    return value
end

local function vehicleSnapshot()
    local seen = {}
    for _, vehicle in ipairs(GetGamePool('CVehicle')) do seen[vehicle] = true end
    local nowMs = GetGameTimer()
    for vehicle, reservedAt in pairs(reservedVehicles) do
        if elapsed(nowMs, reservedAt) > 5000 then reservedVehicles[vehicle] = nil end
    end
    return seen
end

local function localSpawnCandidate(vehicle, seen, playerCoords)
    local reservedAt = reservedVehicles[vehicle]
    if reservedAt then
        if elapsed(GetGameTimer(), reservedAt) <= 5000 then return nil end
        reservedVehicles[vehicle] = nil
    end
    if seen[vehicle] or not DoesEntityExist(vehicle)
            or not NetworkGetEntityIsNetworked(vehicle)
            or NetworkGetEntityOwner(vehicle) ~= PlayerId() then
        return nil
    end
    local coords = GetEntityCoords(vehicle)
    local distance = #(coords - playerCoords)
    if distance > 40.0 then return nil end
    local belongs = false
    pcall(function() belongs = DoesEntityBelongToThisScript(vehicle, true) == true end)
    return { vehicle = vehicle, distance = distance, belongs = belongs }
end

local function beginVehicleSpawnTelemetry()
    local nowMs = GetGameTimer()
    -- SpawnVehicle(string) delegates to SpawnVehicle(uint), and both call
    -- canDoInteraction in the compiled client. Coalesce only that same-frame
    -- wrapper pair; genuinely separate spawns still receive separate tickets.
    if lastBeginAt and elapsed(nowMs, lastBeginAt) <= 100 then return end
    lastBeginAt = nowMs
    requestSequence = requestSequence + 1
    local generation = requestSequence
    local request = ('VM-%08x-%04x'):format(
        nowMs % 0x100000000,
        (generation * 7919 + math.random(0, 0xffff)) % 0x10000)
    pending[request] = {
        generation = generation,
        seen = vehicleSnapshot(),
        createdAt = nowMs,
    }
    CreateThread(function()
        Wait(4100)
        if pending[request] and pending[request].generation == generation then
            pending[request] = nil
        end
    end)
    -- Queue the server request before CommonFunctions returns to CreateVehicle.
    -- No callback is awaited inside this cross-runtime export.
    TriggerServerEvent('vMenu:telemetry:beginVehicleSpawn', request)
end

AddEventHandler('vMenu:telemetry:queueVehicleSpawn', beginVehicleSpawnTelemetry)

RegisterNetEvent('vMenu:telemetry:vehicleSpawnTicket', function(request, ticket)
    local context = pending[request]
    pending[request] = nil
    if not context or elapsed(GetGameTimer(), context.createdAt) > 4000
            or type(ticket) ~= 'string' or ticket == '' then return end

    CreateThread(function()
        local scanStartedAt = GetGameTimer()
        while elapsed(GetGameTimer(), scanStartedAt) <= 3000 do
            local ped = PlayerPedId()
            local playerCoords = GetEntityCoords(ped)
            local best
            for _, vehicle in ipairs(GetGamePool('CVehicle')) do
                local candidate = localSpawnCandidate(vehicle, context.seen, playerCoords)
                if candidate and (not best
                        or (candidate.belongs and not best.belongs)
                        or (candidate.belongs == best.belongs
                            and candidate.distance < best.distance)) then
                    best = candidate
                end
            end
            if best then
                local netId = NetworkGetNetworkIdFromEntity(best.vehicle)
                if netId and netId > 0 then
                    -- Separate overlapping spawn requests cannot consume the
                    -- same new entity. The short reservation expires naturally
                    -- so an eventual handle reuse is not permanently hidden.
                    reservedVehicles[best.vehicle] = GetGameTimer()
                    TriggerServerEvent('psrp_telemetry:sv:claimVehicleSpawn',
                        ticket, netId)
                    return
                end
            end
            Wait(0)
        end
    end)
end)

CreateThread(function()
    Wait(1000)
    TriggerServerEvent('vMenu:telemetry:spawnProvenanceReady', INTEGRATION_VERSION)
end)
