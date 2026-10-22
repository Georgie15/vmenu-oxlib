-- Minimal JSON for the Lua tests (FiveM provides json.encode/json.decode). Objects are encoded
-- with sorted keys; a table with array items is encoded as an array.
-- Use: local json = dofile('tests/lua/json.lua')
local json = {}

local escapes = { ['"'] = '\\"', ['\\'] = '\\\\', ['\b'] = '\\b', ['\f'] = '\\f', ['\n'] = '\\n', ['\r'] = '\\r', ['\t'] = '\\t' }
local function encodeString(s)
    return '"' .. (s:gsub('[%c"\\]', function(c) return escapes[c] or ('\\u%04x'):format(c:byte()) end)) .. '"'
end

function json.encode(v)
    local t = type(v)
    if t == 'string' then return encodeString(v) end
    if t == 'number' or t == 'boolean' then return tostring(v) end
    if t == 'nil' then return 'null' end
    assert(t == 'table', 'cannot encode ' .. t)
    if #v > 0 then
        local parts = {}
        for i = 1, #v do parts[i] = json.encode(v[i]) end
        return '[' .. table.concat(parts, ',') .. ']'
    end
    local keys = {}
    for k in pairs(v) do keys[#keys + 1] = tostring(k) end
    table.sort(keys)
    local parts = {}
    for _, k in ipairs(keys) do parts[#parts + 1] = encodeString(k) .. ':' .. json.encode(v[k]) end
    return '{' .. table.concat(parts, ',') .. '}'
end

function json.decode(text)
    local pos = 1
    local function ws() pos = text:find('[^ \t\r\n]', pos) or #text + 1 end
    local function str()
        pos = pos + 1
        local out = {}
        while true do
            local c = text:sub(pos, pos)
            if c == '' then error('unterminated string') end
            if c == '"' then pos = pos + 1; return table.concat(out) end
            if c == '\\' then
                local e = text:sub(pos + 1, pos + 1)
                local map = { ['"'] = '"', ['\\'] = '\\', ['/'] = '/', b = '\b', f = '\f', n = '\n', r = '\r', t = '\t' }
                if e == 'u' then
                    out[#out + 1] = utf8.char(tonumber(text:sub(pos + 2, pos + 5), 16)); pos = pos + 6
                elseif map[e] then
                    out[#out + 1] = map[e]; pos = pos + 2
                else
                    error('bad escape')
                end
            else
                out[#out + 1] = c; pos = pos + 1
            end
        end
    end
    local value
    local function list(close, item)
        pos = pos + 1; ws()
        if text:sub(pos, pos) == close then pos = pos + 1; return end
        while true do
            item(); ws()
            local d = text:sub(pos, pos); pos = pos + 1
            if d == close then return end
            if d ~= ',' then error('expected , or ' .. close) end
        end
    end
    function value()
        ws()
        local c = text:sub(pos, pos)
        if c == '{' then
            local obj = {}
            list('}', function()
                ws(); if text:sub(pos, pos) ~= '"' then error('expected key') end
                local k = str(); ws()
                if text:sub(pos, pos) ~= ':' then error('expected :') end
                pos = pos + 1; obj[k] = value()
            end)
            return obj
        elseif c == '[' then
            local arr = {}
            list(']', function() arr[#arr + 1] = value() end)
            return arr
        elseif c == '"' then return str()
        elseif text:find('^true', pos) then pos = pos + 4; return true
        elseif text:find('^false', pos) then pos = pos + 5; return false
        elseif text:find('^null', pos) then pos = pos + 4; return nil
        end
        local num = text:match('^-?%d+%.?%d*[eE]?[-+]?%d*', pos)
        if not num then error('unexpected ' .. c) end
        pos = pos + #num
        return tonumber(num)
    end
    local result = value(); ws()
    if pos <= #text then error('trailing data') end
    return result
end

return json
