-- Loads a fresh copy of the production server/guard/requestGuard.lua (VMenuGuard) for tests that run
-- server scripts registering through it. Natives the guard reads and the test hasn't faked yet get
-- harmless defaults; a test sets its own (GetGameTimer, GetConvar, RegisterNetEvent, ...) first.
-- Usage: dofile('tests/lua/request_guard.lua')()
return function()
    local defaults = {
        GetConvar = function(_, default) return default end,
        GetConvarInt = function(_, default) return default end,
        GetGameTimer = function() return 0 end,
        GetPlayerName = function(src) return 'player' .. tostring(src) end,
        GetPlayerIdentifierByType = function() return nil end,
        AddEventHandler = function() end,
        RegisterNetEvent = function() end,
        SetTimeout = function() end,
        PerformHttpRequest = function() end,
    }
    for name, fn in pairs(defaults) do
        if _G[name] == nil then _G[name] = fn end
    end
    local f = assert(io.open('build/vMenu/server/guard/requestGuard.lua', 'rb'))
    local text = f:read('a')
    f:close()
    local realPrint = print
    _G.print = function() end
    assert(load(text, '@server/guard/requestGuard.lua'))()
    _G.print = realPrint
    return VMenuGuard
end
