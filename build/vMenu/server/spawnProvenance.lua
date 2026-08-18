RegisterNetEvent('vMenu:telemetry:beginVehicleSpawn', function(request)
    local src = tonumber(source)
    if not src or type(request) ~= 'string' or #request > 48
            or not request:match('^VM%-%x+%-%x+$')
            or not IsPlayerAceAllowed(src, 'vMenu.VehicleSpawner.Menu')
            or GetResourceState('psrp_telemetry') ~= 'started' then return end

    local ok, issued, nonce = pcall(function()
        -- The exact model is selected inside vMenu's compiled client. The claim
        -- still binds the observed entity/model/owner/bucket, and telemetry only
        -- permits this wildcard form when the invoking resource is vMenu.
        return exports.psrp_telemetry:IssueVehicleSpawnTicket(
            src, nil, 12, 'vMenu vehicle spawner approval')
    end)
    if ok and issued == true and type(nonce) == 'string' then
        TriggerClientEvent('vMenu:telemetry:vehicleSpawnTicket', src, request, nonce)
    end
end)
