-- You should not be editing this file unless you know what you are doing. This shouldn't need to be changed.
-- For integrations, check client/integrations.lua

---@class inputDialog
---@param windowTitle string
---@param defaultText string
---@param maxLength integer | 30
---@return string
exports(
	"inputDialog",
	function(windowTitle, defaultText, maxLength)
		local input =
			lib.inputDialog(
				"vMenu",
				{
					{ type = "input", label = windowTitle, default = defaultText, max = maxLength or 30 }
				}
			)
		return input and input[1] or ""
	end
)

local format = string.format

local function rgbToHex(r, g, b)
	-- Ensure values are within RGB bounds (0-255)
	r = math.max(0, math.min(255, r))
	g = math.max(0, math.min(255, g))
	b = math.max(0, math.min(255, b))

	-- Format RGB values to hexadecimal string
	return format("#%02X%02X%02X", r, g, b)
end

---@class colourDialog
---@param type integer 1 for primary, 2 for secondary
---@return string
exports("colourDialog", function(type)
	if not cache.vehicle then return "" end -- edge case
	local defaultColour = type == 1 and rgbToHex(GetVehicleCustomPrimaryColour(cache.vehicle)) or
		rgbToHex(GetVehicleCustomSecondaryColour(cache.vehicle))
	local input = lib.inputDialog("vMenu", {
		{ type = 'color', label = 'Select a Colour', default = defaultColour },
	})
	return input and input[1] or ""
end)

exports("getUserConfirmation", function(title, description)
	local confirmed = lib.alertDialog({
		header = format("vMenu - %s", title),
		content = description,
		centered = true,
		cancel = true,
	})
	return confirmed and confirmed == "confirm" or false
end)

---@class coordsDialog
---@return string "x,y,z", or "" if cancelled
exports("coordsDialog", function()
	local coords = GetEntityCoords(cache.ped)
	local function field(label, value)
		return { type = "number", label = label, default = math.floor(value * 100 + 0.5) / 100, precision = 2, required = true }
	end
	local input = lib.inputDialog("Teleport To Coords", {
		field("X", coords.x),
		field("Y", coords.y),
		field("Z", coords.z),
	})
	if not input or not input[1] or not input[2] or not input[3] then return "" end
	return format("%.3f,%.3f,%.3f", input[1], input[2], input[3])
end)

---@class copyToClipboard
---@param text string
exports("copyToClipboard", function(text)
	print("Copied to clipboard: " .. text)
	lib.setClipboard(text)
	return ""
end)

CreateThread(function()
	-- # Sets whether or not players can lose their head props when they are hit/pushed.
	-- # true = Props will stay on player (default vMenu and GTA Online behavior)
	-- # false = vMenu will not touch this feature, by default this means that head props will fall off when the player is hit, which is the default behavior for GTA V Single Player peds.
	-- setr keep_player_head_props false (this is configured in your permissions.cfg)

	-- this is just to replace needing to run a constant thread loop on the client
	if GetConvar("keep_player_head_props", "false") == "true" then
		local function keepHeadProps(ped)
			SetPedCanLosePropsOnDamage(ped, false, 0)
			SetPedConfigFlag(ped, 427, true)
		end
		-- onCache only fires when the ped changes, so cover the current ped as well.
		keepHeadProps(cache.ped)
		lib.onCache("ped", keepHeadProps)
	end

	-- keep vehicle lights enabled during blackout when configured
	if GetConvar("vmenu_vehicle_blackout_enabled", "true") == "true" then
		SetArtificialLightsStateAffectsVehicles(false)
	end
end)
