# Factorio Avatar Exporter: the full guide

This Unity tool turns your avatar into an **avatar pack**, a small Factorio mod, for the **VRC
Avatars** mod. The quick steps, installing in Factorio and playing on a server (every player installs every
player's pack) are on the project's GitHub page, where you downloaded this package. This guide explains every option
in the exporter window.

Open it with **Tools ▸ Factorio Avatar Exporter**.

*(Screenshot coming: the whole exporter window, with the sections below labelled. File: `docs/images/exporter-overview.png`)*

## Avatar

- **Choose Avatar:** the object with the VRChat avatar descriptor (from the Hierarchy), or its prefab. Press
  **Load avatar**. The exporter works on a copy in a hidden scene; your scene and avatar are not changed.
- To keep something out of the sprites (a fake ground shadow, a glow, an accessory you don't want), turn
  it off in your avatar (untick it in the Hierarchy) and press **Reload avatar**. The exporter draws the
  game's own shadow, so a fake one isn't needed.
- **Report** (click to open): what the exporter found: the avatar's height, the bones it's missing, the scripts it
  removed from the copy, which loose parts (outfits, accessories) it held on to the body, and which wing
  and tail bones will move.

## Preview

The preview is on the right. Pick an animation, a direction and a frame, or press **Play** to see it at
the speed the game plays it. Scroll to zoom, drag to pan, and double-click to reset zoom. The yellow cross
is where your character stands in the game.

## Lighting

The avatar is lit in its own hidden scene, the same way for every animation and direction. Pick the
**Lighting Style**:

- **Factorio style** (the default) matches the game's own characters: light from the upper left, soft
  light all around, and a neutral studio for shiny materials to reflect.
- **Custom** lets you set it yourself while you watch the preview:
  - **Key light from / height / strength / colour:** the main light. Directions are degrees around the
    avatar as you see it: 0 from behind, 90 from the right, 180 from the front, 270 from the left.
  - **Fill light:** a softer light from the other side, so the shaded side isn't dark.
  - **Rim light:** a light from behind that brightens the edges.
  - **Ambient light:** light from every direction, so nothing is pitch black.
  - **Reflections:** how much shiny materials reflect, and optionally a cubemap for them to reflect
    (a skybox or an HDRI imported with **Texture Shape: Cube**).
  - **Reset to Factorio style** puts everything back.

  Very shiny avatars often look best with the key light higher and more in front, a little fill and rim
  light, and a cubemap that suits them.

The ground shadow isn't affected by the lighting: it's always drawn the way the game draws shadows.

## Avatar pack

- **Name:** shown in the game. The **Pack id** is made from it; keep both the same when you export
  again, so the new pack replaces the old one.
- **Author:** your name as you'd like it shown. The pack appears in Factorio's mod list as "VRC Avatar:
  <name> by <author>". Any name will do; Factorio needs one.
- **Version:** always 1.0.0, so a new export of your avatar has the same file name and simply replaces
  the old one, for you and everyone on your server.
- **Resolution:** **High Quality** (the default) is sharp and right for almost everyone. **Low Quality**
  uses a quarter of the video memory, for very large servers or old computers, but your character looks
  blurry when zoomed in; the window warns you when it's chosen. Both are the same size in the game.

Exporting is only possible once the name, pack id and author are filled in; an empty one is outlined
in red. Every pack includes the
flight animations (hovering and flying with Space Age's mech armor).
- **Export Avatar to .zip:** first measures every frame (a few seconds) to check that each animation fits
  Factorio's sprite sheets. An animation too large for full resolution is drawn at half resolution
  instead (the same size in the game, a little softer); if one is too large even then, it stops and
  says which, before the long render: turn off the part that reaches far out (a long tail, wide
  wings, a big accessory) and export again. Then it renders everything (a few minutes; you can cancel)
  and saves the pack. Keep the file name `vrc-avatar-<id>_1.0.0.zip`, as Factorio expects.
- **Export Selected Animation** (next to Play): saves only the animation in the preview as one image.
  Not needed for the game.

Every export also writes `FactorioAvatarExport/<avatar>/preview/` next to your project's `Assets`
folder: a preview image of every animation and `report.txt`, which says what the exporter did, which
animations were drawn at half resolution, and how much video memory the pack needs.

## How your avatar moves

Every avatar is drawn as tall as the engineer.

- **Standing:** breathes, shifts its weight and looks around. Every 8 seconds one wing stretches out,
  left then right.
- **Running:** a light jog, leaning into it. Shooting while running looks the same, a little calmer, so
  the gun stays on target.
- **Sprinting:** with two or more exoskeletons in the armor, long bounding strides with both feet off the
  ground between them, leaning well forward, the arms pumping.
- **Mining:** the engineer's own swing: the pickaxe raised over the right shoulder, brought down fast,
  the other arm out for balance.
- **Flying** (mech armor): wings flap; hovering, they beat slowly. Take-off and landing are
  empty-handed.
- **Arms as wings:** every pack also has a version of the flight animations where the arms beat as wings,
  for avatars whose wings are their arms (wyverns). Each player chooses it in the game's avatar window
  ("Flap my arms as wings when flying").
- **Wings** move when their bones have "wing" in their names (any case). If no bone does, the bones of
  an object named "wing" (like "Wings") are used. Each wing turns as one piece from its shoulder, even
  when it's made of several bone chains (arm, membrane, fingers). In the air the wings open out (into the shape their mesh was modelled in, or by straightening their joints
  when it was modelled folded) and beat around straight out sideways, however they were saved (folded down the
  back, or spread); standing,
  one stretches out now and then. The **Report** names each wing's hinge and the angle it was saved at
  ("wing hinges").
- **Tails** sway when the avatar's own bones below the hips have "tail" in their names. A tail that's
  part of the body mesh works too, as long as it has its own bones. Anything attached to a tail bone,
  like a tuft, moves with it.
- Not yet: fingers don't close around the gun or pickaxe, ears stay still, and the player colour doesn't
  tint the avatar.

## If something goes wrong

If loading or exporting fails, the exporter writes a log of what happened into **ERROR_LOGS** in its
folder (`Assets/FactorioAvatarExporter/ERROR_LOGS`) and tells you the file's name. Attach that file when
you report a problem. It has no images, and your user name is blanked out of the paths in it.

- **"has no humanoid rig":** set **Animation Type: Humanoid** in the model's import settings (Rig tab).
- **Part of an outfit floats away or stays behind:** outfits added with VRCFury or Modular Avatar are
  only joined to the body in Play mode, so the exporter holds them on itself: by the constraint that held
  them, by bone names (`Chest_Hoodie` follows `Chest`), or else by the nearest bone of the body. The
  **Report** lists every part and what it follows; please report the line of a part that still floats.
- **"Something reached the edge of the picture and was cut off":** a part is drawn further out than its
  mesh. The exporter already retries with more room; whatever is still cut off is listed in
  `report.txt`. If it matters, turn that part off in your avatar and export again.
- **Pink or light-blue patches:** a shader is missing or not compiled yet. Make sure the avatar looks
  right in the Scene view first.

## Installing without the .unitypackage

**Window ▸ Package Manager ▸ + ▸ Add package from disk...** on this folder's `package.json` works too.
Use one way or the other, not both.

Updating from 0.21.0 or older: delete the `Assets/FactorioAvatarExporter` folder first, then import the
new package. The package was renamed in 0.22.0, and Unity would otherwise keep two copies.
