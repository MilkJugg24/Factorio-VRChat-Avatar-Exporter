# Exporter Core: development builds outside Unity

The avatar exporter (`unity/Packages/com.vrcavatars.factorio-avatar-exporter/`) is split in two:

- `Core/`: everything that decides what to draw and writes the pack: poses, camera, matting, sheets,
  Lua and PNG writers. It must not reference UnityEngine (its asmdef sets `noEngineReferences`), so it
  builds and is tested here.
- `Editor/`: the thin Unity layer that clones and poses the avatar and renders frames.

The projects here compile the package's `Core/*.cs` the way Unity 2022.3 does (C# 9, .NET Standard 2.1)
and test them with NUnit.

```sh
dotnet test unity/dotnet/Core.Tests          # needs the .NET 8 SDK
```

Cross-checks against the Python tools are skipped unless `python3` has numpy and Pillow.

Several comments cite `tools/dragon_render`, the Python renderer of the archived Dragon Character mod
that Core's math, camera and sheets were ported from. It is on the `archive/dragon-character` branch.

| Project | What it is |
| --- | --- |
| `Core/` | The package's Core sources as a `netstandard2.1` library. |
| `Core.Tests/` | NUnit tests (`net8.0`). `Goldens/` holds reference data written by Python tools. |
| `PackHarness/` | Writes a real avatar pack from a synthetic rig drawn by the software stick-figure renderer: the whole exporter without Unity. |
| `EditorTypeCheck/` | Compiles Core and the Unity `Editor/` scripts against Unity's own assemblies (the `Unity3D.SDK` NuGet package, Unity 2021.1), so a script that would not compile in Unity fails here and in CI: `dotnet build unity/dotnet/EditorTypeCheck`. |

```sh
dotnet run --project unity/dotnet/PackHarness -- --out build/harness --rig generic --zip
tests/run_avatar_pipeline.sh /path/to/factorio [--space-age] [--rig generic|chibi|minimal|dragon]
```

The pipeline script builds a pack with the harness, checks it with `tools/validate_sprites.py --pack`,
loads it in a headless Factorio (`tests/run_headless.sh --pack`) and writes preview GIFs to
`build/pipeline-preview/`.

How Core is organised:

| Folder | What it does |
| --- | --- |
| `Math/`, `Spec/` | Vectors and rotations, the animation table, coordinate conventions. |
| `Rig/` | Measures the avatar's humanoid rig; synthetic rigs for tests. |
| `Pose/Humanoid/` | Turns joint targets into bone rotations (`HumanoidPoseBuilder`) and poses every animation (`HumanoidAnimations`). |
| `Pose/Dragon/` | Literal port of the archived dragon's posing code, checked against goldens from `tools/dragon_render`; the tests use it as a fixed pose source. |
| `Camera/` | The sprite camera, the ground shadow projection and canvas planning. |
| `Render/` | A software stick-figure renderer standing in for Unity. |
| `Imaging/` | Difference matting, downsampling, sRGB, PNG reading and writing. |
| `Sheets/`, `Pack/` | Sprite sheets and the avatar pack files (`avatar-pack.lua`, `info.json`, zip). |
| `Export/` | `AvatarExporter`: renders every frame of every animation and builds the sheets. |

Rules for Core code, because Unity compiles it too:

- C# 9 without records or `init` setters (Unity's .NET Standard 2.1 profile lacks `IsExternalInit`).
- Only .NET Standard 2.1 APIs: no `System.Text.Json`, no `ZLibStream`.
- Doubles for geometry (`Vec3`, `Quat`); the Unity layer converts to float.
- Invariant culture whenever numbers become text.
