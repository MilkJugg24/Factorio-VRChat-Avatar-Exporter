using System;
using System.Collections.Generic;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// The capsules that draw a pose (<see cref="PoseResult.Joints"/> and <see cref="PoseResult.Prop"/>),
    /// placed in the world:
    /// <list type="bullet">
    /// <item>the torso chain pelvis - waist - chest - neck_base (thick) - neck_top - head (thin);</item>
    /// <item>a head sphere centred on head and reaching head_top;</item>
    /// <item>per side, chest - shoulder - elbow - hand and pelvis - hip - knee - ankle - ball - toe;</item>
    /// <item>the prop: a gun from Base to Tip; or a pickaxe handle from Base to Tip with a head through the
    /// Tip along Up, its point towards -Up (proportions of model.py's <c>add_prop</c>).</item>
    /// </list>
    /// A chain skips missing joints and joins the joints on either side of them. Radii are fractions of
    /// <see cref="FigureHeight"/>, which depends only on bone lengths, so they stay the same in every frame.
    /// </summary>
    public sealed class StickFigure
    {
        // radii as fractions of the figure height
        private const double TorsoRadius = 0.07;
        private const double NeckRadius = 0.03;
        private const double HeadRadius = 0.08; // when head_top is missing
        private const double GirdleRadius = 0.03;
        private const double ArmRadius = 0.028;
        private const double ThighRadius = 0.04;
        private const double ShinRadius = 0.034;
        private const double FootRadius = 0.022;

        // the held tools, public so the Unity exporter draws the same ones
        public const double GunRadius = 0.02;
        public const double HandleRadius = 0.011;
        public const double PickHeadRadius = 0.016;

        // pickaxe head extent along +Up and -Up from the tip, as fractions of the handle length
        // (model.py's 0.11 and 0.18 of the humanoid's 0.56 handle; the dragon's handle is 0.80)
        public const double PickHeadBack = 0.2;
        public const double PickHeadPoint = 0.32;

        private const string PoseParameter = "pose";

        private static readonly int[] Sides = { -1, 1 };

        private readonly Capsule[] capsules;

        private StickFigure(Capsule[] capsules)
        {
            this.capsules = capsules;
            Bounds3 bounds = Bounds3.Empty;
            foreach (Capsule c in capsules)
            {
                bounds = bounds.Union(c.Bounds);
            }
            Bounds = bounds;
        }

        /// <summary>The capsules, world space: torso first, then the head, arms, legs and prop.</summary>
        public IReadOnlyList<Capsule> Capsules => capsules;

        /// <summary>World bounds of all capsules, tiles (empty when there are none).</summary>
        public Bounds3 Bounds { get; }

        /// <summary>
        /// The figure of <paramref name="pose"/> placed with <paramref name="orientation"/> and
        /// <paramref name="mirrored"/> (<see cref="Conventions.ModelPointToWorld"/>), and moved so its lowest
        /// surface point touches the ground (z = 0) when <see cref="PoseResult.GroundToLowestPoint"/> is set.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="pose"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="orientation"/> is not finite.</exception>
        /// <exception cref="ArgumentException">A joint or prop vector is not finite, or a pickaxe has no Up.</exception>
        public static StickFigure Build(PoseResult pose, double orientation, bool mirrored)
        {
            if (pose == null)
            {
                throw new ArgumentNullException(nameof(pose));
            }
            if (!VecChecks.IsFinite(orientation))
            {
                throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "orientation must be finite");
            }
            RequireValid(pose);

            var builder = new Builder(pose, orientation, mirrored, FigureHeight(pose));
            builder.AddBody();
            builder.AddProp(pose.Prop);
            Capsule[] parts = builder.Capsules.ToArray();
            if (pose.GroundToLowestPoint && parts.Length > 0)
            {
                double lowest = double.PositiveInfinity;
                foreach (Capsule c in parts)
                {
                    lowest = Math.Min(lowest, c.Bounds.Min.Z);
                }
                var shift = new Vec3(0, 0, -lowest);
                for (int i = 0; i < parts.Length; i++)
                {
                    parts[i] = parts[i].Translated(shift);
                }
            }
            return new StickFigure(parts);
        }

        /// <summary>
        /// The height the radii scale with, tiles: the length of the chain pelvis - ... - head, plus the head
        /// radius (head to head_top), plus the average length of the legs hip - knee - ankle. Bone lengths do
        /// not change with the pose. <see cref="Conventions.DefaultCharacterHeight"/> when nothing is measurable.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="pose"/> is null.</exception>
        public static double FigureHeight(PoseResult pose)
        {
            if (pose == null)
            {
                throw new ArgumentNullException(nameof(pose));
            }
            Dictionary<string, Vec3> joints = pose.Joints;
            double height = ChainLength(joints, JointNames.Pelvis, JointNames.Waist, JointNames.Chest, JointNames.NeckBase,
                JointNames.NeckTop, JointNames.Head);
            height += ChainLength(joints, JointNames.Head, JointNames.HeadTop);
            double legs = 0;
            int legCount = 0;
            foreach (int side in Sides)
            {
                double leg = ChainLength(joints, JointNames.Hip(side), JointNames.Knee(side), JointNames.Ankle(side));
                if (leg > 0)
                {
                    legs += leg;
                    legCount++;
                }
            }
            if (legCount > 0)
            {
                height += legs / legCount;
            }
            return height > 1e-6 ? height : Conventions.DefaultCharacterHeight;
        }

        /// <summary>Summed distances between the consecutive present joints of a chain.</summary>
        private static double ChainLength(Dictionary<string, Vec3> joints, params string[] chain)
        {
            double sum = 0;
            Vec3? previous = null;
            foreach (string name in chain)
            {
                if (joints.TryGetValue(name, out Vec3 p))
                {
                    if (previous.HasValue)
                    {
                        sum += Vec3.Distance(previous.Value, p);
                    }
                    previous = p;
                }
            }
            return sum;
        }

        private static void RequireValid(PoseResult pose)
        {
            foreach (KeyValuePair<string, Vec3> joint in pose.Joints)
            {
                RequireFinite(joint.Value, "joint " + joint.Key);
            }
            PropPose prop = pose.Prop;
            if (prop == null)
            {
                return;
            }
            RequireFinite(prop.Base, "prop base");
            RequireFinite(prop.Tip, "prop tip");
            RequireFinite(prop.Up, "prop up");
            if (prop.Kind == PropKind.Pickaxe && prop.Up.LengthSquared < 1e-18)
            {
                throw new ArgumentException("the pickaxe's Up (the direction of its head) is zero", PoseParameter);
            }
        }

        private static void RequireFinite(Vec3 v, string what)
        {
            if (!VecChecks.IsFinite(v))
            {
                throw new ArgumentException(what + " is not finite: " + v, PoseParameter);
            }
        }

        /// <summary>Places joints in the world and collects capsules.</summary>
        private sealed class Builder
        {
            private readonly Dictionary<string, Vec3> joints;
            private readonly double orientation;
            private readonly bool mirrored;
            private readonly double height;

            public Builder(PoseResult pose, double orientation, bool mirrored, double height)
            {
                joints = pose.Joints;
                this.orientation = orientation;
                this.mirrored = mirrored;
                this.height = height;
            }

            public List<Capsule> Capsules { get; } = new List<Capsule>();

            public void AddBody()
            {
                Chain(StickPart.Torso,
                    (JointNames.Pelvis, TorsoRadius), (JointNames.Waist, TorsoRadius), (JointNames.Chest, TorsoRadius),
                    (JointNames.NeckBase, NeckRadius), (JointNames.NeckTop, NeckRadius), (JointNames.Head, NeckRadius));
                AddHead();
                foreach (int side in Sides)
                {
                    StickPart arm = side > 0 ? StickPart.RightArm : StickPart.LeftArm;
                    Chain(arm, (JointNames.Chest, GirdleRadius), (JointNames.Shoulder(side), ArmRadius),
                        (JointNames.Elbow(side), ArmRadius), (JointNames.Hand(side), ArmRadius));
                }
                foreach (int side in Sides)
                {
                    StickPart leg = side > 0 ? StickPart.RightLeg : StickPart.LeftLeg;
                    Chain(leg, (JointNames.Pelvis, GirdleRadius), (JointNames.Hip(side), ThighRadius),
                        (JointNames.Knee(side), ShinRadius), (JointNames.Ankle(side), FootRadius),
                        (JointNames.Ball(side), FootRadius), (JointNames.Toe(side), FootRadius));
                }
            }

            public void AddProp(PropPose prop)
            {
                if (prop == null)
                {
                    return;
                }
                Vec3 basePoint = Place(prop.Base);
                Vec3 tip = Place(prop.Tip);
                if (prop.Kind == PropKind.Gun)
                {
                    Add(basePoint, tip, GunRadius * height, StickPart.Prop);
                    return;
                }
                Add(basePoint, tip, HandleRadius * height, StickPart.Prop);
                double handle = Vec3.Distance(basePoint, tip);
                Vec3 up = Place(prop.Up).Normalized;
                Add(tip + up * (PickHeadBack * handle), tip - up * (PickHeadPoint * handle), PickHeadRadius * height,
                    StickPart.Prop);
            }

            private void AddHead()
            {
                if (!joints.TryGetValue(JointNames.Head, out Vec3 head))
                {
                    return;
                }
                double radius = HeadRadius * height;
                if (joints.TryGetValue(JointNames.HeadTop, out Vec3 top) && Vec3.Distance(head, top) > 1e-9)
                {
                    radius = Vec3.Distance(head, top);
                }
                Vec3 centre = Place(head);
                Add(centre, centre, radius, StickPart.Head);
            }

            /// <summary>
            /// Capsules between consecutive present joints; each takes the radius (a fraction of the height)
            /// given with its first joint.
            /// </summary>
            private void Chain(StickPart part, params (string joint, double radius)[] chain)
            {
                Vec3? previous = null;
                double radius = 0;
                foreach ((string joint, double fraction) in chain)
                {
                    if (!joints.TryGetValue(joint, out Vec3 p))
                    {
                        continue;
                    }
                    Vec3 placed = Place(p);
                    if (previous.HasValue)
                    {
                        Add(previous.Value, placed, radius, part);
                    }
                    previous = placed;
                    radius = fraction * height;
                }
            }

            private void Add(Vec3 a, Vec3 b, double radius, StickPart part) => Capsules.Add(new Capsule(a, b, radius, part));

            private Vec3 Place(Vec3 model) => Conventions.ModelPointToWorld(model, orientation, mirrored);
        }
    }
}
