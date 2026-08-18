-- Correlate vMenu's centralized spawn call with the newly created vehicle.
-- The server issues the source-bound nonce before CommonFunctions.CreateVehicle
-- runs; this watcher can only claim that nonce for a new, nearby local entity.

local spawnGeneration = 0
local pending = {}
local lastBeginAt

local function elapsed(nowMs, thenMs)
    local value = nowMs - thenMs
    if value < 0 then value = value + 0x100000000 end
    return value
end

local function vehicleSnapshot()
    local seen = {}
    for _, vehicle in ipairs(GetGamePool('CVehicle')) do seen[vehicle] = true end
    return seen
end

local function localSpawnCandidate(vehicle, seen, playerCoords)
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
    spawnGeneration = spawnGeneration + 1
    local generation = spawnGeneration
    local request = ('VM-%08x-%04x'):format(
        nowMs % 0x100000000,
        (generation * 7919 + math.random(0, 0xffff)) % 0x10000)
    pending[request] = {
        generation = generation,
        seen = vehicleSnapshot(),
        expires = nowMs + 4000,
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
    if not context or context.generation ~= spawnGeneration
            or GetGameTimer() > context.expires
            or type(ticket) ~= 'string' or ticket == '' then return end

    CreateThread(function()
        local deadline = GetGameTimer() + 3000
        while context.generation == spawnGeneration and GetGameTimer() <= deadline do
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
                    TriggerServerEvent('psrp_telemetry:sv:claimVehicleSpawn',
                        ticket, netId)
                    return
                end
            end
            Wait(0)
        end
    end)
end)
