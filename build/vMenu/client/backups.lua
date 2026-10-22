-- Server backup of your vMenu saves (Misc Settings > Back Up My Saves / Restore My Saves).
-- The menu items trigger vMenu:backups:backup and vMenu:backups:restore; server/backups.lua keeps
-- one backup per Rockstar license. Restoring only adds saves you don't have: a save with the same
-- name is never overwritten. With Auto Back Up My Saves on, changed saves are uploaded on their
-- own; with it off, players who never backed up (or not for 30 days) get a weekly reminder.

local MAX_BYTES = 2 * 1024 * 1024
local SEND_BPS = 128000
local REPLY_TIMEOUT_MS = 60000

-- Must match server/backups.lua. Menu settings and temporary keys are deliberately left out.
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

-- Auto backup and the reminder. vmenu_auto_backup is the Misc Settings checkbox (1 = on). The
-- others record the last successful backup from this PC; none of them are saves, so they aren't
-- backed up themselves.
local AUTO_BACKUP_KEY = 'vmenu_auto_backup'
local FINGERPRINT_KEY = 'vmenu_backup_fingerprint'
local LAST_BACKUP_KEY = 'vmenu_backup_last_at'
local REMINDED_KEY = 'vmenu_backup_reminded_at'
local AUTO_INTERVAL_MS = 10 * 60000
local FIRST_CHECK_MS = 2 * 60000
local REMIND_EVERY_S = 7 * 86400
local STALE_AFTER_S = 30 * 86400

--- Identifies a backup's content. GetHashKey ignores letter case, so the count of capitals is
--- included: a change that only alters case (renaming "cruiser" to "Cruiser") still counts.
local function fingerprint(payload)
    local _, capitals = payload:gsub('%u', '')
    return ('%d:%d:%d'):format(#payload, GetHashKey(payload), capitals)
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
-- The upload in flight: its fingerprint, and whether auto backup sent it.
local sending
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
    sending = { fingerprint = fingerprint(payload), auto = false }
    notify('Backing up your saves...', 'inform')
    TriggerLatentServerEvent('vMenu:backups:upload', SEND_BPS, payload)
end)

local autoProblemShown = false

RegisterNetEvent('vMenu:backups:uploadResult', function(ok, message)
    busyUntil = 0
    local upload = sending
    sending = nil
    if ok and upload then
        SetResourceKvp(FINGERPRINT_KEY, upload.fingerprint)
        SetResourceKvpInt(LAST_BACKUP_KEY, GetCloudTimeAsInt())
    end
    if upload and upload.auto then
        -- Quiet: a success is a short note, and only the first failure of a session is shown.
        if ok then
            notify('Your saves were backed up automatically.', 'inform')
        elseif not autoProblemShown then
            autoProblemShown = true
            notify(('Auto backup failed: %s'):format(message), 'error')
        end
        return
    end
    notify(message, ok and 'success' or 'error')
end)

--- Uploads the saves if they changed since the last successful backup. No dialog.
local function autoBackup()
    if GetGameTimer() < busyUntil then return end
    local saves, count = collect()
    if count == 0 then return end
    local payload = json.encode(saves)
    local current = fingerprint(payload)
    if current == GetResourceKvpString(FINGERPRINT_KEY) then return end
    if #payload > MAX_BYTES then
        if not autoProblemShown then
            autoProblemShown = true
            notify(('Auto backup skipped: your saves are %.1f MB, over the 2 MB limit.'):format(#payload / 1048576), 'error')
        end
        return
    end
    busyUntil = GetGameTimer() + REPLY_TIMEOUT_MS
    sending = { fingerprint = current, auto = true }
    TriggerLatentServerEvent('vMenu:backups:upload', SEND_BPS, payload)
end

--- Once a week at most, tells players with saves and no recent backup from this PC.
local function remindIfNeeded()
    if GetResourceKvpInt(AUTO_BACKUP_KEY) == 1 then return end
    local now = GetCloudTimeAsInt()
    if now - GetResourceKvpInt(REMINDED_KEY) < REMIND_EVERY_S then return end
    local saves, count = collect()
    if count == 0 then return end
    local last = GetResourceKvpInt(LAST_BACKUP_KEY)
    if last > 0 and (now - last < STALE_AFTER_S or fingerprint(json.encode(saves)) == GetResourceKvpString(FINGERPRINT_KEY)) then
        return
    end
    SetResourceKvpInt(REMINDED_KEY, now)
    Config.Notify('vMenu', (last > 0 and 'Your last vMenu backup is over 30 days old.' or 'Your vMenu saves are only stored on this PC.')
        .. ' Back them up in Misc Settings > Back Up My Saves, or turn on Auto Back Up My Saves.', 'inform', 15000)
end

-- Turning the setting on backs up straight away.
AddEventHandler('vMenu:backups:autoNow', autoBackup)

CreateThread(function()
    if GetConvar('vmenu_backups', 'false') ~= 'true' then return end
    Wait(FIRST_CHECK_MS)
    remindIfNeeded()
    while true do
        if GetResourceKvpInt(AUTO_BACKUP_KEY) == 1 then autoBackup() end
        Wait(AUTO_INTERVAL_MS)
    end
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
