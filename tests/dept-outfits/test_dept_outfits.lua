-- Run from the repo root: lua tests/dept-outfits/test_dept_outfits.lua
-- Runs the production server/deptOutfits.lua (with the shipped config/config_server.lua) against
-- fake FiveM, ox_lib, oxmysql and PoliceEMSActivity: who may list, wear, publish, replace, rename
-- and delete department outfits, payload and name checks, rate limits, and what happens without
-- PoliceEMSActivity or the database.

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

local MALE, FEMALE = 1885233650, 2627665880

-- A Saved Outfits payload as the client writes it (vMenu/SavedOutfit.cs).
local function outfit(model, edit)
    local data = {
        model = model,
        clothes = { ['1'] = { 0, 0 }, ['3'] = { 4, 0 }, ['4'] = { 35, 0 }, ['11'] = { 412, 2 } },
        props = { ['0'] = { -1, -1 }, ['6'] = { 3, 1 } },
        refs = {
            v = 2, model = model,
            clothes = {
                ['1'] = { col = '', drw = 0, tex = 0, lp = 'ab12' },
                ['3'] = { col = '', drw = 4, tex = 0, lp = 'ab12' },
                ['4'] = { col = '', drw = 35, tex = 0, lp = 'ab12' },
                ['11'] = { col = 'mp_m_deptmerge2', drw = 40, tex = 2, rev = 'psrp-eup-2026-11-06', pp = 'cd34', lp = 'ab12' },
            },
            props = { ['0'] = { empty = true }, ['6'] = { col = '', drw = 3, tex = 1, lp = 'ab12' } },
        },
        layout = 'ab12',
    }
    if edit then edit(data) end
    return json.encode(data)
end

-- A payload exactly as vMenu/SavedOutfit.cs writes it (tests/character-wardrobe checks it still does).
local CSHARP_PAYLOAD = [[{"model":1885233650,"clothes":{"1":[1,0],"3":[3,0],"4":[4,0],"5":[5,0],"6":[6,0],"7":[7,0],"8":[8,0],"9":[9,0],"10":[10,0],"11":[45,5]},"props":{"0":[2,1],"1":[-1,-1],"2":[-1,-1],"3":[-1,-1],"4":[-1,-1],"5":[-1,-1],"6":[-1,-1],"7":[-1,-1]},"refs":{"v":2,"model":1885233650,"clothes":{"1":{"col":"","drw":1,"tex":0,"lp":"38053a671452b083"},"3":{"col":"","drw":3,"tex":0,"lp":"38053a671452b083"},"4":{"col":"","drw":4,"tex":0,"lp":"38053a671452b083"},"5":{"col":"","drw":5,"tex":0,"lp":"38053a671452b083"},"6":{"col":"","drw":6,"tex":0,"lp":"38053a671452b083"},"7":{"col":"","drw":7,"tex":0,"lp":"38053a671452b083"},"8":{"col":"","drw":8,"tex":0,"lp":"38053a671452b083"},"9":{"col":"","drw":9,"tex":0,"lp":"38053a671452b083"},"10":{"col":"","drw":10,"tex":0,"lp":"38053a671452b083"},"11":{"col":"mp_m_deptmerge2","drw":5,"tex":5,"lp":"38053a671452b083"}},"props":{"0":{"col":"","drw":2,"tex":1,"lp":"38053a671452b083"},"1":{"empty":true},"2":{"empty":true},"3":{"empty":true},"4":{"empty":true},"5":{"empty":true},"6":{"empty":true},"7":{"empty":true}}},"layout":"38053a671452b083"}]]

-- A server with an in-memory vmenu_dept_outfits table. Players: 1 LSPD member, 2 LSPD manager,
-- 3 ManageAll, 4 civilian, 5 BCSO member who also manages LSPD and SAHP, 6 department head
-- (ManageOwn) who may /duty as BCSO, 7 department head in no department.
local function newServer()
    local s = {
        now = 100000, enabled = true, rows = {}, nextId = 1, client = {}, latent = {}, handlers = {},
        errors = {}, warnings = {}, infos = {}, failQueries = false, nextRequest = 1, names = {}, events = {}, aceCalls = 0,
        resources = { oxmysql = 'started', PoliceEMSActivity = 'started' },
        duty = { [1] = 'lspd', [2] = nil, [3] = nil, [4] = nil, [5] = 'bcso' },
        aces = {
            [2] = { ['vMenu.DeptOutfits.Manage.lspd'] = true },
            [3] = { ['vMenu.DeptOutfits.ManageAll'] = true },
            [5] = { ['vMenu.DeptOutfits.Manage.lspd'] = true, ['vMenu.DeptOutfits.Manage.sahp'] = true },
            [6] = { ['vMenu.DeptOutfits.ManageOwn'] = true, ['group.bcsoblip'] = true },
            [7] = { ['vMenu.DeptOutfits.ManageOwn'] = true },
        },
        licenses = { [1] = 'license:one', [2] = 'license:two', [3] = 'license:three', [4] = 'license:four', [5] = 'license:five',
            [6] = 'license:six', [7] = 'license:seven' },
        dutyThrows = false,
    }

    _G.GetGameTimer = function() return s.now end
    _G.GetConvarBool = function(name, default) if name == 'vmenu_dept_outfits' then return s.enabled end return default end
    _G.GetResourceState = function(name) return s.resources[name] or 'missing' end
    _G.IsPlayerAceAllowed = function(src, ace) s.aceCalls = s.aceCalls + 1; return (s.aces[src] or {})[ace] == true end
    _G.GetPlayerIdentifierByType = function(src, kind) assert(kind == 'license'); return s.licenses[src] end
    _G.GetPlayerName = function(src) return s.names[src] or ('Player ' .. src) end
    _G.AddEventHandler = function(name, fn) s.events[name] = fn end
    _G.RegisterNetEvent = function(name, fn) s.handlers[name] = fn end
    _G.TriggerClientEvent = function(name, target, ...) s.client[#s.client + 1] = { name = name, target = target, args = { ... } } end
    _G.TriggerLatentClientEvent = function(name, target, bps, ...) s.latent[#s.latent + 1] = { name = name, target = target, bps = bps, args = { ... } } end
    _G.CreateThread = function(fn) s.startup = fn end
    _G.lib = {
        print = {
            info = function(m) s.infos[#s.infos + 1] = m end,
            warn = function(m) s.warnings[#s.warnings + 1] = m end,
            error = function(m) s.errors[#s.errors + 1] = m end,
        },
    }

    local labels = { lspd = 'Los Santos Police Department', bcso = "Blaine County Sheriff's Office", ghost = 'Ghost Unit' }
    local duty = {}
    function duty.GetDutyInfo(_, src)
        if s.dutyThrows then error('PoliceEMSActivity broke') end
        local org = s.duty[src]
        return org and { org = org, orgType = 'LEO', orgLabel = labels[org] } or nil
    end
    function duty.GetDutyOrg(_, src)
        if s.dutyThrows then error('PoliceEMSActivity broke') end
        return s.duty[src]
    end

    local db = {}
    local function fail() if s.failQueries then error('connection lost') end end
    local function same(a, b) return a:lower() == b:lower() end -- utf8mb4_general_ci
    local function find(id) for _, row in ipairs(s.rows) do if row.id == id then return row end end end
    function db.single_async(_, query, params)
        fail()
        if query:find('^SELECT `id`, `org`, `name`, `model`, `payload`') then
            local row = find(params[1])
            return row and { id = row.id, org = row.org, name = row.name, model = row.model, payload = row.payload } or nil
        end
        assert(query:find('^SELECT `id` FROM `vmenu_dept_outfits` WHERE `org` = %? AND `model` = %? AND `name` = %? AND `id` <> %?'), query)
        for _, row in ipairs(s.rows) do
            if row.org == params[1] and row.model == params[2] and same(row.name, params[3]) and row.id ~= params[4] then return { id = row.id } end
        end
        return nil
    end
    function db.scalar_async(_, query, params)
        fail()
        -- Stands in for the wait on the database: another request is handled meanwhile.
        local meanwhile = s.whileCounting
        s.whileCounting = nil
        if meanwhile then meanwhile() end
        assert(query:find('^SELECT COUNT%(%*%) FROM `vmenu_dept_outfits` WHERE `org` = %? AND `model` = %?'), query)
        local n = 0
        for _, row in ipairs(s.rows) do if row.org == params[1] and row.model == params[2] then n = n + 1 end end
        return n
    end
    function db.query_async(_, query, params)
        fail()
        if query == 'SELECT 1 FROM `vmenu_dept_outfits` LIMIT 1' then return {} end
        assert(query:find('WHERE `model` = %? AND `org` IN %(') and query:find('ORDER BY `org`, `name` LIMIT 2000'), query)
        local _, marks = query:gsub('%?', '')
        assert(marks == #params, 'one parameter per placeholder')
        local orgs = {}
        for i = 2, #params do orgs[params[i]] = true end
        local out = {}
        for _, row in ipairs(s.rows) do
            if row.model == params[1] and orgs[row.org] then
                out[#out + 1] = { id = row.id, org = row.org, name = row.name, updated_by_name = row.updated_by_name, updated = '2026-11-17' }
            end
        end
        table.sort(out, function(a, b) return a.org == b.org and a.name < b.name or a.org < b.org end)
        return out
    end
    function db.insert_async(_, query, params)
        fail()
        assert(query:find('^INSERT INTO `vmenu_dept_outfits` %(`org`, `name`, `model`, `payload`, `created_by`, `created_by_name`, `updated_by_name`%)'), query)
        for _, row in ipairs(s.rows) do
            if row.org == params[1] and row.model == params[3] and same(row.name, params[2]) then error('Duplicate entry') end
        end
        local row = { id = s.nextId, org = params[1], name = params[2], model = params[3], payload = params[4], created_by = params[5],
            created_by_name = params[6], updated_by_name = params[7] }
        s.nextId = s.nextId + 1
        s.rows[#s.rows + 1] = row
        return row.id
    end
    function db.update_async(_, query, params)
        fail()
        if query:find('^DELETE FROM `vmenu_dept_outfits` WHERE `id` = %?') then
            for i, row in ipairs(s.rows) do if row.id == params[1] then table.remove(s.rows, i); return 1 end end
            return 0
        end
        local row = find(params[3])
        if not row then return 0 end
        if query:find('^UPDATE `vmenu_dept_outfits` SET `payload` = %?') then
            row.payload, row.updated_by_name = params[1], params[2]
        else
            assert(query:find('^UPDATE `vmenu_dept_outfits` SET `name` = %?'), query)
            row.name, row.updated_by_name = params[1], params[2]
        end
        return 1
    end
    _G.exports = { oxmysql = db, PoliceEMSActivity = duty }

    assert(load(readFile('build/vMenu/config/config_server.lua'), '@config/config_server.lua'))()
    dofile('tests/lua/request_guard.lua')()
    assert(load(readFile('build/vMenu/server/deptOutfits.lua'), '@server/deptOutfits.lua'))()

    -- Sends one request as a client would and returns the decoded reply (nil when none was sent).
    function s.raw(src, requestId, action, argsJson)
        local before, beforeLatent = #s.client, #s.latent
        _G.source = src
        s.handlers['vMenu:DeptOutfits:Request'](requestId, action, argsJson)
        _G.source = nil
        local sent = #s.latent > beforeLatent and s.latent[#s.latent] or (#s.client > before and s.client[#s.client] or nil)
        if not sent then return nil end
        assert(sent.name == 'vMenu:DeptOutfits:Reply' and sent.target == src and sent.args[1] == requestId, 'replies go to the asking player with its request number')
        return json.decode(sent.args[2]), sent
    end
    function s.ask(src, action, args)
        s.nextRequest = s.nextRequest + 1
        return s.raw(src, s.nextRequest, action, json.encode(args or {}))
    end
    -- Moves the clock past every cooldown.
    function s.wait() s.now = s.now + 60000 end
    return s
end

local function count(t) local n = 0 for _ in pairs(t) do n = n + 1 end return n end

-- ---- Switched off, malformed requests -----------------------------------------------------------
local s = newServer()
s.enabled = false
local r = s.ask(1, 'list', { model = MALE })
check(r.ok == false and r.message:find('disabled', 1, true), 'refused while vmenu_dept_outfits is off')
s.enabled = true
check(s.raw(1, 'x', 'list', '{}') == nil and s.raw(1, -1, 'list', '{}') == nil and s.raw(1, 1.5, 'list', '{}') == nil, 'a request without a valid number gets no reply')
check(s.ask(1, 'drop_table', {}).message == 'Unknown request.', 'unknown actions are refused')
check(s.raw(1, 7, 'list', string.rep('x', 12289)).message:find('too large', 1, true), 'oversized requests are refused')
check(s.raw(1, 8, 'list', '{"model":').message:find('could not be read', 1, true), 'unreadable requests are refused')
check(s.raw(1, 9, 'list', 5).ok == false, 'arguments must be a JSON string')
print('PASS: switched off, malformed and unknown requests')

-- ---- Publishing: only managers of that org ------------------------------------------------------
s = newServer()
r = s.ask(4, 'publish', { org = 'lspd', name = 'Class A', payload = outfit(MALE) })
check(r.ok == false and #s.rows == 0, 'a civilian cannot publish')
r = s.ask(1, 'publish', { org = 'lspd', name = 'Class A', payload = outfit(MALE) })
check(r.ok == false and #s.rows == 0, 'being on duty with an org is not managing it')
r = s.ask(2, 'publish', { org = 'bcso', name = 'Class A', payload = outfit(MALE) })
check(r.ok == false and #s.rows == 0, 'a manager of one org cannot publish for another')
r = s.ask(2, 'publish', { org = 'lspd', name = '  Class   A ', payload = outfit(MALE) })
check(r.ok == true and r.id == 1 and r.message == 'Class A published for Los Santos Police Department.', 'a manager publishes for their org: ' .. tostring(r.message))
local row = s.rows[1]
check(row.org == 'lspd' and row.name == 'Class A' and row.model == MALE and row.payload == outfit(MALE), 'stored with the cleaned name, model and the payload exactly as sent')
check(row.created_by == 'license:two' and row.created_by_name == 'Player 2' and row.updated_by_name == 'Player 2', 'who published it is kept')
check(s.infos[#s.infos]:find('license:two', 1, true) and s.infos[#s.infos]:find('published lspd outfit #1', 1, true), 'publishing is logged')
r = s.ask(3, 'publish', { org = 'fire', name = 'Turnouts', payload = outfit(FEMALE) })
check(r.ok == true and s.rows[2].org == 'fire' and s.rows[2].model == FEMALE, 'ManageAll publishes for any org')
s.wait()
r = s.ask(3, 'publish', { org = 'newdept', name = 'Duty', payload = outfit(MALE) })
check(r.ok == true and r.message == 'Duty published for newdept.', 'ManageAll covers an org the config does not list yet')
for _, bad in ipairs({ 'lspd;--', '', ('x'):rep(33), 5, 'ls pd' }) do
    s.wait()
    check(s.ask(3, 'publish', { org = bad, name = 'X', payload = outfit(MALE) }).ok == false, 'bad org ids are refused: ' .. tostring(bad))
end
s.wait()
r = s.ask(2, 'publish', { org = 'lspd', name = 'From CSharp', payload = CSHARP_PAYLOAD })
check(r.ok, 'the JSON the client writes is accepted: ' .. tostring(r.message))
s.wait()
check(s.ask(2, 'wear', { id = r.id }).payload == CSHARP_PAYLOAD, 'and handed back byte for byte')
-- Department heads (ManageOwn) manage only the departments they may /duty as.
r = s.ask(6, 'publish', { org = 'bcso', name = 'Head Uniform', payload = outfit(MALE) })
check(r.ok == true, 'a department head publishes for their own department: ' .. tostring(r.message))
s.wait()
check(s.ask(6, 'publish', { org = 'lspd', name = 'Not Mine', payload = outfit(MALE) }).ok == false, 'but not for another department')
s.wait()
check(s.ask(7, 'publish', { org = 'bcso', name = 'Nobody', payload = outfit(MALE) }).ok == false, 'a department head in no department manages nothing')
s.wait()
local headList = s.ask(6, 'list', { model = MALE })
check(#headList.manage == 1 and headList.manage[1].id == 'bcso', 'a department head is offered only their own department')
print('PASS: only managers publish, only for their orgs; ManageAll covers every org; department heads manage their own department')

-- ---- Names --------------------------------------------------------------------------------------
s.wait()
r = s.ask(2, 'publish', { org = 'lspd', name = 'class a', payload = outfit(MALE) })
check(r.ok == false and r.message:find('already an outfit called class a', 1, true), 'names are unique per org and body type, ignoring case')
s.wait()
r = s.ask(2, 'publish', { org = 'lspd', name = 'Class A', payload = outfit(FEMALE) })
check(r.ok == true, 'the same name is fine for the other body type')
local refusedNames = { '', '   ', ('a'):rep(41), '~r~Red', 'Bell\7','Emoji \u{1F600}', 'a<b>', 'semi;colon', 5 }
local before = #s.rows
for _, bad in ipairs(refusedNames) do
    s.wait()
    check(s.ask(2, 'publish', { org = 'lspd', name = bad, payload = outfit(MALE) }).ok == false, 'refused name: ' .. tostring(bad))
end
s.wait()
check(s.ask(2, 'publish', { org = 'lspd', name = ("SWAT (Night) - K9 & Co. #2/3 + O'Neil, 1"), payload = outfit(MALE) }).ok == true, 'names may use - _ \' . ( ) & / # + ,')
check(#s.rows == before + 1, 'nothing stored for refused names')
print('PASS: names are cleaned, limited and unique per org and body')

-- ---- Payloads -----------------------------------------------------------------------------------
local badPayloads = {
    ['not a string'] = {},
    ['empty'] = '',
    ['not JSON'] = '{"model":',
    ['over 8 KB'] = outfit(MALE, function(d) d.layout = nil; d.clothes['5'] = { 1, 0 }; d.refs.clothes['5'] = { col = ('a'):rep(64), drw = 1, tex = 0, lp = ('f'):rep(9000) } end),
    ['not an MP body'] = outfit(1234),
    ['refs for the other body'] = outfit(MALE, function(d) d.refs.model = FEMALE end),
    ['no refs'] = outfit(MALE, function(d) d.refs = nil end),
    ['old refs version'] = outfit(MALE, function(d) d.refs.v = 1 end),
    ['a piece waiting for review'] = outfit(MALE, function(d) d.refs.clothes['11'] = { legacy = { 412, 2 }, why = 'not-in-baseline' } end),
    ['a piece without a reference'] = outfit(MALE, function(d) d.refs.clothes['11'] = nil end),
    ['a prop without a reference'] = outfit(MALE, function(d) d.refs.props['6'] = nil end),
    ['an unknown field'] = outfit(MALE, function(d) d.script = 'x' end),
    ['an unknown ref field'] = outfit(MALE, function(d) d.refs.clothes['3'].owner = 'me' end),
    ['a bad slot'] = outfit(MALE, function(d) d.clothes['99'] = { 1, 0 } end),
    ['a bad pair'] = outfit(MALE, function(d) d.clothes['3'] = { 4, 0, 9 } end),
    ['a bad drawable'] = outfit(MALE, function(d) d.clothes['3'] = { 5000, 0 } end),
    ['a fractional texture'] = outfit(MALE, function(d) d.clothes['3'] = { 4, 0.5 } end),
    ['a bad collection'] = outfit(MALE, function(d) d.refs.clothes['11'].col = 'x y' end),
    ['a bad layout'] = outfit(MALE, function(d) d.layout = 'not hex' end),
    ['no clothes'] = outfit(MALE, function(d) d.clothes = {}; d.refs.clothes = {} end),
    ['an empty clothing slot'] = outfit(MALE, function(d) d.refs.clothes['3'] = { empty = true, col = '' } end),
}
before = #s.rows
for what, bad in pairs(badPayloads) do
    s.wait()
    r = s.ask(2, 'publish', { org = 'lspd', name = 'Bad ' .. count(s.rows), payload = bad })
    check(r.ok == false and type(r.message) == 'string' and not r.message:find('wait', 1, true), 'refused payload: ' .. what)
end
check(#s.rows == before, 'nothing stored for refused payloads')
s.wait()
check(s.ask(2, 'publish', { org = 'lspd', name = 'Good One', payload = outfit(MALE) }).ok, 'the same request with a good payload is accepted')
print('PASS: payloads must be a whole MP outfit with every piece matched to a collection, under 8 KB')

-- ---- Rate limit and cap -------------------------------------------------------------------------
s = newServer()
check(s.ask(2, 'publish', { org = 'lspd', name = 'One', payload = outfit(MALE) }).ok, 'first publish')
r = s.ask(2, 'publish', { org = 'lspd', name = 'Two', payload = outfit(MALE) })
check(r.ok == false and r.message:find('every 10 seconds', 1, true), 'one publish per 10 seconds')
check(s.ask(2, 'publish', { org = 'lspd', name = '~bad~', payload = outfit(MALE) }).ok == false, 'still refused')
s.now = s.now + 10000
check(s.ask(2, 'publish', { org = 'lspd', name = 'Two', payload = outfit(MALE) }).ok, 'allowed again after 10 seconds')
s.wait()
check(s.ask(2, 'publish', { org = 'lspd', name = '~bad~', payload = outfit(MALE) }).ok == false and
    s.ask(2, 'publish', { org = 'lspd', name = 'Three', payload = outfit(MALE) }).ok, 'a refused publish does not use up the limit')
s.licenses[2] = nil
s.wait()
check(s.ask(2, 'publish', { org = 'lspd', name = 'Four', payload = outfit(MALE) }).ok, 'a player without a license is limited by source')
check(s.ask(2, 'publish', { org = 'lspd', name = 'Five', payload = outfit(MALE) }).ok == false, '... still limited')
for i = 1, 56 do s.rows[#s.rows + 1] = { id = 1000 + i, org = 'lspd', name = 'Filler ' .. i, model = MALE, payload = outfit(MALE) } end
s.wait()
r = s.ask(2, 'publish', { org = 'lspd', name = 'Sixty One', payload = outfit(MALE) })
check(r.ok == false and r.message:find('up to 60', 1, true), 'at most 60 outfits per org and body type')
s.wait()
check(s.ask(2, 'publish', { org = 'lspd', name = 'Sixty One', payload = outfit(FEMALE) }).ok, 'the cap is per body type')
print('PASS: publishing is rate limited per license and capped per org and body')

-- ---- Requests sent together, cached access, org spelling, player names --------------------------
s = newServer()
local during
s.whileCounting = function() during = s.ask(2, 'publish', { org = 'lspd', name = 'Second', payload = outfit(MALE) }) end
check(s.ask(2, 'publish', { org = 'lspd', name = 'First', payload = outfit(MALE) }).ok, 'the first of two publishes sent together')
check(during and during.ok == false and during.message:find('every 10 seconds', 1, true), 'the second is refused while the first waits on the database')
check(#s.rows == 1, 'so only one is stored')
s.wait()
s.rows[#s.rows + 1] = { id = 500, org = 'lspd', name = 'Taken', model = MALE, payload = outfit(MALE) }
check(s.ask(2, 'publish', { org = 'lspd', name = 'Taken', payload = outfit(MALE) }).ok == false and
    s.ask(2, 'publish', { org = 'lspd', name = 'Free', payload = outfit(MALE) }).ok, 'a publish refused after the wait gives the limit back')

s.wait()
r = s.ask(2, 'publish', { org = 'LSPD', name = 'Shouting', payload = outfit(MALE) })
check(r.ok and s.rows[#s.rows].org == 'lspd', 'an org is stored as the config spells it (LSPD -> lspd)')
s.wait()
r = s.ask(3, 'publish', { org = 'certcivt2', name = 'Cert', payload = outfit(MALE) })
check(r.ok and s.rows[#s.rows].org == 'CertCivt2', '... and certcivt2 -> CertCivt2, the org PoliceEMSActivity puts you on duty with')

s.wait()
s.names[2] = ('a'):rep(62) .. '\u{1F600}'
s.ask(2, 'publish', { org = 'lspd', name = 'Long Name', payload = outfit(MALE) })
check(s.rows[#s.rows].created_by_name == ('a'):rep(62), 'a player name cut at 64 bytes drops a half character')
s.wait()
s.names[2] = ('a'):rep(60) .. '\u{1F600}' .. 'b'
s.ask(2, 'publish', { org = 'lspd', name = 'Whole Emoji', payload = outfit(MALE) })
check(s.rows[#s.rows].created_by_name == ('a'):rep(60) .. '\u{1F600}', 'and keeps a whole one')
s.wait()
s.names[2] = ('a'):rep(62) .. '\u{E9}z'
s.ask(2, 'publish', { org = 'lspd', name = 'Accent', payload = outfit(MALE) })
check(s.rows[#s.rows].created_by_name == ('a'):rep(62) .. '\u{E9}', 'a two-byte character ending at byte 64 stays')

s = newServer()
s.ask(6, 'list', { model = MALE })
local firstCalls = s.aceCalls
s.now = s.now + 1000
s.ask(6, 'list', { model = MALE })
check(firstCalls > 0 and s.aceCalls == firstCalls, 'who manages what is read once, then cached (' .. firstCalls .. ' ace checks, then none)')
s.aces[6]['group.bcsoblip'] = nil
check(s.ask(6, 'publish', { org = 'bcso', name = 'Cached', payload = outfit(MALE) }).ok, 'for up to 30 seconds')
s.now = s.now + 30000
check(s.ask(6, 'publish', { org = 'bcso', name = 'Expired', payload = outfit(MALE) }).ok == false, 'then read again')
s.aces[6]['group.bcsoblip'] = true
s.now = s.now + 30000
s.ask(6, 'list', { model = MALE })
s.aces[6]['group.bcsoblip'] = nil
_G.source = 6
s.events.playerDropped('left')
_G.source = nil
s.now = s.now + 10000
check(s.ask(6, 'publish', { org = 'bcso', name = 'Rejoined', payload = outfit(MALE) }).ok == false, 'and forgotten when the player leaves')
s.duty[1] = 'bcso'
local calls = s.aceCalls
s.ask(1, 'list', { model = MALE })
s.now = s.now + 1000
local switched = s.ask(1, 'list', { model = MALE })
s.duty[1] = 'lspd'
s.now = s.now + 1000
switched = s.ask(1, 'list', { model = MALE })
check(switched.duty and switched.duty.id == 'lspd', 'duty is read fresh on every request')
print('PASS: requests sent together, cached access, org spelling and player names')

-- ---- Listing and wearing ------------------------------------------------------------------------
s = newServer()
s.ask(2, 'publish', { org = 'lspd', name = 'Patrol', payload = outfit(MALE) }); s.wait()
s.ask(2, 'publish', { org = 'lspd', name = 'Class A', payload = outfit(MALE) }); s.wait()
s.ask(2, 'publish', { org = 'lspd', name = 'Patrol', payload = outfit(FEMALE) }); s.wait()
s.ask(3, 'publish', { org = 'bcso', name = 'Patrol', payload = outfit(MALE) }); s.wait()
s.ask(3, 'publish', { org = 'sahp', name = 'Motor', payload = outfit(MALE) }); s.wait()

local function names(reply)
    local out = {}
    for _, o in ipairs(reply.outfits) do out[#out + 1] = o.org .. ':' .. o.name end
    return table.concat(out, ',')
end
r = s.ask(1, 'list', { model = MALE, org = 'bcso' })
check(r.ok and r.dutyAvailable == true and r.duty.id == 'lspd' and r.duty.label == 'Los Santos Police Department', 'a member sees the org they are on duty with')
check(names(r) == 'lspd:Class A,lspd:Patrol' and #r.manage == 0, 'only that org, only their body type, whatever org they claim: ' .. names(r))
check(not r.outfits[1].manage and r.outfits[1].by == 'Player 2' and r.outfits[1].updated == '2026-11-17', 'members cannot manage; who and when are shown')
s.wait()
r = s.ask(1, 'list', { model = FEMALE })
check(names(r) == 'lspd:Patrol', 'the other body type has its own list')
s.wait()
r = s.ask(4, 'list', { model = MALE })
check(r.ok and r.duty == nil and #r.manage == 0 and #r.outfits == 0, 'off duty and managing nothing: an empty list')
s.wait()
r = s.ask(5, 'list', { model = MALE })
check(r.duty.id == 'bcso' and names(r) == 'bcso:Patrol,lspd:Class A,lspd:Patrol,sahp:Motor', 'duty org plus managed orgs: ' .. names(r))
local manageIds = {}
for _, m in ipairs(r.manage) do manageIds[#manageIds + 1] = m.id .. '=' .. m.label end
check(table.concat(manageIds, ',') == 'lspd=Los Santos Police Department,sahp=San Andreas Highway Patrol', 'managed orgs in config order with labels: ' .. table.concat(manageIds, ','))
for _, o in ipairs(r.outfits) do check(o.manage == (o.org ~= 'bcso'), 'manage flag per org: ' .. o.org) end
s.wait()
r = s.ask(3, 'list', { model = MALE })
check(#r.manage == 33 and r.manage[1].id == 'lspd' and r.manage[33].id == 'odf', 'ManageAll lists every configured org (' .. #r.manage .. ')')
for _, bad in ipairs({ 1234, 'x', -MALE }) do
    s.wait()
    check(s.ask(1, 'list', { model = bad }).ok == false, 'list refuses a model that is not an MP body: ' .. tostring(bad))
end
s.wait()
check(s.ask(1, 'list', { model = MALE }).ok and s.ask(1, 'list', { model = MALE }).message:find('wait', 1, true), 'listing is rate limited')

local lspdMale = s.rows[1].id
local bcsoMale = s.rows[4].id
s.wait()
r = s.ask(1, 'wear', { id = lspdMale })
check(r.ok and r.payload == outfit(MALE) and r.name == 'Patrol' and r.model == MALE, 'a member on duty wears their org outfit, exactly as stored')
s.wait()
r = s.ask(1, 'wear', { id = bcsoMale })
check(r.ok == false and r.payload == nil and r.message:find("Blaine County Sheriff's Office", 1, true), 'not an org they are on duty with')
s.wait()
check(s.ask(4, 'wear', { id = lspdMale }).ok == false, 'off duty: refused')
s.wait()
check(s.ask(2, 'wear', { id = lspdMale }).ok, 'a manager wears their org outfits off duty')
s.wait()
check(s.ask(5, 'wear', { id = lspdMale }).ok and (function() s.wait(); return s.ask(5, 'wear', { id = bcsoMale }).ok end)(), 'duty and managed orgs both work')
s.wait()
check(s.ask(1, 'wear', { id = 999 }).message:find('deleted', 1, true) and (function() s.wait(); return s.ask(1, 'wear', { id = 'x' }).ok == false end)(), 'missing or bad ids')
s.duty[1] = nil
s.wait()
check(s.ask(1, 'wear', { id = lspdMale }).ok == false, 'going off duty takes access away at once')
print('PASS: members list and wear their duty org only; managers also their orgs; per body type')

-- ---- Replace, rename, delete -------------------------------------------------------------------
s.wait()
local newLook = outfit(MALE, function(d) d.clothes['11'] = { 413, 0 }; d.refs.clothes['11'].tex = 0 end)
check(s.ask(1, 'replace', { id = lspdMale, payload = newLook }).ok == false, 'members cannot replace')
s.wait()
check(s.ask(5, 'replace', { id = bcsoMale, payload = newLook }).ok == false, 'on duty with bcso but not managing it: cannot replace')
s.wait()
r = s.ask(2, 'replace', { id = lspdMale, payload = outfit(FEMALE) })
check(r.ok == false and r.message:find('same body type', 1, true), 'a replacement must be for the same body')
s.wait()
check(s.ask(2, 'replace', { id = lspdMale, payload = outfit(MALE, function(d) d.refs = nil end) }).ok == false, 'replacements are checked like publishes')
s.wait()
r = s.ask(5, 'replace', { id = lspdMale, payload = newLook })
check(r.ok and s.rows[1].payload == newLook and s.rows[1].updated_by_name == 'Player 5', 'a manager replaces it')
check(s.ask(5, 'replace', { id = lspdMale, payload = newLook }).message:find('wait', 1, true), 'replacing is rate limited')
s.wait()
r = s.ask(2, 'rename', { id = lspdMale, name = 'class a' })
check(r.ok == false and r.message:find('already an outfit', 1, true), 'rename cannot take another outfit name')
s.wait()
check(s.ask(1, 'rename', { id = lspdMale, name = 'Day Patrol' }).ok == false, 'members cannot rename')
s.wait()
check(s.ask(2, 'rename', { id = lspdMale, name = '~Bad~' }).ok == false, 'rename checks the name')
s.wait()
r = s.ask(2, 'rename', { id = lspdMale, name = '  Day  Patrol ' })
check(r.ok and s.rows[1].name == 'Day Patrol' and r.message == 'Patrol renamed to Day Patrol.', 'a manager renames it')
s.wait()
check(s.ask(2, 'rename', { id = lspdMale, name = 'DAY PATROL' }).ok, 'changing only the case of its own name is fine')
s.wait()
check(s.ask(1, 'delete', { id = bcsoMale }).ok == false and s.ask(4, 'delete', { id = lspdMale }).ok == false, 'members and civilians cannot delete')
s.wait()
r = s.ask(2, 'delete', { id = lspdMale })
check(r.ok and #s.rows == 4, 'a manager deletes it')
s.wait()
check(s.ask(2, 'delete', { id = lspdMale }).message:find('already deleted', 1, true), 'deleting twice')
s.wait()
check(s.ask(3, 'delete', { id = bcsoMale }).ok, 'ManageAll deletes any org outfit')
check(#s.infos >= 3 and s.infos[#s.infos]:find('deleted bcso outfit', 1, true), 'edits are logged')
print('PASS: only managers of the org replace, rename and delete; checks and limits apply')

-- ---- Without PoliceEMSActivity, and with it failing ---------------------------------------------
s = newServer()
s.ask(2, 'publish', { org = 'lspd', name = 'Patrol', payload = outfit(MALE) }); s.wait()
s.resources.PoliceEMSActivity = 'stopped'
r = s.ask(1, 'list', { model = MALE })
check(r.ok and r.dutyAvailable == false and r.duty == nil and #r.outfits == 0, 'without PoliceEMSActivity nobody counts as on duty')
s.wait()
check(s.ask(1, 'wear', { id = 1 }).ok == false, '... so members cannot wear')
s.wait()
r = s.ask(2, 'list', { model = MALE })
check(r.ok and #r.manage == 1 and names(r) == 'lspd:Patrol', 'managers still list their orgs')
s.wait()
check(s.ask(2, 'wear', { id = 1 }).ok and s.ask(2, 'publish', { org = 'lspd', name = 'Class B', payload = outfit(MALE) }).ok, 'managers still wear and publish')
s.resources.PoliceEMSActivity = 'started'
s.dutyThrows = true
s.wait()
r = s.ask(1, 'list', { model = MALE })
check(r.ok and r.duty == nil and r.dutyAvailable == true, 'a failing duty export counts as off duty, not an error')
s.dutyThrows = false
s.duty[1] = 'ghost'
s.wait()
r = s.ask(1, 'list', { model = MALE })
check(r.duty.id == 'ghost' and r.duty.label == 'Ghost Unit', "an org the config doesn't list is named by PoliceEMSActivity")
s.startup()
check(#s.errors == 0 and s.infos[#s.infos] == 'Department outfits are enabled.', 'startup check passes with oxmysql and the table')
s.resources.PoliceEMSActivity = nil
s.startup()
check(s.warnings[#s.warnings]:find('PoliceEMSActivity is not started', 1, true), 'startup warns without PoliceEMSActivity')
s.resources.oxmysql = nil
s.startup()
check(s.errors[#s.errors]:find('oxmysql is not started', 1, true), 'startup reports a missing oxmysql')
print('PASS: degrades without PoliceEMSActivity; a broken duty export is off duty')

-- ---- Database errors and big replies ----------------------------------------------------------
s = newServer()
s.failQueries = true
r = s.ask(2, 'publish', { org = 'lspd', name = 'Patrol', payload = outfit(MALE) })
check(r.ok == false and r.message:find('database', 1, true) and #s.errors >= 1, 'database errors are a reply, not a crash')
s.wait()
check(s.ask(1, 'list', { model = MALE }).message:find('database', 1, true), 'list reports database errors')
s.failQueries = false
s.wait()
check(s.ask(2, 'publish', { org = 'lspd', name = 'Patrol', payload = outfit(MALE) }).ok, 'a failed publish does not use up the limit')
for i = 1, 120 do s.rows[#s.rows + 1] = { id = 500 + i, org = 'lspd', name = ('Uniform number %03d'):format(i), model = MALE, updated_by_name = 'Someone' } end
s.wait()
local reply, sent = s.ask(1, 'list', { model = MALE })
check(reply.ok and #reply.outfits == 121 and sent.bps and s.latent[#s.latent] == sent, 'a large list is sent as a latent event')
print('PASS: database errors and large replies')

-- ---- Wiring -------------------------------------------------------------------------------------
local sql = readFile('build/vMenu/database.sql')
local tableSql = sql:match('CREATE TABLE IF NOT EXISTS `vmenu_dept_outfits` (%b())')
check(tableSql, 'database.sql creates vmenu_dept_outfits')
for _, column in ipairs({ '`id`', '`org` varchar%(32%)', '`name` varchar%(40%)', '`model` int%(10%) unsigned', '`payload` text', '`created_by`', '`created_by_name`', '`updated_by_name`', '`updated_at`', 'UNIQUE KEY `org_model_name` %(`org`, `model`, `name`%)' }) do
    check(tableSql:find(column), 'database.sql column: ' .. column)
end
local cfg = readFile('build/vMenu/config/permissions.cfg')
check(cfg:find('setr vmenu_dept_outfits', 1, true) and cfg:find('#add_ace group.%w+ "vMenu.DeptOutfits.Manage.lspd" allow') and cfg:find('#add_ace group.admin "vMenu.DeptOutfits.ManageAll" allow', 1, true),
    'permissions.cfg has the convar and commented example aces')
check(cfg:find('\nadd_ace group.depthead "vMenu.DeptOutfits.ManageOwn" allow', 1, true), 'PSRP: department heads manage their own department')
local serverCfg = readFile('build/vMenu/config/config_server.lua')
check(serverCfg:find('{ id = "lspd", label = "Los Santos Police Department", member = "group.lspdblip" }', 1, true)
    and select(2, serverCfg:gsub('member = "group%.', '')) == 33, 'every configured org names its duty permission')
check(readFile('build/vMenu/fxmanifest.lua'):find("'server/*.lua'", 1, true), 'the manifest loads server/deptOutfits.lua')
check(readFile('build/vMenu/server/deptOutfits.lua'):find("VMenuGuard.netEvent('vMenu:DeptOutfits:Request'", 1, true)
    and not readFile('build/vMenu/server/deptOutfits.lua'):find("RegisterNetEvent('vMenu:DeptOutfits:Request'", 1, true),
    'requests go through the request guard (rate limit, argument types, security log)')
local client = readFile('vMenu/DepartmentOutfits.cs')
check(client:find('"vMenu:DeptOutfits:Request"', 1, true) and client:find('"vMenu:DeptOutfits:Reply"', 1, true) and client:find('"vmenu_dept_outfits"', 1, true),
    'the client uses the same events and convar')
print('PASS: database.sql, permissions.cfg, manifest and client wiring')
