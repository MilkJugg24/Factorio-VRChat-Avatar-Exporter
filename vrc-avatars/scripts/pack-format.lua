-- Constants of the avatar pack format (docs/vrc-avatar-format.md), shared by
-- the data stage and the control stage.

local format = {}

format.DATA_TYPE = "vrc-avatars.avatar-pack"
format.SUPPORTED_VERSION = 1

-- mod, mod-data and character prototype names are MOD_PREFIX .. id
format.MOD_PREFIX = "vrc-avatar-"
format.CORPSE_PREFIX = "vrc-avatar-corpse-"
-- the same character sprinting, or flapping its arms in the air, or both
-- (format.character_name): vrc-avatar-[sprint-][flap-]<id>
format.SPRINT_PREFIX = "vrc-avatar-sprint-"
format.SPRITE_PREFIX = "vrc-avatars-pack-"

-- Avatar bundles hold several packs in one mod; a pack's `mod` field then names
-- the bundle its files are in (docs/vrc-avatar-format.md, "Bundles").
format.BUNDLE_PREFIX = "avatar-bundle-"

format.ID_PATTERN = "^[a-z0-9][a-z0-9%-]*$"
format.MAX_ID_LENGTH = 35
format.MAX_TITLE_LENGTH = 80
format.MAX_SHEET_SIZE = 4096
format.MAX_FRAMES = 255 -- Factorio's limit for one animation

-- animation name -> required direction count
format.DIRECTIONS = {
  ["idle"] = 8,
  ["idle-gun"] = 8,
  ["run"] = 8,
  ["run-gun"] = 18,
  ["sprint"] = 8,
  ["mine"] = 8,
  ["corpse"] = 1,
  ["hover"] = 8,
  ["hover-gun"] = 8,
  ["fly"] = 8,
  ["fly-gun"] = 18,
  ["take-off"] = 8,
  ["hover-flap"] = 8,
  ["fly-flap"] = 8,
  ["take-off-flap"] = 8,
}

format.REQUIRED = { "idle", "idle-gun", "run", "run-gun", "mine", "corpse" }
format.FLIGHT = { "hover", "hover-gun", "fly", "fly-gun", "take-off" }
-- Optional on their own. A pack with a sprint gets a second character that
-- runs with it; the sprint has as many frames as the run.
format.OPTIONAL = { "sprint" }
-- Optional, all together and only with the flight set: flying with the arms
-- beating as wings (for avatars whose wings are their arms). A pack with them
-- gets characters that fly with them, which players choose in the avatar window.
format.FLAP = { "hover-flap", "fly-flap", "take-off-flap" }

-- A sprint's distance_per_frame, when the pack leaves it out, is the run's
-- times this: its strides go this much further.
format.SPRINT_CYCLE_SCALE = 2.2

-- The movement bonus of a player's armor equipment (0.3 per exoskeleton) from
-- which they sprint, unless the map setting says otherwise.
format.DEFAULT_SPRINT_BONUS = 0.6

-- Layers an animation may have besides base/shadow/mask.
format.OPTIONAL_LAYERS = { ["run-gun"] = { "shadow-flipped" } }

-- The defaults for a pack's `timing` (see format.default_timing for the frame
-- numbers).
-- distance_per_frame: one running frame takes distance_per_frame / animation_speed
-- tiles, so a 12-frame run cycle (two steps) covers 12 x 0.238 / 0.6 = 4.77 tiles,
-- like the vanilla engineer's (22 x 0.13 / 0.6), and the legs keep its pace.
format.DEFAULT_TIMING = {
  distance_per_frame = 0.238,
  mine_impact_frame = 9,
  left_foot_frames = { 1 },
  right_foot_frames = { 7 },
  animation_speed = {
    ["idle"] = 0.06,
    ["idle-gun"] = 0.06,
    ["run"] = 0.6,
    ["run-gun"] = 0.6,
    ["sprint"] = 0.6,
    ["mine"] = 0.415, -- for a 12-frame swing; scaled to the pack's frame count
    ["hover"] = 0.27,
    ["hover-gun"] = 0.27,
    ["fly"] = 0.3,
    ["fly-gun"] = 0.3,
    ["take-off"] = 0.3,
    ["hover-flap"] = 0.27,
    ["fly-flap"] = 0.3,
    ["take-off-flap"] = 0.3,
    ["corpse"] = 1,
  },
}

-- Frame numbers a pack gets when its `timing` leaves them out. They scale
-- with the pack's own frame counts; for 12-frame run and mine animations
-- they are 1 / 7 / 9.
function format.default_timing(run_frames, mine_frames)
  return {
    mine_impact_frame = math.max(1, math.floor(mine_frames * 9 / 12 + 0.5)),
    left_foot_frames = { 1 },
    right_foot_frames = { math.floor(run_frames / 2) + 1 },
  }
end

-- Length of a UTF-8 string in characters (Lua's # counts bytes).
function format.utf8_length(s)
  return select(2, s:gsub("[^\128-\191]", ""))
end

-- The character prototype of pack `id`: running or sprinting, its arms still
-- or flapping in the air.
function format.character_name(id, sprint, flap)
  return format.MOD_PREFIX .. (sprint and "sprint-" or "") .. (flap and "flap-" or "") .. id
end

function format.sprint_name(id)
  return format.character_name(id, true, false)
end

function format.corpse_name(id)
  return format.CORPSE_PREFIX .. id
end

function format.sprite_name(id)
  return format.SPRITE_PREFIX .. id
end

-- The pack id of an avatar character prototype name (any of its variants),
-- or nil; nil for a corpse. Second and third values: sprinting, flapping.
function format.id_of_character(name)
  if type(name) ~= "string" or name:sub(1, #format.CORPSE_PREFIX) == format.CORPSE_PREFIX
    or name:sub(1, #format.MOD_PREFIX) ~= format.MOD_PREFIX then
    return nil
  end
  local rest = name:sub(#format.MOD_PREFIX + 1)
  local sprint, flap = false, false
  if rest:sub(1, 7) == "sprint-" then
    sprint, rest = true, rest:sub(8)
  end
  if rest:sub(1, 5) == "flap-" then
    flap, rest = true, rest:sub(6)
  end
  return rest, sprint, flap
end

function format.id_of_corpse(name)
  if type(name) == "string" and name:sub(1, #format.CORPSE_PREFIX) == format.CORPSE_PREFIX then
    return name:sub(#format.CORPSE_PREFIX + 1)
  end
  return nil
end

function format.valid_id(id)
  return type(id) == "string" and #id >= 1 and #id <= format.MAX_ID_LENGTH
    and id:match(format.ID_PATTERN) ~= nil and id:sub(1, 7) ~= "corpse-" and id:sub(1, 7) ~= "sprint-" and id:sub(1, 5) ~= "flap-"
end

return format
