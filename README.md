# Export your VRChat Avatar for Factorio!

This is a two-part Unity Package and Factorio Mod that allows you to export your VRChat avatar, and use it in-game. Simply install the Unity Package on your project, export the avatar as .zip, then place it into the mods folder alongside the "vrc-avatars" mod. You can select your avatar in-game via the in-game UI, and it will show in-place of the default model. This also works in multiplayer, however, each player needs to be given the exported avatar .zip files for you to see them.

**AI Disclosure**: The development of this tool was heavily assisted by the use of AI. However, no assets created by the VRChat community and other content creators were directly provided to nor accessed by AI. The rigging and animation is custom-made to work based on common keywords like tail, wing, cloth, etc. Due to this, not every exported avatar will operate like it does in VR, but it will at least be somewhat similar or good enough for gameplay.

**Gameplay Changes**: Due to some avatars having wings or features that would allow them to fly, all armor sets in the game by default will allow you to fly. This is only compatible with Space Age DLC, and there is a toggle switch out-of-game to disable this. Simply build any armor, and equip it to gain the ability to float over objects as if you were wearing mech armor.

---
## Overview

There are two parts, both on the [Releases](../../releases) page:

| Download | What it is | Who needs it |
| --- | --- | --- |
| `vrc-avatars_<version>.zip` | The Factorio mod. | **Every player.** |
| `FactorioAvatarExporter_<version>.unitypackage` | A Unity tool that turns your avatar into an **avatar pack**, a small mod with your character in it. | Only whoever makes an avatar. |

You need Factorio 2.1. Space Age is optional; with it, avatars fly with mech armor.

---

## Play with an avatar

1. Put these zips into Factorio's `mods` folder, **still zipped**:
   - `vrc-avatars_<version>.zip`;
   - one or more avatar packs (`vrc-avatar-<name>_<version>.zip`), as many as you like.

   The `mods` folder is:
   - Windows: `%APPDATA%\Factorio\mods` (paste that into the address bar of File Explorer);
   - macOS: `~/Library/Application Support/factorio/mods`;
   - Linux: `~/.factorio/mods`.
2. Start Factorio (restart it if it was open).
3. In a game, press **Ctrl + Shift + A** (or the avatar button in the shortcut bar) and click a look.

*(Screenshot coming: the avatar window with two avatars and the engineer to choose from. File: `docs/images/avatar-window.png`)*

Your choice is saved with the game. Without any packs installed, the mod changes nothing: everyone stays
the engineer.

With two or more exoskeletons in your armor, your avatar sprints instead of jogging. Avatars exported
before exporter 0.21.0 always jog; export them again to get the sprint.

With Space Age, every armor lets you fly over buildings, cliffs and water, as mech armor does, so a winged
avatar can take to the air. Armor that flies this way has a small feather on its icon. Without any armor
you still walk.

If your avatar's wings are its arms (a wyvern, for example), tick **Flap my arms as wings when flying** in
the avatar window, and its arms beat as wings in the air; untick it to float with the arms hanging. The box
only shows for avatars exported with exporter 0.26.0 or newer.

The mod's settings are in **Settings ▸ Mod settings ▸ Startup**: the avatar button in the top-left corner,
how fast you need to be to sprint (or 0 for never), and whether every armor flies. Changing them restarts
the game. On a server, the host's settings apply, and players get them when they join.

## Make your own avatar

You need Unity 2022.3 with your avatar in a project. Your VRChat avatar project works.

1. **Install the exporter.** In Unity choose **Assets ▸ Import Package ▸ Custom Package...**, pick the
   `.unitypackage` and click **Import**.
2. **Open it.** Choose **Tools ▸ Factorio Avatar Exporter**.
3. **Load your avatar.** Drag your avatar (the object with the VRChat avatar descriptor, or its prefab)
   into the **Avatar** field and press **Load avatar**. The exporter works on a copy; your avatar and
   scene are not changed.

   *(Screenshot coming: the exporter window with an avatar loaded and the preview showing. File: `docs/images/exporter-loaded.png`)*
4. **Check the preview.** Pick an animation and a direction, or press **Play**, to see how your avatar
   will move in the game.
5. **Fill in the pack details.** A **Name** (shown in the game) and an **Author** (any name). Leave
   everything else as it is.
6. **Export.** Press **Export Avatar to .zip** and save the file where you like. Exporting takes a few minutes.
   Keep the file name as it is.

   *(Screenshot coming: the exporter window with the Export Avatar to .zip button. File: `docs/images/exporter-export.png`)*

Then install the pack as in [Play with an avatar](#play-with-an-avatar).

**Updating your avatar later:** export again with the same **Name**, delete the old zip from the `mods`
folder and put in the new one. The number at the end of the file name is the exporter's version, so you
can tell which exporter made a pack.

## Play together on a server

Factorio can't download these mods by itself, so **every player needs exactly the same mods as the
server**, same versions too. Each avatar is its own zip, and you can install as many as you like.

1. Every player makes their own avatar pack and sends the zip to whoever runs the server.
2. The server owner puts `vrc-avatars_<version>.zip` and **all** the players' pack zips into the
   server's `mods` folder, and shares the same set of zips with everyone (a shared folder or a zip of
   zips works well).
3. Every player puts that same set into their own `mods` folder.
4. In the game, everyone picks their own look and sees everyone else's.

**When someone joins later or updates their avatar:** the server owner adds the new zip (and deletes
the old one of an updated avatar) and shares it; everyone does the same.
Nobody loses their character or items.

**If you can't join** ("your active mods don't match the server's"): some of your zips aren't the same
as the server's (an avatar may have been updated). The game's **Synchronize** button can't fetch these mods; get the current zips from the
server owner.

## Removing an avatar or the mod safely

> **Warning:** when you remove a pack, Factorio deletes every character using it **together with
> everything they carry**.

Before removing anything:

1. Make sure everyone using that avatar is online, alive and not in a vehicle.
2. An admin types `/avatar-revert-all <name>` in chat (the pack's name as in its file name, after
   `vrc-avatar-`). Everyone using it turns back into the engineer, items and all. To remove the whole
   mod, type `/avatar-revert-all` on its own.
3. Save, then remove the zip.

If a pack goes missing anyway, its players show as the engineer and get their look back when it's
installed again.

## If something goes wrong

- **My avatar doesn't show up in the game:** check that both zips are in the `mods` folder, still zipped,
  with their original names, and enabled under **Mods** in the main menu.
- **"has no humanoid rig" in Unity:** your model needs **Animation Type: Humanoid** in its import
  settings (Rig tab). VRChat avatars already have it.
- **Something is cut off, floats, or shouldn't be there** (a fake shadow, an effect): turn that object
  off in your avatar (untick it in the Hierarchy), press **Reload avatar** and export again.
- **Pink or light-blue patches:** a shader is missing. Make sure the avatar looks right in Unity's Scene
  view first.
- **The export says an animation is too large:** something reaches far out from the body (a long
  tail, wide wings, a big accessory). Turn it off in your avatar and export again. Animations that are
  only a little too large are drawn at half resolution automatically; they look the same size, just
  softer.

Still stuck? [Open an issue](../../issues) and attach the error log the exporter saved in
`Assets/FactorioAvatarExporter/ERROR_LOGS` (or, if the export worked, the `report.txt` from the export
folder).

## Good to know

- **Every avatar is drawn as tall as the engineer**, whatever its size in VRChat.
- **Wings and tails move** if their bones have "wing" or "tail" in their names: wings flap when flying
  and stretch now and then while standing, and tails sway. Fingers don't grip the gun or pickaxe yet.
- **Video memory:** each avatar uses some video memory on every player's computer, usually a few tens of
  MB; `report.txt` from the export says how much yours needs.
- **Permission:** many avatars may only be used in VRChat. Only share a pack made from someone else's
  avatar if its creator allows it.

---

More detail: the [exporter guide](unity/Packages/com.vrcavatars.factorio-avatar-exporter/README.md)
(lighting, every window option), [making a pack by hand](docs/vrc-avatar-format.md), and
[notes for other mod authors](docs/modding.md). Contributors and coding agents: see [CLAUDE.md](CLAUDE.md).

This project began as the Dragon Character mod, archived on the
[`archive/dragon-character`](../../tree/archive/dragon-character) branch.

## License

The mod and the exporter are licensed under the [Apache License 2.0](LICENSE). Avatar packs you export
are not covered by it: they contain your avatar, which stays under its own creator's terms.
