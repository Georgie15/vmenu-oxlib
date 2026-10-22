-- Run from the repo root: lua tests/share-codes/test_share_codes.lua
-- Runs the production outfit and vehicle share-code scripts (server and client) against fake
-- FiveM, ox_lib and oxmysql: the server only stores real outfits and saved vehicles, limits are per
-- license, and the client turns a damaged code into a message instead of a Lua error.

local function check(condition, message)
    if not condition then error('FAIL: ' .. message, 2) end
end

local function readFile(path)
    local f = assert(io.open(path, 'rb'))
    local text = f:read('a')
    f:close()
    return text
end

local json = dofile('tests/lua/json.lua')
_G.json = json

-- Server: both share-code scripts with one fake database.
local function newServer()
    local s = { callbacks = {}, timeouts = {}, rows = {}, nextId = 1, errors = 0, enabled = true,
        licenses = { [1] = 'license:aaa', [2] = 'license:aaa', [3] = 'license:bbb' }, discord = 'discord:42' }
    _G.GetConvarBool = function() return s.enabled end
    _G.GetPlayerIdentifierByType = function(src, kind)
        if kind == 'license' then return s.licenses[src] end
        if kind == 'discord' then return s.discord end
    end
    _G.GetPlayerName = function(src) return 'player' .. src end
    _G.GetResourceState = function() return 'started' end
    _G.SetTimeout = function(ms, fn) s.timeouts[#s.timeouts + 1] = fn end
    _G.CreateThread = function() end
    _G.lib = {
        callback = { register = function(name, fn) s.callbacks[name] = fn end },
        print = { error = function() s.errors = s.errors + 1 end, info = function() end },
    }
    local db = {}
    function db.insert_async(_, query, params)
        local id = s.nextId
        s.nextId = id + 1
        s.rows[id] = { table = query:match('`(vmenu_%w+)`'), data = params[2] }
        return id
    end
    function db.query_async(_, query, params)
        local row = s.rows[params[1]]
        return row and { { data = row.data } } or {}
    end
    _G.exports = { oxmysql = db }
    assert(load(readFile('build/vMenu/server/oufitCodes.lua'), '@server/oufitCodes.lua'))()
    assert(load(readFile('build/vMenu/server/vehicleCodes.lua'), '@server/vehicleCodes.lua'))()
    function s.call(name, src, ...) return s.callbacks[name](src, ...) end
    function s.expire() local fns = s.timeouts; s.timeouts = {}; for _, fn in ipairs(fns) do fn() end end
    return s
end

local function outfit()
    return { Clothes = { ['1'] = { Item = 0, Texture = 0 }, ['3'] = { Item = 15, Texture = 1 }, ['11'] = { Item = 200, Texture = 4 } },
             Props = { ['0'] = { Item = -1, Texture = -1 }, ['1'] = { Item = 5, Texture = 2 } } }
end

local function vehicle()
    return { model = 2046537925, name = 'Police Cruiser', plateText = 'LSPD 01', livery = 2, turbo = true, enveffScale = 0.5,
             colors = { primary = 111, secondary = 0, customPrimary = 0 }, extras = { ['1'] = true, ['2'] = false },
             mods = { ['11'] = 3, ['12'] = 2 }, cadSaveToken = 'token-of-the-sharer', Category = 'My Police Cars', version = 1 }
end

local s = newServer()
local id = s.call('vMenu:Outfits:Generate', 1, outfit())
check(type(id) == 'number' and s.rows[id].table == 'vmenu_outfits', 'a real outfit is stored')
local again, reason = s.call('vMenu:Outfits:Generate', 2, outfit())
check(again == false and reason:find('too quickly', 1, true), 'reconnecting (same license) does not reset the generate limit')
check(type(s.call('vMenu:Outfits:Generate', 3, outfit())) == 'number', 'another player has their own limit')
s.expire()

local badOutfits = {
    ['an extra field'] = function(o) o.Script = 'x' end,
    ['clothes that are not a table'] = function(o) o.Clothes = 'x' end,
    ['no props'] = function(o) o.Props = nil end,
    ['a fractional item'] = function(o) o.Clothes['3'].Item = 1.5 end,
    ['a slot past 15'] = function(o) o.Clothes['20'] = { Item = 1, Texture = 0 } end,
    ['a slot that is not a number'] = function(o) o.Props.hat = { Item = 1, Texture = 0 } end,
    ['a texture past 255'] = function(o) o.Props['1'].Texture = 300 end,
    ['an item that is text'] = function(o) o.Clothes['1'].Item = '5' end,
    ['an item that is not a table'] = function(o) o.Clothes['1'] = 5 end,
}
for what, spoil in pairs(badOutfits) do
    local o = outfit()
    spoil(o)
    local ok, message = s.call('vMenu:Outfits:Generate', 1, o)
    check(ok == false and message == "This outfit can't be shared.", 'outfit refused: ' .. what)
end
check(s.nextId == 3, 'no refused outfit was stored')

id = s.call('vMenu:Vehicles:Generate', 1, vehicle())
local stored = json.decode(s.rows[id].data)
check(type(id) == 'number' and s.rows[id].table == 'vmenu_vehicles', 'a real saved vehicle is stored')
check(stored.cadSaveToken == nil and stored.Category == 'Uncategorized' and stored.extras['1'] == true,
    'the sharer\'s CAD token and category are dropped, the rest kept')
s.expire()

local badVehicles = {
    ['no model'] = function(v) v.model = nil end,
    ['a table two levels deep'] = function(v) v.mods = { ['11'] = { 3 } } end,
    ['a long text value'] = function(v) v.name = string.rep('x', 129) end,
    ['too many fields'] = function(v) for i = 1, 70 do v['f' .. i] = i end end,
    ['a long field name'] = function(v) v[string.rep('k', 33)] = 1 end,
    ['too many mods'] = function(v) for i = 1, 130 do v.mods[tostring(i)] = 1 end end,
    ['an infinite number'] = function(v) v.enveffScale = math.huge end,
}
for what, spoil in pairs(badVehicles) do
    local v = vehicle()
    spoil(v)
    local ok, message = s.call('vMenu:Vehicles:Generate', 1, v)
    check(ok == false and message == "This vehicle can't be shared.", 'vehicle refused: ' .. what)
end
check(s.nextId == 4, 'no refused vehicle was stored')

check(s.call('vMenu:Outfits:Request', 1, 1) ~= false, 'a code can be loaded')
check(s.call('vMenu:Outfits:Request', 2, 1) == false, 'reconnecting does not reset the load limit')
s.expire()
s.enabled = false
check(select(2, s.call('vMenu:Vehicles:Generate', 1, vehicle())):find('disabled', 1, true), 'refused while the convar is off')
print('PASS: server stores only real outfits and saved vehicles, limits per license')

-- Client: importing a code that was stored before the server checked them.
local function newClient(reply)
    local c = { exports = {}, handlers = {}, kvps = {}, notes = {}, reply = reply, ped = {} }
    _G.exports = function(name, fn) c.exports[name] = fn end
    _G.AddEventHandler = function(name, fn) c.handlers[name] = fn end
    _G.RegisterCommand = function() end
    _G.TriggerEvent = function() end
    _G.SetTimeout = function() end
    _G.GetResourceKvpString = function(k) return c.kvps[k] end
    _G.SetResourceKvp = function(k, v) c.kvps[k] = v end
    _G.Config = { OutfitSharing = { CommandEnabled = false }, Notify = function(_, msg, kind) c.notes[#c.notes + 1] = { msg = msg, kind = kind } end }
    _G.lib = {
        inputDialog = function() return { 7, 'Imported' } end,
        callback = { await = function() return c.reply end },
        print = { debug = function() end, error = function() end },
    }
    _G.GetVehicleClassFromName = function() return 18 end
    _G.IsModelInCdimage = function() return true end
    _G.IsModelValid = function() return true end
    _G.GetLabelText = function() return 'Emergency' end
    _G.PlayerPedId = function() return 1 end
    _G.SetPedComponentVariation = function(_, slot, item) c.ped['c' .. slot] = item end
    _G.ClearPedProp = function(_, slot) c.ped['p' .. slot] = nil end
    _G.SetPedPropIndex = function(_, slot, item) c.ped['p' .. slot] = item end
    assert(load(readFile('build/vMenu/client/vehicleCodes.lua'), '@client/vehicleCodes.lua'))()
    assert(load(readFile('build/vMenu/client/outfitCodes.lua'), '@client/outfitCodes.lua'))()
    function c.lastNote() return c.notes[#c.notes] or { msg = '' } end
    return c
end

for what, reply in pairs({ ['not JSON'] = '{', ['a list'] = '[]', ['no model'] = '{"name":"x"}', ['a number'] = '5' }) do
    local c = newClient(reply)
    check(c.exports.loadSharedVehicle() == false and c.lastNote().msg:find('damaged', 1, true) and c.kvps.veh_Imported == nil,
        'a damaged vehicle code is reported, not saved: ' .. what)
end
local c = newClient(json.encode(vehicle()))
check(c.exports.loadSharedVehicle() == true and json.decode(c.kvps.veh_Imported).cadSaveToken == nil, 'a good vehicle code is saved without the CAD token')

local character = json.encode({ SaveName = 'mp_ped_Main', DrawableVariations = { clothes = { ['3'] = { Key = 1, Value = 0 } } } })
for what, reply in pairs({ ['not JSON'] = '{', ['no clothes'] = '{"Props":{}}', ['clothes that are text'] = '{"Clothes":"x","Props":{}}' }) do
    c = newClient(reply)
    c.kvps.mp_ped_Main = character
    check(c.exports.loadSharedOutfit('mp_ped_Main') == false and c.lastNote().msg:find('damaged', 1, true) and c.kvps.mp_ped_Imported == nil,
        'a damaged outfit code is reported, not saved: ' .. what)
end
c = newClient(json.encode({ Clothes = { ['3'] = { Item = 15, Texture = 1 }, ['4'] = { Item = 'x' }, ['2'] = { Item = 9, Texture = 0 } },
    Props = { ['0'] = { Item = 6, Texture = 0 } } }))
c.kvps.mp_ped_Main = character
check(c.exports.loadSharedOutfit('mp_ped_Main') == true, 'an outfit with some bad entries still loads')
local imported = json.decode(c.kvps.mp_ped_Imported)
check(imported.DrawableVariations.clothes['3'].Key == 15 and imported.DrawableVariations.clothes['4'] == nil
    and imported.DrawableVariations.clothes['2'] == nil and imported.PropVariations.props['0'].Key == 6,
    'good entries applied, bad ones and hair dropped, missing prop data created')
check(imported.SaveName == 'mp_ped_Imported', 'the copy saves under its own name')

c = newClient('{')
check(c.exports.loadOutfitFromCode(7) == false, 'a damaged code applied directly is refused')
c = newClient(json.encode({ Clothes = { ['11'] = { Item = 20, Texture = 0 } }, Props = { ['0'] = { Item = 3, Texture = 1 } } }))
check(c.exports.loadOutfitFromCode(7) == true and c.ped.c11 == 20 and c.ped.p0 == 3, 'a good code applied directly dresses the ped')
print('PASS: client reports damaged codes instead of erroring, and keeps the good parts of an outfit')
