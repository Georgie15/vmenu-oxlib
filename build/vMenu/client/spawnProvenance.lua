-- Correlate vMenu's centralized spawn call with the newly created vehicle.
-- CommonFunctions.SpawnVehicleCore triggers vMenu:telemetry:queueVehicleSpawn right before
-- CreateVehicle and vMenu:telemetry:vehicleSpawned with the new vehicle right after. The server
-- issues a source-bound nonce for the request; once both the nonce and the vehicle are known the
-- vehicle's net id is claimed with it. This used to guess the vehicle by scanning every vehicle
-- in the world for up to 3 seconds.

local INTEGRATION_VERSION = 2
local REQUEST_TTL_MS = 4000
local NETWORK_WAIT_MS = 3000

local requestSequence = 0
local pending = {}
local awaitingVehicle

local function elapsed(nowMs, thenMs)
    local value = nowMs - thenMs
    if value < 0 then value = value + 0x100000000 end
    return value
end

local function claim(request)
    local context = pending[request]
    if not context or not context.ticket or not context.vehicle then return end
    pending[request] = nil

    CreateThread(function()
        local startedAt = GetGameTimer()
        local vehicle = context.vehicle
        -- A freshly created vehicle can take a few frames to be registered on the network.
        while elapsed(GetGameTimer(), startedAt) <= NETWORK_WAIT_MS do
            if not DoesEntityExist(vehicle) then return end
            if NetworkGetEntityIsNetworked(vehicle) then
                local netId = NetworkGetNetworkIdFromEntity(vehicle)
                if netId and netId > 0 then
                    TriggerServerEvent('psrp_telemetry:sv:claimVehicleSpawn', context.ticket, netId)
                    return
                end
            end
            Wait(0)
        end
    end)
end

local function beginVehicleSpawnTelemetry()
    local nowMs = GetGameTimer()
    requestSequence = requestSequence + 1
    local generation = requestSequence
    local request = ('VM-%08x-%04x'):format(
        nowMs % 0x100000000,
        (generation * 7919 + math.random(0, 0xffff)) % 0x10000)
    pending[request] = { generation = generation, createdAt = nowMs }
    awaitingVehicle = request
    CreateThread(function()
        Wait(REQUEST_TTL_MS + 100)
        if pending[request] and pending[request].generation == generation then
            pending[request] = nil
        end
    end)
    -- Queue the server request before CommonFunctions returns to CreateVehicle.
    TriggerServerEvent('vMenu:telemetry:beginVehicleSpawn', request)
end

AddEventHandler('vMenu:telemetry:queueVehicleSpawn', beginVehicleSpawnTelemetry)

AddEventHandler('vMenu:telemetry:vehicleSpawned', function(vehicle)
    local request = awaitingVehicle
    awaitingVehicle = nil
    local context = request and pending[request]
    if not context then return end
    if type(vehicle) ~= 'number' or vehicle == 0 then
        -- CreateVehicle failed: nothing to claim.
        pending[request] = nil
        return
    end
    context.vehicle = vehicle
    claim(request)
end)

RegisterNetEvent('vMenu:telemetry:vehicleSpawnTicket', function(request, ticket)
    local context = pending[request]
    if not context or elapsed(GetGameTimer(), context.createdAt) > REQUEST_TTL_MS
            or type(ticket) ~= 'string' or ticket == '' then
        pending[request] = nil
        return
    end
    context.ticket = ticket
    claim(request)
end)

CreateThread(function()
    Wait(1000)
    TriggerServerEvent('vMenu:telemetry:spawnProvenanceReady', INTEGRATION_VERSION)
end)
