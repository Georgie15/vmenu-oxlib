-- Server backup of your vMenu saves (Misc Settings > Back Up My Saves / Restore My Saves).
-- The menu items trigger vMenu:backups:backup and vMenu:backups:restore; server/backups.lua keeps
-- one backup per Rockstar license. Restoring only adds saves you don't have: a save with the same
-- name is never overwritten.

local MAX_BYTES = 2 * 1024 * 1024
local SEND_BPS = 128000
local REPLY_TIMEOUT_MS = 60000

-- Must match server/backups.lua. Menu settings and temporary keys are deliberately left out.
local BACKUP_PREFIXES = {
    'veh_', 'saved_veh_category_', 'ped_', 'mp_ped_', 'mp_character_category_',
    'vmenu_string_saved_weapon_loadout_', 'vmenu_vehicle_preset_',
}
local BACKUP_KEYS = { 'vmenu_default_character', 'vmenu_last_character', 'vmenu_string_default_loadout' }

local exactKeys = {}
for _, key in ipairs(BACKUP_KEYS) do exactKeys[key] = true end

local function isBackupKey(key)
    if type(key) ~= 'string' or key == '' or #key > 255 then return false end
    if exactKeys[key] then return true end
    for _, prefix in ipairs(BACKUP_PREFIXES) do
        if #key > #prefix and key:sub(1, #prefix) == prefix then return true end
    end
    return false
end

local function notify(message, kind)
    Config.Notify('vMenu', message, kind, 6500)
end

--- Every save that belongs in a backup, and how many there are.
local function collect()
    local saves, count = {}, 0
    local function add(key)
        if saves[key] or not isBackupKey(key) then return end
        local value = GetResourceKvpString(key)
        if value and value ~= '' then
            saves[key] = value
            count = count + 1
        end
    end

    for _, prefix in ipairs(BACKUP_PREFIXES) do
        local handle = StartFindKvp(prefix)
        if handle ~= -1 then
            while true do
                local key = FindKvp(handle)
                if not key or key == '' then break end
                add(key)
            end
            EndFindKvp(handle)
        end
    end
    for _, key in ipairs(BACKUP_KEYS) do add(key) end
    return saves, count
end

--- Adds the saves you don't have yet. Returns how many were added and how many were kept as-is.
local function merge(saves)
    local added, skipped = 0, 0
    for key, value in pairs(saves) do
        if isBackupKey(key) and type(value) == 'string' and value ~= '' then
            local existing = GetResourceKvpString(key)
            if existing and existing ~= '' then
                skipped = skipped + 1
            else
                SetResourceKvp(key, value)
                added = added + 1
            end
        end
    end
    return added, skipped
end

local function confirm(title, content)
    return lib.alertDialog({
        header = ('vMenu - %s'):format(title),
        content = content,
        centered = true,
        cancel = true,
    }) == 'confirm'
end

-- One transfer at a time. Cleared by the server's reply, or after a minute if none comes.
local busyUntil = 0
local function isBusy()
    if GetGameTimer() < busyUntil then
        notify('Your last backup or restore is still in progress.', 'error')
        return true
    end
    return false
end

AddEventHandler('vMenu:backups:backup', function()
    if isBusy() then return end
    local saves, count = collect()
    if count == 0 then return notify('You have no saves to back up.', 'error') end

    local payload = json.encode(saves)
    if #payload > MAX_BYTES then
        return notify(('Your saves are %.1f MB, over the 2 MB backup limit.'):format(#payload / 1048576), 'error')
    end

    local info = lib.callback.await('vMenu:backups:info', false)
    local replaces = info and ('\n\nThis replaces your backup from %s (%d saves).'):format(info.updated, info.entries) or ''
    if not confirm('Back Up My Saves', ('Copy your %d saves (%.0f KB) to the server?%s'):format(count, #payload / 1024, replaces)) then
        return
    end

    busyUntil = GetGameTimer() + REPLY_TIMEOUT_MS
    notify('Backing up your saves...', 'inform')
    TriggerLatentServerEvent('vMenu:backups:upload', SEND_BPS, payload)
end)

RegisterNetEvent('vMenu:backups:uploadResult', function(ok, message)
    busyUntil = 0
    notify(message, ok and 'success' or 'error')
end)

AddEventHandler('vMenu:backups:restore', function()
    if isBusy() then return end
    local info, reason = lib.callback.await('vMenu:backups:info', false)
    if not info then return notify(reason or "You don't have a backup on this server yet.", 'error') end
    if not confirm('Restore My Saves', ('Restore your backup from %s (%d saves)?\n\nSaves you already have with the same name are kept as they are.')
            :format(info.updated, info.entries)) then
        return
    end

    busyUntil = GetGameTimer() + REPLY_TIMEOUT_MS
    notify('Restoring your saves...', 'inform')
    TriggerServerEvent('vMenu:backups:download')
end)

RegisterNetEvent('vMenu:backups:restoreData', function(payload)
    busyUntil = 0
    local ok, saves = pcall(json.decode, payload)
    if not ok or type(saves) ~= 'table' then return notify('Your backup could not be read.', 'error') end

    local added, skipped = merge(saves)
    if added == 0 then
        return notify(('Nothing to restore: you already have all %d saves from your backup.'):format(skipped), 'inform')
    end
    local kept = skipped > 0 and (' %d you already had were kept as they are.'):format(skipped) or ''
    notify(('Restored %d saves.%s Reopen the menu to see them.'):format(added, kept), 'success')
end)

RegisterNetEvent('vMenu:backups:restoreFailed', function(message)
    busyUntil = 0
    notify(message, 'error')
end)
