-- vMenu draws its own map blip for every nearby player. PoliceEMSActivity
-- already draws blips for the players it tracks for this viewer, so tell vMenu
-- those server ids and it leaves them alone (and drops any blip it already made).
-- Nothing is reported while duty blips are off (off duty, /blips off) or the
-- resource is stopped/outdated, so vMenu blips behave as usual again.
--
-- The list is PUSHED to vMenu's C# side as a comma-separated string on a plain
-- event. It used to be pulled by C# through an export every 100 ms; that
-- constant C# -> Lua traffic created and released function references and
-- coincided with vMenu's key handlers failing ("No such reference for 1": F1
-- and noclip stop responding until a game restart). A string event carries no
-- function references.

local DUTY_RESOURCE = 'PoliceEMSActivity'
local EVENT = 'vMenu:SetExternalBlipPlayers'
local POLL_MS = 500                 -- how soon a new duty blip replaces vMenu's
local RESEND_MS = 5000              -- heartbeat; vMenu drops the list after 15 s of silence
local RETRY_AFTER_FAILURE_MS = 10000

---@return integer[]|nil serverIds players whose blip the duty script is drawing, nil on failure
local function readDutyBlipPlayers()
    if GetResourceState(DUTY_RESOURCE) ~= 'started' then return {} end

    local ok, serverIds = pcall(function()
        return exports[DUTY_RESOURCE]:GetActiveBlipServerIds()
    end)
    if ok and type(serverIds) == 'table' then return serverIds end

    -- Usually PoliceEMSActivity has not been updated with the export yet.
    return nil
end

---@param serverIds integer[]
---@return string
local function encode(serverIds)
    local ids = {}
    for i = 1, #serverIds do
        local id = math.tointeger(tonumber(serverIds[i]))
        if id and id > 0 then ids[#ids + 1] = id end
    end
    table.sort(ids)
    return table.concat(ids, ',')
end

CreateThread(function()
    local lastSent, lastSentAt = nil, 0
    while true do
        local serverIds = readDutyBlipPlayers()
        local payload = serverIds and encode(serverIds) or ''
        local now = GetGameTimer()
        -- Send on change, plus a heartbeat so a push missed while vMenu's C#
        -- side was still starting is recovered within a few seconds.
        if payload ~= lastSent or now - lastSentAt >= RESEND_MS then
            TriggerEvent(EVENT, payload)
            lastSent, lastSentAt = payload, now
        end
        Wait(serverIds and POLL_MS or RETRY_AFTER_FAILURE_MS)
    end
end)
