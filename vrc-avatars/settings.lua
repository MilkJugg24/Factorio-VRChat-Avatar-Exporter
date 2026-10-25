-- All startup settings, so the mod has a single tab in the mod settings; a
-- server's settings reach its players when they join.
data:extend({
  {
    type = "bool-setting",
    name = "vrc-avatars-show-top-button",
    setting_type = "startup",
    default_value = true,
    order = "a"
  },
  {
    -- percent; 0 turns sprinting off (control.lua is_fast)
    type = "int-setting",
    name = "vrc-avatars-sprint-bonus",
    setting_type = "startup",
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
    default_value = true,
    order = "c"
  }
})
