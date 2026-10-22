-- Run from the repo root: lua tests/photo-mode/test_photo_mode.lua
-- Runs the production client/photoMode.lua against a fake FiveM: the camera stays within 15 m of the
-- ped's head and never moves through anything solid, zoom is clamped, the help can be hidden, and
-- leaving restores the game camera.

local function check(condition, message)
    if not condition then error('FAIL: ' .. message, 2) end
end

local function readFile(path)
    local f = assert(io.open(path, 'rb'))
    local text = f:read('a')
    f:close()
    return text
end

-- CfxLua's vector3: arithmetic, and # is the length.
local vec = {}
vec.__index = vec
local function vector3(x, y, z) return setmetatable({ x = x, y = y, z = z }, vec) end
vec.__add = function(a, b) return vector3(a.x + b.x, a.y + b.y, a.z + b.z) end
vec.__sub = function(a, b) return vector3(a.x - b.x, a.y - b.y, a.z - b.z) end
vec.__mul = function(a, n) return vector3(a.x * n, a.y * n, a.z * n) end
vec.__len = function(a) return math.sqrt(a.x * a.x + a.y * a.y + a.z * a.z) end
_G.vector3 = vector3

local function newWorld(options)
    local w = { convar = options.convar or 'true', handlers = {}, notes = {}, pressed = {}, justPressed = {}, look = { 0, 0 },
        cam = nil, rendering = false, textUI = nil, wall = options.wall, speed = options.speed or 0.0, commands = {}, mappings = {} }
    _G.GetConvar = function(name, default)
        if name == 'vmenu_photo_mode' then return w.convar end
        if name == 'vmenu_keymapping_id' then return 'Psrp' end
        return default
    end
    _G.Config = { Notify = function(_, msg, kind) w.notes[#w.notes + 1] = { msg = msg, kind = kind } end }
    _G.lib = { showTextUI = function(text) w.textUI = text end, hideTextUI = function() w.textUI = nil end }
    _G.PlayerPedId = function() return 7 end
    _G.IsEntityDead = function() return false end
    _G.IsPedRagdoll = function() return false end
    _G.DoesEntityExist = function() return true end
    _G.GetVehiclePedIsIn = function() return options.vehicle or 0 end
    _G.GetEntitySpeed = function() return w.speed end
    _G.GetPedBoneCoords = function() return vector3(0.0, 0.0, 1.0) end
    _G.GetGameplayCamCoord = function() return vector3(0.0, -3.0, 1.5) end
    _G.GetGameplayCamRot = function() return vector3(0.0, 0.0, 0.0) end
    _G.GetGameplayCamFov = function() return 50.0 end
    _G.CreateCam = function() w.cam = { alive = true }; return 1 end
    _G.SetCamCoord = function(_, x, y, z) w.cam.pos = vector3(x, y, z) end
    _G.SetCamRot = function(_, x, y, z) w.cam.rot = vector3(x, y, z) end
    _G.SetCamFov = function(_, fov) w.cam.fov = fov end
    _G.RenderScriptCams = function(on) w.rendering = on end
    _G.DestroyCam = function() w.cam.alive = false end
    _G.DisableAllControlActions = function() end
    _G.EnableControlAction = function() end
    _G.HideHudAndRadarThisFrame = function() w.hudHidden = true end
    _G.IsDisabledControlPressed = function(_, control) return w.pressed[control] == true end
    _G.IsDisabledControlJustPressed = function(_, control) local p = w.justPressed[control]; w.justPressed[control] = nil; return p == true end
    _G.GetDisabledControlNormal = function(_, control) return control == 1 and w.look[1] or w.look[2] end
    -- A wall across y = 5 when options.wall is set.
    _G.StartExpensiveSynchronousShapeTestLosProbe = function(x1, y1, z1, x2, y2, z2) return { from = y1, to = y2 } end
    _G.GetShapeTestResult = function(probe) return 2, (w.wall and probe.to >= 5.0) and 1 or 0 end
    _G.AddEventHandler = function(name, fn) w.handlers[name] = fn end
    _G.RegisterCommand = function(name, fn) w.commands[name] = fn end
    _G.RegisterKeyMapping = function(name, label, kind, key) w.mappings[#w.mappings + 1] = { name = name, label = label, key = key } end
    _G.Wait = function() coroutine.yield() end
    _G.CreateThread = function(fn) w.thread = coroutine.create(fn) end
    assert(load(readFile('build/vMenu/client/photoMode.lua'), '@client/photoMode.lua'))()
    function w.start() w.handlers['vMenu:photoMode:start']() end
    function w.frames(n) for _ = 1, n do assert(coroutine.resume(w.thread)) end end
    function w.ended() return coroutine.status(w.thread) == 'dead' end
    return w
end

local off = newWorld({ convar = 'false' })
check(#off.mappings == 0 and off.handlers['vMenu:photoMode:start'], 'no key binding while photo mode is off')
off.start()
check(off.cam == nil and off.notes[1].msg:find('disabled', 1, true), 'refused while the convar is off')

local w = newWorld({})
check(#w.mappings == 1 and w.mappings[1].name == 'vMenu:Psrp:PhotoMode' and w.mappings[1].key == '', 'an unbound key binding using the server key mapping id')
w.start()
w.frames(1)
check(w.rendering and w.cam.alive and w.textUI and w.hudHidden, 'the camera takes over, the help shows and the HUD is hidden')
check(math.abs(w.cam.pos.y + 3.0) < 1e-6, 'it starts where the game camera was')

w.pressed[32] = true -- W
w.frames(300)
check(#(w.cam.pos - vector3(0.0, 0.0, 1.0)) <= 15.0 + 1e-6 and w.cam.pos.y > 13.5, 'flying forward stops at 15 m from the head')
w.pressed[32] = nil

w.justPressed[241] = true; w.frames(1)
check(w.cam.fov == 47.5, 'scrolling up zooms in')
for _ = 1, 40 do w.justPressed[241] = true; w.frames(1) end
check(w.cam.fov == 10.0, 'zoom stops at 10')

w.justPressed[74] = true; w.frames(1)
check(w.textUI == nil, 'H hides the help')
w.justPressed[74] = true; w.frames(1)
check(w.textUI ~= nil, 'H shows it again')

w.justPressed[177] = true
w.frames(1)
check(w.ended() and not w.rendering and not w.cam.alive and w.textUI == nil, 'Backspace leaves photo mode and gives the game camera back')
w.start()
check(w.thread and not w.ended(), 'photo mode can be started again')

local walled = newWorld({ wall = true })
walled.start()
walled.frames(1)
walled.pressed[32] = true
walled.frames(300)
check(walled.cam.pos.y < 5.0, 'the camera never passes through a wall')

local driving = newWorld({ vehicle = 12, speed = 10.0 })
driving.start()
check(driving.cam == nil and driving.notes[1].msg:find('Stop the vehicle', 1, true), 'not while the vehicle is moving')
print('PASS: photo mode radius, walls, zoom, help, exit, convar and key binding')
