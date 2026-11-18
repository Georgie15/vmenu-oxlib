-- Run from the repo root: lua tests/whats-new/test_whats_new.lua
-- Runs the production client/whatsNew.lua against a fake FiveM and ox_lib: the note shows once per
-- version, the first time the menu opens, can be shown again from Misc Settings, and only lists
-- features the server has switched on.

local function check(condition, message)
    if not condition then error('FAIL: ' .. message, 2) end
end

local function readFile(path)
    local f = assert(io.open(path, 'rb'))
    local text = f:read('a')
    f:close()
    return text
end

local function newClient(convars, kvps)
    local c = { handlers = {}, dialogs = {}, kvps = kvps or {} }
    _G.GetConvar = function(name, default) return convars[name] or default end
    _G.GetResourceKvpString = function(k) return c.kvps[k] end
    _G.SetResourceKvp = function(k, v) c.kvps[k] = v end
    _G.AddEventHandler = function(name, fn) c.handlers[name] = fn end
    _G.CreateThread = function(fn) fn() end
    _G.lib = { alertDialog = function(d) c.dialogs[#c.dialogs + 1] = d; return 'confirm' end }
    assert(load(readFile('build/vMenu/client/whatsNew.lua'), '@client/whatsNew.lua'))()
    function c.fire(name, ...) c.handlers[name](...) end
    return c
end

local version = readFile('build/vMenu/client/whatsNew.lua'):match("local VERSION = '([^']+)'")
check(version, 'the note has a version')

local c = newClient({ vmenu_backups = 'true', vmenu_photo_mode = 'true' })
c.fire('vMenu:MenuStateChanged', false)
check(#c.dialogs == 0, 'closing the menu shows nothing')
c.fire('vMenu:MenuStateChanged', true)
check(#c.dialogs == 1 and c.kvps.vmenu_whats_new_seen == version, 'the first menu open shows it and remembers this version')
c.fire('vMenu:MenuStateChanged', true)
check(#c.dialogs == 1, 'only once per version')
c.fire('vMenu:whatsNew:show')
check(#c.dialogs == 2, 'Misc Settings shows it again')
local content = c.dialogs[1].content
check(content:find('Back up your saves', 1, true) and content:find('Photo Mode', 1, true), 'switched-on features are listed')
check(content:find('Favourite and Recent Vehicles', 1, true) and content:find('Saved Outfits', 1, true) and content:find('Key Bindings', 1, true),
    'the features everyone has are listed')

local plain = newClient({})
plain.fire('vMenu:MenuStateChanged', true)
check(not plain.dialogs[1].content:find('Back up your saves', 1, true) and not plain.dialogs[1].content:find('Photo Mode', 1, true),
    'features the server has off are not mentioned')

local seenOld = newClient({}, { vmenu_whats_new_seen = 'an-older-version' })
seenOld.fire('vMenu:MenuStateChanged', true)
check(#seenOld.dialogs == 1, 'a player who saw an older note sees the new one')
-- The menu watcher says whether the Player Guide's card is up on this open (PlayerGuide.cs).
local newPlayer = newClient({})
newPlayer.fire('vMenu:MenuStateChanged', true, true)
check(#newPlayer.dialogs == 0 and newPlayer.kvps.vmenu_whats_new_seen == version,
    'a new player gets the guide instead, and the note counts as seen')
newPlayer.fire('vMenu:MenuStateChanged', true, false)
check(#newPlayer.dialogs == 0, 'and it does not come later either')
newPlayer.fire('vMenu:whatsNew:show')
check(#newPlayer.dialogs == 1, 'Misc Settings still shows it')

local veteran = newClient({}, { vmenu_whats_new_seen = 'an-older-version' })
veteran.fire('vMenu:MenuStateChanged', true, true)
check(#veteran.dialogs == 0 and veteran.kvps.vmenu_whats_new_seen == 'an-older-version',
    'a player who saw an older note waits while the guide is up...')
veteran.fire('vMenu:MenuStateChanged', true, false)
check(#veteran.dialogs == 1 and veteran.kvps.vmenu_whats_new_seen == version, '...and gets the new note on the next open')
print('PASS: what\'s new shows once per version, on demand, and only lists switched-on features')
