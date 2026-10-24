# Avatar Characters for Factorio

Play Factorio as your own avatar. Turn your VRChat avatar (or any humanoid model in Unity) into a
Factorio character that idles, runs, shoots, mines and flies, and pick it in the game with one click.
Every player can have their own look, also in multiplayer.

*(Screenshot coming: two avatars and the engineer standing together in a factory. File: `docs/images/hero.png`)*

There are two parts, both on the [Releases](../../releases) page:

| Download | What it is | Who needs it |
| --- | --- | --- |
| `avatar-characters_<version>.zip` | The Factorio mod. | **Every player.** |
| `FactorioAvatarExporter_<version>.unitypackage` | A Unity tool that turns your avatar into an **avatar pack**, a small mod with your character in it. | Only whoever makes an avatar. |

You need Factorio 2.1. Space Age is optional; with it, avatars fly with mech armor.

---

## Play with an avatar

1. Put these zips into Factorio's `mods` folder, **still zipped**:
   - `avatar-characters_<version>.zip`;
   - the avatar pack (`avatar-pack-<name>_<version>.zip`), or a server's bundle
     (`avatar-bundle-<name>_<version>.zip`).

   The `mods` folder is:
   - Windows: `%APPDATA%\Factorio\mods` (paste that into the address bar of File Explorer);
   - macOS: `~/Library/Application Support/factorio/mods`;
   - Linux: `~/.factorio/mods`.
2. Start Factorio (restart it if it was open).
3. In a game, press **Ctrl + Shift + A** (or the avatar button in the shortcut bar) and click a look.

*(Screenshot coming: the avatar window with two avatars and the engineer to choose from. File: `docs/images/avatar-window.png`)*

Your choice is saved with the game. Without any packs installed, the mod changes nothing: everyone stays
the engineer.

## Make your own avatar

You need Unity 2022.3 with your avatar in a project. Your VRChat avatar project works.

1. **Install the exporter.** In Unity choose **Assets ▸ Import Package ▸ Custom Package...**, pick the
   `.unitypackage` and click **Import**.
2. **Open it.** Choose **Tools ▸ Factorio Avatar Exporter ▸ Exporter**.
3. **Load your avatar.** Drag your avatar (the object with the VRChat avatar descriptor, or its prefab)
   into the **Avatar** field and press **Load avatar**. The exporter works on a copy; your avatar and
   scene are not changed.

   *(Screenshot coming: the exporter window with an avatar loaded and the preview showing. File: `docs/images/exporter-loaded.png`)*
4. **Check the preview.** Pick an animation and a direction, or press **Play**, to see how your avatar
   will move in the game.
5. **Fill in the pack details.** A **Name** (shown in the game) and an **Author** (any name). Leave
   everything else as it is.
6. **Export.** Press **Check fit** if you like (it takes a few seconds and warns you about anything too
   large), then **Export All**, and save the file where you like. Exporting takes a few minutes.
   Keep the file name as it is.

   *(Screenshot coming: the exporter window after Check fit, and the Export All button. File: `docs/images/exporter-check-fit.png`)*

Then install the pack as in [Play with an avatar](#play-with-an-avatar).

**Updating your avatar later:** raise the **Version** (1.0.1, 1.1.0, ...), keep the same **Name**,
export again, and replace the old zip in the `mods` folder with the new one.

## Play together on a server

Factorio can't download these mods by itself, so **every player needs exactly the same mods as the
server**, same versions too. The easy way is a **bundle**: one zip with everyone's avatars.

1. Every player makes their own avatar pack and sends the zip to whoever runs the server.
2. The server owner, in Unity, chooses **Tools ▸ Factorio Avatar Exporter ▸ Combine Packs for a
   Server**, adds everyone's packs, names the bundle and presses **Combine...**.

   *(Screenshot coming: the Combine Packs window with a few packs added. File: `docs/images/combine-packs.png`)*
3. The server owner shares two zips: the bundle and `avatar-characters_<version>.zip`. Every player,
   and the server, puts both into the `mods` folder.
4. In the game, everyone picks their own look and sees everyone else's.

**When someone joins later or updates their avatar:** in Combine Packs, add the previous bundle plus the
new packs and combine again. Everyone swaps the old bundle zip for the new one. Nobody loses their
character or items.

**If you can't join** ("your active mods don't match the server's"): your zips aren't the server's
versions. The game's **Synchronize** button can't fetch these mods; get the current zips from the
server owner.

## Removing an avatar or the mod safely

> **Warning:** when you remove a pack, Factorio deletes every character using it **together with
> everything they carry**.

Before removing anything:

1. Make sure everyone using that avatar is online, alive and not in a vehicle.
2. An admin types `/avatar-revert-all <name>` in chat (the pack's name as in its file name, after
   `avatar-pack-`). Everyone using it turns back into the engineer, items and all. To remove the whole
   mod, type `/avatar-revert-all` on its own.
3. Save, then remove the zip.

If a pack goes missing anyway, its players show as the engineer and get their look back when it's
installed again.

## If something goes wrong

- **My avatar doesn't show up in the game:** check that both zips are in the `mods` folder, still zipped,
  with their original names, and enabled under **Mods** in the main menu.
- **"has no humanoid rig" in Unity:** your model needs **Animation Type: Humanoid** in its import
  settings (Rig tab). VRChat avatars already have it.
- **Something is cut off, floats, or shouldn't be there** (a fake shadow, an effect): drag that object
  into **Leave out of the sprites** in the exporter, press **Reload avatar** and export again.
- **Pink or light-blue patches:** a shader is missing. Make sure the avatar looks right in Unity's Scene
  view first.
- **Check fit says an animation is too large:** something reaches far out from the body (a long tail,
  wide wings, a big accessory). Leave it out of the sprites and check again. Animations that are only a
  little too large are drawn at half resolution automatically; they look the same size, just softer.

Still stuck? [Open an issue](../../issues) and attach the `report.txt` from the export folder.

## Good to know

- **Every avatar is drawn as tall as the engineer**, whatever its size in VRChat.
- **Wings and tails move** if their bones have "wing" or "tail" in their names: wings flap when flying
  and stretch now and then while standing, and tails sway. Fingers don't grip the gun or pickaxe yet.
- **Video memory:** each avatar uses some video memory on every player's computer, usually a few tens of
  MB; `report.txt` from the export says how much yours needs.
- **Permission:** many avatars may only be used in VRChat. Only share a pack made from someone else's
  avatar if its creator allows it.

---

More detail: the [exporter guide](unity/Packages/com.milkjugg24.factorio-avatar-exporter/README.md)
(lighting, every window option), [making a pack by hand](docs/avatar-pack-format.md), and
[notes for other mod authors](docs/modding.md). Contributors and coding agents: see [CLAUDE.md](CLAUDE.md).

This project began as the Dragon Character mod, archived on the
[`archive/dragon-character`](../../tree/archive/dragon-character) branch.
