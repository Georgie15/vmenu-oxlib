-- Runs INSIDE vMenu, alongside the rebuilt server DLL. Keep frequent lease
-- renewals in Lua: the old C# dynamic export calls caused allocation/GC spikes.
local requested, inactive = {}, {}
local lastWarning
local staminaAces = { 'vMenu.PlayerOptions.UnlimitedStamina',
    'vMenu.PlayerOptions.All', 'vMenu.Everything' }
local function elapsed(now, before) return (now-before) % 4294967296 end
local function allowed(src)
    for _, ace in ipairs(staminaAces) do
        local value = IsPlayerAceAllowed(src, ace)
        if value == true or value == 1 then return true end
    end
    return false
end
local function telemetry(method, ...)
    if GetResourceState('psrp_telemetry') ~= 'started' then return false end
    local ok, result = pcall(function(...)
        return exports.psrp_telemetry[method](exports.psrp_telemetry, ...)
    end, ...)
    if not ok then
        local now = GetGameTimer()
        if not lastWarning or elapsed(now, lastWarning) >= 60000 then
            lastWarning = now
            print('^3[vMenu] Stamina telemetry integration unavailable: '..tostring(result)..'^7')
        end
    end
    return ok and result ~= false
end
RegisterNetEvent('vMenu:PSRP:StaminaMode', function(active)
    local src = tonumber(source)
    if not src or src <= 0 or type(active) ~= 'boolean' then return end
    if active and allowed(src) then
        requested[src], inactive[src] = GetGameTimer(), nil
    else
        requested[src] = nil
        -- Also tolerates old clients that still send false every two seconds.
        -- Retry a failed revocation; a successful repeated false is a no-op.
        if not inactive[src] then
            inactive[src] = telemetry('SetAuthorizedStaminaMode', src, false) or nil
        end
    end
end)
AddEventHandler('playerDropped', function()
    local src = tonumber(source)
    if src then requested[src], inactive[src] = nil, nil end
end)
CreateThread(function()
    while true do
        Wait(1000)
        telemetry('StaminaIntegrationReady')
        local now = GetGameTimer()
        for src, at in pairs(requested) do
            local valid = GetPlayerName(src) ~= nil and elapsed(now, at) < 5000 and allowed(src)
            if not valid then requested[src] = nil end
            local ok = telemetry('SetAuthorizedStaminaMode', src, valid)
            if not valid then inactive[src] = ok or nil end
        end
    end
end)
