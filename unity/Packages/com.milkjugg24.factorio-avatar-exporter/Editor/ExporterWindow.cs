using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MilkJugg24.FactorioAvatarExporter.Core;
using UnityEditor;
using UnityEngine;
using AnimationInfo = MilkJugg24.FactorioAvatarExporter.Core.AnimationInfo;

namespace MilkJugg24.FactorioAvatarExporter.Editor
{
    /// <summary>
    /// Tools > Factorio Avatar Exporter > Exporter: loads an avatar, reports on its rig, previews it posed in
    /// every animation, direction and frame of an avatar pack, and exports the pack (Export All).
    /// </summary>
    public sealed class ExporterWindow : EditorWindow
    {
        private const double FramesPerSecond = 12;
        private static readonly Color Ground = new Color(0.36f, 0.36f, 0.36f);
        private static readonly Color Anchor = new Color(1f, 0.85f, 0.2f);

        [SerializeField] private GameObject source;
        [SerializeField] private List<GameObject> leaveOut = new List<GameObject>();
        [SerializeField] private int animationIndex;
        [SerializeField] private int row = 4;
        [SerializeField] private int frame;
        [SerializeField] private int zoom = 2;
        [SerializeField] private StageLighting lighting = new StageLighting();

        // the pack
        [SerializeField] private string packFor;
        [SerializeField] private string packTitle;
        [SerializeField] private string packId;
        [SerializeField] private string author = PackContent.DefaultAuthor;
        [SerializeField] private string packVersion = PackContent.DefaultVersion;
        [SerializeField] private int pixelsPerTile = Conventions.DefaultPixelsPerTile;
        [SerializeField] private bool flight = true;
        [SerializeField] private string saveFolder;
        private string exported;
        // the last Check fit's result, for the resolution and flight it was made with
        private string fitReport;
        private bool fitTrouble;
        private (int, bool) fitFor;
        private List<string> edgeDrawers = new List<string>();

        private PosePreview preview;
        private string error;
        private bool playing;
        private double nextFrameAt;
        private Texture2D texture;
        private PreviewFrame shown;
        private Vector2 scroll;

        [MenuItem("Tools/Factorio Avatar Exporter/Exporter")]
        public static void Open()
        {
            ExporterWindow window = GetWindow<ExporterWindow>("Factorio Avatar Exporter");
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
            edgeDrawers.Clear();
            try
            {
                EditorUtility.DisplayProgressBar("Factorio Avatar Exporter", "Loading " + source.name, 0.5f);
                if (packFor != source.name)
                {
                    // another avatar: its own name and id
                    packFor = source.name;
                    packTitle = source.name;
                    packId = "";
                    leaveOut.Clear();
                }
                preview = PosePreview.Load(source, leaveOut: LeaveOutPaths());
                fitReport = null;
                preview.SetLighting(lighting);
                exported = null;
                Rerender();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = e.Message;
                Unload();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Avatar", EditorStyles.boldLabel);
            source = (GameObject)EditorGUILayout.ObjectField("Avatar", source, typeof(GameObject), true);
            using (new EditorGUI.DisabledScope(source == null))
            {
                if (GUILayout.Button(preview == null ? "Load avatar" : "Reload avatar"))
                {
                    Load();
                }
            }
            LeaveOutGUI();
            if (error != null)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            if (edgeDrawers.Count > 0 && source != null &&
                GUILayout.Button("Leave out " + string.Join(", ", edgeDrawers) + " and reload"))
            {
                foreach (string path in edgeDrawers)
                {
                    Transform t = source.transform.Find(path);
                    if (t != null && !leaveOut.Contains(t.gameObject))
                    {
                        leaveOut.Add(t.gameObject);
                    }
                }
                edgeDrawers.Clear();
                Load();
                GUIUtility.ExitGUI();
            }

            if (preview == null)
            {
                EditorGUILayout.HelpBox("Pick your avatar (the object with the VRChat avatar descriptor, or its prefab) " +
                    "and press Load avatar. A copy is made in a hidden scene; your scene and avatar are not changed.",
                    MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }
            foreach (string note in preview.Notes)
            {
                EditorGUILayout.LabelField(note, EditorStyles.wordWrappedMiniLabel);
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
            zoom = EditorGUILayout.IntSlider("Zoom", zoom, 1, 4);
            bool changed = EditorGUI.EndChangeCheck();
            playing = GUILayout.Toggle(playing, playing ? "Pause" : "Play", "Button");
            if (changed)
            {
                Rerender();
            }

            if (texture != null && shown != null)
            {
                Rect rect = GUILayoutUtility.GetRect(texture.width * zoom, texture.height * zoom,
                    GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
                EditorGUI.DrawRect(rect, Ground);
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
                // the entity position: where the character stands in the game
                var anchor = new Vector2(rect.x + shown.Canvas.AnchorX * zoom, rect.y + shown.Canvas.AnchorY * zoom);
                EditorGUI.DrawRect(new Rect(anchor.x - 4, anchor.y, 9, 1), Anchor);
                EditorGUI.DrawRect(new Rect(anchor.x, anchor.y - 4, 1, 9), Anchor);
                string check = double.IsNaN(shown.JointErrorPixels)
                    ? "pose check: not done for this pose"
                    : $"pose check: bones within {shown.JointErrorPixels:0.0} px of where Core put them" +
                      (shown.JointErrorPixels > 1 ? " (more than 1 px: please report)" : "");
                EditorGUILayout.LabelField($"{texture.width} x {texture.height} px; {check}", EditorStyles.wordWrappedMiniLabel);
                if (preview.Retakes > 0 || preview.PlaceholderLeft > 0 || preview.Growths > 0)
                {
                    EditorGUILayout.LabelField($"retaken while shaders compiled: {preview.Retakes}; placeholder pixels " +
                        $"left: {preview.PlaceholderLeft}; canvas grown to fit: {preview.Growths} times",
                        EditorStyles.wordWrappedMiniLabel);
                }
            }

            EditorGUILayout.Space();
            LightingGUI();

            EditorGUILayout.Space();
            PackGUI();

            // rarely needed: the pack is what the game uses
            EditorGUILayout.Space(16);
            if (GUILayout.Button(new GUIContent("Export Selected Animation", "Saves only the animation shown as one " +
                "image, in the preview folder, to look at it closely. Not needed for the game."), EditorStyles.miniButton))
            {
                SaveSheet(info.Name);
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Objects of the avatar to hide in the sprites, such as a fake ground shadow or a particle effect; they
        /// apply when the avatar is (re)loaded.
        /// </summary>
        private void LeaveOutGUI()
        {
            EditorGUILayout.LabelField(new GUIContent("Leave out of the sprites", "Parts of the avatar the sprites " +
                "shouldn't show, such as a fake ground shadow (the exporter draws the game's own) or effects. Drag " +
                "them in from the avatar, then press Reload avatar."), EditorStyles.miniBoldLabel);
            for (int i = 0; i < leaveOut.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    leaveOut[i] = (GameObject)EditorGUILayout.ObjectField(leaveOut[i], typeof(GameObject), true);
                    if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    {
                        leaveOut.RemoveAt(i);
                        GUIUtility.ExitGUI();
                    }
                }
            }
            var added = (GameObject)EditorGUILayout.ObjectField("Add", null, typeof(GameObject), true);
            if (added != null && !leaveOut.Contains(added))
            {
                if (source != null && added.transform.IsChildOf(source.transform) && added != source)
                {
                    leaveOut.Add(added);
                }
                else
                {
                    error = added.name + " isn't part of the avatar: drag in an object from inside it.";
                }
            }
        }

        private List<string> LeaveOutPaths() => leaveOut
            .Where(o => o != null && source != null && o.transform.IsChildOf(source.transform) && o != source)
            .Select(o => AnimationUtility.CalculateTransformPath(o.transform, source.transform))
            .ToList();

        private static readonly string[] LightingModes = { "Factorio style", "Custom" };

        /// <summary>How the avatar is lit (<see cref="StageLighting"/>); the preview follows every change.</summary>
        private void LightingGUI()
        {
            EditorGUILayout.LabelField("Lighting", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            lighting.Custom = EditorGUILayout.Popup(new GUIContent("Lighting", "Factorio style lights the avatar like " +
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
            EditorGUILayout.LabelField(lighting.Custom
                ? "The lights stay where they are as the avatar turns, so every direction is lit alike. The shadow on " +
                  "the ground always falls the way the game's shadows do."
                : "The game's light: from the left and a little in front, above, with soft light all around and a " +
                  "neutral studio for shiny materials to reflect. The shadow on the ground is drawn separately, the " +
                  "way the game's shadows fall.", EditorStyles.wordWrappedMiniLabel);
            if ((EditorGUI.EndChangeCheck() || reset) && preview != null)
            {
                preview.SetLighting(lighting);
                Rerender();
            }
        }

        private static readonly string[] Resolutions = { "64 pixels per tile (sharp)", "32 pixels per tile (a quarter of the memory)" };
        private static readonly int[] ResolutionValues = { 64, 32 };

        /// <summary>
        /// Check fit: whether every animation fits Factorio's sprite sheets at the chosen resolution, which ones
        /// will be drawn at half resolution instead, and which do not fit at all, before the long export.
        /// </summary>
        private void FitGUI()
        {
            if (fitReport != null && fitFor != (pixelsPerTile, flight))
            {
                fitReport = null;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(" ");
                if (GUILayout.Button(new GUIContent("Check fit", "Poses and measures every frame (without drawing them) " +
                    "to see whether each animation fits Factorio's sprite sheets. Takes a few seconds."), GUILayout.Width(100)))
                {
                    CheckFit();
                }
            }
            if (fitReport != null)
            {
                EditorGUILayout.HelpBox(fitReport, fitTrouble ? MessageType.Warning : MessageType.Info);
            }
        }

        private void CheckFit()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Factorio Avatar Exporter", "Measuring every frame", 0.5f);
                IReadOnlyList<AnimationFit> fits = preview.CheckFit(pixelsPerTile, flight);
                fitFor = (pixelsPerTile, flight);
                List<string> half = fits.Where(f => f.Fits && f.PixelsPerTile < pixelsPerTile).Select(f => f.Animation).ToList();
                List<AnimationFit> no = fits.Where(f => !f.Fits).ToList();
                var lines = new List<string>();
                if (no.Count == 0 && half.Count == 0)
                {
                    lines.Add("Fits: every animation at full resolution.");
                }
                if (half.Count > 0)
                {
                    lines.Add("Too large for full resolution, so drawn at half (the same size in the game, a little softer): " +
                        string.Join(", ", half) + ".");
                }
                if (no.Count > 0)
                {
                    lines.Add("Too large even at half resolution: " + string.Join(", ", no.Select(f => $"{f.Animation} ({f.Width} x {f.Height} px)")) +
                        ". Leave out a part that reaches far out (a long tail, wide wings, a big accessory) and check again.");
                }
                lines.Add("Parts drawn past their meshes (fur, glows) aren't measured: an animation near the limit may still be halved.");
                fitReport = string.Join("\n", lines);
                fitTrouble = no.Count > 0;
            }
            catch (Exception e)
            {
                fitReport = "The check failed: " + e.Message;
                fitTrouble = true;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>The avatar pack's settings and the Export All button.</summary>
        private void PackGUI()
        {
            EditorGUILayout.LabelField("Avatar pack", EditorStyles.boldLabel);
            packTitle = EditorGUILayout.TextField(new GUIContent("Name", "Shown in the game's avatar window."), packTitle);
            string derived = PackNaming.DeriveId(packTitle ?? "");
            string id = EditorGUILayout.TextField(new GUIContent("Pack id", "Lowercase letters, digits and -. The mod is " +
                "called avatar-pack-<id>; keep it the same when you export a new version."), string.IsNullOrEmpty(packId) ? derived : packId);
            packId = id == derived ? "" : id;
            author = EditorGUILayout.TextField(new GUIContent("Author", "The name shown with the pack. Factorio needs " +
                "one for every mod; any name will do."), author);
            packVersion = EditorGUILayout.TextField(new GUIContent("Version", "Raise it (1.0.1, 1.1.0, ...) when you export " +
                "the pack again for a server: every player needs the same version."), packVersion);
            pixelsPerTile = EditorGUILayout.IntPopup("Resolution", pixelsPerTile, Resolutions, ResolutionValues);
            EditorGUILayout.LabelField(" ", preview.SizeNote, EditorStyles.miniLabel);
            flight = EditorGUILayout.Toggle(new GUIContent("Flight animations", "Hovering and flying with Space Age's mech armor. " +
                "Without them the avatar walks in the air."), flight);
            FitGUI();

            PackOptions options = Options();
            string problem = options.Problem();
            using (new EditorGUI.DisabledScope(problem != null))
            {
                if (GUILayout.Button(new GUIContent("Export All", "Asks where to save the avatar pack (a Factorio mod zip), " +
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
        /// (avatar-pack-&lt;id&gt;_&lt;version&gt;.zip). False when cancelled.
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
            Version = packVersion,
            PixelsPerTile = pixelsPerTile,
            Flight = flight,
            PreviewFolder = PreviewFolder,
        };

        private void ExportAll(PackOptions options)
        {
            playing = false;
            exported = null;
            var cancel = new CancellationTokenSource();
            try
            {
                PackExportResult result = preview.ExportPack(options, p =>
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Factorio Avatar Exporter",
                        $"Rendering {p.Animation} ({p.Done} of {p.Total})", (float)p.Fraction))
                    {
                        cancel.Cancel();
                    }
                }, cancel.Token);
                exported = $"Saved {result.ZipPath}. Put it, still zipped, in Factorio's mods folder together with the " +
                    "Avatar Characters mod, start Factorio and choose " + $"\"{options.Title.Trim()}\" in the avatar window " +
                    "(shortcut bar, or Ctrl + Shift + A). README.md in Assets/FactorioAvatarExporter has every step. " +
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
                // the failing frame is still set up: find what draws on its edge
                edgeDrawers = preview.EdgeDrawers() ?? new List<string>();
                List<string> suspects = edgeDrawers.Count > 0 ? edgeDrawers : preview.DrawnOutsideMeshes();
                error = "Something on the avatar reaches past the edge of the picture, so it would be cut off, and " +
                    "nothing was saved. " + (edgeDrawers.Count > 0
                        ? "Found by switching the avatar's parts on one at a time: " + string.Join(", ", edgeDrawers) +
                          ". Leave them out of the sprites with the button below, then export again."
                        : (suspects.Count > 0 ? "It may be one of these: " + string.Join("; ", suspects) + ". " : "") +
                          "Find it in the preview, add it to Leave out of the sprites at the top and press Reload " +
                          "avatar.") + " (" + e.Message + ")";
                Debug.LogWarning("Factorio Avatar Exporter: " + error);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = e.Message;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                cancel.Dispose();
            }
        }

        private void Update()
        {
            if (!playing || preview == null || EditorApplication.timeSinceStartup < nextFrameAt)
            {
                return;
            }
            nextFrameAt = EditorApplication.timeSinceStartup + 1 / FramesPerSecond;
            frame = (frame + 1) % AnimationSpec.All[animationIndex].FrameCount;
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
