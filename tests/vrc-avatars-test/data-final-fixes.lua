-- Data stage checks. Runs after vrc-avatars' data-final-fixes (this mod
-- depends on it), so every character prototype exists by now.

local failures = {}

local function check(condition, message)
  if not condition then
    failures[#failures + 1] = message
  end
end

-- Returns nil when equal, otherwise the path of the first difference.
local function diff(a, b, path)
  if type(a) ~= type(b) then
    return path .. ": " .. type(a) .. " vs " .. type(b)
  end
  if type(a) ~= "table" then
    if a ~= b then
      return path .. ": " .. tostring(a) .. " vs " .. tostring(b)
    end
    return nil
  end
  for k, v in pairs(a) do
    local d = diff(v, b[k], path .. "." .. tostring(k))
    if d then
      return d
    end
  end
  for k, v in pairs(b) do
    if a[k] == nil then
      return path .. "." .. tostring(k) .. ": nil vs " .. type(v)
    end
  end
  return nil
end

local flight = false
for _, armor in pairs(data.raw["armor"] or {}) do
  if armor.provides_flight then
    flight = true
    break
  end
end

---------------------------------------------------------------------------
-- "Every armor flies (wings)" (prototypes/armor-flight.lua)
---------------------------------------------------------------------------

if settings.startup["vrc-avatars-armor-flight"].value and feature_flags["space_travel"] then
  for name, armor in pairs(data.raw["armor"]) do
    check(armor.provides_flight == true, "armor flight: " .. name .. " does not fly")
  end
  local light = data.raw["armor"]["light-armor"]
  local badge = light.icons and light.icons[#light.icons]
  check(light.icon == nil and badge and badge.icon == "__vrc-avatars__/graphics/icons/armor-feather.png"
    and light.icons[1].icon:find("light%-armor") ~= nil, "armor flight: no wings on the light armor icon")
  local mech = data.raw["armor"]["mech-armor"]
  if mech then
    check(mech.icon ~= nil, "armor flight: wings added to mech armor, which flew already")
  end
  for i, set in ipairs(data.raw["character"]["character"].animations) do
    for air, ground in pairs({ flying = "running", flying_with_gun = "running_with_gun", idle_in_air = "idle",
        idle_with_gun_in_air = "idle_with_gun", take_off = "idle_with_gun", landing = "idle_with_gun" }) do
      check(set[air] ~= nil or set[ground] == nil, "armor flight: engineer animation set " .. i .. " has no " .. air)
    end
  end
else
  -- off, or without Space Age, where Factorio allows no flying armor
  check(data.raw["armor"]["light-armor"].provides_flight ~= true, "light armor flies with the setting off")
end

---------------------------------------------------------------------------
-- The engineer is left as it is
---------------------------------------------------------------------------

do
  local engineer = data.raw["character"]["character"]
  local idle = engineer.animations[1].idle
  local first = idle.layers and idle.layers[1] or idle
  local file = first.filename or (first.filenames and first.filenames[1]) or ""
  check(file:sub(1, 8) == "__base__", "the engineer's idle animation is no longer the base game's: " .. file)
end

---------------------------------------------------------------------------
-- Prototypes built from the packs written by tools/avatar_pack/make_test_pack.py
---------------------------------------------------------------------------

local function layer_count(animation)
  return #animation.layers
end

-- --id test --flight --tint --max-sheet 1024: split sheets, mask layers, custom timing
if mods["vrc-avatar-test"] then
  local c = data.raw["character"]["vrc-avatar-test"]
  local corpse = data.raw["character-corpse"]["vrc-avatar-corpse-test"]
  check(c ~= nil, "pack test: no character prototype")
  check(corpse ~= nil, "pack test: no corpse prototype")
  check(data.raw["sprite"]["vrc-avatars-pack-test"] ~= nil, "pack test: no GUI sprite")
  if c and corpse then
    local anim = c.animations[1]
    local run_gun = anim.running_with_gun
    check(layer_count(run_gun) == 3, "pack test: run-gun should have base, mask and shadow")
    local base = run_gun.layers[1]
    check(base.filename == nil and base.filenames and #base.filenames == 3, "pack test: run-gun base not split in 3")
    check(base.lines_per_file == 8 and base.slice == 8, "pack test: run-gun lines_per_file/slice")
    check(base.direction_count == 18 and base.frame_count == 8 and base.line_length == 8, "pack test: run-gun counts")
    check(base.scale == 0.5, "pack test: scale should be 0.5 at 64 pixels per tile")
    check(anim.running.layers[1].scale == 1 and anim.running.layers[3].scale == 1,
      "pack test: run, drawn at 32 pixels per tile in a 64 pack, should have scale 1")
    check(run_gun.layers[2].apply_runtime_tint == true, "pack test: mask not tinted")
    check(run_gun.layers[3].draw_as_shadow == true, "pack test: shadow not drawn as shadow")
    check(anim.flipped_shadow_running_with_gun ~= nil, "pack test: no flipped shadow")
    local idle = anim.idle.layers[1]
    check(idle.filename == nil and idle.filenames == nil and idle.stripes and #idle.stripes == 2,
      "pack test: idle (16 frames) not split by frames into 2 stripes")
    check(idle.stripes and idle.stripes[2].filename == "__vrc-avatar-test__/graphics/idle-base-2.png"
      and idle.stripes[1].width_in_frames + idle.stripes[2].width_in_frames == 16
      and idle.stripes[1].height_in_frames == 8 and idle.line_length == nil and idle.frame_count == 16,
      "pack test: idle stripes")
    check(anim.idle_with_gun.layers[1].filename == "__vrc-avatar-test__/graphics/idle-gun-base.png",
      "pack test: idle-gun filename")
    check(anim.idle.layers[1].animation_speed == 0.08, "pack test: idle speed override")
    check(anim.running.layers[1].animation_speed == 0.5, "pack test: run speed override")
    check(math.abs(anim.mining_with_tool.layers[1].animation_speed - 0.415 * 10 / 12) < 1e-9,
      "pack test: default mine speed, scaled to its 10 frames")
    check(c.distance_per_frame == 0.15, "pack test: distance_per_frame")
    -- the sprinting character: the same but for its run
    local s = data.raw["character"]["vrc-avatar-sprint-test"]
    check(s ~= nil, "pack test: no sprinting character")
    if s then
      check(s.distance_per_frame == 0.33, "pack test: sprint distance_per_frame")
      check(s.animations[1].running.layers[1].filename == "__vrc-avatar-test__/graphics/sprint-base.png",
        "pack test: the sprinting character runs with the sprint")
      check(diff(anim.running_with_gun, s.animations[1].running_with_gun, "") == nil
        and diff(anim.idle, s.animations[1].idle, "") == nil, "pack test: sprinting character's other animations")
      check(s.character_corpse == "vrc-avatar-corpse-test", "pack test: sprint corpse link")
      check(diff(c.running_sound_animation_positions, s.running_sound_animation_positions, "") == nil,
        "pack test: sprint footsteps")
    end
    check(c.mining_with_tool_particles_animation_positions[1] == 6, "pack test: mine impact frame")
    check(diff({ 1, 5 }, c.running_sound_animation_positions, "") == nil
      and diff({ 1, 5 }, c.moving_sound_animation_positions, "") == nil,
      "pack test: a footstep sound on both feet's frames")
    check(c.left_footprint_frames[1] == 1 and c.right_footprint_frames[1] == 5, "pack test: footprint frames")
    check(c.character_corpse == "vrc-avatar-corpse-test", "pack test: corpse link")
    check(c.icon == "__vrc-avatar-test__/graphics/icon.png", "pack test: icon")
    check(c.inventory_size == data.raw["character"]["character"].inventory_size, "pack test: inventory size")
    check(layer_count(corpse.pictures[1]) == 3, "pack test: corpse layers")
    if flight then
      check(anim.flying_with_gun and #anim.flying_with_gun.layers[1].filenames == 3, "pack test: fly-gun split")
      check(anim.take_off.layers[1].filename == "__vrc-avatar-test__/graphics/take-off-base.png",
        "pack test: own take-off")
      check(diff({ 5, 4, 3, 2, 1 }, anim.landing.layers[1].frame_sequence, "") == nil, "pack test: landing reversed")
    else
      check(anim.flying == nil, "pack test: flight animations without flying armor")
    end
  end
end

-- --id small --ppt 32 --default-timing --frames run=6,mine=8: no flight, no
-- tint, 32 pixels per tile, timing defaults scaled to its frame counts
if mods["vrc-avatar-small"] then
  local c = data.raw["character"]["vrc-avatar-small"]
  check(c ~= nil, "pack small: no character prototype")
  if c then
    local anim = c.animations[1]
    check(layer_count(anim.idle) == 2, "pack small: idle should have base and shadow")
    check(anim.idle.layers[1].scale == 1, "pack small: scale should be 1 at 32 pixels per tile")
    check(c.distance_per_frame == 0.238, "pack small: default distance_per_frame")
    check(data.raw["character"]["vrc-avatar-sprint-small"] == nil, "pack small: a sprinting character without a sprint")
    check(c.mining_with_tool_particles_animation_positions[1] == 6, "pack small: scaled mine impact frame")
    check(diff({ 1 }, c.left_footprint_frames, "") == nil, "pack small: default left foot frames")
    check(diff({ 4 }, c.right_footprint_frames, "") == nil, "pack small: scaled right foot frames")
    check(diff({ 1, 4 }, c.running_sound_animation_positions, "") == nil, "pack small: footsteps on both feet")
    if flight then
      -- no flight animations: the ground ones are shown in the air
      check(diff(anim.running, anim.flying, "") == nil, "pack small: flying should fall back to running")
      check(diff(anim.running_with_gun, anim.flying_with_gun, "") == nil, "pack small: fly-gun fallback")
      check(diff(anim.idle, anim.idle_in_air, "") == nil, "pack small: hover fallback")
      check(diff(anim.idle, anim.landing, "") == nil and anim.landing.layers[1].frame_sequence == nil,
        "pack small: landing fallback")
    end
  end
end

-- characters of avatar bundles draw their sheets from the bundle's characters/<id>/ folder
for name, mod_data in pairs(data.raw["mod-data"] or {}) do
  local d = mod_data.data_type == "vrc-avatars.avatar-pack" and mod_data.data or nil
  if d and d.mod then
    local folder = "__" .. d.mod .. "__/characters/" .. d.id .. "/"
    local character = data.raw["character"][name]
    local idle = character and character.animations[1].idle.layers[1]
    local file = idle and (idle.filename or (idle.filenames or {})[1] or ((idle.stripes or {})[1] or {}).filename) or ""
    check(file:sub(1, #folder) == folder, "bundled pack " .. name .. " draws " .. file .. ", not from " .. folder)
    check(character and character.icon:sub(1, #folder) == folder, "bundled pack " .. name .. ": icon not in its bundle")
  end
end

if #failures > 0 then
  error("AVATAR-TEST: FAIL (data stage)\n" .. table.concat(failures, "\n"))
end
log("AVATAR-TEST: data stage checks passed")
