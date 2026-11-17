-- Department Outfits (MP Ped Customization > Department Outfits): uniforms a department's heads
-- publish for its members. Members on duty with that department list and wear them; managers also
-- publish, replace, rename and delete them. Everything is decided here, on every request; the
-- client's claims about its org are never used.
--
--   On duty:   exports.PoliceEMSActivity:GetDutyInfo(src) / GetDutyOrg(src). Without
--              PoliceEMSActivity nobody counts as on duty; managers still manage.
--   Managers:  ace vMenu.DeptOutfits.Manage.<org> (the org id as PoliceEMSActivity's Config.Orgs
--              writes it, e.g. vMenu.DeptOutfits.Manage.lspd), or vMenu.DeptOutfits.ManageAll, or
--              vMenu.DeptOutfits.ManageOwn: the orgs whose member ace (DepartmentOutfitOrgs' member,
--              the org's duty permission in PoliceEMSActivity) the player also has. PSRP grants
--              ManageOwn to group.depthead, so a department head manages their own department.
--   Wearing:   the org you're on duty with, and any org you manage.
--   Storage:   the vmenu_dept_outfits table (database.sql); needs oxmysql.
--   Enabled:   setr vmenu_dept_outfits true
--
-- A preset's payload is the client's Saved Outfits JSON for one freemode model (clothing saved by
-- collection with the pack revision and prints, docs/clothing-refs.md), stored exactly as checked.
-- The client sends { requestId, action, args JSON } on vMenu:DeptOutfits:Request and gets
-- { requestId, reply JSON } back on vMenu:DeptOutfits:Reply.

local MAX_ARGS_BYTES = 12288
local MAX_PAYLOAD_BYTES = 8192
local MAX_NAME_LENGTH = 40
local MAX_PER_ORG_AND_MODEL = 60
local MAX_LISTED = 2000 -- names only (no outfit data): every org's presets for one body fit
-- Ace checks cost about 0.3-0.5 ms each on the main thread and an access check makes one or more per
-- org, so who manages what is kept per player for 30 seconds. Duty is always read fresh.
local MANAGE_CACHE_MS = 30000
local LATENT_REPLY_BYTES = 8000
local LATENT_BPS = 64000

-- Per license. Listing is cheap (one indexed query) and happens each time the menu opens.
local COOLDOWN_MS = { list = 300, wear = 1000, publish = 10000, replace = 5000, rename = 3000, delete = 3000 }

local MANAGE_ALL_ACE = 'vMenu.DeptOutfits.ManageAll'
local MANAGE_OWN_ACE = 'vMenu.DeptOutfits.ManageOwn'
local MANAGE_ACE_PREFIX = 'vMenu.DeptOutfits.Manage.'
local DUTY_RESOURCE = 'PoliceEMSActivity'

-- mp_m_freemode_01 and mp_f_freemode_01: presets are only for the two MP bodies.
local MODELS = { [1885233650] = true, [2627665880] = true }

local function enabled()
    return GetConvarBool('vmenu_dept_outfits', false)
end

local function isWhole(value, min, max)
    return math.type(value) ~= nil and value == math.floor(value) and value >= min and value <= max
end

local function isText(value, max, pattern)
    return type(value) == 'string' and #value <= max and value:match(pattern) ~= nil
end

local function isOrgId(value)
    return isText(value, 32, '^[%w_%-]+$')
end

--- Org ids and labels managers can publish for, in menu order: config/config_server.lua's
--- DepartmentOutfitOrgs ({ { id = 'lspd', label = '...' }, ... }).
local function configuredOrgs()
    local list, labels, memberAces = {}, {}, {}
    local orgs = type(Config) == 'table' and Config.DepartmentOutfitOrgs or nil
    if type(orgs) == 'table' then
        for _, org in ipairs(orgs) do
            if type(org) == 'table' and isOrgId(org.id) and not labels[org.id] then
                labels[org.id] = type(org.label) == 'string' and org.label ~= '' and org.label or org.id
                list[#list + 1] = org.id
                if type(org.member) == 'string' and org.member ~= '' then
                    memberAces[org.id] = org.member
                end
            end
        end
    end
    return list, labels, memberAces
end

local function dutyAvailable()
    return GetResourceState(DUTY_RESOURCE) == 'started'
end

--- The org the player is on duty with and its label, from PoliceEMSActivity. Nil when off duty or
--- when PoliceEMSActivity isn't running.
local function dutyOf(src)
    if not dutyAvailable() then return nil end
    local ok, info = pcall(function() return exports[DUTY_RESOURCE]:GetDutyInfo(src) end)
    if ok and type(info) == 'table' and isOrgId(info.org) then
        return info.org, type(info.orgLabel) == 'string' and info.orgLabel ~= '' and info.orgLabel or nil
    end
    local okOrg, org = pcall(function() return exports[DUTY_RESOURCE]:GetDutyOrg(src) end)
    if okOrg and isOrgId(org) then return org, nil end
    return nil
end

local function managesAll(src)
    return IsPlayerAceAllowed(src, MANAGE_ALL_ACE)
end

--- Who this player manages, read once per MANAGE_CACHE_MS: ManageAll, ManageOwn, and the
--- configured orgs they manage (Manage.<org>, or ManageOwn plus the org's duty permission).
local manageCache = {}
local function managerOf(src, list, memberAces)
    local now = GetGameTimer()
    local cached = manageCache[src]
    if cached and now - cached.at < MANAGE_CACHE_MS then return cached end
    local all = managesAll(src)
    local own = not all and IsPlayerAceAllowed(src, MANAGE_OWN_ACE)
    local set = {}
    for _, id in ipairs(list) do
        -- A department head (ManageOwn) manages an org they belong to: its duty permission.
        local member = memberAces[id]
        if all or IsPlayerAceAllowed(src, MANAGE_ACE_PREFIX .. id) or (own and member ~= nil and IsPlayerAceAllowed(src, member)) then
            set[id] = true
        end
    end
    cached = { at = now, all = all, set = set, extra = {} }
    manageCache[src] = cached
    return cached
end

AddEventHandler('playerDropped', function()
    local src = tonumber(source)
    if src then manageCache[src] = nil end
end)

--- Whether the player manages an org: the cached configured set, or (for an org the config doesn't
--- list yet) ManageAll or Manage.<org>, remembered with the cache.
local function manages(src, org, manager)
    if not isOrgId(org) then return false end
    if manager.set[org] then return true end
    local extra = manager.extra[org]
    if extra == nil then
        extra = manager.all or IsPlayerAceAllowed(src, MANAGE_ACE_PREFIX .. org)
        manager.extra[org] = extra
    end
    return extra
end

--- Everything this request may use about the player: duty, managed orgs (configured orgs and the
--- duty org) and labels.
local function access(src)
    local list, labels, memberAces = configuredOrgs()
    local duty, dutyLabel = dutyOf(src)
    local manager = managerOf(src, list, memberAces)
    local a = { duty = duty, labels = labels, manager = manager, manage = {}, manageSet = {} }
    if duty then
        a.labels[duty] = a.labels[duty] or dutyLabel or duty
        local known = false
        for _, id in ipairs(list) do if id == duty then known = true end end
        if not known then list[#list + 1] = duty end
    end
    for _, id in ipairs(list) do
        if manages(src, id, manager) then
            a.manage[#a.manage + 1] = id
            a.manageSet[id] = true
        end
    end
    return a
end

local function labelOf(a, org)
    return a.labels[org] or org
end

local function canManage(src, a, org)
    return a.manageSet[org] == true or manages(src, org, a.manager)
end

local function canWear(src, a, org)
    return a.duty == org or canManage(src, a, org)
end

local function licenseOf(src)
    local license = GetPlayerIdentifierByType(src, 'license')
    return license and license ~= '' and license or ('source:' .. tostring(src))
end

local function playerName(src)
    local name = GetPlayerName(src)
    if type(name) ~= 'string' then return '' end
    -- Cut at 64 bytes without leaving half a UTF-8 character at the end.
    return (name:sub(1, 64):gsub('[\194-\244][\128-\191]*$', function(tail)
        local lead = tail:byte(1)
        local need = lead >= 240 and 4 or lead >= 224 and 3 or 2
        return #tail == need and tail or ''
    end))
end

-- Limits are per Rockstar license and per kind of request, so reconnecting doesn't reset them.
local lastAt = {}
local function onCooldown(kind, license)
    local times = lastAt[kind]
    local last = times and times[license]
    return last ~= nil and GetGameTimer() - last < COOLDOWN_MS[kind]
end

local function startCooldown(kind, license)
    lastAt[kind] = lastAt[kind] or {}
    lastAt[kind][license] = GetGameTimer()
end

--- Runs an oxmysql export. Returns false if the query failed (oxmysql raises on errors).
local function db(method, query, params)
    local ok, result = pcall(function() return exports.oxmysql[method](exports.oxmysql, query, params) end)
    if not ok then
        lib.print.error(('vmenu_dept_outfits query failed: %s'):format(result))
        return false
    end
    return true, result
end

local DB_ERROR = { ok = false, message = 'The server could not reach the outfit database. Please try again later.' }

--- A preset name: trimmed, single spaces, 1 to 40 of letters, numbers, spaces and - _ ' . ( ) & / # + ,
local function cleanName(name)
    if type(name) ~= 'string' then return nil, 'Enter a name.' end
    name = name:gsub('%s+', ' '):gsub('^ ', ''):gsub(' $', '')
    if name == '' then return nil, 'Enter a name.' end
    if #name > MAX_NAME_LENGTH then return nil, ('Names can be up to %d characters.'):format(MAX_NAME_LENGTH) end
    if not name:match("^[%w %-_'%.%(%)&/#+,]+$") then
        return nil, "Names can use letters, numbers, spaces and - _ ' . ( ) & / # + ,"
    end
    return name
end

-- ---- Payload: one Saved Outfits JSON --------------------------------------------------------
--   { model = 1885233650, clothes = { ["11"] = { 40, 2 }, ... }, props = { ["0"] = { -1, -1 } },
--     refs = { v = 2, model = 1885233650, clothes = { ["11"] = { col = "...", drw = 40, tex = 2, rev = "...", pp = "...", lp = "..." } },
--              props = { ["0"] = { empty = true } } },
--     layout = "<layout print>" }
-- Pieces still waiting for review (legacy numbers) are refused: a department never shares a guess.

local function validPairs(map, maxSlot)
    if type(map) ~= 'table' then return false end
    local count = 0
    for key, pair in pairs(map) do
        count = count + 1
        if count > 16 or not isWhole(tonumber(key), 0, maxSlot) or type(pair) ~= 'table' or #pair ~= 2
                or not isWhole(pair[1], -1, 4096) or not isWhole(pair[2], -1, 255) then
            return false
        end
        for k in pairs(pair) do
            if k ~= 1 and k ~= 2 then return false end
        end
    end
    return true
end

local REF_FIELDS = { col = true, drw = true, tex = true, empty = true, rev = true, pp = true, lp = true }
local function validRef(item)
    if type(item) ~= 'table' then return false end
    for key in pairs(item) do
        if not REF_FIELDS[key] then return false end
    end
    if item.empty ~= nil then
        return item.empty == true and item.col == nil and item.drw == nil and item.tex == nil
    end
    return isText(item.col, 64, '^[%w_%-%.]*$') and isWhole(item.drw, 0, 4096) and isWhole(item.tex, 0, 255)
        and (item.rev == nil or isText(item.rev, 64, '^[%w_%-%.]+$'))
        and (item.pp == nil or isText(item.pp, 32, '^%x+$'))
        and (item.lp == nil or isText(item.lp, 32, '^%x+$'))
end

local function validRefSlots(slots, maxSlot)
    if type(slots) ~= 'table' then return false end
    local count = 0
    for key, item in pairs(slots) do
        count = count + 1
        if count > 16 or not isWhole(tonumber(key), 0, maxSlot) or not validRef(item) then
            return false
        end
    end
    return true
end

local PAYLOAD_FIELDS = { model = true, clothes = true, props = true, refs = true, layout = true }
local REFS_FIELDS = { v = true, model = true, clothes = true, props = true }

--- Checks a payload. Returns its model, or nil and a reason.
local function checkPayload(payload)
    if type(payload) ~= 'string' or payload == '' then return nil, 'The outfit was empty.' end
    if #payload > MAX_PAYLOAD_BYTES then return nil, 'This outfit is too large to publish.' end
    local ok, data = pcall(json.decode, payload)
    if not ok or type(data) ~= 'table' then return nil, 'The outfit could not be read.' end
    for key in pairs(data) do
        if not PAYLOAD_FIELDS[key] then return nil, 'The outfit could not be read.' end
    end
    local model = data.model
    if not isWhole(model, 1, 4294967295) or not MODELS[model] then
        return nil, 'Department outfits are only for MP characters.'
    end
    local refs = data.refs
    if type(refs) ~= 'table' or refs.v ~= 2 or refs.model ~= model then
        return nil, 'This outfit has pieces that are not matched to a clothing collection.'
    end
    for key in pairs(refs) do
        if not REFS_FIELDS[key] then return nil, 'The outfit could not be read.' end
    end
    if not validPairs(data.clothes, 11) or not validPairs(data.props, 12)
            or not validRefSlots(refs.clothes, 11) or not validRefSlots(refs.props, 12) then
        return nil, 'This outfit has pieces that are not matched to a clothing collection.'
    end
    if next(data.clothes) == nil then return nil, 'The outfit was empty.' end
    -- Every piece is identified by collection, so a pack change can't turn it into another garment.
    for _, section in ipairs({ 'clothes', 'props' }) do
        for key in pairs(data[section]) do
            if refs[section][key] == nil then
                return nil, 'This outfit has pieces that are not matched to a clothing collection.'
            end
        end
    end
    if data.layout ~= nil and not isText(data.layout, 32, '^%x+$') then return nil, 'The outfit could not be read.' end
    return math.tointeger(model)
end

local function readId(value)
    local id = tonumber(value)
    return isWhole(id, 1, 4294967295) and math.tointeger(id) or nil
end

local function readModel(value)
    local model = tonumber(value)
    return model ~= nil and isWhole(model, 1, 4294967295) and MODELS[model] and math.tointeger(model) or nil
end

local function findPreset(id)
    return db('single_async', 'SELECT `id`, `org`, `name`, `model`, `payload` FROM `vmenu_dept_outfits` WHERE `id` = ?', { id })
end

local function nameTaken(org, model, name, exceptId)
    local ok, row = db('single_async',
        'SELECT `id` FROM `vmenu_dept_outfits` WHERE `org` = ? AND `model` = ? AND `name` = ? AND `id` <> ? LIMIT 1',
        { org, model, name, exceptId or 0 })
    if not ok then return nil end
    return row ~= nil
end

local function audit(src, what)
    lib.print.info(('[dept outfits] %s (%s) %s'):format(playerName(src), licenseOf(src), what))
end

-- ---- Requests --------------------------------------------------------------------------------

local actions = {}

--- The duty org, the orgs you manage, and the presets of both for one body.
function actions.list(src, args)
    local model = readModel(args.model)
    if not model then return { ok = false, message = 'Department outfits are only for MP characters.' } end
    local a = access(src)
    local reply = {
        ok = true,
        dutyAvailable = dutyAvailable(),
        duty = a.duty and { id = a.duty, label = labelOf(a, a.duty) } or nil,
        manage = {},
        outfits = {},
    }
    local orgs, seen = {}, {}
    if a.duty then orgs[1] = a.duty; seen[a.duty] = true end
    for _, id in ipairs(a.manage) do
        reply.manage[#reply.manage + 1] = { id = id, label = labelOf(a, id) }
        if not seen[id] then orgs[#orgs + 1] = id; seen[id] = true end
    end
    if #orgs == 0 then return reply end

    local placeholders = ('?, '):rep(#orgs):sub(1, -3)
    local params = { model }
    for _, id in ipairs(orgs) do params[#params + 1] = id end
    local ok, rows = db('query_async',
        "SELECT `id`, `org`, `name`, `updated_by_name`, DATE_FORMAT(`updated_at`, '%Y-%m-%d') AS `updated` FROM `vmenu_dept_outfits` " ..
        'WHERE `model` = ? AND `org` IN (' .. placeholders .. ') ORDER BY `org`, `name` LIMIT ' .. MAX_LISTED, params)
    if not ok then return DB_ERROR end
    for _, row in ipairs(rows or {}) do
        reply.outfits[#reply.outfits + 1] = {
            id = row.id, org = row.org, name = row.name, updated = row.updated,
            by = row.updated_by_name ~= '' and row.updated_by_name or nil,
            manage = canManage(src, a, row.org),
        }
    end
    return reply
end

--- One preset's payload, if you're on duty with its org or manage it.
function actions.wear(src, args)
    local id = readId(args.id)
    if not id then return { ok = false, message = 'That outfit does not exist.' } end
    local ok, row = findPreset(id)
    if not ok then return DB_ERROR end
    if not row then return { ok = false, message = 'That outfit was deleted. Reopen the menu.' } end
    local a = access(src)
    if not canWear(src, a, row.org) then
        return { ok = false, message = ('Go on duty with %s to wear its outfits.'):format(labelOf(a, row.org)) }
    end
    return { ok = true, payload = row.payload, name = row.name, model = row.model }
end

function actions.publish(src, args)
    local org = args.org
    if not isOrgId(org) then
        return { ok = false, message = "You can't publish outfits for that department." }
    end
    -- The configured spelling (CertCivt2, not certcivt2): the table ignores case, duty doesn't.
    for _, id in ipairs((configuredOrgs())) do
        if id:lower() == org:lower() then org = id break end
    end
    local a = access(src)
    if not canManage(src, a, org) then
        return { ok = false, message = "You can't publish outfits for that department." }
    end
    local name, nameError = cleanName(args.name)
    if not name then return { ok = false, message = nameError } end
    local model, payloadError = checkPayload(args.payload)
    if not model then return { ok = false, message = payloadError } end
    local license = licenseOf(src)
    if onCooldown('publish', license) then
        return { ok = false, message = 'You can publish an outfit every 10 seconds. Please wait and try again.' }
    end
    -- Taken before the database calls below, which wait: requests sent together can't all pass the
    -- check and the per-body cap. A refused publish gives it back.
    startCooldown('publish', license)
    local function refuse(reply)
        lastAt.publish[license] = nil
        return reply
    end

    local okCount, count = db('scalar_async', 'SELECT COUNT(*) FROM `vmenu_dept_outfits` WHERE `org` = ? AND `model` = ?', { org, model })
    if not okCount then return refuse(DB_ERROR) end
    if (tonumber(count) or 0) >= MAX_PER_ORG_AND_MODEL then
        return refuse({ ok = false, message = ('A department can have up to %d outfits per body type. Delete one first.'):format(MAX_PER_ORG_AND_MODEL) })
    end
    local taken = nameTaken(org, model, name)
    if taken == nil then return refuse(DB_ERROR) end
    if taken then return refuse({ ok = false, message = ('There is already an outfit called %s. Choose Replace on it instead.'):format(name) }) end

    local who = playerName(src)
    local okInsert, id = db('insert_async',
        'INSERT INTO `vmenu_dept_outfits` (`org`, `name`, `model`, `payload`, `created_by`, `created_by_name`, `updated_by_name`) VALUES (?, ?, ?, ?, ?, ?, ?)',
        { org, name, model, args.payload, license, who, who })
    if not okInsert or not id then return refuse(DB_ERROR) end
    audit(src, ('published %s outfit #%s "%s"'):format(org, tostring(id), name))
    return { ok = true, id = id, message = ('%s published for %s.'):format(name, labelOf(a, org)) }
end

--- The preset an edit is about, if you manage its org. Returns the row and access, or nil and a reply.
local function editable(src, args, kind)
    local id = readId(args.id)
    if not id then return nil, { ok = false, message = 'That outfit does not exist.' } end
    local ok, row = findPreset(id)
    if not ok then return nil, DB_ERROR end
    if not row then return nil, { ok = false, message = 'That outfit was already deleted. Reopen the menu.' } end
    local a = access(src)
    if not canManage(src, a, row.org) then
        return nil, { ok = false, message = ("You can't change %s outfits."):format(labelOf(a, row.org)) }
    end
    if onCooldown(kind, licenseOf(src)) then
        return nil, { ok = false, message = 'Please wait a moment and try again.' }
    end
    return row, a
end

function actions.replace(src, args)
    local model, payloadError = checkPayload(args.payload)
    if not model then return { ok = false, message = payloadError } end
    local row, a = editable(src, args, 'replace')
    if not row then return a end
    if math.tointeger(tonumber(row.model)) ~= model then
        return { ok = false, message = 'Replace it while using the same body type it was made for.' }
    end
    startCooldown('replace', licenseOf(src))
    local ok = db('update_async',
        'UPDATE `vmenu_dept_outfits` SET `payload` = ?, `updated_by_name` = ?, `updated_at` = CURRENT_TIMESTAMP WHERE `id` = ?',
        { args.payload, playerName(src), row.id })
    if not ok then return DB_ERROR end
    audit(src, ('replaced %s outfit #%s "%s"'):format(row.org, tostring(row.id), row.name))
    return { ok = true, message = ('%s replaced with your outfit.'):format(row.name) }
end

function actions.rename(src, args)
    local name, nameError = cleanName(args.name)
    if not name then return { ok = false, message = nameError } end
    local row, a = editable(src, args, 'rename')
    if not row then return a end
    local taken = nameTaken(row.org, row.model, name, row.id)
    if taken == nil then return DB_ERROR end
    if taken then return { ok = false, message = ('There is already an outfit called %s.'):format(name) } end
    startCooldown('rename', licenseOf(src))
    local ok = db('update_async',
        'UPDATE `vmenu_dept_outfits` SET `name` = ?, `updated_by_name` = ?, `updated_at` = CURRENT_TIMESTAMP WHERE `id` = ?',
        { name, playerName(src), row.id })
    if not ok then return DB_ERROR end
    audit(src, ('renamed %s outfit #%s "%s" to "%s"'):format(row.org, tostring(row.id), row.name, name))
    return { ok = true, message = ('%s renamed to %s.'):format(row.name, name) }
end

function actions.delete(src, args)
    local row, a = editable(src, args, 'delete')
    if not row then return a end
    startCooldown('delete', licenseOf(src))
    local ok = db('update_async', 'DELETE FROM `vmenu_dept_outfits` WHERE `id` = ?', { row.id })
    if not ok then return DB_ERROR end
    audit(src, ('deleted %s outfit #%s "%s"'):format(row.org, tostring(row.id), row.name))
    return { ok = true, message = ('%s deleted.'):format(row.name) }
end

local function send(src, requestId, reply)
    local encoded = json.encode(reply)
    if #encoded > LATENT_REPLY_BYTES then
        TriggerLatentClientEvent('vMenu:DeptOutfits:Reply', src, LATENT_BPS, requestId, encoded)
    else
        TriggerClientEvent('vMenu:DeptOutfits:Reply', src, requestId, encoded)
    end
end

-- Through the request guard (server/guard/requestGuard.lua) like vMenu's other client requests:
-- a real player, the per-player rate limit, the argument types, and the security log. A refused
-- request is answered by its id, so the menu doesn't wait for its timeout.
VMenuGuard.netEvent('vMenu:DeptOutfits:Request', {
    args = { VMenuGuard.number, VMenuGuard.string(32), VMenuGuard.string(MAX_ARGS_BYTES) },
    reject = function(src, why, requestId, action, argsJson)
        if not isWhole(requestId, 0, 2147483647) then return end
        local message = 'That request could not be read.'
        if why == 'rate-limited' then
            message = 'Please wait a moment and try again.'
        elseif type(argsJson) == 'string' and #argsJson > MAX_ARGS_BYTES then
            message = 'That request was too large.'
        end
        send(src, requestId, { ok = false, message = message })
    end,
}, function(requestId, action, argsJson)
    local src = source
    if not isWhole(requestId, 0, 2147483647) then return end
    if not enabled() then
        return send(src, requestId, { ok = false, message = 'Department outfits are disabled on this server.' })
    end
    local handler = type(action) == 'string' and actions[action] or nil
    if not handler then
        return send(src, requestId, { ok = false, message = 'Unknown request.' })
    end
    if type(argsJson) ~= 'string' or #argsJson > MAX_ARGS_BYTES then
        return send(src, requestId, { ok = false, message = 'That request was too large.' })
    end
    local decoded, args = pcall(json.decode, argsJson)
    if not decoded or type(args) ~= 'table' then
        return send(src, requestId, { ok = false, message = 'That request could not be read.' })
    end
    local kind = action == 'list' and 'list' or action == 'wear' and 'wear' or nil
    if kind then
        -- Cheap reads, limited before any database work.
        local license = licenseOf(src)
        if onCooldown(kind, license) then
            return send(src, requestId, { ok = false, message = 'Please wait a moment and try again.' })
        end
        startCooldown(kind, license)
    end
    local ok, reply = pcall(handler, src, args)
    if not ok then
        lib.print.error(('Department outfits request %s failed: %s'):format(action, reply))
        reply = { ok = false, message = 'Something went wrong. Please try again.' }
    end
    send(src, requestId, reply)
end)

CreateThread(function()
    if not enabled() then return end
    if GetResourceState('oxmysql') ~= 'started' then
        lib.print.error('Department outfits are enabled (vmenu_dept_outfits) but oxmysql is not started.')
        return
    end
    if not db('query_async', 'SELECT 1 FROM `vmenu_dept_outfits` LIMIT 1') then
        lib.print.error('Department outfits are enabled but the vmenu_dept_outfits table is missing. Run database.sql.')
        return
    end
    if not dutyAvailable() then
        lib.print.warn('Department outfits: PoliceEMSActivity is not started, so nobody counts as on duty. Managers can still manage.')
    end
    lib.print.info('Department outfits are enabled.')
end)
