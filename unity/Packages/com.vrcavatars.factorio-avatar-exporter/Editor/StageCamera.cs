using System;
using System.Collections.Generic;
using FactorioAvatarExporter.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FactorioAvatarExporter.Editor
{
    /// <summary>
    /// The Factorio sprite camera in an <see cref="AvatarStage"/>'s scene: the lights (<see cref="StageLighting"/>:
    /// key, fill and rim lights, flat ambient light and surroundings to reflect), and renders on black and on
    /// white turned into a transparent frame with Core's difference matting.
    ///
    /// Shaders are compiled before they are drawn (Unity would otherwise draw a cyan placeholder while they
    /// compile in the background), and a render that still shows the placeholder is taken again.
    /// </summary>
    internal sealed class StageCamera : IDisposable
    {
        public const int Supersampling = 2;
        private const int RenderAttempts = 5;
        private const int StudioSize = 64;

        private readonly AvatarStage stage;
        private readonly Camera camera;
        private readonly Light key, fill, rim;
        private readonly ReflectionProbe probe;
        private readonly Cubemap studio = MakeStudio();
        private StageLighting lighting;
        private readonly bool linear = PlayerSettings.colorSpace == UnityEngine.ColorSpace.Linear;
        private RenderTexture target;
        private Texture2D readback;

        // targets by size: an export alternates between the character's and its shadow's canvases
        private const int KeptTargets = 4;
        private readonly Dictionary<(int, int), (RenderTexture target, Texture2D readback)> targets =
            new Dictionary<(int, int), (RenderTexture, Texture2D)>();

        public StageCamera(AvatarStage stage)
        {
            this.stage = stage;
            var cameraObject = new GameObject("Sprite camera");
            SceneManager.MoveGameObjectToScene(cameraObject, stage.Scene);
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = stage.Scene;
            camera.cameraType = CameraType.Preview;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            camera.depthTextureMode = DepthTextureMode.Depth; // for shader effects that read depth

            key = AddLight("Key light");
            fill = AddLight("Fill light");
            rim = AddLight("Rim light");
            // reflections: a probe around the whole stage, and the scene's default reflection below (whichever
            // the render pipeline picks up), both with the same surroundings
            var probeObject = new GameObject("Reflections");
            SceneManager.MoveGameObjectToScene(probeObject, stage.Scene);
            probe = probeObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.size = Vector3.one * 1000;
            probe.boxProjection = false;
            probe.importance = 100;
            SetLighting(new StageLighting());
        }

        /// <summary>The lighting in effect (a copy).</summary>
        public StageLighting Lighting => lighting.Copy();

        /// <summary>Lights the stage as <paramref name="settings"/> say (Factorio style unless they are custom).</summary>
        public void SetLighting(StageLighting settings)
        {
            lighting = settings.Effective.Copy();
            Point(key, lighting.KeyDirection, lighting.KeyHeight, lighting.KeyStrength, lighting.KeyColor);
            Point(fill, lighting.KeyDirection + 180, StageLighting.FillHeight, lighting.FillStrength, Color.white);
            Point(rim, StageLighting.RimDirection, StageLighting.RimHeight, lighting.RimStrength, Color.white);
            probe.customBakedTexture = Surroundings;
            probe.intensity = lighting.ReflectionStrength;
        }

        private Cubemap Surroundings => lighting.Environment != null ? lighting.Environment : studio;

        private Light AddLight(string name)
        {
            var lightObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(lightObject, stage.Scene);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            return light;
        }

        private static void Point(Light light, float direction, float height, float strength, Color color)
        {
            Vector3 towards = StageLighting.TowardsLight(direction, height);
            light.transform.rotation = Quaternion.LookRotation(-towards, Mathf.Abs(towards.y) > 0.99f ? Vector3.forward : Vector3.up);
            light.color = color;
            light.intensity = strength;
            light.enabled = strength > 0;
        }

        /// <summary>
        /// The neutral studio shiny materials reflect: bright above, a soft grey horizon and a dark floor, the same
        /// all around (linear light, with mipmaps for rough materials' blurred reflections).
        /// </summary>
        private static Cubemap MakeStudio()
        {
            var cubemap = new Cubemap(StudioSize, TextureFormat.RGBAHalf, true)
            {
                name = "Neutral studio",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[StudioSize * StudioSize];
            foreach (CubemapFace face in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY,
                CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
            {
                for (int row = 0; row < StudioSize; row++)
                {
                    for (int column = 0; column < StudioSize; column++)
                    {
                        float s = (column + 0.5f) / StudioSize * 2 - 1, t = (row + 0.5f) / StudioSize * 2 - 1;
                        float up = FaceDirection(face, s, t).normalized.y;
                        float v = up >= 0
                            ? Mathf.Lerp(0.4f, 0.9f, Mathf.Sqrt(up))
                            : Mathf.Lerp(0.4f, 0.12f, Mathf.SmoothStep(0, 1, Mathf.Min(1, -up * 3)));
                        pixels[row * StudioSize + column] = new Color(v, v, v, 1);
                    }
                }
                cubemap.SetPixels(pixels, face, 0);
            }
            cubemap.Apply(true, false);
            return cubemap;
        }

        /// <summary>
        /// The direction through a point of a cubemap face, <paramref name="s"/> left to right and
        /// <paramref name="t"/> top to bottom (both -1 to 1), as Unity lays faces out (row 0 at the top).
        /// </summary>
        private static Vector3 FaceDirection(CubemapFace face, float s, float t)
        {
            switch (face)
            {
                case CubemapFace.PositiveX: return new Vector3(1, -t, -s);
                case CubemapFace.NegativeX: return new Vector3(-1, -t, s);
                case CubemapFace.PositiveY: return new Vector3(s, 1, t);
                case CubemapFace.NegativeY: return new Vector3(s, -1, -t);
                case CubemapFace.PositiveZ: return new Vector3(s, -t, 1);
                default: return new Vector3(-s, -t, -1);
            }
        }

        /// <summary>Retakes because a shader was still compiling, since the camera was made.</summary>
        public int Retakes { get; private set; }

        /// <summary>Placeholder pixels left after the last attempt, since the camera was made.</summary>
        public int PlaceholderLeft { get; private set; }

        /// <summary>Points the camera at <paramref name="canvas"/> (render size = canvas size x <see cref="Supersampling"/>).</summary>
        public void Aim(CanvasSpec canvas)
        {
            UnityCameraSetup view = FactorioCamera.Setup(canvas).ToUnity(stage.MetersPerTile);
            camera.transform.SetPositionAndRotation(
                new Vector3((float)view.Position.x, (float)view.Position.y, (float)view.Position.z),
                new Quaternion((float)view.Rotation.x, (float)view.Rotation.y, (float)view.Rotation.z, (float)view.Rotation.w));
            (int, int) size = (view.TargetWidth, view.TargetHeight);
            if (!targets.TryGetValue(size, out (RenderTexture target, Texture2D readback) pair))
            {
                if (targets.Count >= KeptTargets)
                {
                    ReleaseTargets();
                }
                pair = (new RenderTexture(size.Item1, size.Item2, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                    { antiAliasing = 1 }, new Texture2D(size.Item1, size.Item2, TextureFormat.RGBAFloat, false, true));
                targets.Add(size, pair);
            }
            (target, readback) = pair;
            camera.targetTexture = target;
            camera.orthographic = true;
            camera.orthographicSize = (float)view.HalfHeight;
            camera.nearClipPlane = (float)view.Near;
            camera.farClipPlane = (float)view.Far;
            // set the projection itself, after the target: Unity resets the aspect ratio to the target's,
            // which would squeeze the Factorio view (its height is stretched 1.25 times)
            camera.projectionMatrix = Matrix4x4.Ortho((float)-view.HalfWidth, (float)view.HalfWidth,
                (float)-view.HalfHeight, (float)view.HalfHeight, (float)view.Near, (float)view.Far);
        }

        /// <summary>
        /// The avatar as posed now, as a transparent frame at the canvas size (premultiplied, linear light):
        /// rendered on black and on white, matted and reduced by <see cref="Supersampling"/>.
        /// </summary>
        public ImageF RenderFrame()
        {
            (ImageF onBlack, ImageF onWhite) = RenderBlackAndWhite();
            return Downsample.Box(Matting.FromBlackAndWhite(onBlack, onWhite, linear), Supersampling);
        }

        /// <summary>True when renders hold linear light (the project's colour space is Linear).</summary>
        public bool Linear => linear;

        /// <summary>
        /// The avatar as posed now, rendered on black and on white at the render size (canvas size x the
        /// canvas's supersampling), row 0 at the top: the raw input of difference matting.
        /// </summary>
        public (ImageF onBlack, ImageF onWhite) RenderBlackAndWhite()
        {
            using (new SynchronousShaders())
            {
                return (RenderChecked(Color.black), RenderChecked(Color.white));
            }
        }

        /// <summary>
        /// Another preview scene (a shadow proxy's) seen through this camera, on white: the shadow pass's
        /// black silhouette.
        /// </summary>
        public ImageF RenderSilhouette(Scene scene)
        {
            camera.scene = scene;
            try
            {
                return RenderOnce(Color.white, scene);
            }
            finally
            {
                camera.scene = stage.Scene;
            }
        }

        /// <summary>
        /// True when a pixel on the outer edge of <paramref name="frame"/> is drawn on: the canvas was too small
        /// and something is cut off.
        /// </summary>
        public static bool TouchesEdge(Image8 frame)
        {
            int w = frame.Width, h = frame.Height;
            for (int x = 0; x < w; x++)
            {
                if (frame[x, 0, 3] != 0 || frame[x, h - 1, 3] != 0)
                {
                    return true;
                }
            }
            for (int y = 0; y < h; y++)
            {
                if (frame[0, y, 3] != 0 || frame[w - 1, y, 3] != 0)
                {
                    return true;
                }
            }
            return false;
        }

        private ImageF RenderChecked(Color background)
        {
            for (int attempt = 1; ; attempt++)
            {
                ImageF image = RenderOnce(background, stage.Scene);
                int placeholder = PlaceholderPixels(image);
                if (placeholder == 0)
                {
                    return image;
                }
                if (attempt == RenderAttempts)
                {
                    PlaceholderLeft += placeholder;
                    return image;
                }
                Retakes++;
                CompileShaders(stage.Avatar);
                for (int wait = 0; ShaderUtil.anythingCompiling && wait < 200; wait++)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
        }

        /// <summary>Renders <paramref name="scene"/> over a solid background and reads it back with row 0 at the top.</summary>
        private ImageF RenderOnce(Color background, Scene scene)
        {
            camera.backgroundColor = background;
            Unsupported.SetOverrideLightingSettings(scene);
            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                Color ambient = lighting.AmbientColor * lighting.AmbientStrength;
                RenderSettings.ambientLight = new Color(ambient.r, ambient.g, ambient.b, 1);
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
#pragma warning disable 618 // customReflectionTexture, its replacement in 2022, isn't in older Unity
                RenderSettings.customReflection = Surroundings;
#pragma warning restore 618
                RenderSettings.reflectionIntensity = lighting.ReflectionStrength;
                camera.Render();
            }
            finally
            {
                Unsupported.RestoreOverrideLightingSettings();
            }
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, readback.width, readback.height), 0, 0, false);
            RenderTexture.active = previous;
            Color[] pixels = readback.GetPixels();
            int w = readback.width, h = readback.height;
            var image = new ImageF(w, h);
            for (int y = 0; y < h; y++)
            {
                int row = (h - 1 - y) * w;
                for (int x = 0; x < w; x++)
                {
                    Color c = pixels[row + x];
                    image[x, y, 0] = c.r;
                    image[x, y, 1] = c.g;
                    image[x, y, 2] = c.b;
                    image[x, y, 3] = 1;
                }
            }
            return image;
        }

        /// <summary>
        /// Compiles every pass of every material on <paramref name="avatar"/> now, waiting for each. Returns how
        /// many passes needed compiling.
        /// </summary>
        public static int CompileShaders(GameObject avatar)
        {
            int compiled = 0;
            var seen = new HashSet<Material>();
            foreach (Renderer renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null || !seen.Add(material))
                    {
                        continue;
                    }
                    for (int pass = 0; pass < material.passCount; pass++)
                    {
                        if (!ShaderUtil.IsPassCompiled(material, pass))
                        {
                            ShaderUtil.CompilePass(material, pass, true);
                            compiled++;
                        }
                    }
                }
            }
            return compiled;
        }

        /// <summary>Pixels in Unity's shader-compilation placeholder colour, an unlit cyan no lit surface gives.</summary>
        private static int PlaceholderPixels(ImageF image)
        {
            int count = 0;
            float[] p = image.Pixels;
            for (int i = 0; i < p.Length; i += 4)
            {
                if (p[i] < 0.02f && p[i + 1] > 0.97f && p[i + 2] > 0.97f)
                {
                    count++;
                }
            }
            return count;
        }

        private void ReleaseTargets()
        {
            camera.targetTexture = null;
            foreach ((RenderTexture t, Texture2D r) in targets.Values)
            {
                Object.DestroyImmediate(t);
                Object.DestroyImmediate(r);
            }
            targets.Clear();
            target = null;
            readback = null;
        }

        public void Dispose()
        {
            ReleaseTargets();
            Object.DestroyImmediate(studio);
        }

        /// <summary>Turns asynchronous shader compilation off while it lives, then restores the settings.</summary>
        private sealed class SynchronousShaders : IDisposable
        {
            private readonly bool allowAsync = ShaderUtil.allowAsyncCompilation;
            private readonly bool asyncSetting = EditorSettings.asyncShaderCompilation;

            public SynchronousShaders()
            {
                ShaderUtil.allowAsyncCompilation = false;
                EditorSettings.asyncShaderCompilation = false;
            }

            public void Dispose()
            {
                ShaderUtil.allowAsyncCompilation = allowAsync;
                EditorSettings.asyncShaderCompilation = asyncSetting;
            }
        }
    }
}
