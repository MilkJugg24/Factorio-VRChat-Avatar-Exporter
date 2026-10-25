-- Headless test for vrc-avatars' character swap logic and avatar packs.
-- Run with tests/run_headless.sh; prints "AVATAR-TEST: PASS" or errors.
--
-- With the synthetic packs of tests/run_headless.sh --test-packs, the save is
-- made in three phases: on_init (during --create) leaves a pack corpse and an
-- unowned pack character and reverts the "test" pack; the first run checks
-- everything with the packs installed; the second run, after the packs were
-- removed, checks that the reverted corpse survived with its items.

local swap = require("__vrc-avatars__/scripts/swap")
local appearance = require("__vrc-avatars__/scripts/appearance")
local revert = require("__vrc-avatars__/scripts/revert")
local pack_list = require("__vrc-avatars__/scripts/packs")

local FIXTURE_CORPSE_POSITION = { 10, 10 }
local FIXTURE_CHARACTER_POSITION = { -10, -10 }

local failures = {}

local function check(condition, message)
  if not condition then
    failures[#failures + 1] = message
  end
end

local function count_items(entity)
  local counts = {}
  for index = 1, entity.get_max_inventory_index() do
    local inventory = entity.get_inventory(index)
    if inventory then
      for _, item in pairs(inventory.get_contents()) do
        counts[item.name] = (counts[item.name] or 0) + item.count
      end
    end
  end
  return counts
end

local function same_counts(a, b)
  for name, count in pairs(a) do
    if b[name] ~= count then
      return false, name .. ": " .. tostring(count) .. " vs " .. tostring(b[name])
    end
  end
  for name, count in pairs(b) do
    if a[name] ~= count then
      return false, name .. ": " .. tostring(a[name]) .. " vs " .. tostring(count)
    end
  end
  return true
end

local function test_prototypes()
  check(prototypes.shortcut["vrc-avatars-choose"] ~= nil, "missing shortcut")
  check(prototypes.custom_input["vrc-avatars-toggle-gui"] ~= nil, "missing hotkey")
  check(helpers.is_valid_sprite_path("vrc-avatars-icon"), "missing window icon")
  check(helpers.is_valid_sprite_path("entity/character"), "the engineer's look button has no sprite")
end

-- Creates a character carrying a bit of everything a swap has to keep.
-- Returns it and what to compare after the swap.
local function equipped_character(surface, force, name)
  force.character_logistic_requests = true
  force.character_trash_slot_count = 10

  local old = surface.create_entity({ name = name, position = { 0, 0 }, force = force })
  old.character_inventory_slots_bonus = 5
  old.character_running_speed_modifier = 0.25
  old.character_health_bonus = 50

  local armor = old.get_inventory(defines.inventory.character_armor)
  armor.insert({ name = "power-armor-mk2" })
  local grid = armor[1].grid
  grid.put({ name = "fission-reactor-equipment" })
  grid.put({ name = "personal-roboport-equipment" })
  grid.put({ name = "battery-mk2-equipment" })
  local equipment_before = #grid.equipment

  old.get_inventory(defines.inventory.character_guns).insert({ name = "submachine-gun" })
  old.get_inventory(defines.inventory.character_guns).insert({ name = "pistol" })
  old.get_inventory(defines.inventory.character_ammo).insert({ name = "firearm-magazine", count = 200 })
  old.get_inventory(defines.inventory.character_ammo).insert({ name = "piercing-rounds-magazine", count = 50 })
  old.selected_gun_index = 2

  -- fill every main slot, including the ones that only exist thanks to the armor
  local main = old.get_main_inventory()
  main.set_filter(3, "copper-plate")
  for i = 1, #main do
    if i == 3 then
      main[i].set_stack({ name = "copper-plate", count = 100 })
    else
      main[i].set_stack({ name = "iron-plate", count = 100 })
    end
  end
  local main_size = #main
  old.get_inventory(defines.inventory.character_trash).insert({ name = "stone", count = 50 })

  local point = old.get_requester_point()
  local section = point.get_section(1) or point.add_section()
  section.set_slot(1, { value = "iron-gear-wheel", min = 50 })
  point.add_section("avatar-test-group")
  local sections_before = point.sections_count

  old.health = 120
  return old, {
    items = count_items(old),
    equipment = equipment_before,
    main_size = main_size,
    sections = sections_before,
  }
end

-- Moves everything from `old` into a new `name` character and checks it all arrived.
-- Destroys `old` and returns the new character.
local function transfer_and_check(old, expected, name)
  local surface = old.surface
  local new = surface.create_entity({ name = name, position = old.position, force = old.force })
  swap.transfer(old, new)
  local label = old.name .. " -> " .. name .. ": "
  local ok, diff = same_counts(expected.items, count_items(new))
  check(ok, label .. "item counts differ after transfer: " .. tostring(diff))
  check(next(count_items(old)) == nil, label .. "old character still holds items")
  check(#surface.find_entities_filtered({ type = "item-entity" }) == 0, label .. "items were spilled on the ground")
  local new_armor = new.get_inventory(defines.inventory.character_armor)[1]
  check(new_armor.valid_for_read and new_armor.name == "power-armor-mk2", label .. "armor not moved")
  check(new_armor.valid_for_read and #new_armor.grid.equipment == expected.equipment, label .. "equipment grid not kept")
  check(#new.get_main_inventory() == expected.main_size,
    label .. "main inventory size changed: " .. #new.get_main_inventory() .. " vs " .. expected.main_size)
  local filter = new.get_main_inventory().get_filter(3)
  check(filter and filter.name == "copper-plate", label .. "inventory filter not copied")
  check(new.character_inventory_slots_bonus == 5, label .. "inventory slot bonus not copied")
  check(math.abs(new.character_running_speed_modifier - 0.25) < 1e-6, label .. "running speed bonus not copied")
  check(new.character_health_bonus == 50, label .. "health bonus not copied")
  check(math.abs(new.health - 120) < 1e-6, label .. "health not copied: " .. new.health)
  check(new.selected_gun_index == 2, label .. "selected gun not copied")
  local new_point = new.get_requester_point()
  check(new_point.sections_count == expected.sections,
    label .. "logistic sections: " .. new_point.sections_count .. " vs " .. expected.sections)
  local slot = new_point.get_section(1).get_slot(1)
  check(slot and slot.value and slot.value.name == "iron-gear-wheel" and slot.min == 50,
    label .. "logistic request not copied")
  local group_found = false
  for i = 1, new_point.sections_count do
    if new_point.get_section(i).group == "avatar-test-group" then
      group_found = true
    end
  end
  check(group_found, label .. "logistic group section not copied")
  old.destroy()
  return new
end

local function test_transfer(surface, force)
  local old, expected = equipped_character(surface, force, "character")
  transfer_and_check(old, expected, "character").destroy()
end

local function test_revert_argument()
  local function known(id)
    return id == "mika" or id == "vrc-avatar-x"
  end
  local cases = {
    { nil, true, nil },
    { "   ", true, nil },
    { "mika", true, "mika" },
    { "  mika ", true, "mika" },
    { "vrc-avatar-mika", true, "mika" },
    { "vrc-avatar-x", true, "vrc-avatar-x" },
    { "vrc-avatar-corpse-mika", false, nil },
    { "mika now", false, nil },
    { "other", false, nil },
  }
  for _, case in ipairs(cases) do
    local ok, id = revert.parse_pack_argument(case[1], known)
    check(ok == case[2] and id == case[3], "parse_pack_argument(" .. tostring(case[1]) .. "): "
      .. tostring(ok) .. ", " .. tostring(id))
  end
end

local function test_corpse(surface, force)
  local corpse = surface.create_entity({
    name = "character-corpse",
    position = { 5, 5 },
    force = force,
    inventory_size = 5,
  })
  corpse.get_inventory(defines.inventory.character_corpse).insert({ name = "iron-plate", count = 42 })
  local new = swap.replace_corpse(corpse, "character-corpse")
  check(new and new.valid and new.name == "character-corpse", "corpse not replaced")
  check(not corpse.valid, "old corpse still exists")
  check(new and new.get_inventory(defines.inventory.character_corpse).get_item_count("iron-plate") == 42,
    "corpse items not kept")
end

local function call(name, ...)
  return remote.call("vrc-avatars", name, ...)
end

local function corpse_at(surface, position)
  return surface.find_entities_filtered({ type = "character-corpse", position = position, radius = 0.5 })[1]
end

local function create_corpse(surface, name, position, items)
  local corpse = surface.create_entity({ name = name, position = position, force = "player", inventory_size = 5 })
  corpse.get_inventory(defines.inventory.character_corpse).insert({ name = "iron-plate", count = items })
  return corpse
end

-- Every installed pack, whatever made it.
local function test_any_packs(surface, force)
  local packs = call("get_avatar_packs")
  for _, pack in ipairs(packs) do
    local label = "pack " .. pack.id .. ": "
    local character = prototypes.entity[pack.character]
    check(character and character.type == "character", label .. "no character prototype")
    local corpse = prototypes.entity[pack.corpse]
    check(corpse and corpse.type == "character-corpse", label .. "no corpse prototype")
    check(helpers.is_valid_sprite_path("vrc-avatars-pack-" .. pack.id), label .. "no GUI sprite")
    check(call("is_avatar_character", pack.character) == pack.id, label .. "is_avatar_character")
    check(call("is_avatar_character", pack.corpse) == nil, label .. "a corpse is not a character")
    check(pack_list.id_of_character(pack.character) == pack.id, label .. "id_of_character")
    check(pack_list.id_of_corpse(pack.corpse) == pack.id, label .. "id_of_corpse")
    -- copies other mods make of every character (e.g. a jetpack mod's) are not the pack's
    check(call("is_avatar_character", pack.character .. "-jetpack") == nil, label .. "suffixed copy is foreign")
    local resolved, note = appearance.resolve({ avatar = pack.id },
      function(name) return prototypes.entity[name] ~= nil end)
    check(resolved == pack.character and note == nil, label .. "resolve")
  end
  -- engineer -> every pack in turn -> engineer, keeping everything
  if #packs > 0 then
    local character, expected = equipped_character(surface, force, "character")
    for _, pack in ipairs(packs) do
      character = transfer_and_check(character, expected, pack.character)
    end
    transfer_and_check(character, expected, "character").destroy()
  end
  return packs
end

-- The packs written by tests/run_headless.sh --test-packs.
local function test_generated_packs(surface, force, packs)
  local by_id = {}
  local index = {}
  for i, pack in ipairs(packs) do
    by_id[pack.id] = pack
    index[pack.id] = i
  end
  local test, small = by_id["test"], by_id["small"]
  check(test and small, "the test packs are not both listed")
  if not (test and small) then
    return
  end
  -- "small" has a non-ASCII title, which sorts after "Test Avatar" (other packs
  -- added with --pack may come anywhere in between)
  check(index.test < index.small, "packs not sorted by title")
  check(test.title == "Test Avatar" and test.author == "make_test_pack", "pack test: title/author")
  check(small.title == string.rep("竜", 30), "pack small: UTF-8 title")
  check(test.tintable == true and small.tintable == false, "tintable flags")
  check(test.character == "vrc-avatar-test" and test.corpse == "vrc-avatar-corpse-test", "pack test: names")

  -- a missing pack falls back to the engineer; no choice is the engineer
  local function exists(name) return prototypes.entity[name] ~= nil end
  local resolved, note = appearance.resolve({ avatar = "gone" }, exists)
  check(resolved == "character" and note == "avatar-missing", "resolve of a missing pack")
  resolved, note = appearance.resolve({}, exists)
  check(resolved == "character" and note == nil, "resolve of no choice")
  check(call("is_avatar_character", "some-other-mod-character") == nil, "foreign character is not ours")
  check(call("is_avatar_character", "character") == nil, "the engineer is not a pack's")

  -- reverting one pack converts only its corpses and counts its remaining characters
  local test_corpse = create_corpse(surface, "vrc-avatar-corpse-test", { 20, 20 }, 7)
  local small_corpse = create_corpse(surface, "vrc-avatar-corpse-small", { 22, 20 }, 9)
  local engineer_corpse = create_corpse(surface, "character-corpse", { 24, 20 }, 11)
  local unowned = surface.create_entity({ name = "vrc-avatar-small", position = { 26, 20 }, force = force })
  check(call("revert_all", "no-such-pack") == nil, "revert_all of an unknown pack should change nothing")
  check(call("revert_all", "small now") == nil, "revert_all of two words should change nothing")
  check(call("revert_all", 5) == nil, "revert_all of a number should change nothing")
  check(test_corpse.valid and small_corpse.valid and engineer_corpse.valid, "a refused revert_all converted corpses")
  -- the pack's mod name works as well as its id
  local result = call("revert_all", "vrc-avatar-small")
  -- the unowned character above, and the one on_init left for the pack
  check(result.corpses == 1 and result.remaining == 2,
    "revert_all(small): " .. result.corpses .. " corpses, " .. result.remaining .. " remaining")
  check(test_corpse.valid and engineer_corpse.valid and not small_corpse.valid, "revert_all(small) touched other corpses")
  local converted = corpse_at(surface, { 22, 20 })
  check(converted and converted.name == "character-corpse"
    and converted.get_inventory(defines.inventory.character_corpse).get_item_count("iron-plate") == 9,
    "revert_all(small): corpse items not kept")
  result = call("revert_all")
  check(result.corpses == 1, "revert_all(): converted " .. result.corpses .. " corpses, expected 1")
  check(not test_corpse.valid, "revert_all() left a pack corpse")
  check(engineer_corpse.valid, "revert_all() replaced an engineer's corpse")
  for _, entity in pairs(surface.find_entities_filtered({ type = "character-corpse" })) do
    check(not pack_list.id_of_corpse(entity.name), "corpse left after revert_all(): " .. entity.name)
  end
  for _, entity in pairs(surface.find_entities_filtered({ type = "character-corpse", area = { { 18, 18 }, { 28, 22 } } })) do
    entity.destroy()
  end
  unowned.destroy()

  -- what on_init left: one pack corpse converted, one unowned pack character counted
  local fixture = storage.fixture
  check(fixture ~= nil, "no fixture from on_init (did --create run it?)")
  if fixture then
    check(fixture.corpses == 1 and fixture.remaining == 1,
      "revert_all(test) in on_init: " .. fixture.corpses .. " corpses, " .. fixture.remaining .. " remaining")
    local corpse = corpse_at(surface, FIXTURE_CORPSE_POSITION)
    check(corpse and corpse.name == "character-corpse", "fixture corpse not reverted")
  end
end

-- After the packs were removed from the save.
local function test_packs_removed(surface)
  check(#call("get_avatar_packs") == 0, "packs still listed after removing them")
  local corpse = corpse_at(surface, FIXTURE_CORPSE_POSITION)
  check(corpse and corpse.name == "character-corpse", "reverted corpse lost when its pack was removed")
  check(corpse and corpse.get_inventory(defines.inventory.character_corpse).get_item_count("iron-plate") == 42,
    "reverted corpse lost its items when its pack was removed")
  -- the unowned pack character could not be reverted, so the game deleted it
  local left = surface.find_entities_filtered({ type = "character", position = FIXTURE_CHARACTER_POSITION, radius = 1 })
  check(#left == 0, "the unowned pack character should be gone")
end

-- Characters left in the save by on_init, one per pack installed then, are found again in later
-- runs with their items while their pack is installed (also when it moved into a bundle), and are
-- gone once it is not.
local function test_survivors(surface)
  for _, s in ipairs(storage.survivors or {}) do
    local found = surface.find_entities_filtered({ type = "character", position = s.position, radius = 0.5 })[1]
    if pack_list.get(s.id) then
      check(found and found.name == "vrc-avatar-" .. s.id
        and found.get_main_inventory().get_item_count("iron-plate") == 17,
        "the character of pack " .. s.id .. " or its items were lost when the save was loaded again")
    else
      check(found == nil, "the character of the removed pack " .. s.id .. " still exists")
    end
  end
end

script.on_init(function()
  local surface = game.surfaces[1]
  surface.request_to_generate_chunks({ 0, 0 }, 2)
  surface.force_generate_chunk_requests()
  if script.active_mods["vrc-avatar-test"] then
    create_corpse(surface, "vrc-avatar-corpse-test", FIXTURE_CORPSE_POSITION, 42)
    surface.create_entity({ name = "vrc-avatar-test", position = FIXTURE_CHARACTER_POSITION, force = "player" })
    local result = call("revert_all", "test")
    storage.fixture = { corpses = result.corpses, remaining = result.remaining }
  end
  storage.survivors = {}
  for i, pack in ipairs(call("get_avatar_packs")) do
    local position = { 30 + 3 * i, 30 }
    local character = surface.create_entity({ name = pack.character, position = position, force = "player" })
    character.get_main_inventory().insert({ name = "iron-plate", count = 17 })
    storage.survivors[#storage.survivors + 1] = { id = pack.id, position = position }
  end
end)

script.on_event(defines.events.on_tick, function(event)
  if event.tick ~= 2 then
    return
  end
  local surface = game.surfaces[1]
  local force = game.forces.player
  test_prototypes()
  test_revert_argument()
  test_transfer(surface, force)
  test_corpse(surface, force)
  local packs = test_any_packs(surface, force)
  test_survivors(surface)
  local phase = ""
  if script.active_mods["vrc-avatar-test"] then
    test_generated_packs(surface, force, packs)
    phase = " (with avatar packs)"
  elseif storage.fixture then
    test_packs_removed(surface)
    phase = " (avatar packs removed)"
  end
  if #failures > 0 then
    error("AVATAR-TEST: FAIL\n" .. table.concat(failures, "\n"))
  end
  log("AVATAR-TEST: PASS" .. phase)
  print("AVATAR-TEST: PASS" .. phase)
end)
