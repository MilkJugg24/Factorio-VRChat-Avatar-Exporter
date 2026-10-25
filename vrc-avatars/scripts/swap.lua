-- Replaces a player's character entity with another character prototype while
-- keeping everything the player owns: inventories (armor with its equipment
-- grid, guns, ammo, main, trash), inventory filters, logistic requests,
-- per-character bonuses, health and following combat robots.

local swap = {}

-- Raised after a player's character was replaced. Other mods can get the id
-- through the "vrc-avatars" remote interface.
swap.on_character_swapped = script.generate_event_name()

local BONUSES = {
  "character_crafting_speed_modifier",
  "character_mining_speed_modifier",
  "character_additional_mining_categories",
  "character_running_speed_modifier",
  "character_build_distance_bonus",
  "character_item_drop_distance_bonus",
  "character_reach_distance_bonus",
  "character_resource_reach_distance_bonus",
  "character_item_pickup_distance_bonus",
  "character_loot_pickup_distance_bonus",
  "character_trash_slot_count_bonus",
  "character_maximum_following_robot_count_bonus",
  "character_health_bonus",
}

local function try_get(object, key)
  local ok, value = pcall(function() return object[key] end)
  if ok then
    return value
  end
  return nil
end

local function try_set(object, key, value)
  if value == nil then
    return
  end
  pcall(function() object[key] = value end)
end

-- Returns nil when the character can be swapped right now, otherwise a
-- reason string (used for the status message and to retry later).
function swap.blocked_reason(player)
  local controller = player.controller_type
  -- during a cutscene player.character is nil, so check this first
  if controller == defines.controllers.cutscene then
    return "cutscene"
  end
  local character = player.character
  if not (character and character.valid) then
    return "no-character"
  end
  if controller == defines.controllers.remote then
    return "remote"
  elseif controller ~= defines.controllers.character then
    return "no-character"
  end
  if character.vehicle then
    return "vehicle"
  end
  -- never empty the player's hand for them; wait until they do
  local cursor = player.cursor_stack
  if cursor and cursor.valid_for_read then
    return "cursor"
  end
  if try_get(character, "cargo_pod") then
    return "cargo-pod"
  end
  if try_get(character, "is_flying") then
    return "flying"
  end
  if character.crafting_queue_size > 0 then
    return "crafting"
  end
  local cell = try_get(character, "logistic_cell")
  local network = cell and cell.valid and cell.logistic_network
  if network and network.valid and #network.robots > 0 then
    return "robots"
  end
  return nil
end

local function copy_filters(from, to)
  if not (from.supports_filters() and to.supports_filters()) then
    return
  end
  for i = 1, math.min(#from, #to) do
    local filter = from.get_filter(i)
    if filter then
      pcall(to.set_filter, i, filter)
    end
  end
end

local function move_inventory(from, to)
  if not (from and to) then
    return
  end
  copy_filters(from, to)
  if from.supports_bar() and to.supports_bar() then
    pcall(to.set_bar, from.get_bar())
  end
  for i = 1, math.min(#from, #to) do
    local stack = from[i]
    if stack.valid_for_read and not to[i].swap_stack(stack) then
      -- e.g. the slot's filter no longer accepts it; anything that does not
      -- fit is picked up by spill_leftovers()
      local inserted = to.insert(stack)
      if inserted >= stack.count then
        stack.clear()
      elseif inserted > 0 then
        stack.count = stack.count - inserted
      end
    end
  end
end

local function copy_logistics(old, new)
  local old_point = old.get_requester_point()
  local new_point = new.get_requester_point()
  if not (old_point and new_point) then
    return
  end
  try_set(new_point, "enabled", old_point.enabled)
  try_set(new_point, "trash_not_requested", old_point.trash_not_requested)
  -- remove the fresh character's own (empty) manual sections
  for i = new_point.sections_count, 1, -1 do
    local section = new_point.get_section(i)
    if section and section.is_manual then
      new_point.remove_section(i)
    end
  end
  for i = 1, old_point.sections_count do
    local section = old_point.get_section(i)
    if section and section.is_manual then
      local group = section.group
      local copy = new_point.add_section(group ~= "" and group or nil)
      if copy then
        if group == "" then
          pcall(function() copy.filters = section.filters end)
        end
        try_set(copy, "multiplier", section.multiplier)
        try_set(copy, "active", section.active)
      end
    end
  end
end

local function armor_inventory_bonus(character)
  local armor = character.get_inventory(defines.inventory.character_armor)
  local bonus = 0
  if armor then
    for i = 1, #armor do
      local stack = armor[i]
      if stack.valid_for_read then
        bonus = bonus + (stack.prototype.get_inventory_size_bonus(stack.quality) or 0)
      end
    end
  end
  return bonus
end

local function spill_leftovers(old, new)
  for index = 1, old.get_max_inventory_index() do
    local inventory = old.get_inventory(index)
    if inventory then
      for i = 1, #inventory do
        local stack = inventory[i]
        if stack.valid_for_read then
          local inserted = new.insert(stack)
          if inserted < stack.count then
            stack.count = stack.count - inserted
            old.surface.spill_item_stack({
              position = old.position,
              stack = stack,
              enable_looted = true,
              force = old.force,
              allow_belts = false,
            })
          end
          stack.clear()
        end
      end
    end
  end
end

-- Moves everything the player owns from character `old` to character `new`.
-- Both must be valid characters; `old` is left empty but not destroyed.
function swap.transfer(old, new)
  for _, key in ipairs(BONUSES) do
    try_set(new, key, try_get(old, key))
  end

  -- Give both characters spare main-inventory slots while the armor moves so
  -- that losing/gaining the armor's inventory bonus never spills items.
  local slots_bonus = old.character_inventory_slots_bonus
  local spare = armor_inventory_bonus(old) + 10
  old.character_inventory_slots_bonus = slots_bonus + spare
  new.character_inventory_slots_bonus = slots_bonus + spare

  local inv = defines.inventory
  local order = { inv.character_armor, inv.character_guns, inv.character_ammo }
  local done = {}
  for _, index in ipairs(order) do
    done[index] = true
  end
  for index = 1, old.get_max_inventory_index() do
    if not done[index] then
      order[#order + 1] = index
    end
  end
  for _, index in ipairs(order) do
    move_inventory(old.get_inventory(index), new.get_inventory(index))
  end

  new.character_inventory_slots_bonus = slots_bonus
  old.character_inventory_slots_bonus = slots_bonus

  try_set(new, "selected_gun_index", try_get(old, "selected_gun_index"))
  try_set(new, "allow_dispatching_robots", try_get(old, "allow_dispatching_robots"))
  pcall(copy_logistics, old, new)
  new.health = old.health

  local robots = try_get(old, "following_robots")
  if robots then
    for _, robot in pairs(robots) do
      if robot.valid then
        try_set(robot, "combat_robot_owner", new)
      end
    end
  end
  spill_leftovers(old, new)
end

-- Replaces the player's current character with a new entity of prototype
-- `target_name`. The caller must check swap.blocked_reason() first.
-- Returns the new character, or nil and an error reason.
function swap.replace(player, target_name)
  local old = player.character
  if player.cursor_stack and player.cursor_stack.valid_for_read then
    return nil, "cursor"
  end

  local new = old.surface.create_entity({
    name = target_name,
    position = old.position,
    direction = old.direction,
    force = old.force,
    create_build_effect_smoke = false,
  })
  if not new then
    return nil, "failed"
  end

  swap.transfer(old, new)

  local opened_self = player.opened == old
  player.character = new
  if opened_self then
    player.opened = nil
  end
  -- anything that ended up back in the old character while switching
  spill_leftovers(old, new)

  script.raise_event(swap.on_character_swapped, {
    player_index = player.index,
    old_character = old,
    new_character = new,
    old_unit_number = old.unit_number,
    new_unit_number = new.unit_number,
  })

  old.destroy()
  return new
end

-- Re-creates a character corpse as another corpse prototype, keeping its items.
function swap.replace_corpse(corpse, target_name)
  local inventory = corpse.get_inventory(defines.inventory.character_corpse)
  local size = inventory and #inventory or 0
  -- the index is not guaranteed to belong to an existing player
  local player_index = try_get(corpse, "character_corpse_player_index")
  if player_index and not game.get_player(player_index) then
    player_index = nil
  end
  local new = corpse.surface.create_entity({
    name = target_name,
    position = corpse.position,
    force = corpse.force,
    inventory_size = size,
    player_index = player_index,
    color = try_get(corpse, "color"),
  })
  if not new then
    return nil
  end
  try_set(new, "character_corpse_tick_of_death", try_get(corpse, "character_corpse_tick_of_death"))
  try_set(new, "character_corpse_death_cause", try_get(corpse, "character_corpse_death_cause"))
  if inventory then
    move_inventory(inventory, new.get_inventory(defines.inventory.character_corpse))
  end
  corpse.destroy()
  return new
end

return swap
