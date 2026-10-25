using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Humanoid
{
    /// <summary>Random numbers, vectors and rotations, mirroring, and approximate assertions.</summary>
    internal static class TestMath
    {
        public static readonly int[] Sides = { 1, -1 };

        public static double Uniform(Random rng, double lo, double hi) => lo + (hi - lo) * rng.NextDouble();

        public static Vec3 RandomUnit(Random rng)
        {
            while (true)
            {
                var v = new Vec3(Uniform(rng, -1, 1), Uniform(rng, -1, 1), Uniform(rng, -1, 1));
                double n = v.Length;
                if (n > 0.1 && n <= 1)
                {
                    return v / n;
                }
            }
        }

        public static Vec3 RandomHorizontalUnit(Random rng)
        {
            double a = Uniform(rng, -Math.PI, Math.PI);
            return new Vec3(Math.Cos(a), Math.Sin(a), 0);
        }

        public static Quat RandomRotation(Random rng, double maxAngle) =>
            Quat.FromAxisAngle(RandomUnit(rng), Uniform(rng, -maxAngle, maxAngle));

        public static Quat Yaw(double angle) => Quat.FromAxisAngle(Vec3.UnitZ, angle);

        /// <summary>The signed angle about +z from one horizontal direction to another.</summary>
        public static double SignedYaw(Vec3 from, Vec3 to) =>
            Math.Atan2(from.X * to.Y - from.Y * to.X, from.X * to.X + from.Y * to.Y);

        /// <summary>Angle of a vector above the horizontal plane, radians.</summary>
        public static double Elevation(Vec3 v) => Math.Atan2(v.Z, v.Horizontal.Length);

        /// <summary>The unit part of <paramref name="v"/> perpendicular to <paramref name="axis"/>.</summary>
        public static Vec3 Perpendicular(Vec3 v, Vec3 axis)
        {
            Vec3 a = axis.Normalized;
            return (v - a * Vec3.Dot(v, a)).Normalized;
        }

        /// <summary>Reflection x -> -x.</summary>
        public static Vec3 Mirror(Vec3 v) => new Vec3(-v.X, v.Y, v.Z);

        /// <summary>The rotation M q M for the reflection M: x -> -x.</summary>
        public static Quat Mirror(Quat q) => new Quat(q.W, q.X, -q.Y, -q.Z);

        public static string MirrorJointName(string name) =>
            name.EndsWith(".L", StringComparison.Ordinal) ? name.Substring(0, name.Length - 2) + ".R"
            : name.EndsWith(".R", StringComparison.Ordinal) ? name.Substring(0, name.Length - 2) + ".L"
            : name;

        public static bool IsFinite(Vec3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

        public static void AssertClose(Vec3 actual, Vec3 expected, double tolerance, string what)
        {
            Assert.That(actual.ApproximatelyEquals(expected, tolerance), $"{what}: {actual} vs {expected}");
        }

        /// <summary>
        /// The angle in radians between two rotations, accurate near zero too (<see cref="Quat.Angle"/> goes
        /// through acos, which turns rounding errors of 1e-16 into angles of 1e-8).
        /// </summary>
        public static double RotationAngle(Quat a, Quat b)
        {
            Quat p = a.Normalized, q = b.Normalized;
            double minus = new Quat(p.W - q.W, p.X - q.X, p.Y - q.Y, p.Z - q.Z).Norm;
            double plus = new Quat(p.W + q.W, p.X + q.X, p.Y + q.Y, p.Z + q.Z).Norm;
            return 4 * Math.Asin(Math.Min(1, Math.Min(minus, plus) / 2));
        }

        public static void AssertSameRotation(Quat actual, Quat expected, double tolerance, string what)
        {
            Assert.That(RotationAngle(actual, expected), Is.LessThanOrEqualTo(tolerance), $"{what}: {actual} vs {expected}");
        }

        public static void AssertUnit(Quat q, string what)
        {
            Assert.That(q.Norm, Is.EqualTo(1).Within(1e-12), $"{what} is not a unit quaternion: {q}");
        }
    }

    /// <summary>Joint names and forward kinematics checks shared by the pose tests.</summary>
    internal static class TestPoses
    {
        public static IEnumerable<string> AllJointNames()
        {
            yield return JointNames.Pelvis;
            yield return JointNames.Waist;
            yield return JointNames.Chest;
            yield return JointNames.NeckBase;
            yield return JointNames.NeckTop;
            yield return JointNames.Head;
            yield return JointNames.HeadTop;
            foreach (int side in TestMath.Sides)
            {
                yield return JointNames.Shoulder(side);
                yield return JointNames.Elbow(side);
                yield return JointNames.Hand(side);
                yield return JointNames.Hip(side);
                yield return JointNames.Knee(side);
                yield return JointNames.Ankle(side);
                yield return JointNames.Ball(side);
                yield return JointNames.Toe(side);
            }
        }

        public static Dictionary<string, Vec3> FkJoints(HumanoidRig rig, PoseResult pose) =>
            HumanoidJoints.FromSkeleton(HumanoidSkeleton.Pose(rig, pose));

        /// <summary>The pose's joints are exactly the named points of its forward kinematics.</summary>
        public static void AssertJointsAreFk(HumanoidRig rig, PoseResult pose, double tolerance)
        {
            Dictionary<string, Vec3> fk = FkJoints(rig, pose);
            Assert.That(pose.Joints.Keys, Is.EquivalentTo(AllJointNames()));
            foreach (KeyValuePair<string, Vec3> joint in fk)
            {
                TestMath.AssertClose(pose.Joints[joint.Key], joint.Value, tolerance, joint.Key);
            }
        }
    }

    /// <summary>Rig inputs for tests: named variants, random rigs and transformations of inputs.</summary>
    internal static class TestRigs
    {
        /// <summary>Every synthetic variant: generic, proportioned, chibi, each full and minimal.</summary>
        public static IEnumerable<TestCaseData> Variants()
        {
            foreach (bool minimal in new[] { false, true })
            {
                string suffix = minimal ? " minimal" : "";
                yield return new TestCaseData(SyntheticRigs.Generic(1.7, null, minimal)).SetArgDisplayNames("generic" + suffix);
                yield return new TestCaseData(SyntheticRigs.Generic(0.9, new SyntheticRigProportions
                {
                    Legs = 1.4,
                    Arms = 0.7,
                    Torso = 0.8,
                    Head = 1.3,
                    ShoulderWidth = 1.5,
                }, minimal)).SetArgDisplayNames("long legs" + suffix);
                yield return new TestCaseData(SyntheticRigs.Chibi(1.1, minimal)).SetArgDisplayNames("chibi" + suffix);
            }
        }

        /// <summary>
        /// A random synthetic rig: random height and proportions (sometimes chibi), sometimes minimal, otherwise
        /// with random optional bones and sole points left out, sometimes jittered so it is neither symmetric nor
        /// axis aligned, and moved away from the origin.
        /// </summary>
        public static RigInput RandomInput(Random rng)
        {
            double height = TestMath.Uniform(rng, 0.5, 2.2);
            SyntheticRigProportions proportions = rng.Next(4) == 0
                ? SyntheticRigProportions.Chibi()
                : new SyntheticRigProportions
                {
                    Legs = TestMath.Uniform(rng, 0.6, 1.5),
                    Arms = TestMath.Uniform(rng, 0.6, 1.5),
                    Torso = TestMath.Uniform(rng, 0.6, 1.5),
                    Head = TestMath.Uniform(rng, 0.7, 2.6),
                    ShoulderWidth = TestMath.Uniform(rng, 0.6, 1.6),
                };
            bool minimal = rng.Next(4) == 0;
            RigInput input = SyntheticRigs.Generic(height, proportions, minimal);
            if (!minimal)
            {
                foreach (HumanBone bone in HumanBones.Optional)
                {
                    if (rng.Next(10) < 3)
                    {
                        input.Bones.Remove(bone);
                    }
                }
                input.LeftToeTip = rng.Next(10) < 3 ? null : input.LeftToeTip;
                input.RightToeTip = rng.Next(10) < 3 ? null : input.RightToeTip;
                input.LeftHeel = rng.Next(10) < 3 ? null : input.LeftHeel;
                input.RightHeel = rng.Next(10) < 3 ? null : input.RightHeel;
            }
            if (rng.Next(2) == 0)
            {
                foreach (HumanBone bone in new List<HumanBone>(input.Bones.Keys))
                {
                    input.Bones[bone] += TestMath.RandomUnit(rng) * (0.01 * height * rng.NextDouble());
                }
            }
            var offset = new Vec3(TestMath.Uniform(rng, -1, 1), TestMath.Uniform(rng, -1, 1), TestMath.Uniform(rng, -0.5, 0.5));
            return Translate(input, offset);
        }

        public static HumanoidRig Random(Random rng) => HumanoidRig.Measure(RandomInput(rng), TestMath.Uniform(rng, 1.0, 2.0));

        /// <summary>The input moved by <paramref name="offset"/>, meters.</summary>
        public static RigInput Translate(RigInput input, Vec3 offset) => Map(input, p => p + offset, offset.Z, b => b, false);

        /// <summary>The mirror image of the input (x -> -x, left and right swapped).</summary>
        public static RigInput Mirror(RigInput input) => Map(input, TestMath.Mirror, 0, HumanBoneHierarchy.Mirror, true);

        private static RigInput Map(RigInput input, Func<Vec3, Vec3> point, double dz, Func<HumanBone, HumanBone> bone,
            bool swapSides)
        {
            var output = new RigInput { MeshTop = input.MeshTop + dz, MeshBottom = input.MeshBottom + dz };
            foreach (KeyValuePair<HumanBone, Vec3> b in input.Bones)
            {
                output.Bones.Add(bone(b.Key), point(b.Value));
            }
            Vec3? P(Vec3? p) => p.HasValue ? point(p.Value) : (Vec3?)null;
            output.LeftToeTip = P(swapSides ? input.RightToeTip : input.LeftToeTip);
            output.RightToeTip = P(swapSides ? input.LeftToeTip : input.RightToeTip);
            output.LeftHeel = P(swapSides ? input.RightHeel : input.LeftHeel);
            output.RightHeel = P(swapSides ? input.LeftHeel : input.RightHeel);
            return output;
        }
    }

    /// <summary>Targets for a <see cref="HumanoidPoseBuilder"/>, generated so tests know what to expect.</summary>
    internal sealed class PoseTargets
    {
        public Vec3 Pelvis;
        public Quat HipFrame = Quat.Identity;
        public Quat ChestFrame = Quat.Identity;
        public Quat HeadFrame = Quat.Identity;
        public readonly ArmTargets[] Arms = new ArmTargets[2]; // [0] = left, [1] = right
        public readonly FootTargets[] Feet = new FootTargets[2];

        public static int Index(int side) => side > 0 ? 1 : 0;

        public ArmTargets Arm(int side) => Arms[Index(side)];
        public FootTargets Foot(int side) => Feet[Index(side)];

        public HumanoidPoseBuilder ApplyTorso(HumanoidPoseBuilder builder) =>
            builder.SetPelvis(Pelvis, HipFrame).SetTorso(ChestFrame).SetHead(HeadFrame);

        public HumanoidPoseBuilder ApplyLimbs(HumanoidPoseBuilder builder)
        {
            foreach (int side in TestMath.Sides)
            {
                ArmTargets a = Arm(side);
                if (a != null)
                {
                    builder.SetArm(side, a.Hand, a.Pole, a.Direction, a.Grip);
                }
                FootTargets f = Foot(side);
                if (f != null)
                {
                    builder.SetFoot(side, f.Ball, f.Forward, f.HeelLift, f.KneePole, f.ToeBend);
                }
            }
            return builder;
        }

        public PoseResult Build(HumanoidRig rig) => ApplyLimbs(ApplyTorso(new HumanoidPoseBuilder(rig))).Build();

        /// <summary>The mirror image: x -> -x everywhere, left and right swapped.</summary>
        public PoseTargets Mirrored()
        {
            var m = new PoseTargets
            {
                Pelvis = TestMath.Mirror(Pelvis),
                HipFrame = TestMath.Mirror(HipFrame),
                ChestFrame = TestMath.Mirror(ChestFrame),
                HeadFrame = TestMath.Mirror(HeadFrame),
            };
            for (int i = 0; i < 2; i++)
            {
                ArmTargets a = Arms[i];
                if (a != null)
                {
                    m.Arms[1 - i] = new ArmTargets
                    {
                        Hand = TestMath.Mirror(a.Hand),
                        Pole = TestMath.Mirror(a.Pole),
                        Direction = a.Direction.HasValue ? TestMath.Mirror(a.Direction.Value) : (Vec3?)null,
                        Grip = a.Grip,
                    };
                }
                FootTargets f = Feet[i];
                if (f != null)
                {
                    m.Feet[1 - i] = new FootTargets
                    {
                        Ball = TestMath.Mirror(f.Ball),
                        Forward = TestMath.Mirror(f.Forward),
                        HeelLift = f.HeelLift,
                        KneePole = TestMath.Mirror(f.KneePole),
                        ToeBend = f.ToeBend,
                    };
                }
            }
            return m;
        }

        /// <summary>
        /// Random torso frames and limb targets that are all within reach (with a margin), so the pose must
        /// reproduce them. <paramref name="natural"/> keeps hands below and in front of the shoulders with the
        /// elbows pointing back, and feet below the hips with the knees pointing forward.
        /// </summary>
        public static PoseTargets RandomReachable(HumanoidRig rig, Random rng, bool natural)
        {
            var t = new PoseTargets
            {
                Pelvis = rig.Rest(HumanBone.Hips) + new Vec3(TestMath.Uniform(rng, -0.3, 0.3), TestMath.Uniform(rng, -0.3, 0.3),
                    rig.HipsHeight * TestMath.Uniform(rng, -0.3, 0.1)),
                HipFrame = TestMath.Yaw(TestMath.Uniform(rng, -Math.PI, Math.PI)) * TestMath.RandomRotation(rng, 0.4),
            };
            t.ChestFrame = t.HipFrame * TestMath.RandomRotation(rng, 0.5);
            t.HeadFrame = t.ChestFrame * TestMath.RandomRotation(rng, 0.6);
            HumanoidPoseBuilder torso = t.ApplyTorso(new HumanoidPoseBuilder(rig));
            foreach (int side in TestMath.Sides)
            {
                double l1 = rig.UpperArmLength(side), l2 = rig.ForearmLength(side);
                Vec3 armDirection = natural
                    ? t.ChestFrame.Rotate(new Vec3(side * TestMath.Uniform(rng, 0, 0.4), TestMath.Uniform(rng, -0.2, 0.8),
                        -TestMath.Uniform(rng, 0.4, 1))).Normalized
                    : TestMath.RandomUnit(rng);
                t.Arms[Index(side)] = new ArmTargets
                {
                    Hand = torso.TorsoPoint(HumanBoneHierarchy.UpperArm(side)) + armDirection * ReachableDistance(rng, l1, l2),
                    Pole = natural
                        ? t.ChestFrame.Rotate(new Vec3(side * TestMath.Uniform(rng, 0.1, 0.5), -1, -TestMath.Uniform(rng, 0, 0.5)))
                        : TestMath.RandomUnit(rng),
                    Direction = rng.Next(2) == 0 ? TestMath.RandomUnit(rng) : (Vec3?)null,
                    Grip = rng.NextDouble(),
                };

                l1 = rig.ThighLength(side);
                l2 = rig.ShinLength(side);
                Vec3 legDirection = natural
                    ? t.HipFrame.Rotate(new Vec3(side * TestMath.Uniform(rng, 0, 0.2), TestMath.Uniform(rng, -0.3, 0.5), -1))
                        .Normalized
                    : TestMath.RandomUnit(rng);
                Vec3 ankle = torso.TorsoPoint(HumanBoneHierarchy.UpperLeg(side)) + legDirection * ReachableDistance(rng, l1, l2);
                Vec3 forward = natural ? t.HipFrame.Forward.Horizontal.Normalized : TestMath.RandomHorizontalUnit(rng);
                double heelLift = TestMath.Uniform(rng, -0.3, 0.9);
                t.Feet[Index(side)] = new FootTargets
                {
                    Ball = ankle - ExpectedAnkleOffset(rig, side, forward, heelLift),
                    Forward = forward * TestMath.Uniform(rng, 0.5, 2) + Vec3.UnitZ * TestMath.Uniform(rng, -1, 1),
                    HeelLift = heelLift,
                    KneePole = natural
                        ? t.HipFrame.Rotate(new Vec3(side * TestMath.Uniform(rng, 0, 0.3), 1, TestMath.Uniform(rng, -0.2, 0.2)))
                        : TestMath.RandomUnit(rng),
                    ToeBend = TestMath.Uniform(rng, -0.5, 0.9),
                };
            }
            return t;
        }

        /// <summary>
        /// Ankle minus ball for a foot turned to <paramref name="forward"/> and pitched up about the ball by
        /// <paramref name="heelLift"/>, computed with Rodrigues' formula independently of the builder.
        /// </summary>
        public static Vec3 ExpectedAnkleOffset(HumanoidRig rig, int side, Vec3 forward, double heelLift)
        {
            Vec3 restForward = rig.FootForward(side);
            Vec3 offset = rig.Rest(HumanBoneHierarchy.Foot(side)) - rig.Ball(side);
            offset = MathUtil.Rot(offset, Vec3.Cross(restForward, Vec3.UnitZ), -heelLift);
            return MathUtil.Rot(offset, Vec3.UnitZ, TestMath.SignedYaw(restForward, forward));
        }

        private static double ReachableDistance(Random rng, double l1, double l2)
        {
            double margin = Math.Max(0.02 * (l1 + l2), 0.003);
            return TestMath.Uniform(rng, Math.Abs(l1 - l2) + margin, l1 + l2 - margin);
        }
    }

    internal sealed class ArmTargets
    {
        public Vec3 Hand;
        public Vec3 Pole;
        public Vec3? Direction;
        public double Grip;
    }

    internal sealed class FootTargets
    {
        public Vec3 Ball;
        public Vec3 Forward;
        public double HeelLift;
        public Vec3 KneePole;
        public double ToeBend;
    }

    /// <summary>
    /// Applies poses the way Unity does, literally: a transform hierarchy with arbitrary rest rotations and
    /// non-humanoid transforms between the bones (and above the Hips); in ParentFirst order each listed
    /// bone's world rotation is set to delta * rest rotation, which changes its local rotation while every
    /// other transform keeps its own; then the Hips are moved.
    /// </summary>
    internal sealed class TransformHierarchy
    {
        private readonly List<Node> nodes = new List<Node>();
        private readonly Dictionary<HumanBone, int> boneNodes = new Dictionary<HumanBone, int>();
        private readonly List<(HumanBone bone, int node)> leaves = new List<(HumanBone, int)>();

        public TransformHierarchy(HumanoidRig rig, Random rng)
        {
            // an armature transform above the Hips, like Blender exports have
            int armature = Add(-1, null, TestMath.RandomUnit(rng) * 0.2, TestMath.RandomRotation(rng, Math.PI));
            foreach (HumanBone bone in rig.Bones)
            {
                HumanBone? humanParent = rig.Parent(bone);
                int parent = humanParent.HasValue ? boneNodes[humanParent.Value] : armature;
                if (rng.Next(2) == 0)
                {
                    // a non-humanoid transform in between (a twist bone, a skipped optional bone)
                    Vec3 at = Vec3.Lerp(nodes[parent].RestPosition, rig.Rest(bone), rng.NextDouble())
                        + TestMath.RandomUnit(rng) * 0.05;
                    parent = Add(parent, null, at, TestMath.RandomRotation(rng, Math.PI));
                }
                boneNodes[bone] = Add(parent, bone, rig.Rest(bone), TestMath.RandomRotation(rng, Math.PI));
                if (rng.Next(3) == 0)
                {
                    Vec3 at = rig.Rest(bone) + TestMath.RandomUnit(rng) * 0.1;
                    leaves.Add((bone, Add(boneNodes[bone], null, at, TestMath.RandomRotation(rng, Math.PI))));
                }
            }
        }

        public Vec3 Position(HumanBone bone) => nodes[boneNodes[bone]].Position;

        public Quat Delta(HumanBone bone)
        {
            Node n = nodes[boneNodes[bone]];
            return n.Rotation * n.RestRotation.Inverse;
        }

        /// <summary>Non-humanoid leaf transforms: the bone they hang from, their rest and posed positions.</summary>
        public IEnumerable<(HumanBone bone, Vec3 rest, Vec3 posed)> Leaves()
        {
            foreach ((HumanBone bone, int node) in leaves)
            {
                yield return (bone, nodes[node].RestPosition, nodes[node].Position);
            }
        }

        public void Apply(PoseResult pose)
        {
            foreach (Node n in nodes)
            {
                Node parent = n.Parent >= 0 ? nodes[n.Parent] : null;
                Quat parentRotation = parent?.Rotation ?? Quat.Identity;
                Quat local = n.LocalRotation;
                if (n.Bone.HasValue && pose.WorldDeltas.TryGetValue(n.Bone.Value, out Quat delta))
                {
                    // transform.rotation = delta * restRotation: Unity stores it as a new local rotation
                    local = parentRotation.Inverse * delta.Normalized * n.RestRotation;
                }
                n.Rotation = parentRotation * local;
                Vec3 localPosition = n.LocalPosition;
                if (n.Bone == HumanBone.Hips)
                {
                    localPosition = parentRotation.Inverse.Rotate(pose.HipsPosition - parent.Position);
                }
                n.Position = parent == null ? localPosition : parent.Position + parentRotation.Rotate(localPosition);
            }
        }

        private int Add(int parent, HumanBone? bone, Vec3 restPosition, Quat restRotation)
        {
            var n = new Node { Parent = parent, Bone = bone, RestPosition = restPosition, RestRotation = restRotation };
            if (parent >= 0)
            {
                Quat inverse = nodes[parent].RestRotation.Inverse;
                n.LocalPosition = inverse.Rotate(restPosition - nodes[parent].RestPosition);
                n.LocalRotation = inverse * restRotation;
            }
            else
            {
                n.LocalPosition = restPosition;
                n.LocalRotation = restRotation;
            }
            nodes.Add(n);
            return nodes.Count - 1;
        }

        private sealed class Node
        {
            public int Parent;
            public HumanBone? Bone;
            public Vec3 RestPosition;
            public Quat RestRotation;
            public Vec3 LocalPosition;
            public Quat LocalRotation;
            public Vec3 Position;
            public Quat Rotation;
        }
    }
}
