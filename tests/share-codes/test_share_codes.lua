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
        assert(query:find('UNIX_TIMESTAMP(`created`)', 1, true), 'the creation time is read')
        return row and { { data = row.data, created = 1762700000 } } or {}
    end
    _G.exports = { oxmysql = db }
    -- Both scripts register through the request guard; a fresh one per server. A second passes
    -- between calls, as in the menu, so its rate limit (10 at once, then 3 a second) never decides these.
    s.now = 100000
    _G.GetGameTimer = function() return s.now end
    _G.GetConvar = function(_, default) return default end
    _G.GetConvarInt = function(_, default) return default end
    dofile('tests/lua/request_guard.lua')()
    assert(load(readFile('build/vMenu/server/oufitCodes.lua'), '@server/oufitCodes.lua'))()
    assert(load(readFile('build/vMenu/server/vehicleCodes.lua'), '@server/vehicleCodes.lua'))()
    function s.call(name, src, ...) s.now = s.now + 1000; return s.callbacks[name](src, ...) end
    function s.expire() local fns = s.timeouts; s.timeouts = {}; for _, fn in ipairs(fns) do fn() end end
    return s
end

local function outfit()
    return { Clothes = { ['1'] = { Item = 0, Texture = 0 }, ['3'] = { Item = 15, Texture = 1 }, ['11'] = { Item = 200, Texture = 4 } },
             Props = { ['0'] = { Item = -1, Texture = -1 }, ['1'] = { Item = 5, Texture = 2 } } }
end

local function vehicle()
    return { model = 2046537925, name = 'Police Cruiser', plateText = 'LSPD 01', livery = 2, roofLivery = 1, turbo = true, enveffScale = 0.5,
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
check(stored.cadSaveToken == nil and stored.Category == 'Uncategorized' and stored.extras['1'] == true and stored.roofLivery == 1,
    'the sharer\'s CAD token and category are dropped, the rest (roof livery too) kept')
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

local function outfitV2()
    local o = outfit()
    o.v, o.Model, o.Layout = 2, 1885233650, '0123456789abcdef'
    o.Refs = { v = 2, model = 1885233650,
        clothes = { ['3'] = { col = '', drw = 15, tex = 1, lp = 'aaaaaaaaaaaaaaaa' },
                    ['11'] = { col = 'mp_m_deptmerge2', drw = 40, tex = 4, rev = 'psrp-eup-2026-11-06', pp = '0123456789abcdef', lp = 'bbbbbbbbbbbbbbbb' } },
        props = { ['0'] = { empty = true }, ['1'] = { col = 'mp_m_business_01', drw = 5, tex = 2 } } }
    return o
end
local v2 = s.call('vMenu:Outfits:Generate', 1, outfitV2())
check(type(v2) == 'number' and json.decode(s.rows[v2].data).Refs.clothes['11'].col == 'mp_m_deptmerge2', 'a v2 outfit (model and collection references) is stored')
s.expire()
local badV2 = {
    ['references without a version'] = function(o) o.v = nil end,
    ['a version without references'] = function(o) o.Refs = nil end,
    ['references for another model'] = function(o) o.Refs.model = 2627665880 end,
    ['an unknown reference field'] = function(o) o.Refs.clothes['3'].owner = 'x' end,
    ['a piece still waiting for review'] = function(o) o.Refs.clothes['3'] = { legacy = { 15, 1 }, why = 'not-in-baseline' } end,
    ['a collection name with odd characters'] = function(o) o.Refs.clothes['3'].col = 'x/../y' end,
    ['an empty prop with a drawable'] = function(o) o.Refs.props['0'].drw = 3 end,
    ['a negative drawable'] = function(o) o.Refs.clothes['3'].drw = -1 end,
    ['a layout that is not hex'] = function(o) o.Layout = 'zz' end,
    ['a reference slot past 15'] = function(o) o.Refs.props['16'] = { empty = true } end,
}
for what, spoil in pairs(badV2) do
    local o = outfitV2()
    spoil(o)
    local ok, message = s.call('vMenu:Outfits:Generate', 1, o)
    check(ok == false and message == "This outfit can't be shared.", 'v2 outfit refused: ' .. what)
end

local data, created = s.call('vMenu:Outfits:Request', 1, 1)
check(type(data) == 'string' and created == 1762700000, 'a code is loaded with its creation time')
check(s.call('vMenu:Outfits:Request', 2, 1) == false, 'reconnecting does not reset the load limit')
s.expire()
s.enabled = false
check(select(2, s.call('vMenu:Vehicles:Generate', 1, vehicle())):find('disabled', 1, true), 'refused while the convar is off')
print('PASS: server stores only real outfits and saved vehicles, limits per license')

-- Client: the Lua side fetches and stores codes for a character's outfits page (vMenu's C#, which turns codes
-- into outfits and outfits into codes: tests/clothing-refs), and passes the server's reasons on.
local function newClient(reply, created)
    local c = { exports = {}, handlers = {}, kvps = {}, notes = {}, reply = reply, created = created, calls = {}, sent = nil,
        requests = 0, waited = 0, dialogs = 0, dialog = { 7, 'Imported', true }, clipboard = nil }
    c.csharp = {
        vMenuApplyOutfitCode = function(_, code, made)
            c.calls[#c.calls + 1] = { 'apply', code, made }
            return c.applyReply or json.encode({ ok = true, message = '' })
        end,
    }
    _G.exports = setmetatable({}, {
        __call = function(_, name, fn) c.exports[name] = fn end,
        __index = function(_, resource) assert(resource == 'vMenu'); return c.csharp end,
    })
    _G.GetCurrentResourceName = function() return 'vMenu' end
    _G.AddEventHandler = function(name, fn) c.handlers[name] = fn end
    _G.RegisterCommand = function() end
    _G.TriggerEvent = function() end
    _G.SetTimeout = function() end
    _G.Wait = function() c.waited = c.waited + 1 end
    _G.GetResourceKvpString = function(k) return c.kvps[k] end
    _G.SetResourceKvp = function(k, v) c.kvps[k] = v end
    _G.Config = { OutfitSharing = { CommandEnabled = false }, Notify = function(_, msg, kind) c.notes[#c.notes + 1] = { msg = msg, kind = kind } end }
    _G.lib = {
        inputDialog = function() c.dialogs = c.dialogs + 1; return c.dialog end,
        callback = { await = function(name, _, payload)
            if name == 'vMenu:Outfits:Generate' then c.sent = payload; return c.generated or false, c.generateWhy end
            c.requests = c.requests + 1
            return c.reply, c.created
        end },
        print = { debug = function() end, error = function() end },
        setClipboard = function(text) c.clipboard = text end,
    }
    _G.GetVehicleClassFromName = function() return 18 end
    _G.IsModelInCdimage = function() return true end
    _G.IsModelValid = function() return true end
    _G.GetLabelText = function() return 'Emergency' end
    _G.PlayerPedId = function() return 1 end
    _G.SetPedComponentVariation = function() error('outfit codes must not set clothing from Lua') end
    _G.SetPedPropIndex = function() error('outfit codes must not set props from Lua') end
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
check(c.exports.loadSharedVehicle() == true and json.decode(c.kvps.veh_Imported).cadSaveToken == nil and json.decode(c.kvps.veh_Imported).roofLivery == 1,
    'a good vehicle code is saved without the CAD token, with its roof livery')
c = newClient(false, 'You are requesting vehicles too quickly. Please wait a moment.')
check(c.exports.loadSharedVehicle() == false and c.lastNote().msg:find('too quickly', 1, true), "the server's reason for refusing a vehicle code is shown")
c = newClient(false)
check(c.exports.loadSharedVehicle() == false and c.lastNote().msg == "There's no vehicle code #7.", 'an unknown vehicle code says so')

-- Load Outfit Code (a character's outfits page).
local function request(client, taken)
    local answer = client.exports.requestOutfitCode(json.encode(taken or {}))
    check(type(answer) == 'string', 'requestOutfitCode answers JSON')
    return json.decode(answer)
end
local code = json.encode({ Clothes = { ['3'] = { Item = 15, Texture = 1 } }, Props = { ['0'] = { Item = 6, Texture = 0 } } })
c = newClient(code, 1762700000)
local answer = request(c)
check(c.waited > 0, 'the export yields, so vMenu can await it')
check(answer.ok == true and answer.code == code and answer.created == 1762700000 and answer.id == 7 and answer.name == 'Imported' and answer.wear == true,
    'a code comes back with its creation time, number, name and the wear choice')
check(#c.notes == 0 and next(c.kvps) == nil and #c.calls == 0, 'nothing is saved or put on from Lua: vMenu does that')
answer = request(c)
check(answer.ok == false and answer.message:find('Wait', 1, true) and c.requests == 1 and c.dialogs == 1, 'one code at a time: refused before the dialog')

c = newClient(code, 5)
c.dialog = { 1234, '   ', false }
answer = request(c)
check(answer.ok and answer.name == 'Code 1234' and answer.wear == false, 'an empty name becomes the code, and Wear can be unticked')
c = newClient(code, 5)
c.dialog = { 1234, nil, true }
check(request(c).name == 'Code 1234', 'a missing name becomes the code')
c = newClient(code)
answer = request(c, { 'Work', 'Imported' })
check(answer.ok == false and answer.message:find('Imported', 1, true) and c.requests == 0, 'a name already in that list is refused before anything is fetched')
c = newClient(code)
c.dialog = { 7, 'A|B', true }
answer = request(c)
check(answer.ok == false and answer.message:find('|', 1, true) and c.requests == 0, 'names follow the outfit name rules')
c = newClient(code)
c.dialog = { 7, string.rep('x', 31), true }
check(request(c).ok == false and c.requests == 0, 'names are up to 30 characters')
c = newClient(code)
c.dialog = { 7.5, 'Imported', true }
answer = request(c)
check(answer.ok == false and answer.message:find('whole numbers', 1, true) and c.requests == 0, 'a code is a whole number')
c = newClient(code)
c.dialog = nil
answer = request(c)
check(answer.ok == false and answer.message == nil and c.requests == 0, 'closing the dialog is not an error')

c = newClient(false)
answer = request(c)
check(answer.ok == false and answer.message == "There's no outfit code #7.", 'an unknown code says so')
c = newClient(false, 'You are requesting outfits too quickly. Please wait a moment.')
check(request(c).message:find('too quickly', 1, true), "the server's reason is passed on")
c = newClient(false, 'Outfit codes are disabled on this server.')
check(request(c).message:find('disabled', 1, true), 'codes switched off: said so')
for what, reply in pairs({ ['not JSON'] = '{', ['no clothes'] = '{"Props":{}}', ['clothes that are text'] = '{"Clothes":"x","Props":{}}' }) do
    c = newClient(reply)
    answer = request(c)
    check(answer.ok == false and answer.message:find('damaged', 1, true), 'a damaged outfit code is refused: ' .. what)
end

-- Share and Share What I'm Wearing (a character's outfits page).
local payload = { v = 2, Model = 1885233650, Clothes = { ['11'] = { Item = 44, Texture = 2 } }, Props = {}, Refs = { v = 2, model = 1885233650, clothes = {}, props = {} } }
c = newClient()
c.generated = 42
answer = json.decode(c.exports.shareOutfitCode(json.encode(payload)))
-- vMenu copies the number itself, through psrp_notify's copy box (CopyToClipboard); a copy from
-- here, with no click or key press of the player's behind it, is refused since FiveM's Nov 2026 NUI update
check(c.waited > 0 and answer.ok == true and answer.id == 42 and c.clipboard == nil, 'a shared outfit gets its number; vMenu copies it')
check(c.sent and c.sent.v == 2 and c.sent.Model == 1885233650 and c.sent.Clothes['11'].Item == 44, 'the server gets the code vMenu built')
answer = json.decode(c.exports.shareOutfitCode(json.encode(payload)))
check(answer.ok == false and answer.message:find('15 seconds', 1, true), 'one new code every 15 seconds')
c = newClient()
c.generateWhy = 'You need to have Discord linked to generate outfit codes.'
answer = json.decode(c.exports.shareOutfitCode(json.encode(payload)))
check(answer.ok == false and answer.message:find('Discord', 1, true) and c.clipboard == nil, "the server's reason for refusing is passed on")
c.generated = 43
check(json.decode(c.exports.shareOutfitCode(json.encode(payload))).ok == true, 'a refusal does not start the 15 seconds')
c = newClient()
answer = json.decode(c.exports.shareOutfitCode('{'))
check(answer.ok == false and c.sent == nil, 'a damaged payload is not sent')

-- exports.vMenu:loadOutfitFromCode: straight on, through vMenu.
c = newClient('{')
check(c.exports.loadOutfitFromCode(7) == false and #c.calls == 0 and c.lastNote().msg:find('damaged', 1, true), 'a damaged code applied directly is refused, with a message')
c = newClient(false)
check(c.exports.loadOutfitFromCode(7) == false and c.lastNote().msg == "There's no outfit code #7.", 'an unknown code applied directly says so')
c = newClient(code, 5)
check(c.exports.loadOutfitFromCode(7) == true and c.calls[1][1] == 'apply' and c.calls[1][2] == code and c.calls[1][3] == 5, 'a good code applied directly goes to vMenu with its creation time')
c.applyReply = json.encode({ ok = false, message = "None of this outfit's pieces could be matched on this server." })
check(c.exports.loadOutfitFromCode(7) == false and c.lastNote().msg:find('matched', 1, true), 'nothing matched: refused with a message')
check(c.exports.loadOutfitFromCode('7') == false, 'the code must be a number')

check(c.exports.loadSharedOutfit == nil and c.handlers['vMenu:Outfits:GenerateCode'] == nil, 'the old character-copy load and the editor generate event are gone')
print("PASS: client fetches and stores codes for outfits pages and passes the server's reasons on")
