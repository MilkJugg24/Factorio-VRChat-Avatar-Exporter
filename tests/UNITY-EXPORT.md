# Testing the exporter in Unity

The exporter's Unity side (rendering with real shaders, posing real rigs, the window) can only be
checked in Unity. Two ways:

- **By hand:** import the `.unitypackage` from the newest release (**Assets ▸ Import Package ▸ Custom
  Package...**), open **Tools ▸ Factorio Avatar Exporter**, load the avatar, look at the preview and press
  **Export Avatar to .zip**.
- **Unattended:** `tests/run_unity_export.ps1` exports avatars without opening Unity's window.

You need a Unity 2022.3 project with avatars in it (a VRChat avatar project), opened at least once in
Unity so its shaders are imported.

## Unattended (script)

With the project **closed** in Unity, from the repository folder in PowerShell:

```powershell
# every avatar at the top level of a scene
powershell -ExecutionPolicy Bypass -File tests\run_unity_export.ps1 -Project "<project folder>" -Scene "Assets/<scene>.unity"
# or one prefab
powershell -ExecutionPolicy Bypass -File tests\run_unity_export.ps1 -Project "<project folder>" -Avatar "Assets/<path>/<avatar>.prefab"
```

The script builds the `.unitypackage` from this repository (needs Python), imports it into the project
(it lands in `Assets/FactorioAvatarExporter`, as importing by hand does) and has Unity export without
opening a window. It finds Unity through Unity Hub from the project's version, or takes
`-Unity <Unity.exe>`. Add `-SkipImport` if the package is already in the project.

- `-Mode pack` (the default) exports each avatar as **Export Avatar to .zip** does:
  `<avatar>/avatar-pack-<id>_1.0.0.zip` (its README says it is a test export, not for sharing),
  `report.txt` and a preview image per animation.
- `-Mode poses` renders every animation posed by the exporter, one sheet per animation
  (`<avatar>/<animation>.png`, a row per direction), and a `report.txt` with the rig report and, per
  animation, how far the bones ended up from where the exporter meant to put them.

Results go to `unity-export/<date-time>/` in the repository, which git ignores: `summary.txt` (each
avatar's report or error), the per-avatar folders, and Unity's logs `unity-import.log` and
`unity-render.log` (errors and exceptions are in here).

## Keep the results private

Exported packs and renders of real avatars are private: don't commit or push them. Look at them on the
PC, and share only what you choose (a summary, or chosen images uploaded to the chat).

### With a local Claude Code session

Paste into a Claude Code session started in this repository on the PC with Unity:

> Pull `main`. Then test the Unity avatar exporter following `tests/UNITY-EXPORT.md`. Find my VRChat
> Unity project(s) (Unity Hub lists them in `%APPDATA%\UnityHub\projects-v1.json`; ask me if it's
> unclear which to use) and the scene or prefabs with my avatars. Make sure the project is closed in
> Unity, then run `tests\run_unity_export.ps1` on it, once as it is and once with `-Mode poses`. Install
> Python's Pillow first if needed (`py -m pip install pillow`). If the script or Unity fails, read
> `unity-render.log` and `unity-import.log`. Look at each avatar's preview images and pose sheets, and
> write `unity-export/<date-time>/OBSERVATIONS.md`: per avatar, whether it looks like it does in Unity's
> Scene view (colours, outlines, shading), fringes or halos at the edges, missing or cut-off parts,
> Console errors, whether each animation looks right (limbs bent the natural way, nothing twisted, feet
> on the ground when they should be) and the pose check numbers. Don't commit or push anything; finish
> with a short summary I can paste into a chat.
