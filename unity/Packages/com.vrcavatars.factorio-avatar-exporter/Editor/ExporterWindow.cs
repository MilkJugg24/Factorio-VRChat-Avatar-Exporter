using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FactorioAvatarExporter.Core;
using UnityEditor;
using UnityEngine;
using AnimationInfo = FactorioAvatarExporter.Core.AnimationInfo;

namespace FactorioAvatarExporter.Editor
{
    /// <summary>
    /// Tools > Factorio Avatar Exporter: loads an avatar, reports on its rig, previews it posed in
    /// every animation, direction and frame of an avatar pack, and exports the pack (Export Avatar to .zip).
    /// </summary>
    public sealed class ExporterWindow : EditorWindow
    {
        /// <summary>The engineer's running speed, tiles per tick (the game's 0.15).</summary>
        private const double RunningSpeed = 0.15;
        private static readonly Color Ground = new Color(0.36f, 0.36f, 0.36f);
        private static readonly Color Anchor = new Color(1f, 0.85f, 0.2f);

        [SerializeField] private GameObject source;
        [SerializeField] private int animationIndex;
        [SerializeField] private int row = 4;
        [SerializeField] private int frame;
        // the preview's own zoom, 1 showing the whole frame, and the frame's pixel at the middle of the box
        private float viewZoom = 1;
        private Vector2 viewCentre;
        [SerializeField] private StageLighting lighting = new StageLighting();

        // the pack
        [SerializeField] private string packFor;
        [SerializeField] private string packTitle;
        [SerializeField] private string packId;
        // starts empty: the user types the name to show (never filled in from their account)
        [SerializeField] private string author = "";
        [SerializeField] private int pixelsPerTile = PackOptions.DefaultPixelsPerTile;
        [SerializeField] private string saveFolder;
        private string exported;

        private PosePreview preview;
        private string error;
        private bool playing;
        private double playClock;
        private Texture2D texture;
        private PreviewFrame shown;
        private Vector2 scroll;
        [SerializeField] private bool showReport;

        /// <summary>Width of the column of settings on the left; the preview takes the rest.</summary>
        private const float ControlsWidth = 470;

        /// <summary>The size the window opens at: the settings, and the preview beside them as tall as they are.</summary>
        private static readonly Vector2 OpenSize = new Vector2(ControlsWidth + 440, 600);
        [MenuItem("Tools/Factorio Avatar Exporter")]
        public static void Open()
        {
            ExporterWindow window = GetWindow<ExporterWindow>("Factorio Avatar Exporter");
            window.minSize = new Vector2(ControlsWidth + 240, 480);
            // just big enough for everything, without scrolling; a floating window only (a docked one keeps its place)
            if (!window.docked)
            {
                Rect main = EditorGUIUtility.GetMainWindowPosition();
                window.position = new Rect(main.x + Mathf.Max(0, (main.width - OpenSize.x) / 2),
                    main.y + Mathf.Max(0, (main.height - OpenSize.y) / 2), OpenSize.x, OpenSize.y);
            }
            if (window.source == null && Selection.activeGameObject != null)
            {
                window.source = Selection.activeGameObject;
            }
        }

        private void OnDisable()
        {
            Unload();
        }

        private void Unload()
        {
            playing = false;
            preview?.Dispose();
            preview = null;
            shown = null;
            if (texture != null)
            {
                DestroyImmediate(texture);
                texture = null;
            }
        }

        private void Load()
        {
            Unload();
            error = null;
            try
            {
                EditorUtility.DisplayProgressBar("Factorio Avatar Exporter", "Loading " + source.name, 0.5f);
                if (packFor != source.name)
                {
                    // another avatar: its own name and id
                    packFor = source.name;
                    packTitle = source.name;
                    packId = "";
                }
                preview = PosePreview.Load(source);
                preview.SetLighting(lighting);
                exported = null;
                Rerender();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = e.Message + ErrorLog.Note(ErrorLog.Write("Load avatar (" + source.name + ")", e, preview, null, lighting, null));
                Unload();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (var column = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.Width(ControlsWidth)))
                {
                    scroll = column.scrollPosition;
                    ControlsGUI();
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    PreviewImageGUI();
                }
            }
        }

        /// <summary>The left column: the avatar, the preview's controls, the lighting and the pack.</summary>
        private void ControlsGUI()
        {
            EditorGUILayout.LabelField("Avatar", EditorStyles.boldLabel);
            source = (GameObject)EditorGUILayout.ObjectField("Choose Avatar", source, typeof(GameObject), true);
            using (new EditorGUI.DisabledScope(source == null))
            {
                if (GUILayout.Button(preview == null ? "Load avatar" : "Reload avatar"))
                {
                    Load();
                }
            }
            if (error != null)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            if (preview == null)
            {
                EditorGUILayout.HelpBox("Pick your avatar (the object with the VRChat avatar descriptor, or its prefab) " +
                    "and press Load avatar. A copy is made in a hidden scene; your scene and avatar are not changed.",
                    MessageType.Info);
                return;
            }
            showReport = EditorGUILayout.Foldout(showReport, $"Report ({preview.Notes.Count} lines: rig, loose parts, wings, tail)", true);
            if (showReport)
            {
                foreach (string note in preview.Notes)
                {
                    EditorGUILayout.LabelField(note, EditorStyles.wordWrappedMiniLabel);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            string[] names = AnimationSpec.All.Select(a => a.Name).ToArray();
            EditorGUI.BeginChangeCheck();
            animationIndex = EditorGUILayout.Popup("Animation", Mathf.Clamp(animationIndex, 0, names.Length - 1), names);
            AnimationInfo info = AnimationSpec.All[animationIndex];
            row = EditorGUILayout.IntSlider("Direction", Mathf.Clamp(row, 0, info.DirectionCount - 1), 0, info.DirectionCount - 1);
            EditorGUILayout.LabelField(" ", PosePreview.DirectionName(info.Name, row));
            frame = EditorGUILayout.IntSlider("Frame", Mathf.Clamp(frame, 0, info.FrameCount - 1), 0, info.FrameCount - 1);
            bool changed = EditorGUI.EndChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                // rarely needed: the pack is what the game uses
                if (GUILayout.Button(new GUIContent("Export Selected Animation", "Saves only the animation shown as one " +
                    "image, in the preview folder, to look at it closely. Not needed for the game.")))
                {
                    SaveSheet(info.Name);
                }
                bool play = GUILayout.Toggle(playing, playing ? "Pause" : "Play", "Button");
                if (play && !playing)
                {
                    playClock = EditorApplication.timeSinceStartup;
                }
                playing = play;
            }
            if (changed)
            {
                Rerender();
            }

            EditorGUILayout.Space();
            LightingGUI();

            EditorGUILayout.Space();
            PackGUI();
        }

        /// <summary>The right column: the frame shown, on the ground, with where the character stands.</summary>
        private void PreviewImageGUI()
        {
            if (preview == null || texture == null || shown == null)
            {
                return;
            }
            Rect box = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            var size = new Vector2(texture.width, texture.height);
            // zoom 1 fits the whole frame in the box
            float fit = Mathf.Min(box.width / size.x, box.height / size.y);
            if (viewZoom <= 1)
            {
                viewZoom = 1;
                viewCentre = size / 2;
            }
            Event e = Event.current;
            if (box.Contains(e.mousePosition))
            {
                if (e.type == EventType.ScrollWheel)
                {
                    // zoom about the pixel under the mouse
                    Vector2 fromMiddle = e.mousePosition - box.center;
                    Vector2 under = viewCentre + fromMiddle / (fit * viewZoom);
                    viewZoom = Mathf.Clamp(viewZoom * (e.delta.y < 0 ? 1.25f : 0.8f), 1, 16);
                    viewCentre = under - fromMiddle / (fit * viewZoom);
                    e.Use();
                    Repaint();
                }
                else if (e.type == EventType.MouseDown && e.clickCount == 2)
                {
                    viewZoom = 1;
                    e.Use();
                    Repaint();
                }
                else if (e.type == EventType.MouseDrag)
                {
                    viewCentre -= e.delta / (fit * viewZoom);
                    e.Use();
                    Repaint();
                }
            }
            viewCentre = Vector2.Max(Vector2.zero, Vector2.Min(size, viewCentre));
            float scale = fit * viewZoom;
            EditorGUI.DrawRect(box, Ground);
            GUI.BeginClip(box);
            var rect = new Rect(box.width / 2 - viewCentre.x * scale, box.height / 2 - viewCentre.y * scale,
                size.x * scale, size.y * scale);
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
            // the entity position: where the character stands in the game
            var anchor = new Vector2(rect.x + shown.Canvas.AnchorX * scale, rect.y + shown.Canvas.AnchorY * scale);
            EditorGUI.DrawRect(new Rect(anchor.x - 4, anchor.y, 9, 1), Anchor);
            EditorGUI.DrawRect(new Rect(anchor.x, anchor.y - 4, 1, 9), Anchor);
            GUI.EndClip();
            EditorGUILayout.LabelField("Scroll to zoom, drag to pan, and double-click to reset zoom.",
                EditorStyles.wordWrappedMiniLabel);
            if (preview.Retakes > 0 || preview.PlaceholderLeft > 0 || preview.Growths > 0)
            {
                EditorGUILayout.LabelField($"retaken while shaders compiled: {preview.Retakes}; placeholder pixels " +
                    $"left: {preview.PlaceholderLeft}; canvas grown to fit: {preview.Growths} times",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static readonly string[] LightingModes = { "Factorio style", "Custom" };

        /// <summary>How the avatar is lit (<see cref="StageLighting"/>); the preview follows every change.</summary>
        private void LightingGUI()
        {
            EditorGUILayout.LabelField("Lighting", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            lighting.Custom = EditorGUILayout.Popup(new GUIContent("Lighting Style", "Factorio style lights the avatar like " +
                "the game's own characters. Custom lets you set the lights yourself, for example for a shiny avatar."),
                lighting.Custom ? 1 : 0, LightingModes) == 1;
            bool reset = false;
            if (lighting.Custom)
            {
                lighting.KeyDirection = EditorGUILayout.Slider(new GUIContent("Key light from", "Where the main light " +
                    "comes from, in degrees around the avatar as you see it: 0 from behind, 90 from the right, 180 from " +
                    "the front, 270 from the left. Factorio style: " + Mathf.Round(StageLighting.FactorioKeyDirection) + "."),
                    lighting.KeyDirection, 0, 360);
                lighting.KeyHeight = EditorGUILayout.Slider(new GUIContent("Key light height", "Degrees above the " +
                    "horizon; 90 is straight above. Factorio style: " + Mathf.Round(StageLighting.FactorioKeyHeight) + "."),
                    lighting.KeyHeight, 0, 90);
                lighting.KeyStrength = EditorGUILayout.Slider("Key light strength", lighting.KeyStrength, 0, 3);
                lighting.KeyColor = EditorGUILayout.ColorField("Key light colour", lighting.KeyColor);
                lighting.FillStrength = EditorGUILayout.Slider(new GUIContent("Fill light", "A softer light from the " +
                    "opposite side, so the side away from the key light isn't dark."), lighting.FillStrength, 0, 2);
                lighting.RimStrength = EditorGUILayout.Slider(new GUIContent("Rim light", "A light from behind that " +
                    "draws bright edges around the avatar."), lighting.RimStrength, 0, 2);
                lighting.AmbientStrength = EditorGUILayout.Slider(new GUIContent("Ambient light", "Light from every " +
                    "direction, so nothing is pitch black."), lighting.AmbientStrength, 0, 1.5f);
                lighting.AmbientColor = EditorGUILayout.ColorField("Ambient colour", lighting.AmbientColor);
                lighting.ReflectionStrength = EditorGUILayout.Slider(new GUIContent("Reflections", "How strongly shiny " +
                    "and metallic materials reflect their surroundings."), lighting.ReflectionStrength, 0, 2);
                lighting.Environment = (Cubemap)EditorGUILayout.ObjectField(new GUIContent("Reflected surroundings",
                    "Optional: a cubemap shiny materials reflect, such as a skybox or an HDRI imported with Texture " +
                    "Shape: Cube. Empty: a neutral studio, bright above and dark below."), lighting.Environment, typeof(Cubemap), false);
                reset = GUILayout.Button("Reset to Factorio style");
                if (reset)
                {
                    lighting = new StageLighting { Custom = true };
                    GUI.FocusControl(null);
                }
            }
            if ((EditorGUI.EndChangeCheck() || reset) && preview != null)
            {
                preview.SetLighting(lighting);
                Rerender();
            }
        }

        private static readonly string[] Resolutions = { "Normal Quality", "High Quality" };
        private static readonly int[] ResolutionValues = { 32, 64 };

        private static readonly Color MissingColor = new Color(0.9f, 0.2f, 0.2f);

        /// <summary>A text field that must be filled in before exporting: outlined in red with a note while it's empty.</summary>
        private static string RequiredField(GUIContent label, string value)
        {
            string result = EditorGUILayout.TextField(label, value);
            if (string.IsNullOrWhiteSpace(result))
            {
                Rect box = GUILayoutUtility.GetLastRect();
                box.xMin += EditorGUIUtility.labelWidth + 2;
                foreach (Rect edge in new[]
                {
                    new Rect(box.x, box.y, box.width, 2), new Rect(box.x, box.yMax - 2, box.width, 2),
                    new Rect(box.x, box.y, 2, box.height), new Rect(box.xMax - 2, box.y, 2, box.height),
                })
                {
                    EditorGUI.DrawRect(edge, MissingColor);
                }
                var note = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = MissingColor } };
                EditorGUILayout.LabelField(" ", "Required: fill this in to export.", note);
            }
            return result;
        }

        /// <summary>The avatar pack's settings and the export button.</summary>
        private void PackGUI()
        {
            EditorGUILayout.LabelField("Avatar pack", EditorStyles.boldLabel);
            packTitle = RequiredField(new GUIContent("Name", "Shown in the game's avatar window and mod list."), packTitle);
            string derived = PackNaming.DeriveId(packTitle ?? "");
            string id = RequiredField(new GUIContent("Pack id", "Lowercase letters, digits and -. The mod is " +
                "called vrc-avatar-<id>; keep it the same when you export again, so the new pack replaces the old one."),
                string.IsNullOrEmpty(packId) ? derived : packId);
            packId = id == derived ? "" : id;
            author = RequiredField(new GUIContent("Author", "Your name as you'd like it shown with the pack (\"VRC Avatar: " +
                "<name> by <author>\"). Factorio needs one for every mod; any name will do."), author);
            // always 1.0.0: a new export of the same avatar simply replaces the old zip, for everyone
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(new GUIContent("Version", "Always " + PackContent.DefaultVersion + ", so a new export " +
                    "of your avatar has the same file name and simply replaces the old one."), PackContent.DefaultVersion);
            }
            pixelsPerTile = EditorGUILayout.IntPopup("Resolution", pixelsPerTile, Resolutions, ResolutionValues);
            EditorGUILayout.LabelField(" ", preview.SizeNote, EditorStyles.miniLabel);

            PackOptions options = Options();
            string problem = options.Problem();
            using (new EditorGUI.DisabledScope(problem != null))
            {
                if (GUILayout.Button(new GUIContent("Export Avatar to .zip", "Asks where to save the avatar pack (a Factorio mod zip), " +
                    "then renders every animation with its shadow. Takes a few minutes."), GUILayout.Height(28)))
                {
                    if (ChooseZip(options))
                    {
                        ExportAll(options);
                    }
                }
            }
            if (problem != null)
            {
                EditorGUILayout.LabelField(problem, EditorStyles.wordWrappedMiniLabel);
            }
            if (exported != null)
            {
                EditorGUILayout.HelpBox(exported, MessageType.Info);
            }
        }

        /// <summary>
        /// Asks where to save the pack, under the file name Factorio expects for a mod
        /// (vrc-avatar-&lt;id&gt;_&lt;version&gt;.zip). False when cancelled.
        /// </summary>
        private bool ChooseZip(PackOptions options)
        {
            string name = PackNaming.ZipFileName(options.Id, options.Version);
            string folder = !string.IsNullOrEmpty(saveFolder) && Directory.Exists(saveFolder)
                ? saveFolder
                : Path.GetDirectoryName(Application.dataPath);
            string path = EditorUtility.SaveFilePanel("Save the avatar pack", folder, name, "zip");
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            if (Path.GetFileName(path) != name)
            {
                if (!EditorUtility.DisplayDialog("Keep the pack's file name",
                    "Factorio expects a mod's zip to be named after the mod and its version, so this pack must be called " +
                    name + ". Save it under that name in the folder you chose?", "Save as " + name, "Cancel"))
                {
                    return false;
                }
                path = Path.Combine(Path.GetDirectoryName(path), name);
            }
            saveFolder = Path.GetDirectoryName(path);
            options.ZipPath = path;
            return true;
        }

        private PackOptions Options() => new PackOptions
        {
            Title = packTitle,
            Id = string.IsNullOrEmpty(packId) ? PackNaming.DeriveId(packTitle ?? "") : packId,
            Author = author,
            Version = PackContent.DefaultVersion,
            PixelsPerTile = pixelsPerTile,
            PreviewFolder = PreviewFolder,
        };

        private void ExportAll(PackOptions options)
        {
            playing = false;
            exported = null;
            var cancel = new CancellationTokenSource();
            var console = new List<string>();
            void Listen(string message, string stack, LogType type)
            {
                if (type != LogType.Log && console.Count < 200)
                {
                    console.Add($"{type}: {message}");
                }
            }
            Application.logMessageReceived += Listen;
            try
            {
                PackExportResult result = preview.ExportPack(options, p =>
                {
                    string step = p.Total == 0 ? "Measuring every frame, to check that it all fits" : $"Rendering {p.Animation} ({p.Done} of {p.Total})";
                    if (EditorUtility.DisplayCancelableProgressBar("Factorio Avatar Exporter", step, p.Total == 0 ? 0 : (float)p.Fraction))
                    {
                        cancel.Cancel();
                    }
                }, cancel.Token);
                exported = $"Saved {result.ZipPath}. Put it, still zipped, in Factorio's mods folder together with the " +
                    "VRC Avatars mod, start Factorio and choose " + $"\"{options.Title.Trim()}\" in the avatar window " +
                    "(shortcut bar, or Ctrl + Shift + A). The project page on GitHub has every step. " +
                    $"A report and preview images are in {options.PreviewFolder}." + (result.CutOff.Count > 0
                        ? $" Something reached the edge of the picture in {result.CutOff.Count} frames and was cut off " +
                          $"there (the first: {result.CutOff[0]}); look at the preview images to see whether anything " +
                          "visible is missing."
                        : "");
                Debug.Log("Factorio Avatar Exporter:\n" + result.Report);
                EditorUtility.RevealInFinder(result.ZipPath);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Factorio Avatar Exporter: export cancelled; nothing was written");
            }
            catch (InvalidOperationException e) when (e.Message.Contains("drew outside the bounds"))
            {
                Debug.LogException(e);
                string log = ErrorLog.Write("Export Avatar to .zip", e, preview, options, lighting, console);
                // the failing frame is still set up: find what draws on its edge
                List<string> edgeDrawers = preview.EdgeDrawers() ?? new List<string>();
                List<string> suspects = edgeDrawers.Count > 0 ? edgeDrawers : preview.DrawnOutsideMeshes();
                error = "Something on the avatar reaches past the edge of the picture, so it would be cut off, and " +
                    "nothing was saved. " + (edgeDrawers.Count > 0
                        ? "Found by switching the avatar's parts on one at a time: " + string.Join(", ", edgeDrawers) + ". "
                        : (suspects.Count > 0 ? "It may be one of these: " + string.Join("; ", suspects) + ". " : "")) +
                    "Turn that part off in your avatar (untick it in the Hierarchy), press Reload avatar and export " +
                    "again. (" + e.Message + ")";
                error += ErrorLog.Note(log);
                Debug.LogWarning("Factorio Avatar Exporter: " + error);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = e.Message + ErrorLog.Note(ErrorLog.Write("Export Avatar to .zip", e, preview, options, lighting, console));
            }
            finally
            {
                Application.logMessageReceived -= Listen;
                EditorUtility.ClearProgressBar();
                cancel.Dispose();
            }
        }

        /// <summary>
        /// How fast the game plays <paramref name="info"/>: animation_speed frames a tick, 60 ticks a second. Running
        /// and flying frames go by the distance covered instead (distance_per_frame / animation_speed tiles each), here
        /// at the engineer's running speed.
        /// </summary>
        private double GameFramesPerSecond(AnimationInfo info)
        {
            bool moving = info.Name == AnimationSpec.Run || info.Name == AnimationSpec.RunGun ||
                info.Name == AnimationSpec.Fly || info.Name == AnimationSpec.FlyGun;
            double perTick = moving ? RunningSpeed * info.DefaultSpeed / preview.Timing.DistancePerFrame : info.DefaultSpeed;
            return 60 * perTick;
        }

        private void Update()
        {
            if (!playing || preview == null)
            {
                return;
            }
            AnimationInfo info = AnimationSpec.All[animationIndex];
            double fps = GameFramesPerSecond(info), now = EditorApplication.timeSinceStartup;
            int steps = (int)((now - playClock) * fps);
            if (steps <= 0)
            {
                return;
            }
            // frames the editor was too slow to show are skipped, so the animation keeps the game's pace
            playClock = now - playClock > 1 ? now : playClock + steps / fps;
            frame = (frame + steps) % info.FrameCount;
            Rerender();
            Repaint();
        }

        private void Rerender()
        {
            if (preview == null)
            {
                return;
            }
            try
            {
                shown = preview.Render(AnimationSpec.All[animationIndex].Name, row, frame);
                texture = ToTexture(shown.Image, texture);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = e.Message;
                playing = false;
            }
        }

        private string PreviewFolder => Path.Combine(Path.GetDirectoryName(Application.dataPath), "FactorioAvatarExport",
            preview.AvatarName, "preview");

        private void SaveSheet(string animation)
        {
            playing = false;
            try
            {
                Image8 sheet = preview.Sheet(animation, out double worst,
                    p => EditorUtility.DisplayProgressBar("Factorio Avatar Exporter", "Rendering " + animation, p));
                string folder = PreviewFolder;
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, animation + ".png");
                File.WriteAllBytes(path, PngWriter.Encode(sheet));
                Debug.Log($"Factorio Avatar Exporter: wrote {path} (a row per direction; bones within {worst:0.0} px of Core's joints)");
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = e.Message;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>A Core image as a texture to draw (Unity textures have row 0 at the bottom).</summary>
        private static Texture2D ToTexture(Image8 image, Texture2D reuse)
        {
            Texture2D texture = reuse;
            if (texture == null || texture.width != image.Width || texture.height != image.Height)
            {
                if (texture != null)
                {
                    DestroyImmediate(texture);
                }
                texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
            var pixels = new Color32[image.Width * image.Height];
            for (int y = 0; y < image.Height; y++)
            {
                int row = (image.Height - 1 - y) * image.Width;
                for (int x = 0; x < image.Width; x++)
                {
                    pixels[row + x] = new Color32(image[x, y, 0], image[x, y, 1], image[x, y, 2], image[x, y, 3]);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
