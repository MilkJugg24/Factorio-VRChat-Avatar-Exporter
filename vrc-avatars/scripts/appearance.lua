-- Which character prototype a player's choice asks for. Shared between the
-- data stage and the control stage.

local format = require("scripts.pack-format")

local appearance = {}

-- Everyone's look until they choose an avatar pack, and what is left when a
-- chosen pack is removed.
appearance.ENGINEER = "character"
appearance.ENGINEER_CORPSE = "character-corpse"

-- prefs.avatar is the id of the chosen pack, nil for the engineer. `fast`: the
-- player's armor makes them fast enough to sprint, so a pack with a sprint
-- gives its sprinting character. prefs.flap: the player flaps their arms in the
-- air, with a pack that can. When the chosen pack is not installed
-- (`exists(name)` is false) this returns the engineer and the note
-- "avatar-missing" as second value.
function appearance.resolve(prefs, exists, fast)
  if prefs.avatar then
    local name = format.character_name(prefs.avatar)
    if exists(name) then
      local sprint = fast and exists(format.character_name(prefs.avatar, true, false))
      local flap = prefs.flap == true and exists(format.character_name(prefs.avatar, false, true))
      return format.character_name(prefs.avatar, sprint, flap), nil
    end
    return appearance.ENGINEER, "avatar-missing"
  end
  return appearance.ENGINEER, nil
end

return appearance
