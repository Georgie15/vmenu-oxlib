-- vMenu request guard and security log (server).
--
-- Every request a client can send to vMenu's Lua handlers goes through here:
-- VMenuGuard.netEvent and VMenuGuard.callback stand in for RegisterNetEvent and
-- lib.callback.register. vMenuServer/RequestGuard.cs does the same for the C# handlers, reads the
-- same convars, and sends its log entries here (local event vMenu:security:report), so there is one
-- security log. fxmanifest.lua loads this file before server/*.lua.
--
-- Each request, in order: a real player sent it (a server id above 0); the player is within this
-- event's rate limit (a token bucket per player and event: vmenu_guard_burst at once, then
-- vmenu_guard_per_second a second); the player has the permission, when the handler declares one;
-- the arguments have the declared types. Handlers keep their own checks on top, and call
-- VMenuGuard.denied when one of their own permission checks refuses.
--
-- The security log has one line per denied, rate-limited, malformed, sourceless or spoofed request:
-- the event, the player's server id, name and license identifier (never the IP). The same line
-- again within 10 s is counted, then summarised. With vmenu_security_webhook set, the lines also go
-- to that Discord webhook in batches, at most one message every vmenu_security_webhook_interval
-- seconds; sending never holds up a request.

local Guard = {}
VMenuGuard = Guard

local SETTINGS_TTL_MS = 5000
local REPEAT_WINDOW_MS = 10000
local WEBHOOK_MAX_QUEUE = 100
local WEBHOOK_MAX_CHARS = 1800
local WEBHOOK_BACKOFF_MS = 30000
local WEBHOOK_WARNING_MS = 60000

-- Convars, read at most every 5 s rather than on every request.
local settings = { rateLimits = true, burst = 10, perSecond = 3 }
local settingsAt

local function trim(text)
    return (tostring(text or ''):match('^%s*(.-)%s*$'))
end

local function refreshSettings(now)
    if settingsAt and now - settingsAt < SETTINGS_TTL_MS then return end
    settingsAt = now
    settings.rateLimits = trim(GetConvar('vmenu_guard_rate_limits', 'true')):lower() ~= 'false'
    settings.burst = GetConvarInt('vmenu_guard_burst', 10)
    settings.perSecond = GetConvarInt('vmenu_guard_per_second', 3)
end

--- One token bucket per key: `burst` at once, then `perSecond` a second. Limits of 0 or less
--- mean no limit. Returns whether the request may run. Same rule as RequestRateLimiter (C#).
function Guard.take(buckets, key, now, burst, perSecond)
    if burst <= 0 or perSecond <= 0 then return true end
    local bucket = buckets[key]
    if not bucket then
        buckets[key] = { tokens = burst - 1, stamp = now }
        return true
    end
    local elapsed = now - bucket.stamp
    if elapsed < 0 then elapsed = 0 end
    bucket.tokens = math.min(burst, bucket.tokens + elapsed * perSecond / 1000)
    if now > bucket.stamp then bucket.stamp = now end
    if bucket.tokens >= 1 then
        bucket.tokens = bucket.tokens - 1
        return true
    end
    return false
end

-- [eventName][serverId] = bucket
local buckets = {}

---------------------------------------------------------------------------------------------------
-- Security log
---------------------------------------------------------------------------------------------------

-- A player name as one log field: no colour codes, control characters, backticks or quotes
-- (so it can't break the line or the webhook's code block), at most 64 bytes.
local function clean(text)
    text = tostring(text or ''):gsub('%^%d', ''):gsub('[%c`]', ''):gsub('"', "'")
    if #text > 64 then
        text = text:sub(1, 64):gsub('[\192-\255][\128-\191]*$', '')
    end
    return text
end

local function describePlayer(src)
    local id = tonumber(src)
    if not id or id <= 0 then return 'no player' end
    local name = GetPlayerName(id)
    local license = GetPlayerIdentifierByType(id, 'license')
    return ('%d "%s" %s'):format(id, name and clean(name) or '?', license and clean(license) or 'no-license')
end

local queue, dropped = {}, 0
local sendScheduled, lastSentAt, backoffUntil, lastWarningAt = false, nil, 0, nil

local function webhookUrl()
    local url = trim(GetConvar('vmenu_security_webhook', ''))
    return url ~= '' and url or nil
end

-- The webhook URL is a secret: it is never printed.
local function warnWebhook(message)
    local now = GetGameTimer()
    if lastWarningAt and now - lastWarningAt < WEBHOOK_WARNING_MS then return end
    lastWarningAt = now
    print(('^3[vMenu] [SECURITY]^7 security webhook: %s'):format(message))
end

local scheduleSend

local function sendBatch()
    sendScheduled = false
    local url = webhookUrl()
    if not url then
        queue, dropped = {}, 0
        return
    end
    if not url:find('^https://') then
        queue, dropped = {}, 0
        warnWebhook('vmenu_security_webhook must be an https:// URL; nothing was sent')
        return
    end

    local lines, size = {}, 0
    while queue[1] and (#lines == 0 or size + #queue[1] + 1 <= WEBHOOK_MAX_CHARS) do
        local line = table.remove(queue, 1)
        lines[#lines + 1] = line
        size = size + #line + 1
    end
    if not queue[1] and dropped > 0 then
        lines[#lines + 1] = ('... and %d more (too many to send)'):format(dropped)
        dropped = 0
    end
    if #lines == 0 then return end

    lastSentAt = GetGameTimer()
    PerformHttpRequest(url, function(status)
        if status == 429 then
            backoffUntil = GetGameTimer() + WEBHOOK_BACKOFF_MS
            warnWebhook('Discord is rate limiting it; waiting 30 s')
        elseif type(status) ~= 'number' or status < 200 or status >= 300 then
            warnWebhook(('the request failed (HTTP %s)'):format(tostring(status)))
        end
    end, 'POST', json.encode({
        username = 'vMenu security',
        content = '```\n' .. table.concat(lines, '\n') .. '\n```',
        allowed_mentions = { parse = {} },
    }), { ['Content-Type'] = 'application/json' })

    if queue[1] then scheduleSend() end
end

scheduleSend = function()
    if sendScheduled then return end
    sendScheduled = true
    local now = GetGameTimer()
    local interval = math.max(2, GetConvarInt('vmenu_security_webhook_interval', 5)) * 1000
    local at = lastSentAt and lastSentAt + interval or now
    if backoffUntil > at then at = backoffUntil end
    SetTimeout(math.max(0, at - now), sendBatch)
end

local function emit(line)
    print('^3[vMenu] [SECURITY]^7 ' .. line)
    if not webhookUrl() then return end
    if #line > WEBHOOK_MAX_CHARS then line = line:sub(1, WEBHOOK_MAX_CHARS) end
    if #queue >= WEBHOOK_MAX_QUEUE then
        dropped = dropped + 1
    else
        queue[#queue + 1] = line
    end
    scheduleSend()
end

-- The same kind, event and player again within REPEAT_WINDOW_MS only counts up; the count is
-- printed once the window ends.
local recent = {}
local flushScheduled = false

local function summarise(entry)
    if entry.repeats > 0 then
        emit(('%s %s from %s: %d more in the last %d s'):format(entry.kind, entry.eventName, entry.who,
            entry.repeats, REPEAT_WINDOW_MS // 1000))
    end
end

local function flush()
    flushScheduled = false
    local now = GetGameTimer()
    local nextDue
    for key, entry in pairs(recent) do
        local due = entry.at + REPEAT_WINDOW_MS
        if now >= due then
            recent[key] = nil
            summarise(entry)
        elseif not nextDue or due < nextDue then
            nextDue = due
        end
    end
    if nextDue then
        flushScheduled = true
        SetTimeout(nextDue - now, flush)
    end
end

--- Logs one security entry. `count` (default 1) is how many requests it stands for.
function Guard.report(kind, eventName, src, detail, count)
    count = math.max(1, math.floor(tonumber(count) or 1))
    local now = GetGameTimer()
    local id = tonumber(src)
    local key = kind .. '|' .. eventName .. '|' .. tostring(id or '-')
    local entry = recent[key]
    if entry and now - entry.at < REPEAT_WINDOW_MS then
        entry.repeats = entry.repeats + count
        return
    end
    if entry then summarise(entry) end

    local who = describePlayer(id)
    local line = ('%s %s from %s: %s'):format(kind, eventName, who, detail or '')
    if count > 1 then line = line .. (' (%d requests)'):format(count) end
    emit(line)
    recent[key] = { at = now, repeats = 0, kind = kind, eventName = eventName, who = who }
    if not flushScheduled then
        flushScheduled = true
        SetTimeout(REPEAT_WINDOW_MS, flush)
    end
end

--- For a handler's own permission check: logs the refusal.
function Guard.denied(eventName, src, permission)
    Guard.report('denied', eventName, src, 'missing ' .. tostring(permission))
end

---------------------------------------------------------------------------------------------------
-- Argument checks: { name = shown in the log, check = function(value) -> boolean }
---------------------------------------------------------------------------------------------------

function Guard.string(maxLength)
    return {
        name = maxLength and ('text of up to %d characters'):format(maxLength) or 'text',
        check = function(value) return type(value) == 'string' and (not maxLength or #value <= maxLength) end,
    }
end

function Guard.pattern(pattern, maxLength)
    return {
        name = ('text matching %s'):format(pattern),
        check = function(value) return type(value) == 'string' and #value <= maxLength and value:find(pattern) ~= nil end,
    }
end

Guard.number = { name = 'a number', check = function(value) return type(value) == 'number' and value == value end }
Guard.boolean = { name = 'true or false', check = function(value) return type(value) == 'boolean' end }
Guard.table = { name = 'a table', check = function(value) return type(value) == 'table' end }

function Guard.optional(inner)
    return { name = inner.name .. ' or nothing', check = function(value) return value == nil or inner.check(value) end }
end

---------------------------------------------------------------------------------------------------
-- Registration
---------------------------------------------------------------------------------------------------

-- Returns true, or false and why: 'no-source', 'rate-limited', 'denied' or 'malformed'.
local function admit(eventName, src, opts, ...)
    local id = tonumber(src)
    if not id or id <= 0 then
        Guard.report('no-source', eventName, nil, 'no player sent this request')
        return false, 'no-source'
    end

    local now = GetGameTimer()
    refreshSettings(now)
    if settings.rateLimits and not Guard.take(buckets[eventName], id, now, settings.burst, settings.perSecond) then
        Guard.report('rate-limited', eventName, id,
            ('limit %d at once, %d a second'):format(settings.burst, settings.perSecond))
        return false, 'rate-limited'
    end

    if opts.permission and not opts.permission(id) then
        Guard.denied(eventName, id, opts.permissionName or 'permission')
        return false, 'denied'
    end

    local args = opts.args
    if args then
        for i = 1, #args do
            if not args[i].check((select(i, ...))) then
                Guard.report('malformed', eventName, id, ('argument %d is not %s'):format(i, args[i].name))
                return false, 'malformed'
            end
        end
    end
    return true
end

--- RegisterNetEvent through the guard. opts: args (argument checks), permission (function(src) ->
--- boolean) with permissionName, reject (function(src, why, ...), called for a refused player's request
--- with the request's arguments, so it can answer by request id).
--- The handler gets the event's arguments, with `source` set as usual.
function Guard.netEvent(eventName, opts, handler)
    buckets[eventName] = buckets[eventName] or {}
    RegisterNetEvent(eventName, function(...)
        local src = source
        local ok, why = admit(eventName, src, opts, ...)
        if ok then return handler(...) end
        if opts.reject and why ~= 'no-source' then opts.reject(src, why, ...) end
    end)
end

--- lib.callback.register through the guard. Same opts; a refused request gets what
--- opts.reject(src, why) returns, or false.
function Guard.callback(eventName, opts, handler)
    buckets[eventName] = buckets[eventName] or {}
    lib.callback.register(eventName, function(src, ...)
        local ok, why = admit(eventName, src, opts, ...)
        if ok then return handler(src, ...) end
        if opts.reject then return opts.reject(src, why) end
        return false
    end)
end

AddEventHandler('playerDropped', function()
    local id = tonumber(source)
    if not id then return end
    for _, perEvent in pairs(buckets) do perEvent[id] = nil end
end)

-- Entries from the C# guard. Registered with AddEventHandler only, so a client can't send it.
AddEventHandler('vMenu:security:report', function(kind, eventName, serverId, detail, count)
    if type(source) == 'number' then return end
    Guard.report(tostring(kind), tostring(eventName), tonumber(serverId), detail and tostring(detail) or '', count)
end)
