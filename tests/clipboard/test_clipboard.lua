-- copyToClipboard (build/vMenu/client/exports.lua): FiveM's NUI only writes to the clipboard during
-- the player's own click or key press (Nov 2026 update), so vMenu copies through psrp_notify's copy
-- box. Run from the repository root: lua tests/clipboard/test_clipboard.lua

local failures = 0
local function check(ok, what)
    if ok then print('ok   ' .. what) else failures = failures + 1; print('FAIL ' .. what) end
end

local registered, waited, state, boxCalls, setClipboard = {}, 0, 'started', {}, {}
local boxAnswer, boxError = true, nil

_G.exports = setmetatable({}, {
    __call = function(_, name, fn) registered[name] = fn end,
    __index = function(_, resource)
        assert(resource == 'psrp_notify', resource)
        return { copy = function(_, text, title)
            if boxError then error(boxError) end
            boxCalls[#boxCalls + 1] = { text = text, title = title }
            return boxAnswer
        end }
    end,
})
_G.Wait = function() waited = waited + 1 end
_G.GetResourceState = function(name) return name == 'psrp_notify' and state or 'missing' end
_G.CreateThread = function() end -- the head-props thread; not under test
_G.print = function() end
_G.lib = { setClipboard = function(text) setClipboard[#setClipboard + 1] = text end, inputDialog = function() end }

local file = assert(io.open('build/vMenu/client/exports.lua', 'rb'))
local source = file:read('a')
file:close()
assert(load(source, '@client/exports.lua'))()

local copy = registered.copyToClipboard
check(type(copy) == 'function', 'copyToClipboard is exported')

local copied = copy('123.45, -67.89, 30.12, 90.5', 'Copy Coordinates')
check(waited == 1, 'yields first, so vMenu has something to await')
check(copied == true and #boxCalls == 1 and boxCalls[1].text == '123.45, -67.89, 30.12, 90.5'
    and boxCalls[1].title == 'Copy Coordinates', 'the copy box gets the text and title, and reports copied')
check(#setClipboard == 0, 'no silent ox_lib copy while psrp_notify runs')

boxAnswer = false
check(copy('42', 'Outfit code #42') == false, 'closed without copying (Esc): false, so vMenu says nothing')

boxAnswer = true
check(copy(1885233650) == true and boxCalls[#boxCalls].text == '1885233650' and boxCalls[#boxCalls].title == nil,
    'a number is copied as text; no title gives the box its own')

boxError = 'No such export copy in resource psrp_notify'
check(copy('x', 'Copy') == false, 'an old psrp_notify without the box: false, no error')
boxError = nil

state = 'stopped'
check(copy('abc') == true and setClipboard[#setClipboard] == 'abc', 'without psrp_notify: the old ox_lib copy')

if failures > 0 then
    error(('%d clipboard check(s) failed'):format(failures))
end
io.stdout:write('PASS: copyToClipboard goes through the copy box\n')
