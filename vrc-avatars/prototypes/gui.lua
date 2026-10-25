-- Shortcut, hotkey, GUI sprite and styles for the avatar window.

local MOD_PATH = "__vrc-avatars__"

data:extend({
  {
    type = "custom-input",
    name = "vrc-avatars-toggle-gui",
    key_sequence = "CONTROL + SHIFT + A",
    action = "lua",
    consuming = "none",
    order = "a",
  },
  {
    type = "shortcut",
    name = "vrc-avatars-choose",
    order = "z[vrc-avatars]",
    action = "lua",
    toggleable = true,
    associated_control_input = "vrc-avatars-toggle-gui",
    icon = MOD_PATH .. "/graphics/icons/shortcut-x56.png",
    icon_size = 56,
    small_icon = MOD_PATH .. "/graphics/icons/shortcut-x24.png",
    small_icon_size = 24,
  },
  {
    type = "sprite",
    name = "vrc-avatars-icon",
    filename = MOD_PATH .. "/graphics/icons/vrc-avatars.png",
    size = 64,
    flags = { "gui-icon" },
  },
})

data.raw["gui-style"]["default"]["avatar_characters_look_button"] = {
  type = "button_style",
  parent = "slot_button",
  size = 72,
  padding = 4,
}
