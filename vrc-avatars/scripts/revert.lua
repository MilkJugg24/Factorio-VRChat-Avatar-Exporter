-- Turns characters and corpses back into the engineer's, so that this mod (or
-- one avatar pack) can be removed without Factorio deleting them, and the
-- items they hold, when the save is loaded.

local appearance = require("scripts.appearance")
local format = require("scripts.pack-format")
local packs = require("scripts.packs")
local swap = require("scripts.swap")

local revert = {}

-- The pack id named by a /avatar-revert-all parameter: the id or the pack's mod
-- name. Returns true and the id (nil for no parameter, meaning everything), or
-- false when the parameter is not one word or names no pack `is_known(id)` accepts.
function revert.parse_pack_argument(parameter, is_known)
  if parameter == nil or not parameter:find("%S") then
    return true, nil
  end
  local word = parameter:match("^%s*(%S+)%s*$")
  if word then
    if is_known(word) then
      return true, word
    end
    local id = format.id_of_character(word)
    if id and is_known(id) then
      return true, id
    end
  end
  return false, nil
end

-- pack_id: only revert what uses that avatar pack; nil: every pack.
-- prefs_of(player_index) -> prefs; apply(player) applies a player's prefs;
-- pending is storage.pending (offline players are converted when they rejoin).
-- Returns { corpses = converted corpses, remaining = characters still using
-- a mod prototype (offline, dead or busy players) }.
function revert.run(pack_id, prefs_of, apply, pending)
  local function is_target(id)
    if pack_id then
      return id == pack_id
    end
    return id ~= nil
  end

  for _, player in pairs(game.players) do
    local prefs = prefs_of(player.index)
    if not pack_id or prefs.avatar == pack_id then
      prefs.avatar = nil
      if player.connected then
        apply(player)
      else
        pending[player.index] = true
      end
    end
  end

  local corpses = 0
  local remaining = 0
  for _, surface in pairs(game.surfaces) do
    for _, corpse in pairs(surface.find_entities_filtered({ type = "character-corpse" })) do
      if is_target(packs.id_of_corpse(corpse.name)) and swap.replace_corpse(corpse, appearance.ENGINEER_CORPSE) then
        corpses = corpses + 1
      end
    end
    for _, character in pairs(surface.find_entities_filtered({ type = "character" })) do
      if is_target(packs.id_of_character(character.name)) then
        remaining = remaining + 1
      end
    end
  end
  return { corpses = corpses, remaining = remaining }
end

return revert
