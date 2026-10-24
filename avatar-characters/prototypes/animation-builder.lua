-- Builds character animations and corpse pictures from the sprite metadata of
-- avatar packs:
--
--   meta = { direction_count, frame_count, layers = { [key] = info }, pixels_per_tile = 64 | 32 | nil }
--   info = { filename = ... | filenames = {...}, lines_per_file = n, width, height, shift }
--
-- A "look" tells the builder where the metadata comes from and which layers to draw:
--
--   look.get_meta(name)  -> meta, or nil when the look has no such animation
--   look.layer_list      -> { { key = "base" }, { key = "mask", extra = {...} }, ... } in draw order
--   look.scale           -> sprite scale (0.5 for 64 pixels per tile), unless the animation sets its own
--                           pixels_per_tile (one drawn at half resolution because it was too large)
--   look.speeds          -> animation name -> animation_speed
--   look.mine_impact_frame
--   look.label           -> used in error messages

local builder = {}

-- Animations used in the air, and what to show instead when a look has none.
local FLIGHT_FALLBACK = {
  ["hover"] = "idle",
  ["hover-gun"] = "idle-gun",
  ["fly"] = "run",
  ["fly-gun"] = "run-gun",
  ["take-off"] = "idle",
}

local function required_meta(look, name)
  local meta = look.get_meta(name)
  if not meta then
    error("Avatar Characters: missing sprite data for " .. look.label .. "/" .. name)
  end
  return meta
end

local function layer(look, meta, key, extra, rotated)
  local info = meta.layers[key]
  local l = {
    width = info.width,
    height = info.height,
    shift = info.shift,
    scale = meta.pixels_per_tile and 32 / meta.pixels_per_tile or look.scale,
    frame_count = meta.frame_count,
    line_length = meta.frame_count,
    usage = "player",
  }
  if info.frames_per_file then
    -- each file holds every direction of frames_per_file frames (the last file may hold fewer)
    l.line_length = nil
    l.stripes = {}
    local left = meta.frame_count
    for _, path in ipairs(info.filenames) do
      local n = math.min(info.frames_per_file, left)
      l.stripes[#l.stripes + 1] = { filename = path, width_in_frames = n, height_in_frames = meta.direction_count }
      left = left - n
    end
  elseif info.filenames then
    -- one direction per row; each file holds lines_per_file directions
    l.filenames = info.filenames
    l.lines_per_file = info.lines_per_file
    l.slice = meta.frame_count
  else
    l.filename = info.filename
  end
  if rotated then
    l.direction_count = meta.direction_count
  end
  for k, v in pairs(extra or {}) do
    l[k] = v
  end
  return l
end

local function layers(look, name, rotated, extra)
  local meta = required_meta(look, name)
  local common = { animation_speed = look.speeds[name] }
  for k, v in pairs(extra or {}) do
    common[k] = v
  end
  local list = {}
  for _, entry in ipairs(look.layer_list) do
    if meta.layers[entry.key] or not entry.optional then
      local merged = {}
      for k, v in pairs(common) do merged[k] = v end
      for k, v in pairs(entry.extra or {}) do merged[k] = v end
      list[#list + 1] = layer(look, meta, entry.key, merged, rotated)
    end
  end
  return { layers = list }
end

local function rotated(look, name, extra)
  return layers(look, name, true, extra)
end

-- Shadow drawn instead of the regular one while the engine mirrors
-- running_with_gun (gun pointing west): the mirrored sprite's shadow, still
-- falling to the east. Only used when the look provides it.
local function flipped_shadow(look, name)
  local meta = required_meta(look, name)
  if not meta.layers["shadow-flipped"] then
    return nil
  end
  local extra = { animation_speed = look.speeds[name], draw_as_shadow = true }
  return { layers = { layer(look, meta, "shadow-flipped", extra, true) } }
end

local function reversed_sequence(n)
  local seq = {}
  for i = n, 1, -1 do
    seq[#seq + 1] = i
  end
  return seq
end

-- A single CharacterArmorAnimation used for every armor.
-- `flight` adds the in-air animations used by flight-capable armor (Space Age
-- mech armor); a look without flight animations shows its ground ones instead.
function builder.armor_animation(look, flight)
  local anim = {
    idle = rotated(look, "idle"),
    idle_with_gun = rotated(look, "idle-gun"),
    running = rotated(look, "run"),
    running_with_gun = rotated(look, "run-gun"),
    flipped_shadow_running_with_gun = flipped_shadow(look, "run-gun"),
    mining_with_tool = rotated(look, "mine"),
    mining_with_tool_particles_animation_positions = { look.mine_impact_frame },
  }
  if flight then
    local function air(name)
      if look.get_meta(name) then
        return name
      end
      return FLIGHT_FALLBACK[name]
    end
    local take_off = air("take-off")
    anim.idle_in_air = rotated(look, air("hover"))
    anim.idle_with_gun_in_air = rotated(look, air("hover-gun"))
    anim.flying = rotated(look, air("fly"))
    anim.flying_with_gun = rotated(look, air("fly-gun"))
    anim.take_off = rotated(look, take_off)
    if take_off == "take-off" then
      local frames = required_meta(look, take_off).frame_count
      anim.landing = rotated(look, take_off, { frame_sequence = reversed_sequence(frames) })
    else
      anim.landing = rotated(look, take_off)
    end
  end
  return anim
end

function builder.corpse_picture(look)
  return layers(look, "corpse", false)
end

return builder
