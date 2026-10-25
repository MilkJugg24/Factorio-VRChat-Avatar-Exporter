-- The startup setting "Every armor flies": every armor gets provides_flight,
-- like Space Age's mech armor, so winged avatars can fly over buildings,
-- cliffs and water in any armor. Armor that did not fly before gets a small
-- pair of wings on its icon.
-- Runs in data-final-fixes before prototypes/characters.lua, which then gives
-- every avatar its flight animations.
-- Factorio only allows flying armor with Space Age (the space_travel feature
-- flag); without it the setting does nothing.

if not settings.startup["vrc-avatars-armor-flight"].value or not feature_flags["space_travel"] then
  return
end

local MOD_PATH = "__vrc-avatars__"
-- The badge: a quarter of the icon's width, in its top right corner (icon
-- layer shifts are in pixels of a 32 px icon).
local BADGE = {
  icon = MOD_PATH .. "/graphics/icons/armor-wings.png",
  icon_size = 64,
  scale = 0.25,
  shift = { 8, -8 },
  floating = true,
}

for _, armor in pairs(data.raw["armor"] or {}) do
  if not armor.provides_flight then
    armor.provides_flight = true
    if armor.icons then
      armor.icons[#armor.icons + 1] = table.deepcopy(BADGE)
    elseif armor.icon then
      armor.icons = { { icon = armor.icon, icon_size = armor.icon_size or 64 }, table.deepcopy(BADGE) }
      armor.icon = nil
      armor.icon_size = nil
    end
  end
end

-- Characters (the engineer, other mods' characters) whose animations for some
-- armor have nothing for flying: use the flight animations of another armor
-- when they have some (Space Age's mech armor engineer), otherwise the ground
-- ones, so they are never drawn without a sprite in the air.
local AIR = {
  idle_in_air = "idle",
  idle_with_gun_in_air = "idle_with_gun",
  flying = "running",
  flying_with_gun = "running_with_gun",
  take_off = "idle_with_gun",
  landing = "idle_with_gun",
}
for _, character in pairs(data.raw["character"] or {}) do
  local donor = nil
  for _, set in ipairs(character.animations or {}) do
    if set.flying then
      donor = set
      break
    end
  end
  for _, set in ipairs(character.animations or {}) do
    if not set.flying then
      for air, ground in pairs(AIR) do
        if set[air] == nil then
          local from = donor and donor[air] or set[ground]
          set[air] = from and table.deepcopy(from) or nil
        end
      end
    end
  end
end
