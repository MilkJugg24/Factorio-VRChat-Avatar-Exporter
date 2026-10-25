-- Creates one character (and corpse) prototype per installed avatar pack.
-- The vanilla "character" stays the engineer: it is everyone's look until
-- they choose a pack.
-- Runs in data-final-fixes so the copies inherit changes other mods made to
-- the vanilla character (inventory size, reach, ...).

local format = require("scripts.pack-format")
local builder = require("prototypes.animation-builder")
local avatar_packs = require("prototypes.avatar-packs")

local vanilla_character = data.raw["character"]["character"]
local vanilla_corpse = data.raw["character-corpse"]["character-corpse"]
if not vanilla_character then
  error("VRC Avatars: the vanilla \"character\" prototype is missing")
end

-- Flight animations are only needed (and their sprites only loaded) when some
-- armor can fly, e.g. Space Age's mech armor.
local flight = false
for _, armor in pairs(data.raw["armor"] or {}) do
  if armor.provides_flight then
    flight = true
    break
  end
end

-- `sprint`: the copy that runs with the pack's sprint animation, used while the
-- player's armor makes them fast (control.lua); everything else is the same.
local function new_character(pack, look, sprint)
  local character = table.deepcopy(vanilla_character)
  character.name = sprint and format.sprint_name(pack.id) or format.character_name(pack.id)
  character.localised_name = { "entity-name.character" }
  character.localised_description = vanilla_character.localised_description
  character.hidden_in_factoriopedia = true
  character.factoriopedia_alternative = "character"
  character.icon = pack.icon.filename
  character.icon_size = pack.icon.size
  character.icons = nil
  character.animations = { builder.armor_animation(look, flight, sprint and "sprint" or "run") }
  character.mining_with_tool_particles_animation_positions = { pack.timing.mine_impact_frame }
  -- the walking sound plays on running_sound_animation_positions only: every footfall, both feet
  local steps = {}
  for _, frame in ipairs(pack.timing.left_foot_frames) do steps[#steps + 1] = frame end
  for _, frame in ipairs(pack.timing.right_foot_frames) do steps[#steps + 1] = frame end
  table.sort(steps)
  character.running_sound_animation_positions = steps
  character.moving_sound_animation_positions = steps
  character.left_footprint_frames = pack.timing.left_foot_frames
  character.right_footprint_frames = pack.timing.right_foot_frames
  character.distance_per_frame = sprint and pack.timing.sprint_distance_per_frame or pack.timing.distance_per_frame
  if vanilla_corpse then
    character.character_corpse = format.corpse_name(pack.id)
  end
  return character
end

local function new_corpse(pack, look)
  local corpse = table.deepcopy(vanilla_corpse)
  corpse.name = format.corpse_name(pack.id)
  corpse.localised_name = { "entity-name.character-corpse" }
  corpse.hidden_in_factoriopedia = true
  corpse.factoriopedia_alternative = "character-corpse"
  corpse.icon = pack.icon.filename
  corpse.icon_size = pack.icon.size
  corpse.icons = nil
  corpse.picture = nil
  corpse.pictures = { builder.corpse_picture(look) }
  corpse.armor_picture_mapping = {}
  return corpse
end

local new_prototypes = {}
for _, pack in ipairs(avatar_packs.collect()) do
  local look = avatar_packs.look(pack)
  new_prototypes[#new_prototypes + 1] = new_character(pack, look, false)
  if pack.sprint then
    new_prototypes[#new_prototypes + 1] = new_character(pack, look, true)
  end
  if vanilla_corpse then
    new_prototypes[#new_prototypes + 1] = new_corpse(pack, look)
  end
  new_prototypes[#new_prototypes + 1] = {
    type = "sprite",
    name = format.sprite_name(pack.id),
    filename = pack.icon.filename,
    size = pack.icon.size,
    flags = { "gui-icon" },
  }
end
if #new_prototypes > 0 then
  data:extend(new_prototypes)
end
