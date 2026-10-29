-- Runs build/vMenu/client/integrations.lua with stubbed natives and checks the
-- PSRP purge-mode noclip drop. Run from the repository root:
--   lua tests/purge-noclip/test_purge_noclip.lua

local stub
stub = setmetatable({}, {
    __index = function() return stub end,
    __call = function() return nil end,
})

local handlers, stateHandlers, threads = {}, {}, {}
local commands, notices = {}, {}
local globalState = {}

local env = setmetatable({
    GlobalState = globalState,
    Config = { Notify = function(_, message) notices[#notices + 1] = message end },
    RegisterNetEvent = function() end,
    AddEventHandler = function(name, fn) handlers[name] = fn end,
    AddStateBagChangeHandler = function(key, _, fn) stateHandlers[key] = fn end,
    CreateThread = function(fn) threads[#threads + 1] = coroutine.create(fn) end,
    Wait = coroutine.yield,
    SetTimeout = function(_, fn) fn() end,
    GetConvar = function(_, default) return default end,
    ExecuteCommand = function(command) commands[#commands + 1] = command end,
    TriggerEvent = function(name, ...)
        if handlers[name] then handlers[name](...) end
    end,
}, {
    __index = function(_, key)
        local value = _G[key]
        if value ~= nil then return value end
        return stub
    end,
})

local chunk = assert(loadfile('build/vMenu/client/integrations.lua', 't', env))
chunk()

local function runThreads()
    for _ = 1, 10 do
        local alive = false
        for i, thread in ipairs(threads) do
            if coroutine.status(thread) ~= 'dead' then
                local ok, err = coroutine.resume(thread)
                assert(ok, err)
                alive = true
            end
            threads[i] = thread
        end
        if not alive then return end
    end
end

local function check(condition, label)
    if not condition then error('FAIL: ' .. label, 2) end
    print('PASS: ' .. label)
end

runThreads()

local function setNoclip(enabled)
    handlers['vMenu:Integrations:Action']('noclip', { enabled = enabled })
end

-- Staff member clocked in and flying in noclip.
handlers['chimera-staff-clockin:clockedIn']({ bypass = false })
setNoclip(true)
runThreads()

-- Ordinary clock-out (no purge): noclip stays their choice.
handlers['chimera-staff-clockin:clockedOut']({ bypass = false })
runThreads()
check(#commands == 0, 'clock-out outside the purge leaves noclip alone')

-- Purge starts: the state bag and the clock-out events both arrive.
handlers['chimera-staff-clockin:clockedIn']({ bypass = false })
runThreads()
globalState.purgeActive = true
stateHandlers.purgeActive()
handlers['chimera-staff-clockin:clockedOut']({ bypass = false })
handlers['chimera-staff']({ staff = false, bypass = false })
runThreads()
check(#commands == 1, 'purge drops noclip exactly once')
check(commands[1] == 'vMenu:Default:NoClip', 'drop uses the vMenu noclip key-mapping command')
check(#notices == 1, 'player is told why noclip ended')

-- vMenu reports noclip off; nothing more happens.
setNoclip(false)
stateHandlers.purgeActive()
runThreads()
check(#commands == 1, 'no toggle once noclip is off')

-- A bypass holder keeps noclip during the purge.
handlers['chimera-staff']({ staff = false, bypass = true })
setNoclip(true)
stateHandlers.purgeActive()
handlers['chimera-staff-clockin:clockedOut']({ bypass = true })
runThreads()
check(#commands == 1, 'bypass holders keep noclip during the purge')

print('All purge noclip checks passed')
