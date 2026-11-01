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
    /// Tools > Factorio Avatar Exporter: loads an avatar, reports on its rig, plays every animation of an avatar pack
    /// side by side while the animation tuning is adjusted, and exports the pack (Export Avatar to .zip).
    /// </summary>
    public sealed class ExporterWindow : EditorWindow
    {
        /// <summary>The engineer's running speed, tiles per tick (the game's 0.15).</summary>
        private const double RunningSpeed = 0.15;
        internal static readonly Color Ground = new Color(0.36f, 0.36f, 0.36f);
        private static readonly Color Anchor = new Color(1f, 0.85f, 0.2f);

        [SerializeField] private GameObject source;
        // the direction every preview faces, 0 north, clockwise; south shows the face
        [SerializeField] private int direction = 4;
        [SerializeField] private PoseTuning tuning = new PoseTuning();
        // animations left out of the preview (not rendered), to keep it quick while tuning; the corpse starts hidden
        [SerializeField] private List<string> hiddenAnimations = new List<string> { AnimationSpec.Corpse };
        [SerializeField] private bool showAnimationList = true;
        // wing and tail bones chosen by hand for the avatar loaded (Advanced)
        [SerializeField] private BoneChoices boneChoices = new BoneChoices();
        [SerializeField] private bool showAdvanced;
        // the help texts opened with their "?" buttons, by the label of their row
        private readonly HashSet<string> openHelp = new HashSet<string>();
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
        private bool playing = true;
        private readonly List<PreviewCell> cells = new List<PreviewCell>();
        // frames still to render, in the order they are wanted: the first frame of every animation, then the rest
        private readonly List<(PreviewCell cell, int frame)> pending = new List<(PreviewCell, int)>();
        // the tuning or lighting sliders moved: applied once they rest, so dragging one doesn't restart the rendering
        // every step
        private double tuningChangedAt = -1;
        private double lightingChangedAt = -1;
        // while a tuning slider is held: the poses the frames on show were rendered from, the animations it changes,
        // and the part of the guide lines it moves
        private HumanoidAnimations renderedPoses;
        private HashSet<string> affected;
        private GuidePart? activePart;
        // lines over the preview along the bones, wings and tail, each part in its own colour
        [SerializeField] private bool showGuides = true;
        private Vector2 scroll;
        [SerializeField] private bool showReport;

        /// <summary>Width of the column of settings on the left; the preview takes the rest.</summary>
        private const float ControlsWidth = 470;

        /// <summary>The size the window opens at: the settings, and the grid of previews beside them as tall as they are.</summary>
        private static readonly Vector2 OpenSize = new Vector2(ControlsWidth + 640, 760);
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

        /// <summary>What the command line's window check asks the next GUI passes to do, as a click would.</summary>
        internal sealed class CheckRequests
        {
            public bool Load;
            public bool OpenHelp;
            public string ExportTo;

            /// <summary>Where each row with a "?" button was last drawn, in the scrolled column.</summary>
            public readonly Dictionary<string, Rect> Rows = new Dictionary<string, Rect>();
        }

        private readonly CheckRequests check = new CheckRequests();

        internal CheckRequests Check => check;

        internal bool CheckBusy => preview == null || pending.Count > 0 || tuningChangedAt >= 0;

        internal void CheckChoose(GameObject avatar, string author)
        {
            source = avatar;
            this.author = author;
        }

        /// <summary>True in the GUI pass the check starts with a key event: requests act where a click would, not in layout.</summary>
        private static bool CheckPass => Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F15;

        internal void CheckAct() => SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.F15 });

        internal void CheckScrollTo(string label) => scroll.y = Mathf.Max(0, check.Rows[label].y - 100);

        /// <summary>Where a point along a row's slider is in the window, as scrolled now.</summary>
        internal Vector2 CheckSliderPoint(string label, float along)
        {
            Rect row = check.Rows[label];
            float low = EditorGUIUtility.labelWidth + 8, high = row.width - 60;
            return new Vector2(row.x + low + (high - low) * along - scroll.x, row.center.y - scroll.y);
        }

        private void Unload()
        {
            ClearFrames();
            preview?.Dispose();
            preview = null;
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
                    boneChoices = new BoneChoices();
                }
                preview = PosePreview.Load(source, choices: boneChoices);
                preview.SetLighting(lighting);
                preview.SetTuning(tuning);
                exported = null;
                playing = true;
                InvalidateFrames();
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
                if (GUILayout.Button(preview == null ? "Load avatar" : "Reload avatar") || (check.Load && CheckPass))
                {
                    check.Load = false;
                    Load();
                    EndGUIAfterLongAction();
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
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                direction = EditorGUILayout.IntSlider("Direction", Mathf.Clamp(direction, 0, 7), 0, 7);
                if (EditorGUI.EndChangeCheck())
                {
                    InvalidateFrames();
                }
                playing = GUILayout.Toggle(playing, playing ? "Pause" : "Play", "Button", GUILayout.Width(60));
            }
            EditorGUILayout.LabelField(" ", PosePreview.DirectionName(AnimationSpec.Idle, direction));
            showGuides = Helped("Guide lines", "Lines over the preview along the spine (orange), head (white, the way the " +
                "face looks), arms (blue), legs (green), wings (pink) and tail (red). They move as soon as you drag a tuning " +
                "slider, before the preview is drawn again; the part the slider moves stands out.",
                r => EditorGUI.Toggle(r, "Guide lines", showGuides));
            AnimationListGUI();

            EditorGUILayout.Space();
            TuningGUI();

            EditorGUILayout.Space();
            AdvancedGUI();

            EditorGUILayout.Space();
            LightingGUI();

            EditorGUILayout.Space();
            PackGUI();
        }

        /// <summary>
        /// The right column: the animations ticked under Animations shown, side by side and playing, or the only one
        /// ticked, large, with zoom; each on the ground, with where the character stands in the game.
        /// </summary>
        private void PreviewImageGUI()
        {
            if (preview == null || cells.Count == 0)
            {
                return;
            }
            // while a tuning slider is held, only the animations it changes
            List<PreviewCell> shown = affected == null ? cells : cells.Where(c => affected.Contains(c.Info.Name)).ToList();
            if (shown.Count == 0)
            {
                shown = cells;
            }
            if (shown.Count == 1)
            {
                FocusedGUI(shown[0]);
            }
            else
            {
                GridGUI(shown);
            }
            if (pending.Count > 0)
            {
                int total = cells.Sum(c => c.Frames.Length);
                EditorGUILayout.LabelField($"Rendering the previews: {total - pending.Count} of {total} frames",
                    EditorStyles.wordWrappedMiniLabel);
            }
            if (preview.Retakes > 0 || preview.PlaceholderLeft > 0 || preview.Growths > 0)
            {
                EditorGUILayout.LabelField($"retaken while shaders compiled: {preview.Retakes}; placeholder pixels " +
                    $"left: {preview.PlaceholderLeft}; canvas grown to fit: {preview.Growths} times",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        /// <summary>The animations shown, in a grid, all drawn at the same scale.</summary>
        private void GridGUI(List<PreviewCell> cells)
        {
            Rect area = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            const float label = 16;
            int count = cells.Count, columns = 1;
            float best = 0;
            for (int c = 1; c <= count; c++)
            {
                int rows = (count + c - 1) / c;
                float side = Mathf.Min(area.width / c, area.height / rows - label);
                if (side > best)
                {
                    best = side;
                    columns = c;
                }
            }
            // below this it is the layout pass, or a window too small to show anything; the note under the grid is
            // laid out either way, as every pass of the GUI must ask for the same controls
            if (best >= 8)
            {
                float cellWidth = area.width / columns, cellHeight = best + label;
                // one scale for all, so the avatar is the same size in each
                float scale = float.MaxValue;
                foreach (PreviewCell cell in cells.Where(c => c.Canvas != null))
                {
                    scale = Mathf.Min(scale, Mathf.Min(cellWidth / cell.Canvas.Value.Width, best / cell.Canvas.Value.Height));
                }
                for (int i = 0; i < count; i++)
                {
                    PreviewCell cell = cells[i];
                    var box = new Rect(area.x + (i % columns) * cellWidth, area.y + (i / columns) * cellHeight, cellWidth - 2, best);
                    EditorGUI.DrawRect(box, Ground);
                    Texture2D frame = cell.Shown();
                    if (frame != null && cell.Canvas.HasValue && scale < float.MaxValue)
                    {
                        CanvasSpec canvas = cell.Canvas.Value;
                        var size = new Vector2(canvas.Width * scale, canvas.Height * scale);
                        var rect = new Rect(box.center - size / 2, size);
                        GUI.DrawTexture(rect, frame, ScaleMode.StretchToFill, true);
                        DrawAnchor(new Vector2(rect.x + canvas.AnchorX * scale, rect.y + canvas.AnchorY * scale));
                        GUI.BeginClip(box);
                        DrawGuides(cell, rect.position - box.position, scale);
                        GUI.EndClip();
                    }
                    GUI.Label(new Rect(box.x, box.yMax, box.width, label), cell.Info.Name, EditorStyles.centeredGreyMiniLabel);
                }
            }
            EditorGUILayout.LabelField("Tick just one animation under Animations shown to see it large, with zoom.",
                EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>One animation, large: scroll to zoom about the mouse, drag to pan, double-click to reset.</summary>
        private void FocusedGUI(PreviewCell cell)
        {
            GUILayout.Label(cell.Info.Name + ", " + PosePreview.DirectionName(cell.Info.Name, cell.Row), EditorStyles.boldLabel);
            Rect box = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(box, Ground);
            Texture2D frame = cell.Shown();
            if (frame == null || !cell.Canvas.HasValue)
            {
                return;
            }
            CanvasSpec canvas = cell.Canvas.Value;
            var size = new Vector2(canvas.Width, canvas.Height);
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
            GUI.BeginClip(box);
            var rect = new Rect(box.width / 2 - viewCentre.x * scale, box.height / 2 - viewCentre.y * scale,
                size.x * scale, size.y * scale);
            GUI.DrawTexture(rect, frame, ScaleMode.StretchToFill, true);
            DrawAnchor(new Vector2(rect.x + canvas.AnchorX * scale, rect.y + canvas.AnchorY * scale));
            DrawGuides(cell, rect.position, scale);
            GUI.EndClip();
            EditorGUILayout.LabelField("Scroll to zoom, drag to pan, and double-click to reset zoom.",
                EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>
        /// The guide lines of the frame <paramref name="cell"/> shows, as posed now (so they move with a tuning slider
        /// before the frame is rendered again), drawn over the frame placed at <paramref name="origin"/> and scaled by
        /// <paramref name="scale"/>. The part of the slider held stands out.
        /// </summary>
        private void DrawGuides(PreviewCell cell, Vector2 origin, float scale)
        {
            int frame = cell.ShownIndex();
            if (!showGuides || preview == null || Event.current.type != EventType.Repaint || !cell.Canvas.HasValue || frame < 0)
            {
                return;
            }
            List<(GuidePart part, Vec3[] points)> chains = cell.Guides[frame];
            if (chains == null)
            {
                try
                {
                    chains = preview.Guides(cell.Info.Name, cell.Row, frame);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    chains = new List<(GuidePart, Vec3[])>();
                }
                cell.Guides[frame] = chains;
            }
            CanvasSpec canvas = cell.Canvas.Value;
            double orientation = AnimationSpec.Direction(cell.Info.Name, cell.Row).Orientation;
            Color before = Handles.color;
            foreach ((GuidePart part, Vec3[] points) in chains)
            {
                Color color = PosePreview.GuideColors[part];
                bool active = activePart == part;
                if (activePart.HasValue && !active)
                {
                    color.a = 0.3f;
                }
                Vector3[] onScreen = points.Select(p =>
                {
                    (double x, double y) = PosePreview.GuidePixel(p, orientation, canvas);
                    return new Vector3(origin.x + (float)x * scale, origin.y + (float)y * scale, 0);
                }).ToArray();
                // thin enough not to hide a small avatar in the grid, thicker when zoomed in
                float width = Mathf.Clamp(scale * 2, 1, 2.5f) * (active ? 1.6f : 1);
                float dot = Mathf.Clamp(scale * 3, 0, 3);
                Handles.color = color;
                Handles.DrawAAPolyLine(width, onScreen);
                foreach (Vector3 joint in onScreen)
                {
                    if (dot >= 2)
                    {
                        EditorGUI.DrawRect(new Rect(joint.x - dot / 2, joint.y - dot / 2, dot, dot), color);
                    }
                }
            }
            Handles.color = before;
        }

        /// <summary>The entity position, where the character stands in the game: a small yellow cross.</summary>
        private static void DrawAnchor(Vector2 anchor)
        {
            EditorGUI.DrawRect(new Rect(anchor.x - 4, anchor.y, 9, 1), Anchor);
            EditorGUI.DrawRect(new Rect(anchor.x, anchor.y - 4, 1, 9), Anchor);
        }

        /// <summary>
        /// Which animations the preview shows: hidden ones are not rendered, so the preview keeps up while tuning on a
        /// slow computer. Every animation is still exported.
        /// </summary>
        private void AnimationListGUI()
        {
            int shown = AnimationSpec.All.Count(a => !hiddenAnimations.Contains(a.Name));
            showAnimationList = EditorGUILayout.Foldout(showAnimationList,
                $"Animations shown ({shown} of {AnimationSpec.All.Count})", true);
            if (!showAnimationList)
            {
                return;
            }
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("All"))
                {
                    hiddenAnimations.Clear();
                }
                if (GUILayout.Button("None"))
                {
                    hiddenAnimations = AnimationSpec.All.Select(a => a.Name).ToList();
                }
            }
            const int perRow = 3;
            for (int i = 0; i < AnimationSpec.All.Count; i += perRow)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    foreach (AnimationInfo info in AnimationSpec.All.Skip(i).Take(perRow))
                    {
                        bool on = !hiddenAnimations.Contains(info.Name);
                        bool now = EditorGUILayout.ToggleLeft(info.Name, on, GUILayout.Width(ControlsWidth / perRow - 8));
                        if (now != on)
                        {
                            if (now)
                            {
                                hiddenAnimations.Remove(info.Name);
                            }
                            else
                            {
                                hiddenAnimations.Add(info.Name);
                            }
                        }
                    }
                }
            }
            EditorGUILayout.LabelField("Only the ticked animations are drawn, so the preview keeps up while tuning; tick " +
                "just one to see it large. All of them are still exported.", EditorStyles.wordWrappedMiniLabel);
            if (EditorGUI.EndChangeCheck())
            {
                SyncShownAnimations();
            }
        }

        /// <summary>
        /// Advanced: the wing and tail bones, chosen by hand when the ones found by name aren't right. A change loads
        /// the avatar again.
        /// </summary>
        private void AdvancedGUI()
        {
            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced: wing and tail bones", true);
            if (!showAdvanced)
            {
                return;
            }
            EditorGUILayout.LabelField("Normally found by their names (\"wing\", \"tail\"); the Report says which. If they " +
                "aren't right, drag the bones here from your avatar in the Hierarchy. Empty: found automatically.",
                EditorStyles.wordWrappedMiniLabel);
            Transform root = source != null ? source.transform : null;
            BoneChoices before = boneChoices.Copy();
            boneChoices.LeftWing = BoneField("Left wing", "The left wing's first bone (its shoulder): the whole wing turns " +
                "from it.", root, boneChoices.LeftWing);
            boneChoices.RightWing = BoneField("Right wing", "The right wing's first bone.", root, boneChoices.RightWing);
            boneChoices.Tail = BoneField("Tail", "The tail's first bone; it sways from there down to its tip.", root, boneChoices.Tail);
            if (before.LeftWing != boneChoices.LeftWing || before.RightWing != boneChoices.RightWing || before.Tail != boneChoices.Tail)
            {
                if (preview != null)
                {
                    Load();
                    EndGUIAfterLongAction();
                }
            }
        }

        /// <summary>A bone of the avatar <paramref name="root"/>, kept as its path below it ("" for none).</summary>
        private string BoneField(string label, string help, Transform root, string path)
        {
            Transform current = root != null && !string.IsNullOrEmpty(path) ? root.Find(path) : null;
            var picked = Helped(label, help, r => (Transform)EditorGUI.ObjectField(r, label, current, typeof(Transform), true));
            if (picked == current)
            {
                return path;
            }
            if (picked == null)
            {
                return "";
            }
            if (root == null || picked == root || !picked.IsChildOf(root))
            {
                Debug.LogWarning($"Factorio Avatar Exporter: {picked.name} is not part of the avatar chosen above; pick one " +
                    "of its own bones.");
                return path;
            }
            return AnimationUtility.CalculateTransformPath(picked, root);
        }

        /// <summary>
        /// The animation tuning (<see cref="PoseTuning"/>): sliders per part of the avatar. The previews follow once a
        /// slider rests; the export uses them too.
        /// </summary>
        private void TuningGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Animation tuning", EditorStyles.boldLabel);
                if (GUILayout.Button("Reset", GUILayout.Width(60)))
                {
                    tuning = new PoseTuning();
                    GUI.FocusControl(null);
                    activePart = null;
                    TuningChanged();
                }
            }
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("Body", EditorStyles.miniBoldLabel);
            tuning.Lean = Tune(GuidePart.Spine, tuning.Lean, Slider("Lean", "How far the body leans into running, sprinting and flying.", tuning.Lean, 0, 2));
            tuning.HeadTilt = Tune(GuidePart.Head, tuning.HeadTilt, Slider("Head tilt", "Degrees; positive looks down, negative up.", tuning.HeadTilt, -30, 30));
            EditorGUILayout.LabelField("Arms", EditorStyles.miniBoldLabel);
            tuning.ArmSpread = Tune(GuidePart.Arms, tuning.ArmSpread, Slider("Spread", "Arms further out from the body (for wide hips, outfits or wings) or closer in.",
                tuning.ArmSpread, -1, 1));
            tuning.ArmSwing = Tune(GuidePart.Arms, tuning.ArmSwing, Slider("Swing", "How far the arms swing while running and sprinting.", tuning.ArmSwing, 0, 2));
            tuning.ElbowBend = Tune(GuidePart.Arms, tuning.ElbowBend, Slider("Elbow bend", "How far the elbows bend while running and sprinting: lower for " +
                "straighter arms swinging low at the sides, a more casual look.", tuning.ElbowBend, 0, 1));
            EditorGUILayout.LabelField("Legs", EditorStyles.miniBoldLabel);
            tuning.Stride = Tune(GuidePart.Legs, tuning.Stride, Slider("Stride", "Step length of the run and the sprint. The feet stay planted in the game.",
                tuning.Stride, 0.5f, 1.5f));
            EditorGUILayout.LabelField("Wings", EditorStyles.miniBoldLabel);
            tuning.WingFlap = Tune(GuidePart.Wings, tuning.WingFlap, Slider("Flap", "How far the wings beat.", tuning.WingFlap, 0, 2));
            tuning.WingRaise = Tune(GuidePart.Wings, tuning.WingRaise, Slider("Raise", "Degrees; the wings held higher or lower, folded or spread.", tuning.WingRaise, -45, 45));
            tuning.WingSweep = Tune(GuidePart.Wings, tuning.WingSweep, Slider("Sweep", "Degrees; the wings turned back (positive) or forward.", tuning.WingSweep, -45, 45));
            EditorGUILayout.LabelField("Arms as wings (the flap animations)", EditorStyles.miniBoldLabel);
            tuning.ArmWingStroke = Tune(GuidePart.Arms, tuning.ArmWingStroke, Slider("Stroke", "How far the arms stroke.", tuning.ArmWingStroke, 0, 1.5f));
            tuning.ArmWingRoll = Tune(GuidePart.Arms, tuning.ArmWingRoll, Slider("Roll", "Degrees the arms turn from palms down, the thumb side up; turn it to keep " +
                "the membranes from clipping.", tuning.ArmWingRoll, -60, 60));
            EditorGUILayout.LabelField("Tail", EditorStyles.miniBoldLabel);
            tuning.TailSway = Tune(GuidePart.Tail, tuning.TailSway, Slider("Sway", "How far the tail sways.", tuning.TailSway, 0, 2));
            tuning.TailLift = Tune(GuidePart.Tail, tuning.TailLift, Slider("Lift", "Degrees; the tail held higher or lower.", tuning.TailLift, -45, 45));
            if (EditorGUI.EndChangeCheck())
            {
                TuningChanged();
            }
        }

        /// <summary><paramref name="now"/>, the slider's value; when it moved, its part's guide lines stand out.</summary>
        private double Tune(GuidePart part, double before, double now)
        {
            if (now != before)
            {
                activePart = part;
            }
            return now;
        }

        /// <summary>
        /// A tuning slider moved: the poses follow at once, so the guide lines move with it, and the preview shows only
        /// the animations it changes. Rendering waits until the slider is let go (<see cref="SettleTuning"/>).
        /// </summary>
        private void TuningChanged()
        {
            tuningChangedAt = EditorApplication.timeSinceStartup;
            if (preview == null)
            {
                return;
            }
            renderedPoses = renderedPoses ?? preview.Poses;
            preview.SetTuning(tuning, keepCanvases: true);
            affected = new HashSet<string>(cells.Where(c => ChangedFrames(c).Any(changed => changed)).Select(c => c.Info.Name));
            foreach (PreviewCell cell in cells)
            {
                cell.ClearGuides();
            }
            Repaint();
        }

        /// <summary>Which frames of <paramref name="cell"/> the tuning changed since they were rendered.</summary>
        private bool[] ChangedFrames(PreviewCell cell)
        {
            var changed = new bool[cell.Frames.Length];
            for (int f = 0; f < changed.Length; f++)
            {
                changed[f] = renderedPoses != null && !PosePreview.Pose(renderedPoses, cell.Info.Name, cell.Row, f)
                    .SameAs(PosePreview.Pose(preview.Poses, cell.Info.Name, cell.Row, f));
            }
            return changed;
        }

        /// <summary>
        /// The slider was let go: the frames the tuning changed are rendered again (first), the others are kept, and the
        /// preview shows every ticked animation again.
        /// </summary>
        private void SettleTuning()
        {
            tuningChangedAt = -1;
            var redo = new List<(PreviewCell, int)>();
            foreach (PreviewCell cell in cells)
            {
                bool[] changed = ChangedFrames(cell);
                for (int f = 0; f < changed.Length; f++)
                {
                    if (changed[f] && cell.Frames[f] != null)
                    {
                        redo.Add((cell, f));
                    }
                }
            }
            // the frames shown now first, then the rest in turn
            pending.InsertRange(0, redo.OrderBy(q => q.Item2 == q.Item1.Current ? 0 : 1).ThenBy(q => q.Item2));
            renderedPoses = null;
            affected = null;
            activePart = null;
            Repaint();
        }

        private double Slider(string label, string help, double value, float low, float high) =>
            Helped(label, help, r => (double)EditorGUI.Slider(r, label, (float)value, low, high));

        private float Slider(string label, string help, float value, float low, float high) =>
            Helped(label, help, r => EditorGUI.Slider(r, label, value, low, high));

        private const float HelpButtonWidth = 18;

        /// <summary>
        /// One row of the controls with a "?" button at its end, instead of a tooltip: <paramref name="draw"/> draws
        /// the control in the rest of the row, and the button shows or hides <paramref name="help"/> under it.
        /// </summary>
        private T Helped<T>(string label, string help, Func<Rect, T> draw, float height = 0)
        {
            Rect row = EditorGUILayout.GetControlRect(false, height > 0 ? height : EditorGUIUtility.singleLineHeight);
            var button = new Rect(row.xMax - HelpButtonWidth, row.y, HelpButtonWidth, EditorGUIUtility.singleLineHeight);
            row.xMax -= HelpButtonWidth + 2;
            if (Event.current.type == EventType.Repaint)
            {
                check.Rows[label] = row;
            }
            T result = draw(row);
            // the text follows the state this pass was laid out with; a click shows or hides it from the next one
            bool open = openHelp.Contains(label);
            if (check.OpenHelp && CheckPass)
            {
                openHelp.Add(label);
            }
            if (GUI.Toggle(button, open, "?", EditorStyles.miniButton) != open)
            {
                if (open)
                {
                    openHelp.Remove(label);
                }
                else
                {
                    openHelp.Add(label);
                }
                Repaint();
            }
            if (open)
            {
                EditorGUILayout.HelpBox(help, MessageType.None);
            }
            return result;
        }

        private static readonly string[] LightingModes = { "Factorio style", "Custom" };

        /// <summary>How the avatar is lit (<see cref="StageLighting"/>); the preview follows every change.</summary>
        private void LightingGUI()
        {
            EditorGUILayout.LabelField("Lighting", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            lighting.Custom = Helped("Lighting Style", "Factorio style lights the avatar like the game's own characters. " +
                "Custom lets you set the lights yourself, for example for a shiny avatar.",
                r => EditorGUI.Popup(r, "Lighting Style", lighting.Custom ? 1 : 0, LightingModes)) == 1;
            bool reset = false;
            if (lighting.Custom)
            {
                lighting.KeyDirection = Slider("Key light from", "Where the main light comes from, in degrees around " +
                    "the avatar as you see it: 0 from behind, 90 from the right, 180 from the front, 270 from the left. " +
                    "Factorio style: " + Mathf.Round(StageLighting.FactorioKeyDirection) + ".", lighting.KeyDirection, 0, 360);
                lighting.KeyHeight = Slider("Key light height", "Degrees above the horizon; 90 is straight above. " +
                    "Factorio style: " + Mathf.Round(StageLighting.FactorioKeyHeight) + ".", lighting.KeyHeight, 0, 90);
                lighting.KeyStrength = EditorGUILayout.Slider("Key light strength", lighting.KeyStrength, 0, 3);
                lighting.KeyColor = EditorGUILayout.ColorField("Key light colour", lighting.KeyColor);
                lighting.FillStrength = Slider("Fill light", "A softer light from the opposite side, so the side away " +
                    "from the key light isn't dark.", lighting.FillStrength, 0, 2);
                lighting.RimStrength = Slider("Rim light", "A light from behind that draws bright edges around the " +
                    "avatar.", lighting.RimStrength, 0, 2);
                lighting.AmbientStrength = Slider("Ambient light", "Light from every direction, so nothing is pitch " +
                    "black.", lighting.AmbientStrength, 0, 1.5f);
                lighting.AmbientColor = EditorGUILayout.ColorField("Ambient colour", lighting.AmbientColor);
                lighting.ReflectionStrength = Slider("Reflections", "How strongly shiny and metallic materials reflect " +
                    "their surroundings.", lighting.ReflectionStrength, 0, 2);
                lighting.Environment = Helped("Reflected surroundings", "Optional: a cubemap shiny materials reflect, " +
                    "such as a skybox or an HDRI imported with Texture Shape: Cube. Empty: a neutral studio, bright above " +
                    "and dark below.", r => (Cubemap)EditorGUI.ObjectField(r, "Reflected surroundings", lighting.Environment,
                    typeof(Cubemap), false));
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
                // the previews are drawn again once the sliders rest
                lightingChangedAt = EditorApplication.timeSinceStartup;
            }
        }

        private static readonly string[] Resolutions = { "High Quality", "Low Quality" };
        private static readonly int[] ResolutionValues = { 64, 32 };

        private static readonly Color MissingColor = new Color(0.9f, 0.2f, 0.2f);

        /// <summary>A text field that must be filled in before exporting: outlined in red with a note while it's empty.</summary>
        private string RequiredField(string label, string help, string value)
        {
            Rect box = default;
            string result = Helped(label, help, r =>
            {
                box = r;
                return EditorGUI.TextField(r, label, value);
            });
            if (string.IsNullOrWhiteSpace(result))
            {
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
            packTitle = RequiredField("Name", "Shown in the game's avatar window and mod list.", packTitle);
            string derived = PackNaming.DeriveId(packTitle ?? "");
            string id = RequiredField("Pack id", "Lowercase letters, digits and -. The mod is called vrc-avatar-<id>; " +
                "keep it the same when you export again, so the new pack replaces the old one.",
                string.IsNullOrEmpty(packId) ? derived : packId);
            packId = id == derived ? "" : id;
            author = RequiredField("Author", "Your name as you'd like it shown with the pack (\"VRC Avatar: <name> by " +
                "<author>\"). Factorio needs one for every mod; any name will do.", author);
            // the exporter's own version, so a pack's file name says which exporter made it
            Helped("Version", "The version of this exporter (" + ExporterInfo.Version + "), so the pack's file name " +
                "shows which exporter made it. An export with a newer exporter gets a newer version, which Factorio uses " +
                "instead of the older one.", r =>
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        return EditorGUI.TextField(r, "Version", ExporterInfo.Version);
                    }
                });
            pixelsPerTile = EditorGUILayout.IntPopup("Resolution", pixelsPerTile, Resolutions, ResolutionValues);
            if (pixelsPerTile < 64)
            {
                EditorGUILayout.HelpBox("Low Quality: your character will look blurry when zoomed in.", MessageType.Warning);
            }

            PackOptions options = Options();
            string problem = options.Problem();
            bool export = Helped("Export Avatar to .zip", "Asks where to save the avatar pack (a Factorio mod zip), then " +
                "renders every animation with its shadow. Takes a few minutes.", r =>
                {
                    using (new EditorGUI.DisabledScope(problem != null))
                    {
                        return GUI.Button(r, "Export Avatar to .zip");
                    }
                }, 28);
            if (export || (check.ExportTo != null && CheckPass))
            {
                if (check.ExportTo != null)
                {
                    options.ZipPath = check.ExportTo;
                    check.ExportTo = null;
                    ExportAll(options);
                }
                else if (ChooseZip(options))
                {
                    ExportAll(options);
                }
                EndGUIAfterLongAction();
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
        /// Ends this OnGUI pass after something that showed a file dialog, a message box or a progress bar: Unity
        /// handled other GUI events while those were open, which left this pass's layout groups unbalanced
        /// ("EndLayoutGroup: BeginLayoutGroup must be called first"). The window draws itself again next time.
        /// </summary>
        private void EndGUIAfterLongAction()
        {
            Repaint();
            GUIUtility.ExitGUI();
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
            Version = ExporterInfo.Version,
            PixelsPerTile = pixelsPerTile,
            PreviewFolder = PreviewFolder,
        };

        private void ExportAll(PackOptions options)
        {
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
                    string step = p.Total == 0 ? "Initializing" :$"Rendering {p.Animation} ({p.Done} of {p.Total})";
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

        /// <summary>
        /// Plays every animation at the game's pace, renders the frames still missing (as many as fit in a short time,
        /// so the window stays responsive) and applies the tuning once its sliders rest.
        /// </summary>
        private void Update()
        {
            if (preview == null)
            {
                return;
            }
            double now = EditorApplication.timeSinceStartup;
            bool changed = false;
            bool holding = false;
            if (tuningChangedAt >= 0)
            {
                // nothing is rendered while a slider is held (the guide lines follow it); then the frames it changed are
                if (GUIUtility.hotControl == 0 && now - tuningChangedAt > 0.2)
                {
                    SettleTuning();
                    changed = true;
                }
                else
                {
                    holding = true;
                }
            }
            if (lightingChangedAt >= 0 && now - lightingChangedAt > 0.3)
            {
                lightingChangedAt = -1;
                InvalidateFrames();
                changed = true;
            }
            if (playing)
            {
                foreach (PreviewCell cell in cells)
                {
                    changed |= cell.Advance(now);
                }
            }
            if (!holding)
            {
                // a short slice at a time, so the window stays responsive while the previews fill in
                changed |= RenderPending(now + 0.025);
            }
            if (changed)
            {
                Repaint();
            }
        }

        /// <summary>Renders pending frames until <paramref name="until"/> (at least one); true when any was.</summary>
        private bool RenderPending(double until)
        {
            bool any = false;
            while (pending.Count > 0 && (!any || EditorApplication.timeSinceStartup < until))
            {
                (PreviewCell cell, int frame) = pending[0];
                pending.RemoveAt(0);
                any = true;
                try
                {
                    PreviewFrame rendered = preview.Render(cell.Info.Name, cell.Row, frame, Ground);
                    if (cell.Canvas.HasValue && (cell.Canvas.Value.Width != rendered.Canvas.Width
                        || cell.Canvas.Value.Height != rendered.Canvas.Height))
                    {
                        // the canvas grew to fit something: the frames already rendered are drawn again on the new one
                        for (int f = 0; f < cell.Frames.Length; f++)
                        {
                            if (f != frame && cell.Frames[f] != null)
                            {
                                DestroyImmediate(cell.Frames[f]);
                                cell.Frames[f] = null;
                                pending.Insert(0, (cell, f));
                            }
                        }
                    }
                    cell.Canvas = rendered.Canvas;
                    cell.Frames[frame] = ToTexture(rendered.Image, cell.Frames[frame]);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    error = $"{cell.Info.Name}: {e.Message}" + ErrorLog.Note(ErrorLog.Write("Preview " + cell.Info.Name, e, preview,
                        null, lighting, null));
                    pending.RemoveAll(p => p.cell == cell);
                }
            }
            return any;
        }

        /// <summary>
        /// Drops every rendered frame (the direction, the lighting or the tuning changed) and queues them all again:
        /// first the first frame of every animation, so each shows something soon, then the rest in turn.
        /// </summary>
        private void InvalidateFrames()
        {
            ClearFrames();
            if (preview == null)
            {
                return;
            }
            double now = EditorApplication.timeSinceStartup;
            foreach (AnimationInfo info in AnimationSpec.All.Where(a => !hiddenAnimations.Contains(a.Name)))
            {
                cells.Add(new PreviewCell(info, PosePreview.RowFor(info.Name, direction), GameFramesPerSecond(info), now));
            }
            QueueFrames(cells);
            Repaint();
        }

        /// <summary>Queues every frame of <paramref name="added"/>: first frames first, then the rest in turn.</summary>
        private void QueueFrames(IReadOnlyList<PreviewCell> added)
        {
            int most = added.Count == 0 ? 0 : added.Max(c => c.Frames.Length);
            for (int frame = 0; frame < most; frame++)
            {
                foreach (PreviewCell cell in added.Where(c => frame < c.Frames.Length))
                {
                    pending.Add((cell, frame));
                }
            }
        }

        /// <summary>
        /// Brings the preview in line with <see cref="hiddenAnimations"/> without drawing again what is already drawn:
        /// hidden animations go (with their frames), shown ones that are missing are added and queued.
        /// </summary>
        private void SyncShownAnimations()
        {
            if (preview == null)
            {
                return;
            }
            foreach (PreviewCell gone in cells.Where(c => hiddenAnimations.Contains(c.Info.Name)).ToList())
            {
                foreach (Texture2D frame in gone.Frames.Where(f => f != null))
                {
                    DestroyImmediate(frame);
                }
                pending.RemoveAll(q => q.cell == gone);
                cells.Remove(gone);
            }
            double now = EditorApplication.timeSinceStartup;
            var added = new List<PreviewCell>();
            foreach (AnimationInfo info in AnimationSpec.All.Where(a => !hiddenAnimations.Contains(a.Name)
                && cells.All(c => c.Info.Name != a.Name)))
            {
                added.Add(new PreviewCell(info, PosePreview.RowFor(info.Name, direction), GameFramesPerSecond(info), now));
            }
            cells.AddRange(added);
            // in the order of the animation list
            cells.Sort((a, b) => AnimationSpec.All.ToList().IndexOf(a.Info).CompareTo(AnimationSpec.All.ToList().IndexOf(b.Info)));
            QueueFrames(added);
            Repaint();
        }

        private void ClearFrames()
        {
            foreach (PreviewCell cell in cells)
            {
                foreach (Texture2D frame in cell.Frames.Where(f => f != null))
                {
                    DestroyImmediate(frame);
                }
            }
            cells.Clear();
            pending.Clear();
            // everything is rendered again from the poses as they are now
            renderedPoses = null;
            affected = null;
        }

        private string PreviewFolder => Path.Combine(Path.GetDirectoryName(Application.dataPath), "FactorioAvatarExport",
            preview.AvatarName, "preview");

        /// <summary>One animation in the preview: its rendered frames and where it is in its loop.</summary>
        private sealed class PreviewCell
        {
            private readonly double framesPerSecond;
            private double clock;

            public PreviewCell(AnimationInfo info, int row, double framesPerSecond, double now)
            {
                Info = info;
                Row = row;
                Frames = new Texture2D[info.FrameCount];
                Guides = new List<(GuidePart, Vec3[])>[info.FrameCount];
                this.framesPerSecond = framesPerSecond;
                clock = now;
            }

            public AnimationInfo Info { get; }

            /// <summary>The sheet row shown (<see cref="PosePreview.RowFor"/>).</summary>
            public int Row { get; }

            /// <summary>The rendered frames; null until rendered.</summary>
            public Texture2D[] Frames { get; }

            /// <summary>Each frame's guide lines as posed now, worked out when first drawn; null until then.</summary>
            public List<(GuidePart part, Vec3[] points)>[] Guides { get; }

            public void ClearGuides() => Array.Clear(Guides, 0, Guides.Length);

            /// <summary>The canvas the frames are drawn on (null before the first one).</summary>
            public CanvasSpec? Canvas { get; set; }

            public int Current { get; private set; }

            /// <summary>Moves on as many frames as the game would have since the last call; true when it moved.</summary>
            public bool Advance(double now)
            {
                if (Frames.Length < 2)
                {
                    clock = now;
                    return false;
                }
                int steps = (int)((now - clock) * framesPerSecond);
                if (steps <= 0)
                {
                    return false;
                }
                // frames the editor was too slow to show are skipped, so the animation keeps the game's pace
                clock = now - clock > 1 ? now : clock + steps / framesPerSecond;
                Current = (Current + steps) % Frames.Length;
                return true;
            }

            /// <summary>The frame to draw: the current one, or while that is still being rendered, the latest before it.</summary>
            public Texture2D Shown()
            {
                int index = ShownIndex();
                return index < 0 ? null : Frames[index];
            }

            /// <summary>The index of <see cref="Shown"/>'s frame, or -1 when none is rendered yet.</summary>
            public int ShownIndex()
            {
                for (int i = 0; i < Frames.Length; i++)
                {
                    int index = (Current - i + Frames.Length) % Frames.Length;
                    if (Frames[index] != null)
                    {
                        return index;
                    }
                }
                return -1;
            }
        }

        /// <summary>A Core image as a texture to draw (Unity textures have row 0 at the bottom).</summary>
        internal static Texture2D ToTexture(Image8 image, Texture2D reuse)
        {
            using (PreviewTimings.Time("ToTexture"))
            {
                return FillTexture(image, reuse);
            }
        }

        private static Texture2D FillTexture(Image8 image, Texture2D reuse)
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
