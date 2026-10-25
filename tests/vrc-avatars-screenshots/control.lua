-- In-game screenshots of the engineer and every installed avatar pack, side by side.
-- Run with tests/run_ingame_checks.ps1 (Windows, the real game); see tests/INGAME-CHECKS.md.
--
-- The save is made with --create (on_init builds a flat stage on its own surface); when the game
-- loads it, the scenes below play one after the other and take screenshots every few ticks,
-- following the characters as they move.
--
-- Walking, flying and dying work on any character, so those scenes put one character per subject
-- in its own lane and drive them all at once. Shooting and mining only happen when a player
-- controls the character, so those scenes run once per subject, with the game's player taking
-- control of it; tools/ingame_contact_sheet.py lines the runs up afterwards.
--
-- Output, in script-output/avatar-checks/: <scene>/<scene>[-<subject>]-<n>.png, manifest.json
-- (scenes, runs, frames, and what happened: distance walked, shots fired, ore mined, flight) and
-- done.txt when finished. A headless server takes no screenshots and has no player, but plays the
-- other scenes, so the scripted moves can be checked without graphics.

local SURFACE = "avatar-checks"
local OUT = "avatar-checks/"
local PIXELS_PER_TILE = 64 -- zoom 2 shows the sprites at their own resolution
local LANE = 3.5 -- tiles between subjects
local VIEW_ALONG = 5 -- tiles of view along the direction of travel
local START_DELAY = 60
local TARGET_DISTANCE = 16 -- tiles from a shooting subject: in the gun's range, out of the picture
local FLIGHT_ROOM = 3 -- extra tiles of view above the lanes in flight scenes, for the flight height

local D = defines.direction

-- Scenes, in order. `move`: walking direction. `face`: standing subjects face this way (they walk
-- one tick that way, then stop). Screenshots every `every` ticks from tick `from` to `length`;
-- 2-tick steps give GIFs at the game's real speed (30 fps). `solo`: needs the player, one run per
-- subject. `gun`: shoots at a target in direction `aim` (default north). `fly`: wears mech armor and
-- walks onto a lake that starts `water_from` tiles ahead and is `water_width` tiles wide (default
-- 40, more than the scene covers); `stop_at`: stops walking at that tick.
local SCENES = {
  { name = "idle-south", face = D.south, length = 50, from = 40, every = 10 },
  { name = "run-east", move = D.east, length = 100, from = 40, every = 2 },
  { name = "run-south", move = D.south, length = 90, from = 40, every = 2 },
  { name = "run-gun-east-aim-north", move = D.east, gun = true, solo = true, length = 100, from = 40, every = 2 },
  { name = "run-gun-west-aim-north", move = D.west, gun = true, solo = true, length = 100, from = 40, every = 2 },
  -- the gun in view: aimed the way the subject runs, and across it
  { name = "run-gun-east-aim-east", move = D.east, aim = D.east, gun = true, solo = true, length = 100, from = 40, every = 2 },
  { name = "run-gun-east-aim-south", move = D.east, aim = D.south, gun = true, solo = true, length = 100, from = 40, every = 2 },
  -- aimed west the game mirrors the sprite and draws flipped_shadow_running_with_gun
  { name = "run-gun-north-aim-west", move = D.north, aim = D.west, gun = true, solo = true, length = 100, from = 40, every = 2 },
  -- one ore takes 120 ticks at the engineer's mining speed
  { name = "mine-east", face = D.east, mine = true, solo = true, length = 160, from = 100, every = 2 },
  { name = "fly-east", move = D.east, fly = true, water_from = 4, stop_at = 130, length = 190, from = 20, every = 3 },
  -- across a narrow lake: take off, fly over it and land on the far side
  { name = "fly-land-east", move = D.east, fly = true, water_from = 4, water_width = 7, length = 190, from = 20, every = 3 },
  -- flying while shooting (flying_with_gun, then hovering with the gun once it stops): the gun aimed
  -- across the flight and along it. Shooting slows everyone to 30%, so the lake starts a tile ahead
  { name = "fly-gun-east-aim-north", move = D.east, aim = D.north, fly = true, gun = true, solo = true, water_from = 1,
    stop_at = 160, length = 190, from = 20, every = 3 },
  { name = "fly-gun-east-aim-east", move = D.east, aim = D.east, fly = true, gun = true, solo = true, water_from = 1,
    stop_at = 160, length = 190, from = 20, every = 3 },
  { name = "corpse", face = D.south, die = 10, length = 40, from = 39, every = 10 },
}

local DIRECTION_VECTORS = {
  [D.north] = { 0, -1 }, [D.east] = { 1, 0 }, [D.south] = { 0, 1 }, [D.west] = { -1, 0 },
}
local DIRECTION_NAMES = { [D.north] = "north", [D.east] = "east", [D.south] = "south", [D.west] = "west" }

-- the engineer, then every avatar pack character
local function subjects()
  local list = { { label = "engineer", name = "character" } }
  local prefix = "vrc-avatar-"
  local names = {}
  for name, proto in pairs(prototypes.entity) do
    if proto.type == "character" and name:sub(1, #prefix) == prefix then
      names[#names + 1] = name
    end
  end
  table.sort(names)
  for _, name in ipairs(names) do
    list[#list + 1] = { label = name:sub(#prefix + 1), name = name }
  end
  return list
end

local function the_player()
  for _, player in pairs(game.players) do
    if player.connected then
      return player
    end
  end
  return nil
end

local function set_tiles(surface, name, x1, y1, x2, y2)
  local tiles = {}
  for x = x1, x2 - 1 do
    for y = y1, y2 - 1 do
      tiles[#tiles + 1] = { name = name, position = { x, y } }
    end
  end
  surface.set_tiles(tiles, true, false, true, false)
end

local function clear_stage(surface)
  local player = the_player()
  if player and player.character and player.character.surface == surface then
    player.set_controller({ type = defines.controllers.spectator })
  end
  for _, entity in pairs(surface.find_entities_filtered({ area = { { -96, -96 }, { 96, 96 } } })) do
    if entity.valid then
      entity.destroy()
    end
  end
  set_tiles(surface, "lab-dark-2", -96, -96, 96, 96)
end

local function build_stage()
  -- an empty world: no trees, rocks, ores or enemy bases
  local surface = game.surfaces[SURFACE] or game.create_surface(SURFACE, {
    default_enable_all_autoplace_controls = false,
    autoplace_settings = {
      entity = { treat_missing_as_default = false, settings = {} },
      decorative = { treat_missing_as_default = false, settings = {} },
    },
    no_enemies_mode = true,
  })
  surface.always_day = true
  surface.request_to_generate_chunks({ 0, 0 }, 4)
  surface.force_generate_chunk_requests()
  clear_stage(surface)
end

-- The runs to play: one per scene, or one per subject for solo scenes.
local function plan_runs()
  local runs = {}
  local list = subjects()
  for _, scene in ipairs(SCENES) do
    if scene.solo then
      for _, subject in ipairs(list) do
        runs[#runs + 1] = { scene = scene, subjects = { subject }, tag = subject.label }
      end
    else
      runs[#runs + 1] = { scene = scene, subjects = list }
    end
  end
  return runs
end

-- the subjects stand in lanes across the direction of travel, centred on the origin
local function lane_offset(index, count, along)
  local offset = (index - (count + 1) / 2) * LANE
  if along[1] ~= 0 then
    return { 0, offset }
  end
  return { offset, 0 }
end

-- where a shooting subject at `position` aims: TARGET_DISTANCE tiles in the scene's `aim` direction
local function target_position(scene, position)
  local aim = DIRECTION_VECTORS[scene.aim or D.north]
  local x = position.x or position[1]
  local y = position.y or position[2]
  return { x + aim[1] * TARGET_DISTANCE, y + aim[2] * TARGET_DISTANCE }
end

local function setup(run)
  local scene = run.scene
  local surface = game.surfaces[SURFACE]
  clear_stage(surface)
  local along = DIRECTION_VECTORS[scene.move or scene.face]
  local back = scene.move and -12 or 0 -- walkers start behind the origin so they cross the stage
  local state = { run = run, along = along, entries = {}, tick = 0, frames = {}, notes = {} }
  if scene.solo then
    state.player = the_player()
    if not state.player then
      state.notes[#state.notes + 1] = "skipped: shooting and mining need the game's player (not in a headless benchmark)"
      state.skipped = true
      return state
    end
    -- the player can only take control of a character on its own surface; its own character stays
    -- behind on the starting surface, out of the pictures
    if state.player.surface ~= surface then
      state.player.set_controller({ type = defines.controllers.spectator })
      state.player.teleport({ 0, 0 }, surface)
    end
  end
  if scene.fly then
    -- a lake ahead of the start line (fly scenes go east): walking onto it with flying armor takes off
    local from = back + scene.water_from
    set_tiles(surface, "water", from, -40, from + (scene.water_width or 40), 40)
    if not prototypes.item["mech-armor"] then
      state.notes[#state.notes + 1] = "no mech-armor (Space Age not enabled): the subjects stop at the water"
    end
  end
  for i, subject in ipairs(run.subjects) do
    local lane = lane_offset(i, #run.subjects, along)
    local position = { lane[1] + along[1] * back, lane[2] + along[2] * back }
    local character = surface.create_entity({ name = subject.name, position = position, force = "player" })
    local entry = { label = subject.label, name = subject.name, character = character, start = position }
    if scene.gun then
      character.get_inventory(defines.inventory.character_guns).insert({ name = "submachine-gun" })
      character.get_inventory(defines.inventory.character_ammo).insert({ name = "firearm-magazine", count = 50 })
      -- something to shoot at, far enough away to stay out of the screenshots (characters don't
      -- shoot at indestructible entities, so it is healed every tick instead)
      entry.target = surface.create_entity({
        name = "character", position = target_position(scene, position), force = "enemy",
      })
    end
    if scene.fly and prototypes.item["mech-armor"] then
      character.get_inventory(defines.inventory.character_armor).insert({ name = "mech-armor" })
    end
    if scene.mine then
      entry.ore = surface.create_entity({
        name = "iron-ore", position = { position[1] + along[1] * 1.5, position[2] + along[2] * 1.5 }, amount = 10000,
      })
    end
    if state.player then
      state.player.set_controller({ type = defines.controllers.character, character = character })
    end
    state.entries[i] = entry
  end
  return state
end

local function drive(state)
  local scene = state.run.scene
  for _, s in ipairs(state.entries) do
    local c = s.character
    if c.valid then
      -- a player's input replaces the character's own each tick, so a controlled character is driven
      -- through the player
      local control = state.player or c
      if scene.move and not (scene.stop_at and state.tick >= scene.stop_at) then
        control.walking_state = { walking = true, direction = scene.move }
      elseif scene.face and state.tick == 0 then
        control.walking_state = { walking = true, direction = scene.face }
      else
        control.walking_state = { walking = false, direction = scene.move or scene.face }
      end
      if scene.gun and s.target.valid then
        s.target.teleport(target_position(scene, c.position))
        s.target.health = s.target.max_health
        -- aim at the nearest enemy, the target: selecting it does not work while it is off screen
        control.shooting_state = { state = defines.shooting.shooting_enemies, position = s.target.position }
      end
      if scene.mine and state.tick > 2 and s.ore and s.ore.valid then
        control.selected = s.ore
        -- set once: setting it again may start the ore over
        if not control.mining_state.mining then
          control.mining_state = { mining = true, position = s.ore.position }
        end
      end
      if scene.fly and c.is_flying then
        s.flew = true
      elseif s.flew and not s.landed_at then
        s.landed_at = state.tick
      end
      if scene.die and state.tick == scene.die then
        s.died_at = c.position
        c.die()
      end
    end
  end
end

-- the view follows the first subject along the direction of travel and spans every lane
local function screenshot(state)
  local run = state.run
  local scene = run.scene
  local first = state.entries[1]
  local anchor = first.character.valid and first.character.position or first.died_at
  local along = state.along
  local across = #state.entries * LANE + 1
  -- a flying character is drawn higher up the screen: more view above, the lanes stay put
  local room = scene.fly and FLIGHT_ROOM or 0
  local position, size
  if along[1] ~= 0 then
    position = { anchor.x, -0.75 - room / 2 }
    size = { VIEW_ALONG, across + room }
  else
    position = { 0, anchor.y - 0.75 - room / 2 }
    size = { across, VIEW_ALONG + room }
  end
  local n = #state.frames + 1
  local file = string.format("%s/%s%s-%03d.png", scene.name, scene.name, run.tag and ("-" .. run.tag) or "", n)
  game.take_screenshot({
    surface = SURFACE,
    position = position,
    resolution = { math.floor(size[1] * PIXELS_PER_TILE), math.floor(size[2] * PIXELS_PER_TILE) },
    zoom = PIXELS_PER_TILE / 32,
    path = OUT .. file,
    show_entity_info = false,
    hide_clouds = true,
    hide_fog = true,
    daytime = 0,
    force_render = true,
  })
  state.frames[n] = { file = file, tick = state.tick }
end

local function summary(state)
  local scene = state.run.scene
  local result = {
    scene = scene.name, every = scene.every, solo = scene.solo or false, notes = state.notes,
    frames = state.frames, subjects = {},
    -- for laying out the lanes: the direction of travel and the extra view above them, in tiles
    travel = DIRECTION_NAMES[scene.move or scene.face], room = scene.fly and FLIGHT_ROOM or 0,
  }
  for i, s in ipairs(state.entries) do
    local info = { label = s.label, prototype = s.name }
    local c = s.character
    if c.valid then
      local p = c.position
      info.moved = math.floor(math.sqrt((p.x - s.start[1]) ^ 2 + (p.y - s.start[2]) ^ 2) * 100 + 0.5) / 100
      if s.target then
        local ammo = c.get_inventory(defines.inventory.character_ammo)[1]
        info.shots_fired = 500 - (ammo.valid_for_read and ((ammo.count - 1) * 10 + ammo.ammo) or 0)
      end
    end
    if s.ore then
      info.ore_mined = s.ore.valid and (10000 - s.ore.amount) or "all"
    end
    if scene.fly then
      info.flew = s.flew or false
      if scene.water_width then
        -- a lake with a far side: the subject should be back on the ground there
        info.landed = s.landed_at ~= nil
        info.landed_at_tick = s.landed_at
      end
    end
    if s.died_at then
      info.corpse = #game.surfaces[SURFACE].find_entities_filtered({
        type = "character-corpse", position = s.died_at, radius = 1,
      }) > 0
    end
    result.subjects[i] = info
  end
  if state.skipped then
    result.subjects = { { label = state.run.tag } }
  end
  return result
end

-- The player as the first avatar pack (or the engineer), with the avatar window open, for one
-- screenshot with the GUI. Returns what happened, for the manifest.
local function window_shot()
  local player = the_player()
  if not player then
    return { skipped = "no player" }
  end
  local surface = game.surfaces[SURFACE]
  local character = surface.create_entity({ name = "character", position = { 0, 0 }, force = "player" })
  player.set_controller({ type = defines.controllers.character, character = character })
  local packs = remote.call("vrc-avatars", "get_avatar_packs")
  local status = nil
  if packs[1] then
    status = remote.call("vrc-avatars", "set_appearance", player.index, { avatar = packs[1].id })
  end
  remote.call("vrc-avatars", "open_window", player.index)
  -- the player's own screen size: the GUI is laid out for it, so a smaller picture cuts the window off
  local screen = player.display_resolution
  game.take_screenshot({
    player = player, by_player = player, surface = surface, position = { 0, -1 },
    resolution = { screen.width, screen.height }, zoom = 1, path = OUT .. "window/window.png", show_gui = true, daytime = 0,
  })
  return {
    pack = packs[1] and packs[1].id or nil,
    status = status or "applied",
    character = player.character and player.character.name or nil,
    window_open = player.gui.screen["avatar_characters_frame"] ~= nil,
  }
end

local function finish()
  storage.finished = true
  clear_stage(game.surfaces[SURFACE])
  helpers.write_file(OUT .. "manifest.json", helpers.table_to_json({
    pixels_per_tile = PIXELS_PER_TILE, lane_tiles = LANE, runs = storage.results, mods = script.active_mods,
    window = storage.window,
  }))
  helpers.write_file(OUT .. "done.txt", "done at tick " .. game.tick .. "\n")
  log("AVATAR-CHECKS: DONE")
end

script.on_init(function()
  -- no intro cutscene: vrc-avatars re-applies the player's look after one, which would undo
  -- the characters the player takes control of
  if remote.interfaces["freeplay"] then
    remote.call("freeplay", "set_skip_intro", true)
    remote.call("freeplay", "set_disable_crashsite", true)
  end
  build_stage()
  storage.wait = START_DELAY
  storage.results = {}
end)

script.on_event(defines.events.on_tick, function()
  if storage.finished then
    return
  end
  if storage.wait > 0 then
    storage.wait = storage.wait - 1
    return
  end
  -- the runs are planned once the game runs, when its player exists
  storage.runs = storage.runs or plan_runs()
  storage.next_run = storage.next_run or 1
  local state = storage.state
  if not state then
    local run = storage.runs[storage.next_run]
    if not run and not storage.window then
      clear_stage(game.surfaces[SURFACE])
      storage.window = window_shot()
      storage.wait = 30 -- lets the screenshot be written before the game is closed
      return
    end
    if not run then
      finish()
      return
    end
    storage.next_run = storage.next_run + 1
    storage.state = setup(run)
    return
  end
  local scene = state.run.scene
  if not state.skipped then
    drive(state)
    if state.tick >= scene.from and (state.tick - scene.from) % scene.every == 0 then
      screenshot(state)
    end
  end
  state.tick = state.tick + 1
  if state.skipped or state.tick > scene.length then
    storage.results[#storage.results + 1] = summary(state)
    storage.state = nil
    storage.wait = 10 -- lets the last screenshots finish before the stage is rebuilt
  end
end)
