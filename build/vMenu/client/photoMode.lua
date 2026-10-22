-- Photo Mode (Misc Settings > Photo Mode, or the unbound "vMenu Photo Mode" key binding).
-- A free camera for screenshots: the HUD is hidden and your ped stays where it is. The camera stays
-- within 15 m of your ped's head and only moves where the head has a clear line of sight, so it
-- can't look through walls or into closed rooms. Needs `setr vmenu_photo_mode true`.

local RADIUS = 15.0
local LOOK_SPEED = 8.0
local MOVE_SPEED = 0.08
local FAST_MOVE_SPEED = 0.25
local MIN_FOV, MAX_FOV = 10.0, 90.0
local HEAD_BONE = 31086

-- Controls (all disabled while the camera is active, then read as disabled controls).
local LOOK_LR, LOOK_UD = 1, 2
local SPRINT, JUMP, DUCK = 21, 22, 36
local MOVE_UP, MOVE_DOWN, MOVE_LEFT, MOVE_RIGHT = 32, 33, 34, 35
local SCROLL_UP, SCROLL_DOWN = 241, 242
local TOGGLE_HELP = 74 -- H
local EXIT_BACK, EXIT_PAUSE = 177, 200 -- Backspace, Esc
local PUSH_TO_TALK = 249

local HELP = '[W/A/S/D] Move  \n[Space / Ctrl] Up / Down  \n[Mouse] Look  \n[Scroll] Zoom  \n[Shift] Faster  \n'
    .. '[H] Hide this help  \n[Backspace] Exit photo mode'

local active = false

local function enabled()
    return GetConvar('vmenu_photo_mode', 'false') == 'true'
end

local function forwardOf(rot)
    local x, z = math.rad(rot.x), math.rad(rot.z)
    local flat = math.abs(math.cos(x))
    return vector3(-math.sin(z) * flat, math.cos(z) * flat, math.sin(x))
end

local function rightOf(rot)
    local z = math.rad(rot.z)
    return vector3(math.cos(z), math.sin(z), 0.0)
end

--- Whether the camera may be at `to`: close enough to the head, with nothing solid in between.
local function allowed(head, to, ped)
    if #(to - head) > RADIUS then return false end
    local probe = StartExpensiveSynchronousShapeTestLosProbe(head.x, head.y, head.z, to.x, to.y, to.z, 1 | 16, ped, 4)
    local _, hit = GetShapeTestResult(probe)
    return hit == 0 or hit == false
end

local function notify(message, kind)
    Config.Notify('vMenu', message, kind, 6500)
end

local function start()
    if active then return end
    if not enabled() then return notify('Photo mode is disabled on this server.', 'error') end
    local ped = PlayerPedId()
    if IsEntityDead(ped) or IsPedRagdoll(ped) then return notify("You can't use photo mode right now.", 'error') end
    local vehicle = GetVehiclePedIsIn(ped, false)
    if vehicle ~= 0 and GetEntitySpeed(vehicle) > 1.0 then return notify('Stop the vehicle before using photo mode.', 'error') end

    active = true
    local head = GetPedBoneCoords(ped, HEAD_BONE, 0.0, 0.0, 0.0)
    local pos = GetGameplayCamCoord()
    if #(pos - head) > RADIUS then pos = head end
    local rot = GetGameplayCamRot(2)
    local fov = GetGameplayCamFov()
    local cam = CreateCam('DEFAULT_SCRIPTED_CAMERA', true)
    SetCamCoord(cam, pos.x, pos.y, pos.z)
    SetCamRot(cam, rot.x, 0.0, rot.z, 2)
    SetCamFov(cam, fov)
    RenderScriptCams(true, false, 0, true, true)
    local showHelp = true
    lib.showTextUI(HELP, { position = 'left-center' })

    CreateThread(function()
        while active do
            DisableAllControlActions(0)
            EnableControlAction(0, PUSH_TO_TALK, true)
            HideHudAndRadarThisFrame()

            if IsDisabledControlJustPressed(0, EXIT_BACK) or IsDisabledControlJustPressed(0, EXIT_PAUSE)
                    or IsEntityDead(ped) or not DoesEntityExist(ped) then
                break
            end
            if IsDisabledControlJustPressed(0, TOGGLE_HELP) then
                showHelp = not showHelp
                if showHelp then lib.showTextUI(HELP, { position = 'left-center' }) else lib.hideTextUI() end
            end

            local pitch = math.max(-89.0, math.min(89.0, rot.x - GetDisabledControlNormal(0, LOOK_UD) * LOOK_SPEED))
            rot = vector3(pitch, 0.0, rot.z - GetDisabledControlNormal(0, LOOK_LR) * LOOK_SPEED)

            local forward, right = forwardOf(rot), rightOf(rot)
            local move = vector3(0.0, 0.0, 0.0)
            if IsDisabledControlPressed(0, MOVE_UP) then move = move + forward end
            if IsDisabledControlPressed(0, MOVE_DOWN) then move = move - forward end
            if IsDisabledControlPressed(0, MOVE_RIGHT) then move = move + right end
            if IsDisabledControlPressed(0, MOVE_LEFT) then move = move - right end
            if IsDisabledControlPressed(0, JUMP) then move = move + vector3(0.0, 0.0, 1.0) end
            if IsDisabledControlPressed(0, DUCK) then move = move - vector3(0.0, 0.0, 1.0) end
            if #move > 0.0 then
                local speed = IsDisabledControlPressed(0, SPRINT) and FAST_MOVE_SPEED or MOVE_SPEED
                local target = pos + move * speed
                if allowed(head, target, ped) then pos = target end
            end

            if IsDisabledControlJustPressed(0, SCROLL_UP) then fov = math.max(MIN_FOV, fov - 2.5) end
            if IsDisabledControlJustPressed(0, SCROLL_DOWN) then fov = math.min(MAX_FOV, fov + 2.5) end

            SetCamCoord(cam, pos.x, pos.y, pos.z)
            SetCamRot(cam, rot.x, 0.0, rot.z, 2)
            SetCamFov(cam, fov)
            Wait(0)
        end

        active = false
        lib.hideTextUI()
        RenderScriptCams(false, false, 0, true, true)
        DestroyCam(cam, false)
    end)
end

AddEventHandler('vMenu:photoMode:start', start)

if enabled() then
    local command = ('vMenu:%s:PhotoMode'):format(GetConvar('vmenu_keymapping_id', 'Default'))
    RegisterCommand(command, start, false)
    RegisterKeyMapping(command, 'vMenu Photo Mode', 'keyboard', '')
end
