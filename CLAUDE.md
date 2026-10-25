# Notes for contributors and coding agents

The README is for players and avatar makers only. Keep developer and agent material here, in
`docs/`, `tests/` or `unity/dotnet/README.md`, not in the README.

## What's here

| Path | What it is |
| --- | --- |
| `vrc-avatars/` | The Factorio 2.1 mod (Lua). Turns installed avatar packs into characters; the avatar window; `/avatar-revert-all`. It still loads legacy bundles (several packs in one mod), which the exporter no longer makes; `Core/Pack/PackBundler.cs` and `PackHarness bundle` remain only to test that. |
| `unity/Packages/com.vrcavatars.factorio-avatar-exporter/` | The Unity package. `Core/` decides everything and writes packs; it has no Unity references and is built and tested outside Unity. `Editor/` is the thin Unity layer (window, stage, camera, renderer). Its `README.md` ships inside the `.unitypackage` as the user guide to the window. |
| `unity/dotnet/` | Builds and tests `Core/` with .NET, the stick-figure `PackHarness`, and `EditorTypeCheck` (compiles `Editor/` against Unity's assemblies). See its README for Core's layout and C# rules. |
| `docs/vrc-avatar-format.md` | The pack format (the contract between exporter and mod). |
| `docs/modding.md` | The mod's prototypes and remote interface, for other mod authors. |
| `docs/images/` | Screenshots for the README. |
| `tools/avatar_pack/` | The pack format in Python: `check.py` (validator), `make_test_pack.py`, `preview.py` (GIFs). |
| `tools/validate_sprites.py`, `tools/make_unitypackage.py`, `tools/build_mod.sh` | Pack validation; build the `.unitypackage` and mod zip into `build/`. |
| `tests/` | Headless Factorio tests, the in-game screenshot checks (`INGAME-CHECKS.md`), the Unity runner (`UNITY-EXPORT.md`, exports or poses avatars in Unity without opening it), Python tool tests. |

## Checks to run before pushing

```sh
dotnet test unity/dotnet/Core.Tests                    # Core (needs the .NET 8 SDK)
dotnet build unity/dotnet/EditorTypeCheck              # Editor scripts compile against Unity
python3 -m unittest discover -s tests -p "test_*.py"   # Python tools
tests/run_headless.sh /path/to/factorio --test-packs [--space-age]
tests/run_headless.sh /path/to/factorio --broken-packs
tests/run_headless.sh /path/to/factorio --pack some-pack.zip
```

The headless server is a free download from factorio.com. It draws nothing, so the sprites, the avatar
window and sounds are checked in the real game (`tests/INGAME-CHECKS.md`) or by the user.

To look at poses without Unity: `dotnet run --project unity/dotnet/PackHarness -- --out build/harness
--rig generic`, then `tools/avatar_pack/preview.py` on the result.

## Releases

CI runs on every push. After CI passes on `main`, `.github/workflows/release.yml` publishes a full
release, marked latest, with two files: `FactorioAvatarExporter_<v>.unitypackage` and
`vrc-avatars_<v>.zip`. Older releases and their tags are deleted, so only the newest is offered.

- A new exporter version (`package.json` and `ExporterInfo.Version` in `Editor/PackExport.cs`, kept equal)
  makes a new release.
- A new mod version alone (`vrc-avatars/info.json`, plus an entry in `changelog.txt`) swaps the
  mod zip in the current release.
- When the pack format gains something the mod must understand, raise `PackFormat.MinMainModVersion`
  so packs depend on the new mod, and document it in
  `docs/vrc-avatar-format.md`.

## Conventions

- Never hard-code or auto-fill personal data (names, e-mail addresses, Unity account details). The
  exporter's Author field starts empty and must be filled in (packs are titled "VRC Avatar: <name> by
  <author>").
- The mod is shown as "VRC Avatars"; its internal name is `vrc-avatars` (packs depend on it,
  saves and zip names use it).
- In-game screenshots and exported packs of real avatars are private: don't commit them. The repository
  is public. README screenshots are added by the owner.
- Keep the README plain-English and task-based (play, make an avatar, run a server, remove safely,
  troubleshooting). Multiplayer means every player installs every player's pack; don't bring bundles
  back into user-facing docs. Technical detail goes in the files above.
- Code comments and docs: plain sentences, matching the surrounding style.
