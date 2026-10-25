using System;
using System.Collections.Generic;
using System.Linq;
using MilkJugg24.FactorioAvatarExporter.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using HumanBone = MilkJugg24.FactorioAvatarExporter.Core.HumanBone;
using Object = UnityEngine.Object;

namespace MilkJugg24.FactorioAvatarExporter.Editor
{
    /// <summary>
    /// A copy of an avatar in its own preview scene, ready to be posed by Core. Nothing in the open scene is
    /// changed.
    ///
    /// The copy hangs under <see cref="Pivot"/> at the world origin. Its scripts (VRChat components,
    /// PhysBones and other tools' components) and EditorOnly objects are removed, its humanoid bones are set
    /// to the rest pose stored in its humanoid avatar (the pose the rig was configured in), and it is measured
    /// for Core (<see cref="Rig"/>). It is then placed so that Core's model space maps onto the pivot's space:
    /// a model point p (tiles) is at pivot-local <c>UnitySpace.ToUnity(p * MetersPerTile)</c>, the origin being
    /// the ground under the hips.
    /// </summary>
    internal sealed class AvatarStage : IDisposable
    {
        private readonly List<Object> temporary = new List<Object>();
        private readonly Dictionary<HumanBone, Transform> bones = new Dictionary<HumanBone, Transform>();
        private readonly Dictionary<HumanBone, Quaternion> restRotations = new Dictionary<HumanBone, Quaternion>();
        private readonly List<(Transform t, Vector3 position, Quaternion rotation, Vector3 scale)> rest =
            new List<(Transform, Vector3, Quaternion, Vector3)>();

        /// <summary>Loose parts held to a body bone: the part's rest position and rotation in the bone's frame.</summary>
        private readonly List<(Transform part, Transform bone, Vector3 position, Quaternion rotation)> links =
            new List<(Transform, Transform, Vector3, Quaternion)>();

        // wing roots (bones named "wing" whose parent bone is not) and which side each is on; those of the body
        // turn before the loose parts follow their bones, the others (on a part of their own) after
        private readonly List<(Transform root, bool left, bool inBody)> wings = new List<(Transform, bool, bool)>();

        // the angle each wing root was saved at, radians above straight out sideways (folded down the back is about
        // -1.4); each root is a whole wing from its shoulder, and its own hinge
        private readonly Dictionary<Transform, double> restElevation = new Dictionary<Transform, double>();

        // the joints inside each wing (elbow, wrist, fingers: its bones below the root that hold others), parent
        // first, with the bones each one points at; when the wing's mesh was modelled folded too, they straighten as
        // the wing spreads, so a folded wing opens out
        private readonly Dictionary<Transform, List<(Transform joint, Transform[] next)>> wingJoints =
            new Dictionary<Transform, List<(Transform, Transform[])>>();

        // how far a spread wing's joints straighten: 1 would line every bone up with the one before it (and close
        // the fingers)
        private const float WingUnfold = 0.75f;

        // the turn of each bone inside a wing, against its parent, in the shape the wing's mesh was modelled in (its
        // bind pose), parent first: a spread wing opens into that shape, fingers fanned as their maker drew them.
        // Empty for a wing modelled the way it is saved (then its joints straighten instead)
        private readonly Dictionary<Transform, List<(Transform bone, Quaternion modelled)>> wingModelled =
            new Dictionary<Transform, List<(Transform, Quaternion)>>();

        // the tail's bones (named "tail"), root first
        private readonly List<Transform> tail = new List<Transform>();

        // the body's right and forward at rest, pivot space
        private Vector3 restRight, restForward;

        // the held tools, built from Unity's primitives (the sizes of Core's stick figure)
        private GameObject gun;
        private GameObject pickaxe;
        private Transform pickHandle;
        private Transform pickHead;

        private AvatarStage(Scene scene)
        {
            Scene = scene;
        }

        public Scene Scene { get; }
        public Transform Pivot { get; private set; }
        public GameObject Avatar { get; private set; }
        public HumanoidRig Rig { get; private set; }
        public double MetersPerTile { get; private set; }

        /// <summary>How tall the avatar is drawn, tiles (<see cref="SetHeight"/>).</summary>
        public double HeightTiles { get; private set; }

        // the rest pose measured when loading, to measure the rig again for another height
        private RigInput restPose;

        /// <summary>What was found and done, one line each, for reports.</summary>
        public List<string> Notes { get; } = new List<string>();

        /// <summary>
        /// Copies <paramref name="source"/> (a prefab or a scene object with a humanoid Animator) into a new
        /// preview scene and measures it. Throws <see cref="ArgumentException"/> when it is not a humanoid.
        /// </summary>
        /// <param name="leaveOut">
        /// Paths below <paramref name="source"/> (as <see cref="AnimationUtility.CalculateTransformPath"/> gives
        /// them) of objects to hide in the copy, such as a fake ground shadow.
        /// </param>
        public static AvatarStage Load(GameObject source, double heightTiles = Conventions.EngineerHeight,
            IEnumerable<string> leaveOut = null)
        {
            var stage = new AvatarStage(EditorSceneManager.NewPreviewScene()) { HeightTiles = heightTiles };
            try
            {
                stage.Build(source, leaveOut ?? Enumerable.Empty<string>());
                return stage;
            }
            catch
            {
                stage.Dispose();
                throw;
            }
        }

        private void Build(GameObject source, IEnumerable<string> leaveOut)
        {
            var pivot = new GameObject("Pivot");
            SceneManager.MoveGameObjectToScene(pivot, Scene);
            Pivot = pivot.transform;
            Avatar = Object.Instantiate(source);
            Avatar.name = source.name;
            SceneManager.MoveGameObjectToScene(Avatar, Scene);
            Avatar.transform.SetParent(Pivot, false);
            Avatar.transform.localPosition = Vector3.zero;
            Avatar.transform.localRotation = Quaternion.identity;

            Animator animator = Avatar.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                throw new ArgumentException(source.name + " has no humanoid rig: its Animator needs an Avatar with " +
                    "Animation Type Humanoid (the model's Rig tab in the import settings)");
            }
            foreach (HumanBone bone in HumanBones.ParentFirst)
            {
                Transform t = animator.GetBoneTransform((HumanBodyBones)(int)bone);
                if (t != null)
                {
                    bones.Add(bone, t);
                }
            }
            foreach (string path in leaveOut)
            {
                Transform hidden = Avatar.transform.Find(path);
                if (hidden != null)
                {
                    hidden.gameObject.SetActive(false);
                    Notes.Add("left out of the sprites: " + path);
                }
            }
            Dictionary<Transform, Transform> constrained = ParentConstraintSources();
            Notes.Add(AvatarCleaner.Strip(Avatar));
            // the offsets are taken as the avatar was saved, where its outfits fit the body
            LinkLooseParts(constrained);
            ApplyRestPose(animator);
            FollowLinks();

            // measure the rest pose: bone positions and the meshes' top and bottom, meters, in pivot space
            List<Vector3> points = MeshPoints();
            if (points.Count == 0)
            {
                throw new ArgumentException(source.name + " has no visible meshes");
            }
            var input = new RigInput { MeshTop = points.Max(p => p.y), MeshBottom = points.Min(p => p.y) };
            foreach (KeyValuePair<HumanBone, Transform> bone in bones)
            {
                input.Bones.Add(bone.Key, ToCore(Pivot.InverseTransformPoint(bone.Value.position)));
            }
            restPose = input;
            SetHeight(HeightTiles);
            Notes.Add($"height: {Rig.Height:0.000} m; legs: {Rig.LegStyle}");
            List<HumanBone> missing = HumanBones.Optional.Where(b => !bones.ContainsKey(b)).ToList();
            if (missing.Count > 0)
            {
                Notes.Add("optional bones not mapped (estimated instead): " + string.Join(", ", missing));
            }
            foreach (string warning in Rig.Warnings)
            {
                Notes.Add("warning: " + warning);
            }

            // Core's model origin is the ground under the hips: move the avatar so it sits on the pivot
            Vec3 hips = input.Bones[HumanBone.Hips];
            Avatar.transform.localPosition = -ToUnity(new Vec3(hips.X, hips.Y, input.MeshBottom), 1);

            foreach (Transform t in Avatar.GetComponentsInChildren<Transform>(true))
            {
                rest.Add((t, t.localPosition, t.localRotation, t.localScale));
            }
            foreach (KeyValuePair<HumanBone, Transform> bone in bones)
            {
                restRotations.Add(bone.Key, Quaternion.Inverse(Pivot.rotation) * bone.Value.rotation);
            }
            Vector3 across = Pivot.InverseTransformPoint(bones[HumanBone.RightUpperArm].position) -
                Pivot.InverseTransformPoint(bones[HumanBone.LeftUpperArm].position);
            restRight = Vector3.ProjectOnPlane(across, Vector3.up).normalized;
            restForward = Vector3.Cross(restRight, Vector3.up);
            FindWings();
            FindTail();
            BuildProps();
        }

        private static bool Named(Transform t, string word) => t.name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>The highest bone of the trunk the rig maps (upper chest, chest, spine or hips).</summary>
        private HumanBone TopOfTrunk() =>
            new[] { HumanBone.UpperChest, HumanBone.Chest, HumanBone.Spine, HumanBone.Hips }.First(bones.ContainsKey);

        /// <summary>
        /// The world rotation <paramref name="bone"/> had when a mesh of the avatar was modelled on it (its bind pose,
        /// placed by the mesh's renderer as it is now), or null when no mesh is bound to it.
        /// </summary>
        private Quaternion? Modelled(Transform bone)
        {
            foreach (SkinnedMeshRenderer r in Avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int i = r.sharedMesh == null ? -1 : Array.IndexOf(r.bones, bone);
                if (i >= 0 && i < r.sharedMesh.bindposes.Length)
                {
                    return (r.transform.localToWorldMatrix * r.sharedMesh.bindposes[i].inverse).rotation;
                }
            }
            return null;
        }

        /// <summary>
        /// The wings' root bones: bones of any mesh named "wing" whose nearest parent bone is not, each on the side
        /// its bones lie. A root that only follows a body wing bone of its own name is left to follow it.
        /// </summary>
        private void FindWings()
        {
            var skin = new HashSet<Transform>(Avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .SelectMany(r => r.bones).Where(b => b != null));
            skin.UnionWith(bones.Values);
            Transform ParentBone(Transform t)
            {
                for (Transform p = t.parent; p != null && p != Avatar.transform; p = p.parent)
                {
                    if (skin.Contains(p))
                    {
                        return p;
                    }
                }
                return null;
            }
            List<Transform> roots = skin.Where(t => Named(t, "wing") && !(ParentBone(t) is Transform p && Named(p, "wing")))
                .OrderBy(t => t.name, StringComparer.Ordinal).ToList();
            // copies of the body's bones in a part's own armature (a wing prefab's Hips, Spine, Chest), held on the
            // body by name: never wings themselves
            var copies = new HashSet<Transform>(links.Select(l => l.part));
            if (roots.Count == 0)
            {
                // no bone is named "wing": the bones of the meshes that are, apart from the body's humanoid bones,
                // what holds them and their copies, from the first of each chain
                var humanoid = new HashSet<Transform>();
                foreach (Transform bone in bones.Values)
                {
                    for (Transform t = bone; t != null && t != Avatar.transform; t = t.parent)
                    {
                        humanoid.Add(t);
                    }
                }
                var wingBones = new HashSet<Transform>(Avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(r => Named(r.transform, "wing"))
                    .SelectMany(r => r.bones).Where(b => b != null && !humanoid.Contains(b) && !copies.Contains(b)));
                roots = wingBones.Where(t => !(ParentBone(t) is Transform p && wingBones.Contains(p)))
                    .OrderBy(t => t.name, StringComparer.Ordinal).ToList();
                if (roots.Count > 0)
                {
                    Notes.Add("no bone is named \"wing\": using the bones of the meshes named \"wing\"");
                }
            }
            var followers = new HashSet<Transform>(links.Where(l => roots.Contains(l.bone)).Select(l => l.part));
            Vector3 top = Pivot.InverseTransformPoint(bones[TopOfTrunk()].position);
            var unsided = new List<string>();
            // a root in the middle (one bone holding both wings) hands on to its child bones, a few levels down
            var queue = new Queue<(Transform bone, int depth)>(roots.Where(r => !followers.Contains(r)).Select(r => (r, 0)));
            while (queue.Count > 0)
            {
                (Transform root, int depth) = queue.Dequeue();
                Vector3 centre = Vector3.zero;
                Transform[] chain = root.GetComponentsInChildren<Transform>(true);
                foreach (Transform t in chain)
                {
                    centre += Pivot.InverseTransformPoint(t.position);
                }
                float side = Vector3.Dot(centre / chain.Length - top, restRight);
                if (Math.Abs(side) >= 0.01f)
                {
                    // up to the top of the wing on this side (its shoulder), below the bone in the middle that holds
                    // both wings: joint bones that move no mesh are not in the bones the search started from
                    Transform top1 = root;
                    for (Transform p = root.parent; p != null && p != Avatar.transform && !copies.Contains(p) && !bones.ContainsValue(p); p = p.parent)
                    {
                        float at = Vector3.Dot(Pivot.InverseTransformPoint(p.position) - top, restRight);
                        if (Math.Abs(at) < 0.01f || (at < 0) != (side < 0))
                        {
                            break;
                        }
                        top1 = p;
                    }
                    if (!wings.Any(w => w.root == top1))
                    {
                        wings.Add((top1, side < 0, InBody(top1)));
                    }
                    continue;
                }
                List<Transform> children = root.Cast<Transform>().Where(c => skin.Contains(c) && !copies.Contains(c)).ToList();
                if (depth < 3 && children.Count > 0)
                {
                    foreach (Transform child in children)
                    {
                        queue.Enqueue((child, depth + 1));
                    }
                }
                else
                {
                    unsided.Add(root.name);
                }
            }
            // a chain inside another wing chain would turn twice
            wings.RemoveAll(w => wings.Any(o => o.root != w.root && w.root.IsChildOf(o.root)));
            foreach ((Transform root, bool left, bool _) in wings)
            {
                // the saved angle: from the shoulder to the middle of the wing's bones, in the body's upright crosswise plane
                Vector3 centre = Vector3.zero;
                Transform[] chain = root.GetComponentsInChildren<Transform>(true);
                foreach (Transform t in chain)
                {
                    centre += Pivot.InverseTransformPoint(t.position);
                }
                Vector3 along = centre / chain.Length - Pivot.InverseTransformPoint(root.position);
                restElevation[root] = Math.Atan2(Vector3.Dot(along, Vector3.up), Vector3.Dot(along, left ? -restRight : restRight));
                var joints = new List<(Transform, Transform[])>();
                foreach (Transform t in chain)
                {
                    Transform[] next = t.Cast<Transform>().Where(c => skin.Contains(c)).ToArray();
                    if (t != root && next.Length > 0 && skin.Contains(t))
                    {
                        joints.Add((t, next));
                    }
                }
                wingJoints[root] = joints;
                var modelled = new List<(Transform, Quaternion)>();
                double turned = 0;
                foreach (Transform t in chain)
                {
                    if (t != root && Modelled(t) is Quaternion bone && Modelled(t.parent) is Quaternion parent)
                    {
                        Quaternion local = Quaternion.Inverse(parent) * bone;
                        modelled.Add((t, local));
                        turned = Math.Max(turned, Quaternion.Angle(local, t.localRotation));
                    }
                }
                wingModelled[root] = turned > 10 ? modelled : new List<(Transform, Quaternion)>();
            }
            if (wings.Count > 0)
            {
                Notes.Add("wings that open out, flap and stretch, from their hinge: " + string.Join(", ", wings.Select(w =>
                    $"{w.root.name} ({(w.left ? "left" : "right")}, saved at {restElevation[w.root] * 180 / Math.PI:0} degrees from straight out, " +
                    (wingModelled[w.root].Count > 0 ? "opens into the shape its mesh was modelled in)" : "opens by straightening its joints)"))));
            }
            else
            {
                Notes.Add("no wing bones (named \"wing\") found: nothing flaps");
            }
            if (unsided.Count > 0)
            {
                Notes.Add("wing bones in the middle, left still (neither left nor right): " + string.Join(", ", unsided));
            }
        }

        /// <summary>
        /// The tail: the longest chain of the body's bones named "tail", from a bone whose parent is not, through
        /// the child named "tail" with the most bones below it.
        /// </summary>
        private void FindTail()
        {
            Transform hips = bones[HumanBone.Hips];
            List<Transform> best = null;
            foreach (Transform root in hips.GetComponentsInChildren<Transform>(true).Where(t => Named(t, "tail") && !Named(t.parent, "tail")))
            {
                var chain = new List<Transform>();
                for (Transform t = root; t != null;)
                {
                    chain.Add(t);
                    t = t.Cast<Transform>().Where(c => Named(c, "tail"))
                        .OrderByDescending(c => c.GetComponentsInChildren<Transform>(true).Length).FirstOrDefault();
                }
                if (best == null || chain.Count > best.Count)
                {
                    best = chain;
                }
            }
            if (best == null)
            {
                Notes.Add("no tail bones (named \"tail\") found: nothing sways");
                return;
            }
            tail.AddRange(best);
            Notes.Add($"tail that sways: {best[0].name} to {best[best.Count - 1].name} ({best.Count} bones)");
        }

        /// <summary>How <paramref name="bone"/> is turned from its rest, world space.</summary>
        private Quaternion TurnFromRest(HumanBone bone) =>
            bones[bone].rotation * Quaternion.Inverse(Pivot.rotation * restRotations[bone]);

        /// <summary>
        /// Swings the tail (each bone a little more than its parent, a wave running down it) about the hips' up axis
        /// and raises it about their sideways axis, as <paramref name="pose"/> asks.
        /// </summary>
        private void PoseTail(PoseResult pose)
        {
            if (tail.Count == 0 || (pose.TailSwing == 0 && pose.TailLift == 0))
            {
                return;
            }
            Quaternion hips = TurnFromRest(HumanBone.Hips) * Pivot.rotation;
            Vector3 up = hips * Vector3.up, right = hips * restRight;
            Transform tip = tail[tail.Count - 1];
            Vector3 along = tip.position - tail[0].position;
            if (tip.childCount > 0 && along.sqrMagnitude < 1e-8f)
            {
                along = tip.GetChild(0).position - tail[0].position;
            }
            // raising turns the tail's end up, whichever way it hangs
            float liftSign = Vector3.Dot(Quaternion.AngleAxis(1, right) * along, up) >= Vector3.Dot(along, up) ? 1 : -1;
            float lift = (float)(pose.TailLift / tail.Count * 180 / Math.PI) * liftSign;
            // the swing and the wave's delay are set for a tail of TailBones bones; a longer chain shares them out,
            // so a fluffy tail of twenty bones sways as far as one of five, not four times as far
            double share = Math.Min(1.0, PoseResult.TailBones / (double)tail.Count);
            for (int i = 0; i < tail.Count; i++)
            {
                double delay = PoseResult.TailWaveDelay * share * i;
                float swing = (float)(pose.TailSwing * share * Math.Sin(pose.TailPhase - delay) * 180 / Math.PI);
                tail[i].rotation = Quaternion.AngleAxis(swing, up) * Quaternion.AngleAxis(lift, right) * tail[i].rotation;
            }
        }

        /// <summary>
        /// Opens each wing (of the body, or the others) as far as its spread says: into the shape its mesh was
        /// modelled in (<see cref="wingModelled"/>), or else its joints straighten towards the bone before them
        /// (<see cref="WingUnfold"/>). Then turns the whole wing about the chest's forward axis through
        /// its root (its shoulder), from the angle the opened wing is at towards <see cref="PoseResult.WingElevation"/>.
        /// </summary>
        private void PoseWings(PoseResult pose, bool inBody)
        {
            Quaternion chest = TurnFromRest(TopOfTrunk()) * Pivot.rotation;
            Vector3 forward = chest * restForward, up = chest * Vector3.up;
            foreach ((Transform root, bool left, bool body) in wings)
            {
                double spread = left ? pose.LeftWingSpread : pose.RightWingSpread;
                if (body != inBody || spread == 0)
                {
                    continue;
                }
                foreach ((Transform bone, Quaternion modelled) in wingModelled[root])
                {
                    bone.localRotation = Quaternion.Slerp(bone.localRotation, modelled, (float)spread);
                }
                foreach ((Transform joint, Transform[] next) in wingModelled[root].Count > 0 ? new List<(Transform, Transform[])>() : wingJoints[root])
                {
                    Vector3 before = joint.position - joint.parent.position;
                    Vector3 after = next.Aggregate(Vector3.zero, (sum, c) => sum + c.position) / next.Length - joint.position;
                    if (before.sqrMagnitude < 1e-10f || after.sqrMagnitude < 1e-10f)
                    {
                        continue;
                    }
                    Vector3 straighter = Vector3.Slerp(after.normalized, before.normalized, WingUnfold * (float)spread);
                    joint.rotation = Quaternion.FromToRotation(after, straighter) * joint.rotation;
                }
                Vector3 outward = chest * (left ? -restRight : restRight);
                Vector3 centre = Vector3.zero;
                Transform[] chain = root.GetComponentsInChildren<Transform>(true);
                foreach (Transform t in chain)
                {
                    centre += t.position;
                }
                Vector3 along = centre / chain.Length - root.position;
                double now = Math.Atan2(Vector3.Dot(along, up), Vector3.Dot(along, outward));
                double lift = spread * (pose.WingElevation - now);
                float sign = Vector3.Dot(Quaternion.AngleAxis(1, forward) * outward, up) >= Vector3.Dot(outward, up) ? 1 : -1;
                root.RotateAround(root.position, forward, sign * (float)(lift * 180 / Math.PI));
            }
        }

        /// <summary>
        /// The gun and the pickaxe, hidden until a pose holds one: plain shapes the size of Core's stick
        /// figure's (a dark rod; a wooden handle with a metal head). They are children of the avatar, so they
        /// are drawn, measured and cast shadows like the rest of it.
        /// </summary>
        private void BuildProps()
        {
            Material metal = PropMaterial("Prop metal", new Color(0.22f, 0.22f, 0.24f), 0.7f, 0.45f);
            Material wood = PropMaterial("Prop wood", new Color(0.45f, 0.29f, 0.14f), 0f, 0.25f);
            Material steel = PropMaterial("Prop steel", new Color(0.55f, 0.56f, 0.58f), 0.8f, 0.5f);
            gun = new GameObject("Factorio gun");
            SceneManager.MoveGameObjectToScene(gun, Scene);
            gun.transform.SetParent(Avatar.transform, false);
            Cylinder("Barrel", gun.transform, metal);
            pickaxe = new GameObject("Factorio pickaxe");
            SceneManager.MoveGameObjectToScene(pickaxe, Scene);
            pickaxe.transform.SetParent(Avatar.transform, false);
            // the engineer's pick: a wooden handle, thicker at the hand, and a curved steel head pointed at both
            // ends, the front point longer
            pickHandle = Shape("Handle", pickaxe.transform, wood, HandleMesh());
            pickHead = Shape("Head", pickaxe.transform, steel, HeadMesh());
            gun.SetActive(false);
            pickaxe.SetActive(false);
        }

        private Material PropMaterial(string name, Color color, float metallic, float smoothness)
        {
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave, color = color };
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
            temporary.Add(material);
            return material;
        }

        private Transform Shape(string name, Transform parent, Material material, Mesh mesh)
        {
            var shape = new GameObject(name);
            SceneManager.MoveGameObjectToScene(shape, Scene);
            shape.transform.SetParent(parent, false);
            shape.AddComponent<MeshFilter>().sharedMesh = mesh;
            shape.AddComponent<MeshRenderer>().sharedMaterial = material;
            temporary.Add(mesh);
            return shape.transform;
        }

        /// <summary>
        /// A tube along <paramref name="centers"/> with a ring of <paramref name="radii"/> round each (a point where
        /// the radius is 0), its rings square to the path and turned by <paramref name="across"/>, ends closed.
        /// </summary>
        private static Mesh Tube(string name, IList<Vector3> centers, IList<float> radii, Vector3 across, int sides = 12)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < centers.Count; i++)
            {
                Vector3 along = (centers[Math.Min(i + 1, centers.Count - 1)] - centers[Math.Max(i - 1, 0)]).normalized;
                Vector3 u = Vector3.Cross(along, across).normalized, v = Vector3.Cross(u, along);
                for (int k = 0; k < sides; k++)
                {
                    float angle = 2 * Mathf.PI * k / sides;
                    vertices.Add(centers[i] + (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * radii[i]);
                }
            }
            for (int i = 0; i + 1 < centers.Count; i++)
            {
                for (int k = 0; k < sides; k++)
                {
                    int a = i * sides + k, b = i * sides + (k + 1) % sides;
                    triangles.AddRange(new[] { a, a + sides, b, b, a + sides, b + sides });
                }
            }
            foreach ((int ring, bool first) in new[] { (0, true), (centers.Count - 1, false) })
            {
                int centre = vertices.Count;
                vertices.Add(centers[ring]);
                for (int k = 0; k < sides; k++)
                {
                    int a = ring * sides + k, b = ring * sides + (k + 1) % sides;
                    // facing out: back along the path at the first end, on along it at the last
                    triangles.AddRange(first ? new[] { centre, a, b } : new[] { centre, b, a });
                }
            }
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The handle in the unit primitive cylinder's place (2 tall along y, radius 0.5) for <see cref="Span"/>:
        /// swelling a little towards the hand end (-y), with a knob at it.
        /// </summary>
        private static Mesh HandleMesh()
        {
            var centers = new List<Vector3>();
            var radii = new List<float>();
            foreach ((float y, float r) in new[] { (-1f, 0.62f), (-0.96f, 0.68f), (-0.9f, 0.56f), (-0.5f, 0.52f), (0.5f, 0.46f), (1f, 0.44f) })
            {
                centers.Add(new Vector3(0, y, 0));
                radii.Add(r);
            }
            return Tube("Pick handle", centers, radii, Vector3.forward);
        }

        /// <summary>
        /// The head, in units of the handle's length, the eye at the origin round the handle (+y): the back end
        /// along +x (<see cref="StickFigure.PickHeadBack"/>), the point along -x (<see cref="StickFigure.PickHeadPoint"/>),
        /// both curving down towards the hand and tapering to points; thickest at the eye.
        /// </summary>
        private static Mesh HeadMesh()
        {
            var centers = new List<Vector3>();
            var radii = new List<float>();
            const int steps = 14;
            for (int i = 0; i <= steps; i++)
            {
                float s = -1 + 2f * i / steps;
                float length = s < 0 ? (float)StickFigure.PickHeadPoint : (float)StickFigure.PickHeadBack;
                float x = s * length;
                centers.Add(new Vector3(x, -0.35f * x * x, 0));
                radii.Add(Mathf.Lerp(0.055f, 0.006f, Mathf.Pow(Mathf.Abs(s), 0.8f)));
            }
            return Tube("Pick head", centers, radii, Vector3.forward);
        }

        private Transform Cylinder(string name, Transform parent, Material material)
        {
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(cylinder.GetComponent<Collider>());
            cylinder.name = name;
            SceneManager.MoveGameObjectToScene(cylinder, Scene);
            cylinder.transform.SetParent(parent, false);
            cylinder.GetComponent<MeshRenderer>().sharedMaterial = material;
            return cylinder.transform;
        }

        /// <summary>
        /// Shows the tool <paramref name="prop"/> holds, if any, from its base to its tip (model space,
        /// tiles), like Core's stick figure: the gun as a rod, the pickaxe as a handle with a head through the
        /// tip along Up, its point towards -Up.
        /// </summary>
        /// <summary>Draws the avatar <paramref name="tiles"/> tall from now on: measures the rig again for that height.</summary>
        public void SetHeight(double tiles)
        {
            HeightTiles = tiles;
            Rig = HumanoidRig.Measure(restPose, tiles);
            MetersPerTile = 1 / Rig.TilesPerMeter;
        }

        private void PlaceProp(PropPose prop)
        {
            gun.SetActive(prop != null && prop.Kind == PropKind.Gun);
            pickaxe.SetActive(prop != null && prop.Kind == PropKind.Pickaxe);
            if (prop == null)
            {
                return;
            }
            // thickness in proportion to the avatar, like the props' length
            double height = Rig.HeadTopHeight;
            if (prop.Kind == PropKind.Gun)
            {
                Span(gun.transform.GetChild(0), prop.Base, prop.Tip, StickFigure.GunRadius * height);
                return;
            }
            Span(pickHandle, prop.Base, prop.Tip, StickFigure.HandleRadius * height);
            // the head's mesh is in handle lengths: x along the pick's Up, y along the handle
            Vector3 tip = Pivot.TransformPoint(ToUnity(prop.Tip, MetersPerTile));
            Vector3 along = Pivot.TransformPoint(ToUnity(prop.Base, MetersPerTile));
            along = tip - along;
            Vector3 up = Pivot.TransformDirection(ToUnity(prop.Up.Normalized, 1));
            pickHead.SetPositionAndRotation(tip, Quaternion.LookRotation(Vector3.Cross(up, along), along));
            Vector3 parentScale = pickHead.parent.lossyScale;
            float length = along.magnitude;
            pickHead.localScale = new Vector3(length / parentScale.x, length / parentScale.y, length / parentScale.z);
        }

        /// <summary>Stretches a unit primitive cylinder (2 tall, 1 wide, along y) from a to b (model tiles), radius r tiles.</summary>
        private void Span(Transform cylinder, Vec3 a, Vec3 b, double radius)
        {
            Vector3 from = Pivot.TransformPoint(ToUnity(a, MetersPerTile));
            Vector3 to = Pivot.TransformPoint(ToUnity(b, MetersPerTile));
            float width = (float)(2 * radius * MetersPerTile);
            cylinder.position = (from + to) / 2;
            cylinder.rotation = Quaternion.FromToRotation(Vector3.up, to - from);
            // the parent chain (the avatar root) may be scaled: undo it
            Vector3 parentScale = cylinder.parent.lossyScale;
            cylinder.localScale = new Vector3(width / parentScale.x, (to - from).magnitude / 2 / parentScale.y,
                width / parentScale.z);
        }

        /// <summary>
        /// Sets the humanoid bones to the rotations stored in the humanoid avatar's skeleton (the pose the rig
        /// was configured in, usually a T-pose). Positions and scales stay as the avatar has them, so bone
        /// adjustments made in the prefab are kept.
        /// </summary>
        private void ApplyRestPose(Animator animator)
        {
            HumanDescription description = animator.avatar.humanDescription;
            var skeleton = new Dictionary<string, SkeletonBone>();
            foreach (SkeletonBone b in description.skeleton)
            {
                if (!skeleton.ContainsKey(b.name))
                {
                    skeleton.Add(b.name, b);
                }
            }
            int applied = 0;
            foreach (Transform t in bones.Values)
            {
                if (skeleton.TryGetValue(t.name, out SkeletonBone b))
                {
                    t.localRotation = b.rotation;
                    applied++;
                }
            }
            Notes.Add(applied == bones.Count
                ? "rest pose: the rig's configured pose"
                : $"rest pose: the rig's configured pose for {applied} of {bones.Count} bones, the rest as placed");
        }

        /// <summary>
        /// The objects parent constraints hold on to a bone of the body, and that bone: VRChat's constraints (read
        /// before the stage removes them, as they are scripts) and Unity's (switched off, as the stage moves
        /// those objects itself now).
        /// </summary>
        private Dictionary<Transform, Transform> ParentConstraintSources()
        {
            var sources = new Dictionary<Transform, Transform>();
            foreach (MonoBehaviour script in Avatar.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (script == null || !script.GetType().Name.EndsWith("ParentConstraint", StringComparison.Ordinal))
                {
                    continue;
                }
                Transform target = script.transform, source = null;
                using (var serialized = new SerializedObject(script))
                {
                    SerializedProperty p = serialized.GetIterator();
                    while (p.Next(true))
                    {
                        if (p.propertyType != SerializedPropertyType.ObjectReference || p.name.StartsWith("m_", StringComparison.Ordinal))
                        {
                            continue;
                        }
                        Transform t = p.objectReferenceValue is GameObject g ? g.transform : p.objectReferenceValue as Transform;
                        if (t != null && p.name == "TargetTransform")
                        {
                            target = t;
                        }
                        else if (t != null && source == null && InBody(t))
                        {
                            source = t;
                        }
                    }
                }
                if (source != null && !InBody(target))
                {
                    sources[target] = source;
                }
            }
            foreach (ParentConstraint constraint in Avatar.GetComponentsInChildren<ParentConstraint>(true))
            {
                for (int i = 0; i < constraint.sourceCount; i++)
                {
                    Transform source = constraint.GetSource(i).sourceTransform;
                    if (source != null && InBody(source) && !InBody(constraint.transform))
                    {
                        sources[constraint.transform] = source;
                        constraint.constraintActive = false;
                        break;
                    }
                }
            }
            return sources;
        }

        /// <summary>Whether <paramref name="t"/> is the hips or below them: the body the poses move.</summary>
        private bool InBody(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
            {
                if (p == bones[HumanBone.Hips])
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Holds on to the body the parts that are only joined to it by scripts the stage removed: outfits and
        /// accessories with their own armature, which VRCFury's Armature Link or Modular Avatar's Merge Armature
        /// merge into the body only in Play mode, and objects that constraints keep on a bone. An object a parent
        /// constraint held follows that bone; a part's bones follow the body's bones of the same name (allowing a
        /// prefix or suffix the part adds to all of them, like "Chest_Hoodie"); a part with neither follows, as a
        /// whole, the bone of the trunk (hips to head) nearest to it. Each keeps its offset as it is now.
        /// </summary>
        private void LinkLooseParts(Dictionary<Transform, Transform> constrained)
        {
            Transform hips = bones[HumanBone.Hips];
            var spine = new HashSet<Transform>(); // the hips and the transforms above them
            for (Transform t = hips; t != null && t != Avatar.transform; t = t.parent)
            {
                spine.Add(t);
            }
            var body = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            foreach (Transform t in hips.GetComponentsInChildren<Transform>(true))
            {
                if (!body.ContainsKey(t.name))
                {
                    body.Add(t.name, t);
                }
            }

            // what the meshes outside the body hang on: their bones, or the mesh object itself
            var loose = new List<Transform>();
            foreach (Renderer renderer in Avatar.GetComponentsInChildren<Renderer>(true))
            {
                Transform[] meshBones = renderer is SkinnedMeshRenderer skinned ? skinned.bones : null;
                if (meshBones != null && meshBones.Length > 0)
                {
                    loose.AddRange(meshBones.Where(b => b != null && !InBody(b) && !spine.Contains(b)));
                }
                else if (!InBody(renderer.transform))
                {
                    loose.Add(renderer.transform);
                }
            }
            // grouped by part: the top-most transform holding them that is neither the body nor above it
            var parts = new Dictionary<Transform, List<Transform>>();
            foreach (Transform t in loose.Distinct())
            {
                Transform part = t;
                while (part.parent != null && part.parent != Avatar.transform && !spine.Contains(part.parent))
                {
                    part = part.parent;
                }
                if (!parts.TryGetValue(part, out List<Transform> held))
                {
                    parts.Add(part, held = new List<Transform>());
                }
                held.Add(t);
            }

            string[] humanoidNames = HumanBones.ParentFirst.Where(bones.ContainsKey).Select(b => bones[b].name).ToArray();
            HumanBone[] trunk = new[] { HumanBone.Hips, HumanBone.Spine, HumanBone.Chest, HumanBone.UpperChest, HumanBone.Head }
                .Where(bones.ContainsKey).ToArray();
            foreach (KeyValuePair<Transform, List<Transform>> part in parts)
            {
                (string prefix, string suffix) = Affixes(part.Key, humanoidNames);
                var held = new List<string>();
                int byName = 0;
                foreach (Transform t in part.Key.GetComponentsInChildren<Transform>(true))
                {
                    if (constrained.TryGetValue(t, out Transform source))
                    {
                        Link(t, source);
                        held.Add($"{t.name} to {source.name}");
                    }
                    else if (body.TryGetValue(t.name, out Transform bone) || body.TryGetValue(StripAffixes(t.name, prefix, suffix), out bone))
                    {
                        Link(t, bone);
                        byName++;
                    }
                }
                if (held.Count > 0)
                {
                    Notes.Add($"{part.Key.name}: held by its constraints ({string.Join(", ", held)})");
                }
                if (byName > 0)
                {
                    Notes.Add($"{part.Key.name}: {byName} bones follow the body's bones of the same name");
                }
                if (held.Count > 0 || byName > 0)
                {
                    continue;
                }
                Vector3 centre = Vector3.zero;
                foreach (Transform t in part.Value)
                {
                    centre += t.position;
                }
                centre /= part.Value.Count;
                HumanBone nearest = trunk.OrderBy(b => (bones[b].position - centre).sqrMagnitude).First();
                Link(part.Key, bones[nearest]);
                Notes.Add($"{part.Key.name}: follows the {nearest} bone, the nearest (no constraint or bone names join it to the body)");
            }
            // parents before children, so a child's place is set after its parent moved
            links.Sort((a, b) => Depth(a.part).CompareTo(Depth(b.part)));
        }

        private void Link(Transform part, Transform bone)
        {
            links.Add((part, bone, bone.InverseTransformPoint(part.position), Quaternion.Inverse(bone.rotation) * part.rotation));
        }

        /// <summary>Puts the linked parts back on their bones, at the offsets they were linked with.</summary>
        private void FollowLinks()
        {
            foreach ((Transform part, Transform bone, Vector3 position, Quaternion rotation) in links)
            {
                part.SetPositionAndRotation(bone.TransformPoint(position), bone.rotation * rotation);
            }
        }

        /// <summary>The prefix and suffix a part adds to the names of its copy of the body's bones ("" when none).</summary>
        private static (string prefix, string suffix) Affixes(Transform part, string[] humanoidNames)
        {
            foreach (string name in humanoidNames)
            {
                foreach (Transform t in part.GetComponentsInChildren<Transform>(true))
                {
                    int i = t.name.IndexOf(name, StringComparison.OrdinalIgnoreCase);
                    if (i >= 0)
                    {
                        return (t.name.Substring(0, i), t.name.Substring(i + name.Length));
                    }
                }
            }
            return ("", "");
        }

        private static string StripAffixes(string name, string prefix, string suffix)
        {
            if (name.Length > prefix.Length + suffix.Length &&
                name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
            }
            return name;
        }

        private static int Depth(Transform t)
        {
            int depth = 0;
            for (Transform p = t.parent; p != null; p = p.parent)
            {
                depth++;
            }
            return depth;
        }

        /// <summary>Faces the model <paramref name="orientation"/> (a fraction of a clockwise turn from north).</summary>
        public void SetOrientation(double orientation)
        {
            Pivot.rotation = Quaternion.Euler(0, (float)(360 * orientation), 0);
        }

        /// <summary>
        /// Pivot space to world space for a model facing <paramref name="orientation"/>, mirrored left/right
        /// first (model x -> -x) when <paramref name="mirrored"/>. Mirroring is only ever measured and
        /// projected, never rendered, so the pivot itself is never mirrored.
        /// </summary>
        private static Matrix4x4 Placement(double orientation, bool mirrored) =>
            Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, (float)(360 * orientation), 0),
                mirrored ? new Vector3(-1, 1, 1) : Vector3.one);

        /// <summary>Puts every transform back where it was in the rest pose.</summary>
        public void ApplyRest()
        {
            foreach ((Transform t, Vector3 position, Quaternion rotation, Vector3 scale) in rest)
            {
                if (t != null)
                {
                    t.localPosition = position;
                    t.localRotation = rotation;
                    t.localScale = scale;
                }
            }
        }

        /// <summary>
        /// Poses the avatar as PoseResult documents: from the rest pose, parent first, every listed bone gets
        /// the rotation <c>delta * rest</c> (in the pivot's space), then the hips move to the pose's hips
        /// position, the tail sways and the wings turn (<see cref="PoseResult.TailSwing"/>,
        /// <see cref="PoseResult.LeftWingSpread"/>), then the parts held on the body follow their bones and the held tool is placed, then the
        /// whole avatar drops onto the ground if the pose asks for it. Fingers don't close around the tools yet.
        /// </summary>
        public void ApplyPose(PoseResult pose)
        {
            ApplyRest();
            foreach (HumanBone bone in HumanBones.ParentFirst)
            {
                if (bones.TryGetValue(bone, out Transform t) && pose.WorldDeltas.TryGetValue(bone, out Quat delta))
                {
                    t.rotation = Pivot.rotation * (ToUnity(delta) * restRotations[bone]);
                }
            }
            bones[HumanBone.Hips].position = Pivot.TransformPoint(ToUnity(pose.HipsPosition, MetersPerTile));
            // the tail and the body's wings first, so the parts held on them follow
            PoseTail(pose);
            PoseWings(pose, true);
            FollowLinks();
            PoseWings(pose, false);
            PlaceProp(pose.Prop);
            if (pose.GroundToLowestPoint)
            {
                float lowest = MeshPoints().Min(p => p.y);
                Avatar.transform.localPosition -= new Vector3(0, lowest, 0);
            }
        }

        /// <summary>
        /// How far, in tiles, the posed bones are from where Core put the matching joints of
        /// <paramref name="pose"/> (shoulders, elbows, wrists, hips, knees, ankles, pelvis, waist, neck top): 0 when
        /// the Unity posing does exactly what Core computed. Call after <see cref="ApplyPose"/>; not meaningful for
        /// poses that are dropped onto the ground.
        /// </summary>
        public double JointError(PoseResult pose)
        {
            var pairs = new List<(string joint, HumanBone bone)>
            {
                (JointNames.Pelvis, HumanBone.Hips), (JointNames.Waist, HumanBone.Spine), (JointNames.NeckTop, HumanBone.Head),
            };
            foreach (int side in new[] { 1, -1 })
            {
                pairs.Add((JointNames.Shoulder(side), HumanBoneHierarchy.UpperArm(side)));
                pairs.Add((JointNames.Elbow(side), HumanBoneHierarchy.LowerArm(side)));
                pairs.Add((JointNames.Hand(side), HumanBoneHierarchy.Hand(side)));
                pairs.Add((JointNames.Hip(side), HumanBoneHierarchy.UpperLeg(side)));
                pairs.Add((JointNames.Knee(side), HumanBoneHierarchy.LowerLeg(side)));
                pairs.Add((JointNames.Ankle(side), HumanBoneHierarchy.Foot(side)));
            }
            double worst = 0;
            foreach ((string joint, HumanBone bone) in pairs)
            {
                if (bones.TryGetValue(bone, out Transform t) && pose.Joints.TryGetValue(joint, out Vec3 expected))
                {
                    Vec3 actual = ToCore(Pivot.InverseTransformPoint(t.position)) * Rig.TilesPerMeter;
                    worst = Math.Max(worst, Vec3.Distance(actual, expected));
                }
            }
            return worst;
        }

        /// <summary>
        /// The vertices of every enabled renderer, in the pivot's space, meters: skinned meshes as posed now.
        /// </summary>
        public List<Vector3> MeshPoints()
        {
            var points = new List<Vector3>();
            Matrix4x4 toPivot = Pivot.worldToLocalMatrix;
            ForEachMesh((world, _) =>
            {
                foreach (Vector3 v in world)
                {
                    points.Add(toPivot.MultiplyPoint3x4(v));
                }
            });
            return points;
        }

        /// <summary>
        /// Fills <paramref name="shadow"/> with the ground shadow of the avatar as posed now, facing
        /// <paramref name="orientation"/> (mirrored when <paramref name="mirrored"/>): every triangle of every
        /// visible renderer flattened onto the ground (world y = 0) along Core's shadow light
        /// (<see cref="PlanarShadow"/>), in world space. Leaves the pivot facing north.
        /// </summary>
        public void BakeShadow(Mesh shadow, double orientation, bool mirrored)
        {
            SetOrientation(0);
            Matrix4x4 place = Placement(orientation, mirrored);
            double[] p = PlanarShadow.UnityMatrix();
            var project = new Matrix4x4();
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    project[row, column] = (float)p[row * 4 + column];
                }
            }
            Matrix4x4 m = project * place;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            ForEachMesh((world, shape) =>
            {
                int start = vertices.Count;
                foreach (Vector3 v in world)
                {
                    vertices.Add(m.MultiplyPoint3x4(v));
                }
                for (int sub = 0; sub < shape.subMeshCount; sub++)
                {
                    if (shape.GetTopology(sub) != MeshTopology.Triangles)
                    {
                        continue;
                    }
                    foreach (int index in shape.GetTriangles(sub))
                    {
                        triangles.Add(start + index);
                    }
                }
            });
            shadow.Clear();
            shadow.indexFormat = IndexFormat.UInt32;
            shadow.SetVertices(vertices);
            shadow.subMeshCount = 1;
            shadow.SetTriangles(triangles, 0);
            shadow.RecalculateBounds();
        }

        /// <summary>What skinning needs of a mesh, read once: its vertices, bone weights, bind poses and blend shapes.</summary>
        private sealed class SkinData
        {
            public Vector3[] Vertices;
            public BoneWeight[] Weights;
            public Matrix4x4[] BindPoses;
            // per blend shape: each frame's weight and vertex offsets (read when the shape is first used: avatars
            // have hundreds of face shapes, mostly at 0)
            public (float weight, Vector3[] deltas)[][] Shapes;
            public Mesh Mesh;

            public Vector3[] Deltas(int shape, int frame)
            {
                if (Shapes[shape][frame].deltas == null)
                {
                    var deltas = new Vector3[Vertices.Length];
                    Mesh.GetBlendShapeFrameVertices(shape, frame, deltas, null, null);
                    Shapes[shape][frame].deltas = deltas;
                }
                return Shapes[shape][frame].deltas;
            }
        }

        private readonly Dictionary<Mesh, SkinData> skinData = new Dictionary<Mesh, SkinData>();

        private SkinData Skin(Mesh mesh)
        {
            if (!skinData.TryGetValue(mesh, out SkinData data))
            {
                data = new SkinData { Vertices = mesh.vertices, Weights = mesh.boneWeights, BindPoses = mesh.bindposes, Mesh = mesh };
                data.Shapes = new (float, Vector3[])[mesh.blendShapeCount][];
                for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                {
                    int frames = mesh.GetBlendShapeFrameCount(shape);
                    data.Shapes[shape] = new (float, Vector3[])[frames];
                    for (int frame = 0; frame < frames; frame++)
                    {
                        data.Shapes[shape][frame] = (mesh.GetBlendShapeFrameWeight(shape, frame), null);
                    }
                }
                skinData.Add(mesh, data);
            }
            return data;
        }

        /// <summary>
        /// The world positions of a skinned mesh's vertices as posed now, skinned here the way the renderer does:
        /// blend shapes at their weights, then each vertex moved by its bones (bone to world times bind pose,
        /// weighted). Unity's own BakeMesh was not trusted for this: for some avatars it put the body, wings and
        /// shorts at the wrong scale, so only the hoodie cast a shadow.
        /// </summary>
        private Vector3[] Skinned(SkinnedMeshRenderer skinned)
        {
            SkinData data = Skin(skinned.sharedMesh);
            var local = (Vector3[])data.Vertices.Clone();
            for (int shape = 0; shape < data.Shapes.Length; shape++)
            {
                float weight = skinned.GetBlendShapeWeight(shape);
                (float weight, Vector3[] deltas)[] frames = data.Shapes[shape];
                if (weight == 0 || frames.Length == 0)
                {
                    continue;
                }
                // between the frames around the weight (from no offset below the first), on past the last
                int k = 0;
                while (k < frames.Length - 1 && frames[k].weight < weight)
                {
                    k++;
                }
                float fromWeight = k == 0 ? 0 : frames[k - 1].weight;
                Vector3[] from = k == 0 ? null : data.Deltas(shape, k - 1);
                float span = frames[k].weight - fromWeight;
                float t = span != 0 ? (weight - fromWeight) / span : 1;
                Vector3[] to = data.Deltas(shape, k);
                for (int v = 0; v < local.Length; v++)
                {
                    Vector3 a = from != null ? from[v] : Vector3.zero;
                    local[v] += a + (to[v] - a) * t;
                }
            }
            Transform[] skinBones = skinned.bones;
            var world = new Vector3[local.Length];
            if (data.Weights.Length != local.Length || skinBones.Length == 0)
            {
                // not skinned to bones: it moves with its own object
                Matrix4x4 rigid = skinned.transform.localToWorldMatrix;
                for (int v = 0; v < local.Length; v++)
                {
                    world[v] = rigid.MultiplyPoint3x4(local[v]);
                }
                return world;
            }
            var matrices = new Matrix4x4[skinBones.Length];
            for (int b = 0; b < skinBones.Length; b++)
            {
                matrices[b] = skinBones[b] != null && b < data.BindPoses.Length
                    ? skinBones[b].localToWorldMatrix * data.BindPoses[b]
                    : skinned.transform.localToWorldMatrix;
            }
            for (int v = 0; v < local.Length; v++)
            {
                BoneWeight w = data.Weights[v];
                Vector3 p = local[v];
                Vector3 sum = Vector3.zero;
                float total = 0;
                void Add(int bone, float weight)
                {
                    if (weight > 0 && bone >= 0 && bone < matrices.Length)
                    {
                        sum += matrices[bone].MultiplyPoint3x4(p) * weight;
                        total += weight;
                    }
                }
                Add(w.boneIndex0, w.weight0);
                Add(w.boneIndex1, w.weight1);
                Add(w.boneIndex2, w.weight2);
                Add(w.boneIndex3, w.weight3);
                world[v] = total > 0 ? sum / total : skinned.transform.localToWorldMatrix.MultiplyPoint3x4(p);
            }
            return world;
        }

        /// <summary>
        /// Calls <paramref name="visit"/> with the world positions of the vertices of every enabled mesh of the
        /// avatar as posed now, and the mesh whose triangles join them (particles, lines and trails left out).
        /// Renderers count whatever their Cast Shadows setting: avatars often turn it off for VRChat's world
        /// lighting, and the sprite's shadow is of the whole avatar.
        /// </summary>
        private void ForEachMesh(Action<Vector3[], Mesh> visit)
        {
            foreach (Renderer renderer in Avatar.GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled)
                {
                    continue;
                }
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    if (skinned.sharedMesh != null)
                    {
                        visit(Skinned(skinned), skinned.sharedMesh);
                    }
                }
                else if (renderer is MeshRenderer)
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                    {
                        Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
                        visit(filter.sharedMesh.vertices.Select(v => toWorld.MultiplyPoint3x4(v)).ToArray(), filter.sharedMesh);
                    }
                }
            }
        }

        /// <summary>
        /// World space bounds (tiles) of the meshes as posed now, facing <paramref name="orientation"/>, mirrored
        /// when <paramref name="mirrored"/>. Leaves the pivot facing north.
        /// </summary>
        public Bounds3 WorldBounds(double orientation, bool mirrored = false)
        {
            SetOrientation(0);
            Matrix4x4 toWorld = Placement(orientation, mirrored);
            Bounds3 bounds = Bounds3.Empty;
            foreach (Vector3 p in MeshPoints())
            {
                Vector3 w = toWorld.MultiplyPoint3x4(p);
                bounds = bounds.Encapsulate(ToCore(w) * Rig.TilesPerMeter);
            }
            return bounds;
        }

        /// <summary>
        /// What on the avatar may draw outside its measured meshes, as posed now: particles, lines and trails
        /// (rendered, but not measured), renderers whose own bounds reach well past the meshes, and materials
        /// whose shaders are named for shadows or outlines. One line each; empty when nothing is suspect.
        /// </summary>
        public List<string> DrawnOutsideMeshes()
        {
            var found = new List<string>();
            List<Vector3> points = MeshPoints();
            if (points.Count == 0)
            {
                return found;
            }
            var meshes = new Bounds(Pivot.TransformPoint(points[0]), Vector3.zero);
            foreach (Vector3 p in points)
            {
                meshes.Encapsulate(Pivot.TransformPoint(p));
            }
            const float slack = 0.3f; // meters
            meshes.Expand(2 * slack);
            foreach (Renderer renderer in Avatar.GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled)
                {
                    continue;
                }
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, Avatar.transform);
                if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer))
                {
                    found.Add($"{path} ({renderer.GetType().Name}: drawn, but not measured)");
                }
                else if (!meshes.Contains(renderer.bounds.min) || !meshes.Contains(renderer.bounds.max))
                {
                    found.Add($"{path} (reaches {renderer.bounds.size.magnitude:0.0} m across)");
                }
                foreach (Material material in renderer.sharedMaterials)
                {
                    string shader = material != null && material.shader != null ? material.shader.name : "";
                    string lower = shader.ToLowerInvariant();
                    if (lower.Contains("shadow") || lower.Contains("outline"))
                    {
                        found.Add($"{path} (shader {shader})");
                    }
                }
            }
            return found.Distinct().ToList();
        }

        public static Vec3 ToCore(Vector3 v) => UnitySpace.ToCore(v.x, v.y, v.z);

        public static Vector3 ToUnity(Vec3 v, double scale)
        {
            (double x, double y, double z) = UnitySpace.ToUnity(v * scale);
            return new Vector3((float)x, (float)y, (float)z);
        }

        public static Quaternion ToUnity(Quat q)
        {
            (double x, double y, double z, double w) = UnitySpace.RotationToUnity(q);
            return new Quaternion((float)x, (float)y, (float)z, (float)w);
        }

        public void Dispose()
        {
            EditorSceneManager.ClosePreviewScene(Scene);
            foreach (Object o in temporary)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }
        }
    }
}
