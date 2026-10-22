-- Server backup of a player's vMenu saves (Misc Settings > Back Up My Saves / Restore My Saves).
-- Saves live in each player's own game storage (resource KVPs). This keeps one copy per Rockstar
-- license in the `vmenu_backups` table (database.sql), so they survive a cache wipe or a new PC.
-- Enabled with `setr vmenu_backups true`; needs oxmysql.

local MAX_BYTES = 2 * 1024 * 1024
local MAX_ENTRIES = 5000
local MAX_KEY_LENGTH = 255
local BACKUP_COOLDOWN_MS = 60000
local RESTORE_COOLDOWN_MS = 30000
local INFO_COOLDOWN_MS = 2000
local SEND_BPS = 128000

-- Must match client/backups.lua.
local BACKUP_PREFIXES = {
    'veh_', 'saved_veh_category_', 'ped_', 'mp_ped_', 'mp_character_category_',
    'vmenu_string_saved_weapon_loadout_', 'vmenu_vehicle_preset_', 'vmenu_outfit_',
}
local BACKUP_KEYS = {
    'vmenu_default_character', 'vmenu_last_character', 'vmenu_string_default_loadout',
    'vmenu_vehicle_favourites', 'vmenu_vehicle_recent', 'vmenu_ped_favourites', 'vmenu_ped_recent',
}

local exactKeys = {}
for _, key in ipairs(BACKUP_KEYS) do exactKeys[key] = true end

local function isBackupKey(key)
    if type(key) ~= 'string' or key == '' or #key > MAX_KEY_LENGTH then return false end
    if exactKeys[key] then return true end
    for _, prefix in ipairs(BACKUP_PREFIXES) do
        if #key > #prefix and key:sub(1, #prefix) == prefix then return true end
    end
    return false
end

local function backupsEnabled()
    return GetConvarBool('vmenu_backups', false)
end

local function licenseOf(source)
    local license = GetPlayerIdentifierByType(source, 'license')
    return license and license ~= '' and license or nil
end

-- Last backup, restore and lookup per license, so reconnecting doesn't reset the limits.
local lastBackupAt, lastRestoreAt, lastInfoAt = {}, {}, {}

local function onCooldown(times, license, cooldownMs)
    local last = times[license]
    return last ~= nil and GetGameTimer() - last < cooldownMs
end

--- Runs an oxmysql export. Returns false if the query failed (oxmysql raises on errors).
local function db(method, query, params)
    local ok, result = pcall(function() return exports.oxmysql[method](exports.oxmysql, query, params) end)
    if not ok then
        lib.print.error(('vmenu_backups query failed: %s'):format(result))
        return false
    end
    return true, result
end

--- Checks an uploaded backup. Returns the saves and their count, or nil and a reason.
local function validate(payload)
    if type(payload) ~= 'string' or payload == '' then return nil, 'The backup was empty.' end
    if #payload > MAX_BYTES then return nil, 'Your saves are larger than the 2 MB backup limit.' end
    local ok, saves = pcall(json.decode, payload)
    if not ok or type(saves) ~= 'table' then return nil, 'The backup could not be read.' end

    local clean, count = {}, 0
    for key, value in pairs(saves) do
        if not isBackupKey(key) or type(value) ~= 'string' or value == '' then
            return nil, 'The backup contained something that is not a vMenu save.'
        end
        count = count + 1
        if count > MAX_ENTRIES then return nil, 'You have too many saves to back up.' end
        clean[key] = value
    end
    if count == 0 then return nil, 'You have no saves to back up.' end
    return clean, count
end

lib.callback.register('vMenu:backups:info', function(source)
    if not backupsEnabled() then return false, 'Save backups are disabled on this server.' end
    local license = licenseOf(source)
    if not license then return false, 'Your Rockstar license could not be read.' end
    if onCooldown(lastInfoAt, license, INFO_COOLDOWN_MS) then return false, 'Please wait a moment and try again.' end
    lastInfoAt[license] = GetGameTimer()

    local ok, row = db('single_async',
        "SELECT `entries`, DATE_FORMAT(`updated`, '%Y-%m-%d %H:%i') AS `updated` FROM `vmenu_backups` WHERE `license` = ?",
        { license })
    if not ok then return false, 'The server could not read your backup. Please try again later.' end
    if not row then return false, "You don't have a backup on this server yet." end
    return { entries = row.entries, updated = row.updated }
end)

RegisterNetEvent('vMenu:backups:upload', function(payload)
    local src = source
    local function reply(ok, message)
        TriggerClientEvent('vMenu:backups:uploadResult', src, ok, message)
    end

    if not backupsEnabled() then return reply(false, 'Save backups are disabled on this server.') end
    local license = licenseOf(src)
    if not license then return reply(false, 'Your Rockstar license could not be read.') end
    if onCooldown(lastBackupAt, license, BACKUP_COOLDOWN_MS) then
        return reply(false, 'You can back up your saves once a minute. Please wait and try again.')
    end

    local saves, countOrReason = validate(payload)
    if not saves then
        lib.print.warn(('%s [%s] sent a backup that was refused: %s'):format(GetPlayerName(src), src, countOrReason))
        return reply(false, countOrReason)
    end
    lastBackupAt[license] = GetGameTimer()

    local ok = db('query_async',
        'INSERT INTO `vmenu_backups` (`license`, `data`, `entries`) VALUES (?, ?, ?) ' ..
        'ON DUPLICATE KEY UPDATE `data` = VALUES(`data`), `entries` = VALUES(`entries`), `updated` = CURRENT_TIMESTAMP',
        { license, json.encode(saves), countOrReason })
    if not ok then return reply(false, 'The server could not save your backup. Please try again later.') end
    reply(true, ('Backed up %d saves to the server.'):format(countOrReason))
end)

RegisterNetEvent('vMenu:backups:download', function()
    local src = source
    local function fail(message)
        TriggerClientEvent('vMenu:backups:restoreFailed', src, message)
    end

    if not backupsEnabled() then return fail('Save backups are disabled on this server.') end
    local license = licenseOf(src)
    if not license then return fail('Your Rockstar license could not be read.') end
    if onCooldown(lastRestoreAt, license, RESTORE_COOLDOWN_MS) then
        return fail('You can restore your saves once every 30 seconds. Please wait and try again.')
    end
    lastRestoreAt[license] = GetGameTimer()

    local ok, row = db('single_async', 'SELECT `data` FROM `vmenu_backups` WHERE `license` = ?', { license })
    if not ok then return fail('The server could not read your backup. Please try again later.') end
    if not row or type(row.data) ~= 'string' then return fail("You don't have a backup on this server yet.") end
    TriggerLatentClientEvent('vMenu:backups:restoreData', src, SEND_BPS, row.data)
end)

CreateThread(function()
    if not backupsEnabled() then return end
    if GetResourceState('oxmysql') ~= 'started' then
        lib.print.error('Save backups are enabled (vmenu_backups) but oxmysql is not started.')
        return
    end
    if not db('query_async', 'SELECT 1 FROM `vmenu_backups` LIMIT 1') then
        lib.print.error('Save backups are enabled but the vmenu_backups table is missing. Run database.sql.')
        return
    end
    lib.print.info('Save backups are enabled.')
end)
