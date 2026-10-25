-- Which character prototype a player's choice asks for. Shared between the
-- data stage and the control stage.

local format = require("scripts.pack-format")

local appearance = {}

-- Everyone's look until they choose an avatar pack, and what is left when a
-- chosen pack is removed.
appearance.ENGINEER = "character"
appearance.ENGINEER_CORPSE = "character-corpse"

-- prefs.avatar is the id of the chosen pack, nil for the engineer. When the
-- chosen pack is not installed (`exists(name)` is false) this returns the
-- engineer and the note "avatar-missing" as second value.
function appearance.resolve(prefs, exists)
  if prefs.avatar then
    local name = format.character_name(prefs.avatar)
    if exists(name) then
      return name, nil
    end
    return appearance.ENGINEER, "avatar-missing"
  end
  return appearance.ENGINEER, nil
end

return appearance
