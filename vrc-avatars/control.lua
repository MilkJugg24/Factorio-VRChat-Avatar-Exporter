local appearance = require("scripts.appearance")
local swap = require("scripts.swap")
local gui = require("scripts.gui")
local packs = require("scripts.packs")
local revert = require("scripts.revert")

-- How often (in ticks) appearance changes that had to wait are retried.
local RETRY_INTERVAL = 30

-- storage.players[player_index] = { avatar, status, gui_location, avatar_gone_notified }
--   avatar: id of the chosen avatar pack, nil for the engineer
--   avatar_gone_notified: the missing pack the player was told about
-- storage.pending[player_index] = true while the player's character does not yet match
-- storage.swap_tick[player_index] = tick of the player's last character swap
local function init_storage()
  storage.players = storage.players or {}
  storage.pending = storage.pending or {}
  storage.swap_tick = storage.swap_tick or {}
end

local function prefs_of(player_index)
  local prefs = storage.players[player_index]
  if not prefs then
    prefs = {}
    storage.players[player_index] = prefs
  end
  return prefs
end

-- Statuses that do not clear up by themselves; everything else (leaving a
-- vehicle, finishing crafting, ...) is retried.
local PERMANENT = { missing = true }

local function prototype_exists(name)
  return prototypes.entity[name] ~= nil
end

-- The engineer and installed packs' characters; anything else comes from
-- another mod.
local function is_own_character(name)
  return name == appearance.ENGINEER or packs.id_of_character(name) ~= nil
end

-- Makes the player's character match their chosen appearance, or remembers to
-- try again later. Returns nil on success, otherwise a status string.
local function apply_appearance(player)
  local prefs = prefs_of(player.index)
  -- `note` is "avatar-missing" when the chosen avatar pack is gone; the engineer is used instead
  local target, note = appearance.resolve(prefs, prototype_exists)
  local character = player.character
  local status
  if character and character.valid and character.name == target then
    status = nil
  elseif character and character.valid and not is_own_character(character.name) then
    -- A character prototype from another mod (e.g. a temporary jetpack
    -- character). Leave it alone and try again once it is ours again.
    status = "foreign"
  elseif not prototypes.entity[target] then
    status = "missing"
  else
    status = swap.blocked_reason(player)
    if not status then
      -- set before swapping: on_gui_closed may fire while the character changes
      storage.swap_tick[player.index] = game.tick
      local new_character, err = swap.replace(player, target)
      status = err
      if new_character then
        -- switching characters may close whatever the player had open; keep
        -- the avatar window open
        local frame = gui.get_frame(player)
        if frame and player.opened ~= frame then
          player.opened = frame
        end
      end
    end
  end
  if status and not PERMANENT[status] then
    storage.pending[player.index] = true
  else
    storage.pending[player.index] = nil
  end
  status = status or note
  prefs.status = status
  gui.refresh(player, prefs)
  return status
end

local function retry_if_pending(event)
  local index = event.player_index
  if index and storage.pending[index] then
    local player = game.get_player(index)
    if player and player.connected then
      apply_appearance(player)
    end
  end
end

local function toggle_gui(player)
  local prefs = prefs_of(player.index)
  if gui.get_frame(player) then
    gui.close(player, prefs)
  else
    gui.open(player, prefs)
  end
end

local function setup_player(player)
  prefs_of(player.index)
  gui.update_top_button(player)
end

---------------------------------------------------------------------------
-- Lifecycle
---------------------------------------------------------------------------

script.on_init(function()
  init_storage()
  for _, player in pairs(game.players) do
    setup_player(player)
    apply_appearance(player)
  end
end)

script.on_configuration_changed(function()
  init_storage()
  for _, player in pairs(game.players) do
    -- rebuild the window in case its layout changed between versions
    local prefs = prefs_of(player.index)
    if prefs.avatar and not packs.get(prefs.avatar) then
      -- the preference is kept, so reinstalling the pack brings the look back;
      -- tell each player once (offline players see it in the window instead)
      if player.connected and prefs.avatar_gone_notified ~= prefs.avatar then
        player.print({ "vrc-avatars.vrc-avatar-gone", prefs.avatar })
        prefs.avatar_gone_notified = prefs.avatar
      end
    else
      prefs.avatar_gone_notified = nil
    end
    local was_open = gui.get_frame(player) ~= nil
    gui.close(player, prefs)
    setup_player(player)
    storage.pending[player.index] = true
    if player.connected then
      apply_appearance(player)
      if was_open then
        gui.open(player, prefs)
      end
    end
  end
end)

script.on_event(defines.events.on_player_created, function(event)
  local player = game.get_player(event.player_index)
  setup_player(player)
  apply_appearance(player)
end)

script.on_event(defines.events.on_player_joined_game, function(event)
  local player = game.get_player(event.player_index)
  -- a window left open when the player disconnected is no longer their
  -- opened GUI (Esc would not close it), so start without it
  gui.close(player, prefs_of(player.index))
  setup_player(player)
  retry_if_pending(event)
end)

script.on_event(defines.events.on_player_removed, function(event)
  storage.players[event.player_index] = nil
  storage.pending[event.player_index] = nil
  storage.swap_tick[event.player_index] = nil
end)

script.on_event(defines.events.on_player_respawned, function(event)
  local player = game.get_player(event.player_index)
  if player then
    apply_appearance(player)
  end
end)

script.on_event(defines.events.on_player_died, function(event)
  local player = game.get_player(event.player_index)
  if player then
    storage.pending[player.index] = true
    -- dying closes the opened GUI without raising on_gui_closed, which would
    -- leave the window on screen with Esc no longer closing it
    gui.close(player, prefs_of(player.index))
  end
end)

for _, name in ipairs({
  "on_player_controller_changed",
  "on_player_driving_changed_state",
  "on_cutscene_finished",
  "on_cutscene_cancelled",
  "on_player_toggled_map_editor",
}) do
  script.on_event(defines.events[name], retry_if_pending)
end

script.on_nth_tick(RETRY_INTERVAL, function()
  if not next(storage.pending) then
    return
  end
  for index in pairs(storage.pending) do
    local player = game.get_player(index)
    if not player then
      storage.pending[index] = nil
    elseif player.connected then
      apply_appearance(player)
    end
  end
end)

script.on_event(defines.events.on_runtime_mod_setting_changed, function(event)
  if event.setting == "vrc-avatars-show-top-button" and event.player_index then
    local player = game.get_player(event.player_index)
    if player then
      gui.update_top_button(player)
    end
  end
end)

---------------------------------------------------------------------------
-- Opening the window
---------------------------------------------------------------------------

script.on_event(defines.events.on_lua_shortcut, function(event)
  if event.prototype_name == gui.SHORTCUT then
    toggle_gui(game.get_player(event.player_index))
  end
end)

script.on_event("vrc-avatars-toggle-gui", function(event)
  toggle_gui(game.get_player(event.player_index))
end)

---------------------------------------------------------------------------
-- Window interaction
---------------------------------------------------------------------------

local function set_avatar(player, id)
  local prefs = prefs_of(player.index)
  if prefs.avatar ~= id then
    prefs.avatar = id
    apply_appearance(player)
  else
    gui.refresh(player, prefs)
  end
end

local function is_ours(element)
  return element and element.valid and element.tags and element.tags.avatar_action ~= nil
end

script.on_event(defines.events.on_gui_click, function(event)
  local element = event.element
  if not is_ours(element) then
    return
  end
  local player = game.get_player(event.player_index)
  local tags = element.tags
  local action = tags.avatar_action
  if action == "toggle" then
    toggle_gui(player)
  elseif action == "close" then
    gui.close(player, prefs_of(player.index))
  elseif action == "look" then
    local id = tags.avatar
    if id == "" then
      set_avatar(player, nil)
    elseif packs.get(id) then
      set_avatar(player, id)
    end
  end
end)

script.on_event(defines.events.on_gui_closed, function(event)
  local element = event.element
  if element and element.valid and element.name == gui.FRAME then
    local player = game.get_player(event.player_index)
    if storage.swap_tick[player.index] == game.tick then
      -- closed as a side effect of replacing the character, not by the player
      player.opened = element
      return
    end
    gui.close(player, prefs_of(player.index))
  end
end)

---------------------------------------------------------------------------
-- Commands
---------------------------------------------------------------------------

commands.add_command("avatar", { "vrc-avatars.command-avatar-help" }, function(command)
  local player = command.player_index and game.get_player(command.player_index)
  if player then
    toggle_gui(player)
  end
end)

-- Before removing the mod (or an avatar pack), every character and corpse using
-- one of its prototypes must be turned back into the engineer's, otherwise the
-- game deletes them (and the items they hold) when the save is loaded.
local function revert_all(pack_id)
  return revert.run(pack_id, prefs_of, apply_appearance, storage.pending)
end

-- Packs a revert can name: installed ones, and removed ones that players still
-- have chosen (reverting those clears the choice).
local function is_known_pack(id)
  if packs.get(id) then
    return true
  end
  for _, prefs in pairs(storage.players) do
    if prefs.avatar == id then
      return true
    end
  end
  return false
end

commands.add_command("avatar-revert-all", { "vrc-avatars.command-revert-help" }, function(command)
  local caller = command.player_index and game.get_player(command.player_index)
  local function reply(message)
    if caller then
      caller.print(message)
    else
      game.print(message)
    end
  end
  if caller and not caller.admin then
    reply({ "vrc-avatars.admin-only" })
    return
  end
  local ok, pack_id = revert.parse_pack_argument(command.parameter, is_known_pack)
  if not ok then
    local ids = {}
    for _, pack in ipairs(packs.list()) do
      ids[#ids + 1] = pack.id
    end
    local installed = #ids > 0 and table.concat(ids, ", ") or { "vrc-avatars.no-packs" }
    reply({ "vrc-avatars.revert-unknown-pack", command.parameter, installed })
    return
  end
  local result = revert_all(pack_id)
  if pack_id then
    reply({ "vrc-avatars.revert-pack-done", pack_id, result.corpses, result.remaining })
  else
    reply({ "vrc-avatars.revert-done", result.corpses, result.remaining })
  end
end)

---------------------------------------------------------------------------
-- Remote interface for other mods
---------------------------------------------------------------------------

remote.add_interface("vrc-avatars", {
  -- Event raised after a player's character entity was replaced:
  -- { player_index, old_character, new_character, old_unit_number, new_unit_number }
  get_on_character_swapped_event = function()
    return swap.on_character_swapped
  end,
  -- Returns { avatar = pack id, or nil for the engineer }
  get_appearance = function(player_index)
    return { avatar = prefs_of(player_index).avatar }
  end,
  -- Changes the player's avatar (a pack id, or false for the engineer).
  -- Returns nil when applied; "avatar-missing" when applied but the chosen
  -- pack is not installed, so the engineer is shown; or a status string when
  -- the change has to wait (e.g. "vehicle", "crafting") or failed
  -- ("unknown-avatar", "no-player", "missing").
  set_appearance = function(player_index, new)
    local player = game.get_player(player_index)
    if not player then
      return "no-player"
    end
    new = new or {}
    if new.avatar and not packs.get(new.avatar) then
      return "unknown-avatar"
    end
    local prefs = prefs_of(player_index)
    if new.avatar == false then
      prefs.avatar = nil
    elseif new.avatar ~= nil then
      prefs.avatar = new.avatar
    end
    return apply_appearance(player)
  end,
  -- Opens the avatar window for a player (for example from a server's welcome message).
  open_window = function(player_index)
    local player = game.get_player(player_index)
    if player and player.connected then
      gui.open(player, prefs_of(player_index))
    end
  end,
  -- Installed avatar packs: array of { id, title, author, tintable, character, corpse }
  get_avatar_packs = function()
    local list = {}
    for _, pack in ipairs(packs.list()) do
      list[#list + 1] = {
        id = pack.id,
        title = pack.title,
        author = pack.author,
        tintable = pack.tintable,
        character = pack.character,
        corpse = pack.corpse,
      }
    end
    return list
  end,
  -- The avatar pack id of a character prototype name, or nil.
  is_avatar_character = function(name)
    return packs.id_of_character(name)
  end,
  -- Same as /avatar-revert-all [pack_id] (a pack id or the pack's mod name; nil
  -- reverts everything). Returns { corpses, remaining }, or nil when pack_id
  -- names no pack, in which case nothing was changed.
  revert_all = function(pack_id)
    if pack_id ~= nil and type(pack_id) ~= "string" then
      return nil
    end
    local ok, id = revert.parse_pack_argument(pack_id, is_known_pack)
    if not ok then
      return nil
    end
    return revert_all(id)
  end,
})
