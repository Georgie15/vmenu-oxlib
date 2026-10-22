-- Run from the repo root: lua tests/save-backups/test_backups.lua
-- Runs the production client/backups.lua and server/backups.lua against fake FiveM, ox_lib and
-- oxmysql: what goes into a backup, the server's checks and rate limits, the merge-only restore
-- that never overwrites a save you already have, and auto backup with its reminder.

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

local function count(t) local n = 0 for _ in pairs(t) do n = n + 1 end return n end

-- GTA's joaat, as GetHashKey returns it (case-insensitive, signed).
local function joaat(text)
    text = text:lower()
    local h = 0
    for i = 1, #text do
        h = (h + text:byte(i)) & 0xFFFFFFFF
        h = (h + (h << 10)) & 0xFFFFFFFF
        h = h ~ (h >> 6)
    end
    h = (h + (h << 3)) & 0xFFFFFFFF
    h = h ~ (h >> 11)
    h = (h + (h << 15)) & 0xFFFFFFFF
    return h >= 0x80000000 and h - 0x100000000 or h
end

-- A client with these KVPs. lib.alertDialog answers client.answer ('confirm' by default).
local function newClient(kvps)
    local c = { kvps = kvps, notes = {}, latent = {}, server = {}, handlers = {}, dialogs = {}, now = 0, answer = 'confirm',
        clock = 1700000000, convar = 'true' }
    c.info = function() return false, "You don't have a backup on this server yet." end

    _G.GetGameTimer = function() return c.now end
    _G.GetResourceKvpString = function(k) local v = c.kvps[k]; return type(v) == 'string' and v or nil end
    _G.SetResourceKvp = function(k, v) c.kvps[k] = v end
    local finds, nextHandle = {}, 1
    _G.StartFindKvp = function(prefix)
        local keys = {}
        for k in pairs(c.kvps) do if k:sub(1, #prefix) == prefix then keys[#keys + 1] = k end end
        table.sort(keys)
        finds[nextHandle] = { keys = keys, i = 0 }
        nextHandle = nextHandle + 1
        return nextHandle - 1
    end
    _G.FindKvp = function(h) local f = finds[h]; f.i = f.i + 1; return f.keys[f.i] end
    _G.EndFindKvp = function(h) finds[h] = nil end
    _G.AddEventHandler = function(name, fn) c.handlers[name] = fn end
    _G.RegisterNetEvent = function(name, fn) c.handlers[name] = fn end
    _G.TriggerServerEvent = function(name, ...) c.server[#c.server + 1] = { name = name, args = { ... } } end
    _G.TriggerLatentServerEvent = function(name, bps, ...) c.latent[#c.latent + 1] = { name = name, bps = bps, args = { ... } } end
    _G.Config = { Notify = function(_, msg, kind) c.notes[#c.notes + 1] = { msg = msg, kind = kind } end }
    _G.GetConvar = function(name, default) if name == 'vmenu_backups' then return c.convar end return default end
    _G.GetHashKey = joaat
    _G.GetResourceKvpInt = function(k) local v = c.kvps[k]; return math.type(v) == 'integer' and v or 0 end
    _G.SetResourceKvpInt = function(k, v) c.kvps[k] = math.tointeger(v) end
    _G.GetCloudTimeAsInt = function() return c.clock end
    _G.Wait = function(ms) coroutine.yield(ms) end
    _G.CreateThread = function(fn) c.thread = coroutine.create(fn) end
    _G.lib = {
        alertDialog = function(d) c.dialogs[#c.dialogs + 1] = d; return c.answer end,
        callback = { await = function(name) assert(name == 'vMenu:backups:info'); return c.info() end },
    }
    assert(load(readFile('build/vMenu/client/backups.lua'), '@client/backups.lua'))()

    function c.fire(name, ...) c.handlers[name](...) end
    -- Runs the background thread to its next Wait and returns how long it waits (nil once it ends).
    function c.step()
        local ok, waitMs = coroutine.resume(c.thread)
        assert(ok, waitMs)
        return coroutine.status(c.thread) ~= 'dead' and waitMs or nil
    end
    function c.lastNote() return c.notes[#c.notes] or { msg = '' } end
    return c
end

-- A server with an in-memory vmenu_backups table.
local function newServer()
    local s = { now = 100000, rows = {}, client = {}, latent = {}, handlers = {}, callbacks = {}, enabled = true,
        licenses = { [1] = 'license:aaa', [2] = 'license:bbb' }, warnings = {}, errors = {}, failQueries = false }

    _G.GetGameTimer = function() return s.now end
    _G.GetConvarBool = function(name, default) if name == 'vmenu_backups' then return s.enabled end return default end
    _G.GetPlayerIdentifierByType = function(src, kind) assert(kind == 'license'); return s.licenses[src] end
    _G.GetPlayerName = function(src) return 'player' .. src end
    _G.GetResourceState = function() return 'started' end
    _G.RegisterNetEvent = function(name, fn) s.handlers[name] = fn end
    _G.TriggerClientEvent = function(name, target, ...) s.client[#s.client + 1] = { name = name, target = target, args = { ... } } end
    _G.TriggerLatentClientEvent = function(name, target, bps, ...) s.latent[#s.latent + 1] = { name = name, target = target, bps = bps, args = { ... } } end
    _G.CreateThread = function(fn) s.startup = fn end
    _G.lib = {
        callback = { register = function(name, fn) s.callbacks[name] = fn end },
        print = {
            warn = function(m) s.warnings[#s.warnings + 1] = m end,
            error = function(m) s.errors[#s.errors + 1] = m end,
            info = function() end,
        },
    }
    local db = {}
    function db.query_async(_, query, params)
        if s.failQueries then error('connection lost') end
        if query:find('^INSERT') then
            s.rows[params[1]] = { data = params[2], entries = params[3], updated = '2026-10-21 12:00' }
            return { affectedRows = 1 }
        end
        return {}
    end
    function db.single_async(_, query, params)
        if s.failQueries then error('connection lost') end
        local row = s.rows[params[1]]
        if not row then return nil end
        if query:find('`entries`') then return { entries = row.entries, updated = row.updated } end
        return { data = row.data }
    end
    _G.exports = { oxmysql = db }
    assert(load(readFile('build/vMenu/server/backups.lua'), '@server/backups.lua'))()

    function s.send(src, name, ...) _G.source = src; s.handlers[name](...); _G.source = nil end
    function s.info(src) return s.callbacks['vMenu:backups:info'](src) end
    function s.lastReply() return s.client[#s.client] end
    return s
end

local SAVES = {
    veh_Cruiser = '{"model":1,"name":"Cruiser \\"LSPD\\""}',
    veh_Bike = '{"model":2}',
    saved_veh_category_Police = '{"Name":"Police"}',
    ped_Cop = '{"model":3}',
    mp_ped_Main = '{"SaveName":"mp_ped_Main"}',
    mp_character_category_Work = '{"Name":"Work"}',
    vmenu_string_saved_weapon_loadout_Patrol = '[{"Hash":1}]',
    vmenu_vehicle_preset_2046537925 = '{"extras":{"1":true}}',
    vmenu_default_character = 'mp_ped_Main',
    vmenu_last_character = 'mp_ped_Main',
    vmenu_string_default_loadout = 'vmenu_string_saved_weapon_loadout_Patrol',
    vmenu_vehicle_favourites = '[2046537925]',
    vmenu_vehicle_recent = '[1,2,2046537925]',
    vmenu_outfit_Work = '{"model":1885233650,"clothes":{"11":[5,0]},"props":{}}',
    vmenu_ped_favourites = '[3459037009]',
    vmenu_ped_recent = '[3459037009]',
}
local NOT_SAVES = {
    settings_noclip_speed = '3',
    vmenu_temp_weapons_loadout_before_respawn = '[]',
    vMenu_tmp_saved_ped = '{}',
    vmenu_last_mp_character_snapshot = '{}',
    veh_ = 'just the prefix',
    vmenu_cleanup_version = 2,
    vmenu_auto_backup = 1,
    vmenu_backup_fingerprint = '10:20:30',
    vmenu_backup_last_at = 1600000000,
    vmenu_pv_autolock = 1,
    vmenu_whats_new_seen = '2026-10-21-f',
}

local function copy(...)
    local out = {}
    for _, t in ipairs({ ... }) do for k, v in pairs(t) do out[k] = v end end
    return out
end

-- What a backup contains.
local c = newClient(copy(SAVES, NOT_SAVES))
c.fire('vMenu:backups:backup')
check(#c.latent == 1 and c.latent[1].name == 'vMenu:backups:upload' and c.latent[1].bps > 0, 'backup sent as one latent event')
local sent = json.decode(c.latent[1].args[1])
check(count(sent) == count(SAVES), 'only saves are backed up (got ' .. count(sent) .. ')')
for k, v in pairs(SAVES) do check(sent[k] == v, 'backed up unchanged: ' .. k) end
check(c.dialogs[1].content:find('16 saves', 1, true), 'the confirmation says how many saves')

c.fire('vMenu:backups:backup')
check(#c.latent == 1 and c.lastNote().msg:find('in progress', 1, true), 'one transfer at a time')
c.fire('vMenu:backups:uploadResult', true, 'Backed up 13 saves to the server.')
c.fire('vMenu:backups:backup')
check(#c.latent == 2, 'the server reply ends the transfer')
c.now = c.now + 61000
c.fire('vMenu:backups:backup')
check(#c.latent == 3, 'no reply within a minute also ends it')
print('PASS: backups hold saves, categories, loadouts, presets, outfits, favourites, recents and defaults, not settings or temporary keys')

c = newClient(copy(SAVES))
c.answer = 'cancel'
c.fire('vMenu:backups:backup')
check(#c.latent == 0, 'cancelling sends nothing')
c = newClient(copy(NOT_SAVES))
c.fire('vMenu:backups:backup')
check(#c.latent == 0 and #c.dialogs == 0 and c.lastNote().kind == 'error', 'nothing to back up')
c = newClient({ veh_Huge = string.rep('a', 2 * 1024 * 1024) })
c.fire('vMenu:backups:backup')
check(#c.latent == 0 and c.lastNote().msg:find('2 MB', 1, true), 'over 2 MB is refused before sending')
c = newClient(copy(SAVES))
c.info = function() return { entries = 4, updated = '2026-10-01 09:30' } end
c.fire('vMenu:backups:backup')
check(c.dialogs[1].content:find('replaces your backup from 2026-10-01 09:30 (4 saves)', 1, true), 'the confirmation names the backup it replaces')
print('PASS: cancel, nothing to back up, size limit and replace warning')

-- The server's checks.
local s = newServer()
local payload = c.latent[1].args[1]
check(s.info(1) == false, 'no backup yet')
s.send(1, 'vMenu:backups:upload', payload)
check(s.lastReply().name == 'vMenu:backups:uploadResult' and s.lastReply().target == 1 and s.lastReply().args[1] == true, 'a valid backup is stored')
check(s.rows['license:aaa'].entries == 16 and count(json.decode(s.rows['license:aaa'].data)) == 16, 'stored per license with its count')
s.send(1, 'vMenu:backups:upload', payload)
check(s.lastReply().args[1] == false and s.lastReply().args[2]:find('once a minute', 1, true), 'backups are limited to one a minute')
s.now = s.now + 60000
s.send(1, 'vMenu:backups:upload', payload)
check(s.lastReply().args[1] == true, 'allowed again after a minute')

local refused = {
    ['not a string'] = {},
    ['empty'] = '',
    ['over 2 MB'] = string.rep('x', 2 * 1024 * 1024 + 1),
    ['not JSON'] = '{"veh_a":',
    ['a list'] = '["veh_a"]',
    ['empty object'] = '{}',
    ['a setting'] = '{"veh_a":"{}","settings_menu_key":"1"}',
    ['a temporary key'] = '{"vMenu_tmp_saved_ped":"{}"}',
    ['a non-string value'] = '{"veh_a":5}',
    ['an empty value'] = '{"veh_a":""}',
}
for what, bad in pairs(refused) do
    s.send(2, 'vMenu:backups:upload', bad)
    check(s.lastReply().target == 2 and s.lastReply().args[1] == false, 'refused: ' .. what)
end
check(s.rows['license:bbb'] == nil and #s.warnings == count(refused), 'nothing stored, each refusal logged')
s.send(2, 'vMenu:backups:upload', payload)
check(s.lastReply().args[1] == true, 'refused uploads do not use up the rate limit')

local many = {}
for i = 1, 5001 do many['veh_' .. i] = '{}' end
s.licenses[3] = 'license:ccc'
s.send(3, 'vMenu:backups:upload', json.encode(many))
check(s.lastReply().args[1] == false and s.rows['license:ccc'] == nil, 'more than 5000 saves is refused')
s.licenses[4] = nil
s.send(4, 'vMenu:backups:upload', payload)
check(s.lastReply().args[1] == false and s.lastReply().args[2]:find('license', 1, true), 'a player without a license is refused')
s.enabled = false
s.licenses[5] = 'license:eee'
s.send(5, 'vMenu:backups:upload', payload)
check(s.lastReply().args[1] == false and s.rows['license:eee'] == nil, 'refused while vmenu_backups is off')
check(s.info(5) == false, 'info refused while off')
s.enabled = true
s.failQueries = true
s.send(5, 'vMenu:backups:upload', payload)
check(s.lastReply().args[1] == false and s.lastReply().args[2]:find('could not save', 1, true) and #s.errors == 1, 'a database error is reported, not thrown')
s.failQueries = false
print('PASS: server validation, rate limit, license, convar and database errors')

-- Info and download.
s.now = s.now + 3000
local info = s.info(1)
check(type(info) == 'table' and info.entries == 16 and info.updated == '2026-10-21 12:00', 'info returns the count and date')
local ok, reason = s.info(1)
check(ok == false and reason:find('wait', 1, true), 'info is rate limited')
s.send(1, 'vMenu:backups:download')
check(#s.latent == 1 and s.latent[1].name == 'vMenu:backups:restoreData' and s.latent[1].target == 1, 'restore sent as a latent event')
s.send(1, 'vMenu:backups:download')
check(#s.latent == 1 and s.lastReply().name == 'vMenu:backups:restoreFailed', 'restores are limited to one per 30 s')
s.now = s.now + 30000
s.send(1, 'vMenu:backups:download')
check(#s.latent == 2, 'allowed again after 30 s')
s.licenses[6] = 'license:fff'
s.send(6, 'vMenu:backups:download')
check(s.lastReply().name == 'vMenu:backups:restoreFailed' and s.lastReply().args[1]:find("don't have a backup", 1, true), 'no backup to restore')
print('PASS: info, download and restore rate limit')

-- Restoring only adds what is missing. (Client and server fakes share globals, so ask the server first.)
s.now = s.now + 3000
local backupInfo = s.info(1)
c = newClient({ veh_Cruiser = '{"model":1,"mine":"newer"}', settings_noclip_speed = '1' })
c.info = function() return backupInfo end
c.fire('vMenu:backups:restore')
check(#c.server == 1 and c.server[1].name == 'vMenu:backups:download', 'confirming asks the server for the backup')
check(c.dialogs[1].content:find('2026-10-21 12:00 (16 saves)', 1, true), 'the confirmation names the backup')
local restored = json.decode(s.latent[1].args[1])
restored.settings_injected = 'x'
c.fire('vMenu:backups:restoreData', json.encode(restored))
check(c.kvps.veh_Cruiser == '{"model":1,"mine":"newer"}', 'a save you already have is never overwritten')
check(c.kvps.settings_injected == nil and c.kvps.settings_noclip_speed == '1', 'only saves are written')
for k, v in pairs(SAVES) do
    if k ~= 'veh_Cruiser' then check(c.kvps[k] == v, 'restored: ' .. k) end
end
check(c.lastNote().msg == 'Restored 15 saves. 1 you already had were kept as they are. Reopen the menu to see them.', 'reports what was added and kept: ' .. c.lastNote().msg)
c.fire('vMenu:backups:restoreData', json.encode(restored))
check(c.lastNote().msg:find('Nothing to restore', 1, true), 'restoring twice adds nothing')
c.fire('vMenu:backups:restoreData', 'garbage')
check(c.lastNote().kind == 'error', 'an unreadable backup is reported')

c = newClient({})
c.fire('vMenu:backups:restore')
check(#c.server == 0 and #c.dialogs == 0 and c.lastNote().msg:find("don't have a backup", 1, true), 'nothing to restore')
c = newClient({})
c.info = function() return { entries = 1, updated = 'x' } end
c.answer = 'cancel'
c.fire('vMenu:backups:restore')
check(#c.server == 0, 'cancelling a restore sends nothing')
c.answer = 'confirm'
c.fire('vMenu:backups:restore')
c.fire('vMenu:backups:restoreFailed', 'You can restore your saves once every 30 seconds. Please wait and try again.')
c.fire('vMenu:backups:restore')
check(#c.server == 2, 'a failed restore ends the transfer')
print('PASS: merge-only restore, never overwrites, ignores non-saves')

-- Auto backup: uploads only changed saves, quietly, and records each successful backup.
c = newClient(copy(SAVES))
c.fire('vMenu:backups:backup')
c.fire('vMenu:backups:uploadResult', true, 'Backed up 13 saves to the server.')
check(type(c.kvps.vmenu_backup_fingerprint) == 'string' and c.kvps.vmenu_backup_last_at == c.clock, 'a successful backup is recorded on this PC')
c.fire('vMenu:backups:autoNow')
check(#c.latent == 1, 'unchanged saves are not uploaded again')
c.kvps.veh_New = '{"model":9,"name":"cruiser"}'
c.fire('vMenu:backups:autoNow')
check(#c.latent == 2 and #c.dialogs == 1 and json.decode(c.latent[2].args[1]).veh_New, 'changed saves are uploaded without a dialog')
c.fire('vMenu:backups:autoNow')
check(#c.latent == 2, 'one upload at a time')
local before = c.kvps.vmenu_backup_fingerprint
c.fire('vMenu:backups:uploadResult', true, 'Backed up 14 saves to the server.')
check(c.kvps.vmenu_backup_fingerprint ~= before and c.lastNote().msg == 'Your saves were backed up automatically.' and c.lastNote().kind == 'inform',
    'an automatic backup is a quiet note')
c.kvps.veh_New = '{"model":9,"name":"Cruiser"}'
c.fire('vMenu:backups:autoNow')
check(#c.latent == 3, 'a change of letter case alone still counts as a change')
c.fire('vMenu:backups:uploadResult', false, 'You can back up your saves once a minute. Please wait and try again.')
check(c.lastNote().msg:find('Auto backup failed', 1, true) and c.lastNote().kind == 'error', 'the first automatic failure is shown')
local notes = #c.notes
c.fire('vMenu:backups:autoNow')
c.fire('vMenu:backups:uploadResult', false, 'You can back up your saves once a minute. Please wait and try again.')
check(#c.latent == 4 and #c.notes == notes, 'later automatic failures are quiet, and the upload is retried')
c.fire('vMenu:backups:uploadResult', true, 'Backed up 14 saves to the server.')
c.fire('vMenu:backups:backup')
c.fire('vMenu:backups:uploadResult', false, 'You can back up your saves once a minute. Please wait and try again.')
check(c.lastNote().msg:find('once a minute', 1, true) and c.lastNote().kind == 'error', 'a manual backup still reports its own result')
print('PASS: auto backup uploads changes only, quietly, one at a time, and retries')

-- The background thread: a reminder for players who never backed up, then auto backup every 10 minutes.
c = newClient(copy(SAVES))
c.kvps.vmenu_auto_backup = nil
check(c.step() == 120000, 'the first check waits 2 minutes')
check(c.step() == 600000 and c.lastNote().msg:find('only stored on this PC', 1, true) and c.kvps.vmenu_backup_reminded_at == c.clock,
    'a player who never backed up is reminded, then checks repeat every 10 minutes')
check(#c.latent == 0, 'nothing is uploaded with auto backup off')
c.kvps.vmenu_auto_backup = 1
c.step()
check(#c.latent == 1, 'with auto backup on, the next check uploads')

local function reminded(setup)
    local r = newClient(copy(SAVES))
    r.kvps.vmenu_auto_backup = nil
    setup(r)
    r.step(); r.step()
    return #r.notes > 0 and r.lastNote().msg or nil
end
check(reminded(function(r) r.kvps.vmenu_backup_reminded_at = r.clock - 3600 end) == nil, 'at most one reminder a week')
check(reminded(function(r) r.kvps.vmenu_auto_backup = 1; r.kvps.vmenu_backup_last_at = nil end) == nil, 'auto backup on: no reminder')
check(reminded(function(r) r.kvps.vmenu_backup_last_at = r.clock - 86400 end) == nil, 'no reminder after a recent backup')
check((reminded(function(r) r.kvps.vmenu_backup_last_at = r.clock - 31 * 86400 end) or ''):find('over 30 days old', 1, true),
    'a stale backup with changed saves gets a reminder')
local empty = newClient({})
empty.step(); empty.step()
check(#empty.notes == 0, 'no reminder without saves')
local off = newClient(copy(SAVES))
off.convar = 'false'
check(off.step() == nil, 'nothing runs while vmenu_backups is off')
print('PASS: weekly reminder, 10-minute auto backup, off without the convar')

-- Both sides must agree on what a save is.
local function lists(path)
    local text = readFile(path)
    return text:match('local BACKUP_PREFIXES = (%b{})'), text:match('local BACKUP_KEYS = (%b{})')
end
local cp, ck = lists('build/vMenu/client/backups.lua')
local sp, sk = lists('build/vMenu/server/backups.lua')
check(cp and ck and cp:gsub('%s+', '') == sp:gsub('%s+', '') and ck:gsub('%s+', '') == sk:gsub('%s+', ''), 'client and server allow the same keys')
print('PASS: client and server key lists match')
