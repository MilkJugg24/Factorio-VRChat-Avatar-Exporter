-- Finds and validates avatar packs (docs/vrc-avatar-format.md) in the data stage.
--
-- A broken pack stops the game from loading with an error naming the pack and
-- the problem. Skipping it instead would make Factorio delete every character
-- that uses it, together with the items it carries.

local format = require("scripts.pack-format")

local avatar_packs = {}

local function fail(pack_name, message)
  error("VRC Avatars: avatar pack \"" .. tostring(pack_name) .. "\" " .. message
    .. ". Update or remove that mod.", 0)
end

local function is_int(value, low, high)
  return type(value) == "number" and value == math.floor(value) and value >= low
    and (high == nil or value <= high)
end

local function is_number(value)
  return type(value) == "number" and value == value and value ~= math.huge and value ~= -math.huge
end

local function check_file(pack_name, prefix, path, what)
  if type(path) ~= "string" or path:sub(1, #prefix) ~= prefix or path:find("..", 1, true) then
    fail(pack_name, what .. " must be a file inside the pack (" .. prefix .. "...)")
  end
end

local function check_layer(pack_name, prefix, anim_name, key, meta, info)
  local where = "animation \"" .. anim_name .. "\" layer \"" .. key .. "\""
  if type(info) ~= "table" then
    fail(pack_name, where .. " is not a table")
  end
  local limit = format.MAX_SHEET_SIZE
  if not is_int(info.width, 1, limit) or not is_int(info.height, 1, limit) then
    fail(pack_name, where .. " needs an integer width and height between 1 and " .. limit)
  end
  if meta.frame_count * info.width > limit and not (info.filenames and info.frames_per_file) then
    fail(pack_name, where .. " is wider than " .. limit .. " px (frame_count x width); split it with filenames + frames_per_file")
  end
  local shift = info.shift
  if type(shift) ~= "table" or not is_number(shift[1]) or not is_number(shift[2]) then
    fail(pack_name, where .. " needs a shift of two numbers")
  end
  if info.filenames ~= nil then
    if type(info.filenames) ~= "table" or #info.filenames == 0 then
      fail(pack_name, where .. " has an empty filenames list")
    end
    for i, path in ipairs(info.filenames) do
      check_file(pack_name, prefix, path, where .. " file " .. i)
    end
    if info.frames_per_file ~= nil then
      local per = info.frames_per_file
      if info.lines_per_file ~= nil then
        fail(pack_name, where .. " has both lines_per_file and frames_per_file; a layer is split one way only")
      end
      if not is_int(per, 1) or per * info.width > limit then
        fail(pack_name, where .. " needs frames_per_file between 1 and floor(" .. limit .. " / width)")
      end
      if meta.direction_count * info.height > limit then
        fail(pack_name, where .. " is taller than " .. limit .. " px (direction_count x height)")
      end
      if #info.filenames ~= math.ceil(meta.frame_count / per) then
        fail(pack_name, where .. " has " .. #info.filenames .. " files but needs "
          .. math.ceil(meta.frame_count / per) .. " for " .. meta.frame_count
          .. " frames at " .. per .. " per file")
      end
    else
      local lines = info.lines_per_file
      if not is_int(lines, 1) or lines * info.height > limit then
        fail(pack_name, where .. " needs lines_per_file between 1 and floor(" .. limit .. " / height)")
      end
      if #info.filenames ~= math.ceil(meta.direction_count / lines) then
        fail(pack_name, where .. " has " .. #info.filenames .. " files but needs "
          .. math.ceil(meta.direction_count / lines) .. " for " .. meta.direction_count
          .. " directions at " .. lines .. " per file")
      end
    end
  else
    check_file(pack_name, prefix, info.filename, where .. " filename")
    if meta.direction_count * info.height > limit then
      fail(pack_name, where .. " is taller than " .. limit .. " px; split it with filenames + lines_per_file")
    end
  end
end

local function check_animation(pack_name, prefix, name, meta, tintable)
  local where = "animation \"" .. name .. "\""
  if type(meta) ~= "table" then
    fail(pack_name, where .. " is not a table")
  end
  if meta.direction_count ~= format.DIRECTIONS[name] then
    fail(pack_name, where .. " must have " .. format.DIRECTIONS[name] .. " directions, not "
      .. tostring(meta.direction_count))
  end
  if not is_int(meta.frame_count, 1, format.MAX_FRAMES) then
    fail(pack_name, where .. " needs an integer frame_count between 1 and " .. format.MAX_FRAMES)
  end
  if meta.pixels_per_tile ~= nil and meta.pixels_per_tile ~= 64 and meta.pixels_per_tile ~= 32 then
    fail(pack_name, where .. " has pixels_per_tile " .. tostring(meta.pixels_per_tile) .. "; only 64 and 32 are supported")
  end
  if type(meta.layers) ~= "table" then
    fail(pack_name, where .. " has no layers")
  end
  local required = { "base", "shadow" }
  if tintable then
    required[#required + 1] = "mask"
  end
  for _, key in ipairs(required) do
    if meta.layers[key] == nil then
      fail(pack_name, where .. " is missing its \"" .. key .. "\" layer")
    end
    check_layer(pack_name, prefix, name, key, meta, meta.layers[key])
  end
  for _, key in ipairs(format.OPTIONAL_LAYERS[name] or {}) do
    if meta.layers[key] ~= nil then
      check_layer(pack_name, prefix, name, key, meta, meta.layers[key])
    end
  end
end

local function check_frames(pack_name, field, list, count)
  if type(list) ~= "table" or #list == 0 then
    fail(pack_name, "timing." .. field .. " must be a non-empty list of frame numbers")
  end
  for _, frame in ipairs(list) do
    if not is_int(frame, 1, count) then
      fail(pack_name, "timing." .. field .. " has frame " .. tostring(frame) .. " outside 1.." .. count)
    end
  end
end

local function merged_timing(pack_name, timing, animations)
  local defaults = format.DEFAULT_TIMING
  local frames = format.default_timing(animations["run"].frame_count, animations["mine"].frame_count)
  local result = {
    distance_per_frame = defaults.distance_per_frame,
    mine_impact_frame = frames.mine_impact_frame,
    left_foot_frames = frames.left_foot_frames,
    right_foot_frames = frames.right_foot_frames,
    animation_speed = {},
  }
  for name, speed in pairs(defaults.animation_speed) do
    result.animation_speed[name] = speed
  end
  -- a swing takes the same time however many frames it has: the default is for 12 frames
  result.animation_speed["mine"] = defaults.animation_speed["mine"] * animations["mine"].frame_count / 12
  if timing == nil then
    timing = {}
  elseif type(timing) ~= "table" then
    fail(pack_name, "timing must be a table")
  end
  if timing.distance_per_frame ~= nil then
    local d = timing.distance_per_frame
    if not is_number(d) or d <= 0 or d > 2 then
      fail(pack_name, "timing.distance_per_frame must be a number above 0 and at most 2")
    end
    result.distance_per_frame = d
  end
  if timing.mine_impact_frame ~= nil then
    result.mine_impact_frame = timing.mine_impact_frame
  end
  if not is_int(result.mine_impact_frame, 1, animations["mine"].frame_count) then
    fail(pack_name, "timing.mine_impact_frame must be a frame of the mine animation (1.."
      .. animations["mine"].frame_count .. ")")
  end
  local run_frames = animations["run"].frame_count
  for _, field in ipairs({ "left_foot_frames", "right_foot_frames" }) do
    if timing[field] ~= nil then
      result[field] = timing[field]
    end
    check_frames(pack_name, field, result[field], run_frames)
  end
  if timing.animation_speed ~= nil then
    if type(timing.animation_speed) ~= "table" then
      fail(pack_name, "timing.animation_speed must be a table")
    end
    for name, speed in pairs(timing.animation_speed) do
      if format.DIRECTIONS[name] == nil then
        fail(pack_name, "timing.animation_speed names an unknown animation \"" .. tostring(name) .. "\"")
      end
      if not is_number(speed) or speed <= 0 then
        fail(pack_name, "timing.animation_speed." .. name .. " must be a positive number")
      end
      result.animation_speed[name] = speed
    end
  end
  return result
end

-- Checks one mod-data prototype and returns the pack description used below.
local function validate(mod_data)
  local pack_name = mod_data.name
  local d = mod_data.data
  if type(d) ~= "table" then
    fail(pack_name, "has no data")
  end
  if not is_int(d.format_version, 1) then
    fail(pack_name, "has no valid format_version")
  end
  if d.format_version > format.SUPPORTED_VERSION then
    fail(pack_name, "uses avatar pack format " .. d.format_version
      .. ", but this version of VRC Avatars only supports up to " .. format.SUPPORTED_VERSION
      .. " (update VRC Avatars)")
  end
  local id = d.id
  if not format.valid_id(id) then
    fail(pack_name, "has an invalid id \"" .. tostring(id) .. "\" (lowercase letters, digits and -, "
      .. "at most " .. format.MAX_ID_LENGTH .. " characters, not starting with corpse-)")
  end
  local mod_name = format.MOD_PREFIX .. id
  if pack_name ~= mod_name then
    fail(pack_name, "must be named \"" .. mod_name .. "\" to match its id")
  end
  -- the mod holding its files: its own, or the avatar bundle it is part of
  local files_mod = mod_name
  if d.mod ~= nil then
    if type(d.mod) ~= "string" or d.mod:sub(1, #format.BUNDLE_PREFIX) ~= format.BUNDLE_PREFIX then
      fail(pack_name, "names the mod \"" .. tostring(d.mod) .. "\" for its files; only avatar bundles ("
        .. format.BUNDLE_PREFIX .. "<name>) hold other packs' files")
    end
    files_mod = d.mod
  end
  if mods[files_mod] == nil then
    fail(pack_name, "must come from a mod named \"" .. files_mod .. "\"")
  end
  local prefix = "__" .. files_mod .. "__/"
  local title_length = type(d.title) == "string" and format.utf8_length(d.title) or 0
  if title_length == 0 or title_length > format.MAX_TITLE_LENGTH then
    fail(pack_name, "needs a title of 1 to " .. format.MAX_TITLE_LENGTH .. " characters")
  end
  if d.author ~= nil and type(d.author) ~= "string" then
    fail(pack_name, "has an author that is not a string")
  end
  if type(d.icon) ~= "table" or not is_int(d.icon.size, 1, 512) then
    fail(pack_name, "needs an icon with a filename and a size")
  end
  check_file(pack_name, prefix, d.icon.filename, "icon")
  local ppt = d.pixels_per_tile or 64
  if ppt ~= 64 and ppt ~= 32 then
    fail(pack_name, "has pixels_per_tile " .. tostring(ppt) .. "; only 64 and 32 are supported")
  end
  for _, flag in ipairs({ "tintable", "flight" }) do
    if d[flag] ~= nil and type(d[flag]) ~= "boolean" then
      fail(pack_name, flag .. " must be true or false")
    end
  end
  local tintable = d.tintable == true
  local flight = d.flight == true
  local animations = d.animations
  if type(animations) ~= "table" then
    fail(pack_name, "has no animations")
  end
  for _, name in ipairs(format.REQUIRED) do
    if animations[name] == nil then
      fail(pack_name, "is missing the required \"" .. name .. "\" animation")
    end
    check_animation(pack_name, prefix, name, animations[name], tintable)
  end
  for _, name in ipairs(format.FLIGHT) do
    if flight and animations[name] == nil then
      fail(pack_name, "has flight = true but no \"" .. name .. "\" animation")
    elseif not flight and animations[name] ~= nil then
      fail(pack_name, "has a \"" .. name .. "\" animation but flight is not true")
    elseif flight then
      check_animation(pack_name, prefix, name, animations[name], tintable)
    end
  end
  return {
    id = id,
    mod_name = mod_name,
    title = d.title,
    author = d.author,
    icon = { filename = d.icon.filename, size = d.icon.size },
    scale = 32 / ppt,
    tintable = tintable,
    flight = flight,
    timing = merged_timing(pack_name, d.timing, animations),
    animations = animations,
  }
end

-- All installed avatar packs, validated, sorted by mod-data name.
function avatar_packs.collect()
  local names = {}
  for name, mod_data in pairs(data.raw["mod-data"] or {}) do
    if mod_data.data_type == format.DATA_TYPE then
      names[#names + 1] = name
    end
  end
  table.sort(names)
  local list = {}
  for _, name in ipairs(names) do
    list[#list + 1] = validate(data.raw["mod-data"][name])
  end
  return list
end

-- The animation-builder look of a validated pack.
function avatar_packs.look(pack)
  local layer_list = { { key = "base" } }
  if pack.tintable then
    layer_list[#layer_list + 1] = { key = "mask", extra = { apply_runtime_tint = true } }
  end
  layer_list[#layer_list + 1] = { key = "shadow", extra = { draw_as_shadow = true } }
  return {
    get_meta = function(name)
      return pack.animations[name]
    end,
    layer_list = layer_list,
    scale = pack.scale,
    speeds = pack.timing.animation_speed,
    mine_impact_frame = pack.timing.mine_impact_frame,
    label = "avatar pack " .. pack.id,
  }
end

return avatar_packs
