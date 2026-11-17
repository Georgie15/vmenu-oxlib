-- Run from the repo root: lua tests/request-guard/test_request_guard.lua
-- Runs the production server/guard/requestGuard.lua (VMenuGuard) against a fake FiveM server: who
-- gets through (a real player, within the rate limit, with the permission, with well-formed
-- arguments), what the security log prints (event, server id, name, license; never the IP), how
-- repeats are summarised, the C# guard's entries, and the Discord webhook's batching, rate limit and
-- secrecy. The C# guard and the wiring of every handler: dotnet run --project tests/request-guard

local function check(condition, message)
    if not condition then error('FAIL: ' .. message, 2) end
end

-- The fake server captures print; results go to stdout directly.
local function pass(message) io.write('PASS: ', message, '\n') end

local json = dofile('tests/lua/json.lua')
_G.json = json

local function readFile(path)
    local f = assert(io.open(path, 'rb'))
    local text = f:read('a')
    f:close()
    return text
end

local function newServer()
    local s = { now = 1000000, convars = {}, net = {}, callbacks = {}, handlers = {}, printed = {}, http = {}, timers = {},
        names = { [1] = 'Alice', [2] = 'Bob' }, licenses = { [1] = 'license:aaa111', [2] = 'license:bbb222' }, endpoints = { [1] = '203.0.113.7' } }
    _G.GetGameTimer = function() return s.now end
    _G.GetConvar = function(name, default) local v = s.convars[name]; if v == nil then return default end return tostring(v) end
    _G.GetConvarInt = function(name, default) local v = s.convars[name]; if v == nil then return default end return math.tointeger(tonumber(v)) or default end
    _G.GetPlayerName = function(id) return s.names[tonumber(id)] end
    _G.GetPlayerIdentifierByType = function(id, kind) assert(kind == 'license', 'only the license is read'); return s.licenses[tonumber(id)] end
    _G.GetPlayerEndpoint = function(id) error('the IP must never be read') end
    _G.GetPlayerIdentifiers = function() error('only the license identifier is read') end
    _G.RegisterNetEvent = function(name, fn) s.net[name] = fn end
    _G.AddEventHandler = function(name, fn) s.handlers[name] = fn end
    _G.lib = { callback = { register = function(name, fn) s.callbacks[name] = fn end } }
    _G.SetTimeout = function(ms, fn) s.timers[#s.timers + 1] = { at = s.now + ms, fn = fn } end
    _G.PerformHttpRequest = function(url, cb, method, body, headers)
        s.http[#s.http + 1] = { url = url, cb = cb, method = method, body = json.decode(body), headers = headers }
    end
    _G.print = function(line) s.printed[#s.printed + 1] = line end
    assert(load(readFile('build/vMenu/server/guard/requestGuard.lua'), '@server/guard/requestGuard.lua'))()
    s.guard = VMenuGuard

    -- A client sends a net event (source = their server id; '' for TriggerEvent on the server).
    function s.send(src, name, ...) _G.source = src; local r = { s.net[name](...) }; _G.source = nil; return table.unpack(r) end
    function s.call(src, name, ...) return s.callbacks[name](src, ...) end
    -- Advance the clock, running timers that come due, in order.
    function s.advance(ms)
        local untilAt = s.now + ms
        while true do
            table.sort(s.timers, function(a, b) return a.at < b.at end)
            local t = s.timers[1]
            if not t or t.at > untilAt then break end
            table.remove(s.timers, 1)
            s.now = math.max(s.now, t.at)
            t.fn()
        end
        s.now = untilAt
    end
    function s.lines() local all = s.printed; s.printed = {}; return all end
    return s
end

-- Whether each request ran, with the guard's defaults (10 at once, then 3 a second).
local s = newServer()
local ran = {}
s.guard.netEvent('test:event', {}, function(a, b) ran[#ran + 1] = { source, a, b } end)
s.send(1, 'test:event', 'x', 5)
check(#ran == 1 and ran[1][1] == 1 and ran[1][2] == 'x' and ran[1][3] == 5, 'a player\'s request runs with its arguments and source')
check(#s.lines() == 0, 'nothing logged for a normal request')

-- The shared scenario (tests/request-guard/Program.cs runs the same through the C# limiter).
local b, allowed = {}, {}
local function take(now) allowed[#allowed + 1] = s.guard.take(b, 1, now, 10, 3) end
for _ = 1, 15 do take(0) end
take(500); take(600); take(1700)
for _ = 1, 4 do take(1700) end
for _ = 1, 11 do take(600000) end
local expected = {}
for i = 1, 10 do expected[i] = true end
for i = 11, 15 do expected[i] = false end
local tail = { true, false, true, true, true, true, false, true, true, true, true, true, true, true, true, true, true, false }
for _, v in ipairs(tail) do expected[#expected + 1] = v end
check(#allowed == #expected, 'scenario length')
for i = 1, #expected do check(allowed[i] == expected[i], 'token bucket matches the C# limiter at step ' .. i) end
check(s.guard.take({}, 1, 0, 0, 3) and s.guard.take({}, 1, 0, 10, 0), 'limits of 0 mean no limit')
pass('token bucket (same scenario as the C# limiter)')

-- Rate limit through the wrapper: per player, per event, refills, forgotten on leaving.
s = newServer()
local count = 0
s.guard.netEvent('test:a', {}, function() count = count + 1 end)
s.guard.netEvent('test:b', {}, function() count = count + 100 end)
for _ = 1, 15 do s.send(1, 'test:a') end
check(count == 10, '10 at once, then refused')
local lines = s.lines()
check(#lines == 1 and lines[1]:find('rate%-limited test:a from 1 "Alice" license:aaa111: limit 10 at once, 3 a second'), 'one log line for the burst: ' .. tostring(lines[1]))
s.send(2, 'test:a'); s.send(1, 'test:b')
check(count == 111, 'other players and other events have their own limits')
s.advance(1000)
for _ = 1, 4 do s.send(1, 'test:a') end
check(count == 114, '3 more a second later')
s.handlers.playerDropped()  -- source nil: ignored
_G.source = 1; s.handlers.playerDropped(); _G.source = nil
for _ = 1, 10 do s.send(1, 'test:a') end
check(count == 124, 'a player who left starts afresh')
pass('rate limit per player and event, refill, forgotten on playerDropped')

-- Convars: read at most every 5 s; the rate limit can be turned off; the burst and rate change.
s = newServer()
count = 0
s.guard.netEvent('test:a', {}, function() count = count + 1 end)
s.convars.vmenu_guard_rate_limits = 'false'
for _ = 1, 20 do s.send(1, 'test:a') end
check(count == 20, 'rate limits off: everything runs')
s.convars.vmenu_guard_rate_limits = nil
s.convars.vmenu_guard_burst = 2
s.send(1, 'test:a')
check(count == 21, 'a changed convar is not read on every request')
s.advance(5000)
count = 0
for _ = 1, 5 do s.send(2, 'test:a') end
check(count == 2, 'read again after 5 s: burst 2')
pass('convars (off switch, burst, cached for 5 s)')

-- No real player: dropped and logged, the handler's reject is not called (there is no one to answer).
s = newServer()
local rejected = {}
count = 0
s.guard.netEvent('test:a', { reject = function(src, why) rejected[#rejected + 1] = why end }, function() count = count + 1 end)
s.send('', 'test:a'); s.send(nil, 'test:a'); s.send(0, 'test:a')
lines = s.lines()
check(count == 0 and #rejected == 0, 'no source: dropped without a reply')
check(#lines == 1 and lines[1]:find('no%-source test:a from no player: no player sent this request'), 'logged once (repeats summarised): ' .. tostring(lines[1]))
pass('requests without a real player are dropped and logged')

-- Permission and argument checks, with the handler's own replies.
s = newServer()
local aces = { [1] = true }
count = 0
s.guard.netEvent('test:capture', {
    permission = function(src) return aces[src] == true end,
    permissionName = 'vMenu.ClothingCapture',
    args = { s.guard.string(8), s.guard.optional(s.guard.boolean) },
    reject = function(src, why) rejected[#rejected + 1] = src .. ':' .. why end,
}, function() count = count + 1 end)
rejected = {}
s.send(1, 'test:capture', 'ok'); s.send(1, 'test:capture', 'ok', true)
check(count == 2, 'well-formed requests from a permitted player run (an optional argument may be missing)')
s.send(2, 'test:capture', 'ok')
s.send(1, 'test:capture', { 'table' })
s.send(1, 'test:capture', string.rep('x', 9))
s.send(1, 'test:capture', 'ok', 'yes')
check(count == 2 and table.concat(rejected, ',') == '2:denied,1:malformed,1:malformed,1:malformed', 'refused: ' .. table.concat(rejected, ','))
lines = s.lines()
check(lines[1]:find('denied test:capture from 2 "Bob" license:bbb222: missing vMenu.ClothingCapture', 1, false), 'denied line: ' .. lines[1])
check(lines[2]:find('malformed test:capture from 1 "Alice" license:aaa111: argument 1 is not text of up to 8 characters', 1, true), 'malformed line: ' .. lines[2])
check(#lines == 2, 'the same malformed request again is counted, not printed')
s.guard.callback('test:cb', { args = { s.guard.number } }, function(src, id) return 'row' .. id end)
s.guard.callback('test:cb2', { args = { s.guard.table }, reject = function(_, why) return false, 'refused: ' .. why end }, function() return true end)
check(s.call(1, 'test:cb', 7) == 'row7', 'a callback runs and returns')
local ok, why = s.call(1, 'test:cb', 'seven')
check(ok == false and why == nil, 'a refused callback returns false by default')
ok, why = s.call(1, 'test:cb2', 'x')
check(ok == false and why == 'refused: malformed', 'or what its reject returns')
check(s.guard.pattern('^VM%-%x+%-%x+$', 48).check('VM-00ab-0001') and not s.guard.pattern('^VM%-%x+%-%x+$', 48).check('VM-zz'), 'pattern check')
check(not s.guard.number.check(0/0) and s.guard.number.check(1.5), 'a number, not NaN')
pass('permission and argument checks, rejects for net events and callbacks')

-- The security log: names cleaned, never the IP, repeats summarised after 10 s.
s = newServer()
s.names[3] = '^1Evil`\n"Name"' .. string.rep('é', 40)
s.licenses[3] = nil
s.guard.report('denied', 'vMenu:ClearArea', 3, 'missing vMenu.MiscSettings.ClearArea')
lines = s.lines()
check(#lines == 1, 'one line')
local line = lines[1]
check(line:sub(1, 23) == '^3[vMenu] [SECURITY]^7 ', 'console prefix')
check(not line:find('\n', 1, true) and not line:find('`', 1, true) and not line:find('^1', 1, true), 'no newline, backtick or colour code from the name: ' .. line)
check(line:find(' from 3 "Evil\'Name\'', 1, true) and line:find(' no-license: missing', 1, true), 'the name is one quoted field; a missing license says so')
local name = line:match('"(.-)"')
check(#name <= 64 and utf8.len(name) ~= nil, 'the name is cut to 64 bytes without splitting a character')
for _ = 1, 30 do s.guard.report('rate-limited', 'vMenu:RequestPermissions', 1, 'limit 3 at once, 3 a minute') end
s.guard.report('rate-limited', 'vMenu:RequestPermissions', 2, 'limit 3 at once, 3 a minute')
lines = s.lines()
check(#lines == 2, 'repeats within 10 s are counted, other players still printed')
s.advance(9999)
check(#s.lines() == 0, 'nothing more before the window ends')
s.advance(1)
lines = s.lines()
check(#lines == 1 and lines[1]:find('rate-limited vMenu:RequestPermissions from 1 "Alice" license:aaa111: 29 more in the last 10 s', 1, true),
    'then one summary line for the repeats (none for entries without repeats): ' .. tostring(lines[1]))
s.guard.report('rate-limited', 'vMenu:RequestPermissions', 1, 'limit')
check(#s.lines() == 1, 'a new window prints again')
for _, l in ipairs(s.printed) do check(not l:find('203.0.113.7', 1, true), 'no IP') end
pass('security log (event, id, name, license; cleaned; repeats summarised; never the IP)')

-- Entries from the C# guard arrive on a local event, with how many requests each stands for.
s = newServer()
s.handlers['vMenu:security:report']('rate-limited', 'vMenu:PSRP:GodmodeState', 2, 'limit 10 at once, 3 a second', 25)
lines = s.lines()
check(#lines == 1 and lines[1]:find('rate-limited vMenu:PSRP:GodmodeState from 2 "Bob" license:bbb222: limit 10 at once, 3 a second (25 requests)', 1, true), 'C# entry: ' .. tostring(lines[1]))
s.handlers['vMenu:security:report']('rate-limited', 'vMenu:PSRP:GodmodeState', 2, 'limit', 40)
s.handlers['vMenu:security:report']('spoofed', 'playerDropped', 1, 'a client sent the server\'s own player event; ignored', 1)
s.handlers['vMenu:security:report']('no-source', 'vMenu:ClearArea', 0, 'no player sent this request', 1)
lines = s.lines()
check(#lines == 2 and lines[1]:find('spoofed playerDropped from 1 "Alice"', 1, true) and lines[2]:find('no-source vMenu:ClearArea from no player', 1, true), 'spoofed and no-source entries')
s.advance(10000)
check(s.lines()[1]:find('from 2 "Bob" license:bbb222: 40 more in the last 10 s', 1, true), 'C# counts add up in the summary')
_G.source = 5
s.handlers['vMenu:security:report']('denied', 'x', 1, 'forged', 1)
_G.source = nil
check(#s.lines() == 0, 'an entry with a player source (not from this server) is ignored')
check(s.net['vMenu:security:report'] == nil, 'and it is not a net event')
pass('C# guard entries (local event, counts, spoofed and no-source)')

-- Discord webhook: off by default, batched, at most one message per interval, no pings, URL never printed.
s = newServer()
s.guard.report('denied', 'a', 1, 'x')
s.advance(60000)
check(#s.http == 0, 'no webhook set: nothing sent')
local URL = 'https://discord.com/api/webhooks/123/SECRET-TOKEN'
s.convars.vmenu_security_webhook = URL
s.lines()
for i = 1, 5 do s.guard.report('denied', 'e' .. i, 1, 'missing x') end
check(#s.http == 0, 'never sent on the request path')
s.advance(0)
check(#s.http == 1, 'lines from one moment go out together')
local msg = s.http[1]
check(msg.url == URL and msg.method == 'POST' and msg.headers['Content-Type'] == 'application/json', 'a JSON POST to the webhook')
check(msg.body.content:sub(1, 4) == '```\n' and msg.body.content:sub(-4) == '\n```', 'in a code block')
check(select(2, msg.body.content:gsub('\n', '')) == 6 and msg.body.content:find('denied e5 from 1 "Alice" license:aaa111: missing x', 1, true), 'all five lines, no colour codes')
check(not msg.body.content:find('^3', 1, true), 'colour codes stripped')
check(type(msg.body.allowed_mentions) == 'table' and next(msg.body.allowed_mentions.parse) == nil, 'mentions never ping')
s.guard.report('denied', 'f', 1, 'x')
s.advance(4999)
check(#s.http == 1, 'at most one message every 5 s')
s.advance(1)
check(#s.http == 2, 'the next one after 5 s')
s.convars.vmenu_security_webhook_interval = 1
s.guard.report('denied', 'g', 1, 'x'); s.advance(1999)
check(#s.http == 2, 'the interval is never under 2 s')
s.advance(1)
check(#s.http == 3, 'sent at 2 s')
s.convars.vmenu_security_webhook_interval = nil
-- Too many lines: split into messages under Discord's limit, the overflow counted.
s.advance(10000)
for i = 1, 150 do s.guard.report('malformed', ('event%03d'):format(i), 1, string.rep('d', 40)) end
s.advance(0)
local sent = #s.http
s.advance(60000)
local total, last = 0, nil
for i = 4, #s.http do
    local content = s.http[i].body.content
    check(#content <= 2000, 'under Discord\'s 2000 characters')
    total = total + select(2, content:gsub('malformed event', ''))
    last = content
end
check(#s.http - 3 >= 3 and total == 100 and last:find('... and 50 more (too many to send)', 1, true), 'queue capped at 100 lines, the rest counted: ' .. total)
-- 429: wait 30 s.
s.advance(10000)
local before = #s.http
s.guard.report('denied', 'h', 1, 'x'); s.advance(0)
s.http[#s.http].cb(429)
s.guard.report('denied', 'i', 1, 'x')
s.advance(29000)
check(#s.http == before + 1, 'rate limited by Discord: nothing for 30 s')
s.advance(1000)
check(#s.http == before + 2, 'then it continues')
s.http[#s.http].cb(500)
-- Bad URL: nothing sent.
s.convars.vmenu_security_webhook = 'http://example.invalid/SECRET-TOKEN'
s.advance(60000)
before = #s.http
s.guard.report('denied', 'j', 1, 'x'); s.advance(60000)
check(#s.http == before, 'a non-https webhook gets nothing')
for _, l in ipairs(s.printed) do
    check(not l:find('SECRET', 1, true) and not l:find('webhooks/', 1, true), 'the webhook URL is never printed: ' .. l)
end
local warnings = 0
for _, l in ipairs(s.printed) do if l:find('security webhook:', 1, true) then warnings = warnings + 1 end end
check(warnings >= 2, 'webhook problems are reported (without the URL)')
s.convars.vmenu_security_webhook = ''
s.guard.report('denied', 'k', 1, 'x'); s.advance(60000)
check(#s.http == before, 'turned off again')
pass('Discord webhook (off by default, batched, one message per interval, capped, 429 backoff, no pings, URL secret)')

-- The production handlers register through the guard and keep their replies.
s = newServer()
_G.IsPlayerAceAllowed = function(src, ace) return src == 1 and ace == 'vMenu.ClothingCapture' end
_G.TriggerClientEvent = function(name, target, message) s.replies = s.replies or {}; s.replies[#s.replies + 1] = { name, target, message } end
_G.SaveResourceFile = function() return true end
_G.GetCurrentResourceName = function() return 'vMenu' end
_G.GetNumResources = function() return 0 end
assert(load(readFile('build/vMenu/server/clothingCapture.lua'), '@server/clothingCapture.lua'))()
s.send(2, 'vMenu:ClothingCapture:Save', '{}')
check(s.replies[1][1] == 'vMenu:ClothingCapture:Saved' and s.replies[1][2] == 2 and s.replies[1][3]:find("aren't allowed", 1, true), 'clothing capture: refused players get the same reply as before')
check(s.lines()[1]:find('denied vMenu:ClothingCapture:Save from 2 "Bob" license:bbb222: missing vMenu.ClothingCapture', 1, true), 'and it is logged')
s.send(1, 'vMenu:ClothingCapture:Save', { kind = 'table' })
check(s.replies[2][3] == 'That capture is too large or damaged.', 'a capture that is not text gets the damaged reply')
pass('production handlers keep their replies')
