local INTEGRATION_VERSION = 2
local readySources = {}
local status = { requests = 0, issued = 0, rejected = 0, errors = 0, throttled = 0 }

-- One ticket per spawn: the client can't spawn faster than vmenu_vehicle_spawner_cooldown, so
-- requests closer together than half of it (allowing for network jitter) are refused. Without
-- this a client could hold a stream of wildcard tickets to pass off vehicles it injected.
local lastTicketAt = {}
local function minTicketIntervalMs()
    return math.max(250, math.floor(GetConvarInt('vmenu_vehicle_spawner_cooldown', 1000) / 2))
end

RegisterNetEvent('vMenu:telemetry:spawnProvenanceReady', function(version)
    local src = tonumber(source)
    if not src or tonumber(version) ~= INTEGRATION_VERSION then return end
    readySources[src] = true
end)

AddEventHandler('playerDropped', function()
    local src = tonumber(source)
    if src then readySources[src], lastTicketAt[src] = nil, nil end
end)

RegisterNetEvent('vMenu:telemetry:beginVehicleSpawn', function(request)
    local src = tonumber(source)
    if not src or type(request) ~= 'string' or #request > 48
            or not request:match('^VM%-%x+%-%x+$') then return end
    status.requests = status.requests + 1
    local now = GetGameTimer()
    if lastTicketAt[src] and now - lastTicketAt[src] < minTicketIntervalMs() then
        status.throttled = status.throttled + 1
        return
    end
    lastTicketAt[src] = now
    if not IsPlayerAceAllowed(src, 'vMenu.VehicleSpawner.Menu')
            or GetResourceState('psrp_telemetry') ~= 'started' then
        status.rejected = status.rejected + 1
        return
    end

    local ok, issued, nonce = pcall(function()
        -- The exact model is selected inside vMenu's compiled client. The claim
        -- still binds the observed entity/model/owner/bucket, and telemetry only
        -- permits this wildcard form when the invoking resource is vMenu.
        return exports.psrp_telemetry:IssueVehicleSpawnTicket(
            src, nil, 12, 'vMenu vehicle spawner approval')
    end)
    if ok and issued == true and type(nonce) == 'string' then
        status.issued = status.issued + 1
        TriggerClientEvent('vMenu:telemetry:vehicleSpawnTicket', src, request, nonce)
    elseif not ok then
        status.errors = status.errors + 1
    else
        status.rejected = status.rejected + 1
    end
end)

exports('GetVehicleSpawnProvenanceStatus', function()
    local ready = 0
    for _ in pairs(readySources) do ready = ready + 1 end
    return {
        version = INTEGRATION_VERSION,
        readyClients = ready,
        requests = status.requests,
        issued = status.issued,
        rejected = status.rejected,
        errors = status.errors,
        throttled = status.throttled,
    }
end)

print(('[vMenu] vehicle spawn provenance bridge v%d loaded'):format(
    INTEGRATION_VERSION))
