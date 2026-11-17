-- Saves a clothing layout capture (F8: vmenu_clothing_capture) for staff with the
-- vMenu.ClothingCapture ace. The client reports, drawable by drawable, how the running game
-- numbers the clothes of both freemode models; tools/clothing-layouts turns a verified capture
-- into the baseline that saves made before collection references are read with. Captures are
-- written next to this resource's fxmanifest as clothing-capture-<UTC time>.json and change
-- nothing else. See docs/clothing-refs.md.

local MAX_BYTES = 4 * 1024 * 1024
local lastCapture = {}

-- Through the request guard (server/guard/requestGuard.lua), which checks the ace and that the
-- capture is text; a refused request gets the matching reply.
local REFUSED = {
    denied = "You aren't allowed to save clothing captures (vMenu.ClothingCapture).",
    malformed = "That capture is too large or damaged.",
    ["rate-limited"] = "Please wait a moment and try again.",
}

VMenuGuard.netEvent("vMenu:ClothingCapture:Save", {
    permission = function(src) return IsPlayerAceAllowed(src, "vMenu.ClothingCapture") end,
    permissionName = "vMenu.ClothingCapture",
    args = { VMenuGuard.string() },
    reject = function(src, why) TriggerClientEvent("vMenu:ClothingCapture:Saved", src, REFUSED[why]) end,
}, function(payload)
    local src = source
    local function reply(message)
        TriggerClientEvent("vMenu:ClothingCapture:Saved", src, message)
    end

    local now = os.time()
    if lastCapture[src] and now - lastCapture[src] < 30 then
        reply("Wait 30 seconds between captures.")
        return
    end
    if type(payload) ~= "string" or #payload > MAX_BYTES then
        reply("That capture is too large or damaged.")
        return
    end
    local ok, capture = pcall(json.decode, payload)
    if not ok or type(capture) ~= "table" or capture.kind ~= "vmenu-clothing-capture" or type(capture.models) ~= "table" then
        reply("That capture is damaged.")
        return
    end
    lastCapture[src] = now

    local resources = {}
    for i = 0, GetNumResources() - 1 do
        local name = GetResourceByFindIndex(i)
        if name and GetResourceState(name) == "started" then
            resources[#resources + 1] = name
        end
    end
    capture.capturedAt = os.date("!%Y-%m-%dT%H:%M:%SZ", now)
    capture.capturedAtUnix = now
    capture.capturedBy = GetPlayerName(src)
    capture.serverResources = resources
    capture.serverGameBuild = GetConvar("sv_enforceGameBuild", "")

    local file = ("clothing-capture-%s.json"):format(os.date("!%Y%m%d-%H%M%S", now))
    if SaveResourceFile(GetCurrentResourceName(), file, json.encode(capture), -1) then
        print(("[vMenu] clothing capture from %s saved as %s"):format(GetPlayerName(src), file))
        reply(("Clothing capture saved on the server as %s/%s."):format(GetCurrentResourceName(), file))
    else
        reply("The server couldn't write the capture file.")
    end
end)

AddEventHandler("playerDropped", function()
    lastCapture[source] = nil
end)
