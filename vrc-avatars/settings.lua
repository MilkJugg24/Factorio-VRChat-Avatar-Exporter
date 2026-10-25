data:extend({
  {
    type = "bool-setting",
    name = "vrc-avatars-show-top-button",
    setting_type = "runtime-per-user",
    default_value = true,
    order = "a"
  },
  {
    -- percent; 0 turns sprinting off (scripts/pack-format.lua DEFAULT_SPRINT_BONUS)
    type = "int-setting",
    name = "vrc-avatars-sprint-bonus",
    setting_type = "runtime-global",
    default_value = 60,
    minimum_value = 0,
    maximum_value = 1000,
    order = "b"
  },
  {
    -- prototypes/armor-flight.lua
    type = "bool-setting",
    name = "vrc-avatars-armor-flight",
    setting_type = "startup",
    default_value = false,
    order = "c"
  }
})
