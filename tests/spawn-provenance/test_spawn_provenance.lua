-- Run from the repo root: lua tests/spawn-provenance/test_spawn_provenance.lua
-- Runs the production client/spawnProvenance.lua against a fake FiveM runtime: the ticket and
-- the spawned vehicle can arrive in either order, the vehicle may take a few frames to be
-- networked, and failed or late spawns must not be claimed.

local function check(condition, message)
    if not condition then error('FAIL: ' .. message, 2) end
end

local function readFile(path)
    local f = assert(io.open(path, 'rb'))
    local text = f:read('a')
    f:close()
    return text
end

local function newWorld()
    local world = { now = 0, handlers = {}, server = {}, threads = {}, entities = {} }

    _G.GetGameTimer = function() return world.now end
    _G.AddEventHandler = function(name, fn) world.handlers[name] = fn end
    _G.RegisterNetEvent = function(name, fn) world.handlers[name] = fn end
    _G.TriggerServerEvent = function(name, ...) world.server[#world.server + 1] = { name = name, args = { ... } } end
    _G.Wait = function(ms) coroutine.yield(ms or 0) end
    _G.CreateThread = function(fn)
        local thread = { co = coroutine.create(fn), wakeAt = world.now }
        world.threads[#world.threads + 1] = thread
    end
    _G.DoesEntityExist = function(h) return world.entities[h] ~= nil end
    _G.NetworkGetEntityIsNetworked = function(h) return world.entities[h] ~= nil and world.entities[h].networked end
    _G.NetworkGetNetworkIdFromEntity = function(h) return world.entities[h] and world.entities[h].netId or 0 end
    math.random = function() return 1 end

    -- Advance time in 10 ms frames, resuming threads that are due.
    function world.run(ms)
        local untilAt = world.now + (ms or 0)
        repeat
            for _, thread in ipairs(world.threads) do
                if coroutine.status(thread.co) ~= 'dead' and thread.wakeAt <= world.now then
                    local ok, waitMs = coroutine.resume(thread.co)
                    assert(ok, waitMs)
                    thread.wakeAt = world.now + (waitMs or 0)
                end
            end
            if world.now >= untilAt then break end
            world.now = world.now + 10
        until false
    end

    function world.sent(name)
        local found = {}
        for _, e in ipairs(world.server) do if e.name == name then found[#found + 1] = e end end
        return found
    end

    function world.fire(name, ...) world.handlers[name](...) end

    assert(load(readFile('build/vMenu/client/spawnProvenance.lua'), 'spawnProvenance.lua'))()
    return world
end

-- Ticket before the vehicle.
local w = newWorld()
w.fire('vMenu:telemetry:queueVehicleSpawn')
local begin = w.sent('vMenu:telemetry:beginVehicleSpawn')
check(#begin == 1, 'one ticket request per spawn')
local request = begin[1].args[1]
w.fire('vMenu:telemetry:vehicleSpawnTicket', request, 'nonce-1')
w.run(50)
check(#w.sent('psrp_telemetry:sv:claimVehicleSpawn') == 0, 'nothing claimed before the vehicle exists')
w.entities[100] = { networked = true, netId = 55 }
w.fire('vMenu:telemetry:vehicleSpawned', 100)
w.run(50)
local claims = w.sent('psrp_telemetry:sv:claimVehicleSpawn')
check(#claims == 1 and claims[1].args[1] == 'nonce-1' and claims[1].args[2] == 55, 'ticket then vehicle: claimed with the vehicle net id')

-- Vehicle before the ticket, networked a few frames later.
w = newWorld()
w.fire('vMenu:telemetry:queueVehicleSpawn')
request = w.sent('vMenu:telemetry:beginVehicleSpawn')[1].args[1]
w.entities[200] = { networked = false, netId = 0 }
w.fire('vMenu:telemetry:vehicleSpawned', 200)
w.fire('vMenu:telemetry:vehicleSpawnTicket', request, 'nonce-2')
w.run(40)
check(#w.sent('psrp_telemetry:sv:claimVehicleSpawn') == 0, 'waits while the vehicle is not networked yet')
w.entities[200] = { networked = true, netId = 77 }
w.run(40)
claims = w.sent('psrp_telemetry:sv:claimVehicleSpawn')
check(#claims == 1 and claims[1].args[1] == 'nonce-2' and claims[1].args[2] == 77, 'vehicle then ticket: claimed once networked')

-- CreateVehicle failed.
w = newWorld()
w.fire('vMenu:telemetry:queueVehicleSpawn')
request = w.sent('vMenu:telemetry:beginVehicleSpawn')[1].args[1]
w.fire('vMenu:telemetry:vehicleSpawned', 0)
w.fire('vMenu:telemetry:vehicleSpawnTicket', request, 'nonce-3')
w.run(100)
check(#w.sent('psrp_telemetry:sv:claimVehicleSpawn') == 0, 'a failed spawn is never claimed')

-- Ticket arriving too late.
w = newWorld()
w.fire('vMenu:telemetry:queueVehicleSpawn')
request = w.sent('vMenu:telemetry:beginVehicleSpawn')[1].args[1]
w.entities[300] = { networked = true, netId = 9 }
w.fire('vMenu:telemetry:vehicleSpawned', 300)
w.run(4500)
w.fire('vMenu:telemetry:vehicleSpawnTicket', request, 'nonce-4')
w.run(50)
check(#w.sent('psrp_telemetry:sv:claimVehicleSpawn') == 0, 'a ticket after the 4 s window is ignored')

-- Two spawns in a row each claim their own vehicle.
w = newWorld()
w.fire('vMenu:telemetry:queueVehicleSpawn')
local first = w.sent('vMenu:telemetry:beginVehicleSpawn')[1].args[1]
w.entities[400] = { networked = true, netId = 40 }
w.fire('vMenu:telemetry:vehicleSpawned', 400)
w.fire('vMenu:telemetry:queueVehicleSpawn')
local second = w.sent('vMenu:telemetry:beginVehicleSpawn')[2].args[1]
check(first ~= second, 'each spawn gets its own request id')
w.entities[500] = { networked = true, netId = 50 }
w.fire('vMenu:telemetry:vehicleSpawned', 500)
w.fire('vMenu:telemetry:vehicleSpawnTicket', second, 'nonce-b')
w.fire('vMenu:telemetry:vehicleSpawnTicket', first, 'nonce-a')
w.run(50)
claims = w.sent('psrp_telemetry:sv:claimVehicleSpawn')
local byTicket = {}
for _, c in ipairs(claims) do byTicket[c.args[1]] = c.args[2] end
check(#claims == 2 and byTicket['nonce-a'] == 40 and byTicket['nonce-b'] == 50, 'back-to-back spawns claim their own vehicles')

w.run(1100)
check(#w.sent('vMenu:telemetry:spawnProvenanceReady') == 1, 'ready handshake still sent')
print('PASS: ticket/vehicle in either order, network delay, failed and late spawns, back-to-back spawns')

-- Wiring: no world scan, and the spawn core (not canDoInteraction) drives it.
local source = readFile('build/vMenu/client/spawnProvenance.lua')
check(not source:find('GetGamePool', 1, true), 'no longer scans every vehicle in the world')
local integrations = readFile('build/vMenu/client/integrations.lua')
check(not integrations:find('vMenu:telemetry:queueVehicleSpawn', 1, true), 'canDoInteraction no longer starts telemetry')
local core = readFile('vMenu/CommonFunctions.cs')
local queueAt = core:find('TriggerEvent("vMenu:telemetry:queueVehicleSpawn");', 1, true)
local createAt = core:find('new Vehicle(CreateVehicle(vehicleHash', 1, true)
local spawnedAt = core:find('TriggerEvent("vMenu:telemetry:vehicleSpawned", vehicle.Handle);', 1, true)
check(queueAt and createAt and spawnedAt and queueAt < createAt and createAt < spawnedAt, 'spawn core: queue, create, then hand over the vehicle')
print('PASS: wiring')

-- Server: one ticket per spawn. Requests closer together than half the spawner cooldown are refused,
-- so a client can't hold a stream of wildcard tickets.
local function newServer(cooldown)
    local srv = { now = 50000, handlers = {}, issued = 0, toClient = {}, aceAllowed = true }
    _G.GetGameTimer = function() return srv.now end
    _G.GetConvarInt = function(name, default) if name == 'vmenu_vehicle_spawner_cooldown' then return cooldown end return default end
    _G.RegisterNetEvent = function(name, fn) srv.handlers[name] = fn end
    _G.AddEventHandler = function(name, fn) srv.handlers[name] = fn end
    _G.IsPlayerAceAllowed = function() return srv.aceAllowed end
    _G.GetResourceState = function() return 'started' end
    _G.TriggerClientEvent = function(name, target, ...) srv.toClient[#srv.toClient + 1] = { name = name, target = target, args = { ... } } end
    _G.exports = setmetatable({ psrp_telemetry = { IssueVehicleSpawnTicket = function() srv.issued = srv.issued + 1; return true, 'nonce-' .. srv.issued end } },
        { __call = function(_, name, fn) srv.exportsFn = fn end })
    local realPrint = print
    _G.print = function() end
    assert(load(readFile('build/vMenu/server/spawnProvenance.lua'), '@server/spawnProvenance.lua'))()
    _G.print = realPrint
    function srv.begin(src, request) _G.source = src; srv.handlers['vMenu:telemetry:beginVehicleSpawn'](request or 'VM-0000abcd-0001'); _G.source = nil end
    function srv.drop(src) _G.source = src; srv.handlers['playerDropped'](); _G.source = nil end
    return srv
end

local srv = newServer(1000)
srv.begin(1)
check(srv.issued == 1 and srv.toClient[1].name == 'vMenu:telemetry:vehicleSpawnTicket' and srv.toClient[1].target == 1, 'a spawn gets a ticket')
for _ = 1, 20 do srv.now = srv.now + 10; srv.begin(1) end
check(srv.issued == 1 and srv.exportsFn().throttled == 20, 'a burst of requests gets no more tickets')
srv.begin(2)
check(srv.issued == 2, 'each player has their own limit')
srv.now = srv.now + 500
srv.begin(1)
check(srv.issued == 3, 'allowed again after half the cooldown (network jitter)')
srv.drop(1)
srv.begin(1)
check(srv.issued == 4, 'the limit is forgotten when the player leaves')
srv = newServer(100)
srv.begin(1); srv.now = srv.now + 200; srv.begin(1); srv.now = srv.now + 200; srv.begin(1)
check(srv.issued == 2, 'never less than 250 ms apart, however short the cooldown')
srv = newServer(1000)
srv.aceAllowed = false
srv.begin(1)
check(srv.issued == 0 and srv.exportsFn().rejected == 1, 'no ticket without the spawner permission')
print('PASS: server issues at most one ticket per spawn interval, per player')
