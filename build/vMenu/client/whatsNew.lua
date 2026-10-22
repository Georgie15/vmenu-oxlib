-- "What's new in vMenu": shown once, the first time a player opens the menu after an update, and
-- again from Misc Settings > What's New In vMenu. Change VERSION when you change the list, so
-- everyone sees it once more.

local VERSION = '2026-10-21-f'
local SEEN_KEY = 'vmenu_whats_new_seen'

local function lines()
    local items = {
        '- **Favourite and Recent Vehicles** in the Vehicle Spawner, and **Favourite and Recent Peds** in Spawn Peds.',
        '- **Vehicle presets** (Vehicle Options > Vehicle Extras): your extras and livery on every spawn of that model.',
        '- Changed a **saved vehicle** in Vehicle Options? When you close the menu, vMenu offers to update its save.',
        '- **Saved Outfits** (MP Ped Customization): change clothes without touching your face or hair.',
        '- **Clothing Quick Toggles** (Player Appearance): take off and put back your hat, glasses, mask and more.',
        '- **Personal Vehicle**: Auto-Lock When I Walk Away, and Set Waypoint To My Car.',
        '- **Ped Pack**: browse the pack and preview its peds before you buy.',
        '- **New key bindings**, unbound until you pick keys: Settings > Key Bindings > FiveM, look for "vMenu"'
            .. ' (personal vehicle lock and engine, your vehicle\'s engine, hood, trunk, windows and seat, clothing toggles).',
    }
    if GetConvar('vmenu_backups', 'false') == 'true' then
        table.insert(items, 1, '- **Back up your saves** to the server and restore them on any PC (Misc Settings),'
            .. ' or turn on **Auto Back Up My Saves**.')
    end
    if GetConvar('vmenu_photo_mode', 'false') == 'true' then
        items[#items + 1] = '- **Photo Mode** (Misc Settings): a free camera for screenshots, with the HUD hidden.'
    end
    return table.concat(items, '\n')
end

local function show()
    CreateThread(function()
        lib.alertDialog({ header = "What's new in vMenu", content = lines(), centered = true, size = 'lg' })
    end)
end

AddEventHandler('vMenu:MenuStateChanged', function(open)
    if not open or GetResourceKvpString(SEEN_KEY) == VERSION then return end
    SetResourceKvp(SEEN_KEY, VERSION)
    show()
end)

AddEventHandler('vMenu:whatsNew:show', show)
