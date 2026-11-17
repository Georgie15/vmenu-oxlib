-- Install in vMenu/server; validates existing vMenu ACEs before granting identity tickets.
local models, last = {}, {}
local data=json.decode(LoadResourceFile(GetCurrentResourceName(),'config/vehicleIdentityModels.json') or '{}') or {}
for model,permission in pairs(data) do models[GetHashKey(model) & 0xffffffff]=permission end
local addons=json.decode(LoadResourceFile(GetCurrentResourceName(),'config/addons.json') or '{}') or {}
for _,model in ipairs(addons.vehicles or {}) do models[GetHashKey(model) & 0xffffffff]='Addon' end
local function allowed(src,model)
    if IsPlayerAceAllowed(src,'vMenu.Everything') or IsPlayerAceAllowed(src,'vMenu.VehicleSpawner.All') then return true end
    local category=models[model]
    if category then return IsPlayerAceAllowed(src,'vMenu.VehicleSpawner.'..category) end
    return IsPlayerAceAllowed(src,'vMenu.VehicleSpawner.SpawnByName')
end
-- Through the request guard (server/guard/requestGuard.lua). not_authorized is a normal answer
-- (any vMenu spawn asks), so it isn't logged as denied.
local REFUSED={['rate-limited']={error='throttled'},malformed={error='invalid_model'}}
VMenuGuard.callback('vMenu:vehicleIdentityTicket',{
    args={VMenuGuard.optional(VMenuGuard.table)},
    reject=function(_,why) return REFUSED[why] or {error='throttled'} end,
},function(src,p)
    p=p or {}; local model=tonumber(p.model)
    if not model or model~=math.floor(model) then return {error='invalid_model'} end
    model=model & 0xffffffff
    local now=GetGameTimer()
    if last[src] and now-last[src]<500 then return {error='throttled'} end
    last[src]=now
    if not allowed(src,model) then return {error='not_authorized'} end
    if GetResourceState('PSRP_cad')~='started' then return {error='unavailable'} end
    local ok,ticket=pcall(function() return exports.PSRP_cad:IssueVehicleIdentityTicket(src,model,p.save_token) end)
    return {ticket=ok and ticket or nil}
end)
AddEventHandler('playerDropped',function() last[source]=nil end)
