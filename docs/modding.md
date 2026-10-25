# VRC Avatars: notes for other mod authors

## Prototypes

- The vanilla `character` prototype stays the engineer.
- Each pack (and each character of a legacy bundle) adds an `vrc-avatar-<id>` character and an
  `vrc-avatar-corpse-<id>` corpse. They are copied from `character` and `character-corpse` in
  `data-final-fixes`, so they inherit changes other mods make to those.
- A pack with a `sprint` animation also gets `vrc-avatar-sprint-<id>`: the same character, running
  with the sprint and a longer `distance_per_frame`. VRC Avatars swaps a player to it while their
  armor grid's `movement_bonus` is at least the startup setting `vrc-avatars-sprint-bonus` (a percent,
  60 by default, 0 off), and back, when the armor or its equipment changes and nothing is open.
- With the startup setting `vrc-avatars-armor-flight` (on by default) and Space Age (the `space_travel` feature flag,
  which Factorio requires for flying armor), every armor prototype gets
  `provides_flight = true` in `data-final-fixes` (before the characters are made), with a feather badge
  added to the icons of those that didn't fly. Character animation sets without flight animations get
  their own ground ones, so each armor looks the same in the air.
- If another mod temporarily swaps a player's character (a jetpack, for example), VRC Avatars
  leaves it alone and reapplies the player's look once they're back to a normal character.

## Remote interface `vrc-avatars`

- `get_appearance(player_index)` returns `{avatar = pack id or nil}`.
- `set_appearance(player_index, {avatar = id or false})` (`false` is the engineer) returns:
  - `nil` when applied;
  - `"avatar-missing"` when applied, but the pack isn't installed, so the engineer is shown;
  - the reason the change has to wait, such as `"vehicle"`;
  - `"unknown-avatar"` or `"no-player"` when nothing was changed.
- `get_avatar_packs()` (each with `character`, `sprint` (nil without one) and `corpse`),
  `is_avatar_character(name)` (the pack id, also for a sprinting character) and
  `open_window(player_index)`.
- `revert_all(pack_id)` turns everyone using a pack back into the engineer (as `/avatar-revert-all`);
  it returns `nil` when `pack_id` names no pack.
- `get_on_character_swapped_event()`: the event raised whenever the mod replaces a player's character
  entity, with `old_character` and `new_character`.

## Making packs by hand

The pack format is in [vrc-avatar-format.md](vrc-avatar-format.md). `tools/validate_sprites.py --pack
<zip or folder>` checks a pack the way the mod does when the game loads.
