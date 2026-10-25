using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Humanoid
{
    public class HumanoidPoseBuilderTests
    {
        private const double Eps = 1e-9;

        private sealed class Case
        {
            public HumanoidRig Rig;
            public PoseTargets Targets;
            public PoseResult Pose;
            public bool Natural;
        }

        /// <summary>Random rigs with random reachable targets, natural or not, and the poses built from them.</summary>
        private static IEnumerable<Case> RandomCases(int seed, int count)
        {
            var rng = new Random(seed);
            for (int i = 0; i < count; i++)
            {
                HumanoidRig rig = TestRigs.Random(rng);
                bool natural = rng.Next(2) == 0;
                PoseTargets targets = PoseTargets.RandomReachable(rig, rng, natural);
                yield return new Case { Rig = rig, Targets = targets, Pose = targets.Build(rig), Natural = natural };
            }
        }

        private static (HumanBone root, HumanBone joint, HumanBone end, Vec3 restAxis)[] Limbs(HumanoidRig rig, int side) =>
            new[]
            {
                (HumanBoneHierarchy.UpperArm(side), HumanBoneHierarchy.LowerArm(side), HumanBoneHierarchy.Hand(side),
                    rig.ElbowBendAxis(side)),
                (HumanBoneHierarchy.UpperLeg(side), HumanBoneHierarchy.LowerLeg(side), HumanBoneHierarchy.Foot(side),
                    rig.KneeBendAxis(side)),
            };

        [TestCaseSource(typeof(TestRigs), nameof(TestRigs.Variants))]
        public void WithoutTargetsTheRigStaysInItsRestPose(RigInput input)
        {
            HumanoidRig rig = HumanoidRig.Measure(input);
            PoseResult pose = new HumanoidPoseBuilder(rig).Build();
            Assert.That(pose.HipsPosition, Is.EqualTo(rig.Rest(HumanBone.Hips)));
            var expected = new List<HumanBone> { HumanBone.Hips, HumanBone.Spine, HumanBone.Head };
            foreach (HumanBone optional in new[] { HumanBone.Chest, HumanBone.UpperChest, HumanBone.Neck })
            {
                if (rig.Has(optional))
                {
                    expected.Add(optional);
                }
            }
            Assert.That(pose.WorldDeltas.Keys, Is.EquivalentTo(expected));
            foreach (KeyValuePair<HumanBone, Quat> delta in pose.WorldDeltas)
            {
                TestMath.AssertSameRotation(delta.Value, Quat.Identity, 1e-15, delta.Key.ToString());
            }
            Dictionary<string, Vec3> rest = TestPoses.FkJoints(rig, new PoseResult { HipsPosition = rig.Rest(HumanBone.Hips) });
            foreach (KeyValuePair<string, Vec3> joint in rest)
            {
                TestMath.AssertClose(pose.Joints[joint.Key], joint.Value, 1e-12, joint.Key);
            }
            Assert.That(pose.LeftGrip, Is.EqualTo(0));
            Assert.That(pose.RightGrip, Is.EqualTo(0));
            Assert.That(pose.Prop, Is.Null);
            Assert.That(pose.GroundToLowestPoint, Is.False);
        }

        [Test]
        public void ReproducesPelvisHandAndBallTargetsInReach()
        {
            foreach (Case c in RandomCases(51, 300))
            {
                Assert.That(c.Pose.HipsPosition, Is.EqualTo(c.Targets.Pelvis));
                TestMath.AssertClose(c.Pose.Joints[JointNames.Pelvis], c.Targets.Pelvis, Eps, "pelvis");
                foreach (int side in TestMath.Sides)
                {
                    TestMath.AssertClose(c.Pose.Joints[JointNames.Hand(side)], c.Targets.Arm(side).Hand, Eps, "hand");
                    FootTargets foot = c.Targets.Foot(side);
                    TestMath.AssertClose(c.Pose.Joints[JointNames.Ball(side)], foot.Ball, Eps, "ball");
                    Vec3 ankle = foot.Ball + PoseTargets.ExpectedAnkleOffset(c.Rig, side, foot.Forward, foot.HeelLift);
                    TestMath.AssertClose(c.Pose.Joints[JointNames.Ankle(side)], ankle, Eps, "ankle");
                }
            }
        }

        [Test]
        public void KeepsTheSkeletonsProportions()
        {
            foreach (Case c in RandomCases(52, 200))
            {
                HumanoidRig rig = c.Rig;
                Dictionary<string, Vec3> j = c.Pose.Joints;
                double Distance(string a, string b) => Vec3.Distance(j[a], j[b]);
                foreach (int side in TestMath.Sides)
                {
                    Assert.That(Distance(JointNames.Shoulder(side), JointNames.Elbow(side)),
                        Is.EqualTo(rig.UpperArmLength(side)).Within(Eps));
                    Assert.That(Distance(JointNames.Elbow(side), JointNames.Hand(side)), Is.EqualTo(rig.ForearmLength(side)).Within(Eps));
                    Assert.That(Distance(JointNames.Hip(side), JointNames.Knee(side)), Is.EqualTo(rig.ThighLength(side)).Within(Eps));
                    Assert.That(Distance(JointNames.Knee(side), JointNames.Ankle(side)), Is.EqualTo(rig.ShinLength(side)).Within(Eps));
                    Assert.That(Distance(JointNames.Ankle(side), JointNames.Ball(side)),
                        Is.EqualTo(Vec3.Distance(rig.Rest(HumanBoneHierarchy.Foot(side)), rig.Ball(side))).Within(Eps));
                    Assert.That(Distance(JointNames.Ball(side), JointNames.Toe(side)),
                        Is.EqualTo(Vec3.Distance(rig.Ball(side), rig.ToeTip(side))).Within(Eps));
                    Assert.That(Distance(JointNames.Pelvis, JointNames.Hip(side)),
                        Is.EqualTo(Vec3.Distance(rig.Rest(HumanBone.Hips), rig.Rest(HumanBoneHierarchy.UpperLeg(side)))).Within(Eps));
                }
                Assert.That(Distance(JointNames.Shoulder(1), JointNames.Shoulder(-1)), Is.EqualTo(rig.ShoulderWidth).Within(Eps));
                Assert.That(Distance(JointNames.Hip(1), JointNames.Hip(-1)), Is.EqualTo(rig.HipWidth).Within(Eps));
            }
        }

        [Test]
        public void ElbowsAndKneesBendTowardsTheirPoles()
        {
            int tested = 0;
            foreach (Case c in RandomCases(53, 300))
            {
                foreach (int side in TestMath.Sides)
                {
                    tested += CheckBendsTowards(c.Pose, JointNames.Shoulder(side), JointNames.Elbow(side), JointNames.Hand(side),
                        c.Targets.Arm(side).Pole);
                    tested += CheckBendsTowards(c.Pose, JointNames.Hip(side), JointNames.Knee(side), JointNames.Ankle(side),
                        c.Targets.Foot(side).KneePole);
                }
            }
            Assert.That(tested, Is.GreaterThan(1000));
        }

        private static int CheckBendsTowards(PoseResult pose, string root, string joint, string end, Vec3 pole)
        {
            Vec3 direction = (pose.Joints[end] - pose.Joints[root]).Normalized;
            Vec3 polePerpendicular = pole - direction * Vec3.Dot(pole, direction);
            if (polePerpendicular.Length < 0.05)
            {
                return 0;
            }
            Vec3 offset = pose.Joints[joint] - pose.Joints[root];
            offset -= direction * Vec3.Dot(offset, direction);
            Assert.That(Vec3.Dot(offset.Normalized, polePerpendicular.Normalized), Is.EqualTo(1).Within(Eps), joint);
            return 1;
        }

        [Test]
        public void JointsFlexTheAnatomicalWayWhateverThePole()
        {
            foreach (Case c in RandomCases(54, 300))
            {
                foreach (int side in TestMath.Sides)
                {
                    foreach ((HumanBone root, HumanBone joint, HumanBone end, Vec3 restAxis) in Limbs(c.Rig, side))
                    {
                        PosedSkeleton s = HumanoidSkeleton.Pose(c.Rig, c.Pose);
                        Vec3 upper = s.Position(joint) - s.Position(root), lower = s.Position(end) - s.Position(joint);
                        Vec3 axis = Vec3.Cross(upper, lower).Normalized;
                        // each bone turns its own anatomical hinge axis into the joint's actual one: the joint
                        // flexes positively about it, with the twist that takes
                        Vec3 restUpper = c.Rig.Rest(joint) - c.Rig.Rest(root), restLower = c.Rig.Rest(end) - c.Rig.Rest(joint);
                        TestMath.AssertClose(c.Pose.WorldDeltas[root].Rotate(TestMath.Perpendicular(restAxis, restUpper)), axis, Eps,
                            root + " hinge");
                        TestMath.AssertClose(c.Pose.WorldDeltas[joint].Rotate(TestMath.Perpendicular(restAxis, restLower)), axis, Eps,
                            joint + " hinge");
                    }
                }
            }
        }

        [Test]
        public void NaturalTargetsBendElbowsBackAndKneesForward()
        {
            int tested = 0;
            foreach (Case c in RandomCases(55, 400))
            {
                if (!c.Natural)
                {
                    continue;
                }
                tested++;
                foreach (int side in TestMath.Sides)
                {
                    Assert.That(Vec3.Dot(JointOffset(c.Pose, JointNames.Shoulder(side), JointNames.Elbow(side), JointNames.Hand(side)),
                        c.Targets.ChestFrame.Forward), Is.LessThan(0), "elbow back");
                    Assert.That(Vec3.Dot(JointOffset(c.Pose, JointNames.Hip(side), JointNames.Knee(side), JointNames.Ankle(side)),
                        c.Targets.HipFrame.Forward), Is.GreaterThan(0), "knee forward");
                    // the forearm bends towards the front of the upper arm (seen in the upper arm's rest frame),
                    // the shin towards the back of the thigh
                    Vec3 forearm = c.Pose.Joints[JointNames.Hand(side)] - c.Pose.Joints[JointNames.Elbow(side)];
                    Vec3 shin = c.Pose.Joints[JointNames.Ankle(side)] - c.Pose.Joints[JointNames.Knee(side)];
                    Assert.That(c.Pose.WorldDeltas[HumanBoneHierarchy.UpperArm(side)].Inverse.Rotate(forearm).Y, Is.GreaterThan(0));
                    Assert.That(c.Pose.WorldDeltas[HumanBoneHierarchy.UpperLeg(side)].Inverse.Rotate(shin).Y, Is.LessThan(0));
                }
            }
            Assert.That(tested, Is.GreaterThan(150));
        }

        // the middle joint's offset from the line between the limb's ends
        private static Vec3 JointOffset(PoseResult pose, string root, string joint, string end)
        {
            Vec3 direction = (pose.Joints[end] - pose.Joints[root]).Normalized;
            Vec3 offset = pose.Joints[joint] - pose.Joints[root];
            return offset - direction * Vec3.Dot(offset, direction);
        }

        [Test]
        public void FeetStayFlatWithoutHeelLift()
        {
            var rng = new Random(56);
            int tested = 0;
            for (int i = 0; i < 300; i++)
            {
                HumanoidRig rig = TestRigs.Random(rng);
                Quat yaw = TestMath.Yaw(TestMath.Uniform(rng, -Math.PI, Math.PI));
                var builder = new HumanoidPoseBuilder(rig);
                builder.SetPelvis(yaw.Rotate(rig.Rest(HumanBone.Hips)) - Vec3.UnitZ * (TestMath.Uniform(rng, 0.03, 0.3) * rig.LegLength),
                    yaw * TestMath.RandomRotation(rng, 0.2));
                var balls = new Vec3[2];
                foreach (int side in TestMath.Sides)
                {
                    Vec3 ball = yaw.Rotate(rig.Ball(side)) + new Vec3(TestMath.Uniform(rng, -0.1, 0.1), TestMath.Uniform(rng, -0.2, 0.2), 0)
                        * rig.LegLength;
                    Vec3 forward = yaw.Rotate(MathUtil.Rot(rig.FootForward(side), Vec3.UnitZ, TestMath.Uniform(rng, -0.4, 0.4)));
                    builder.SetFoot(side, ball, forward, 0, yaw.Forward, 0);
                    balls[PoseTargets.Index(side)] = ball;
                }
                PoseResult pose = builder.Build();
                PosedSkeleton s = HumanoidSkeleton.Pose(rig, pose);
                foreach (int side in TestMath.Sides)
                {
                    if (!pose.Joints[JointNames.Ball(side)].ApproximatelyEquals(balls[PoseTargets.Index(side)], Eps))
                    {
                        continue; // out of reach: the foot aims at the ball instead
                    }
                    tested++;
                    HumanBone foot = HumanBoneHierarchy.Foot(side);
                    TestMath.AssertClose(pose.WorldDeltas[foot].Rotate(Vec3.UnitZ), Vec3.UnitZ, 1e-12, "foot upright");
                    if (rig.Has(HumanBoneHierarchy.Toes(side)))
                    {
                        TestMath.AssertClose(pose.WorldDeltas[HumanBoneHierarchy.Toes(side)].Rotate(Vec3.UnitZ), Vec3.UnitZ, 1e-12,
                            "toes upright");
                    }
                    Assert.That(pose.Joints[JointNames.Toe(side)].Z, Is.EqualTo(0).Within(Eps), "toe tip on the ground");
                    Assert.That(s.Attached(foot, rig.Heel(side)).Z, Is.EqualTo(0).Within(Eps), "heel on the ground");
                    Assert.That(pose.Joints[JointNames.Ankle(side)].Z, Is.EqualTo(rig.AnkleHeight(side)).Within(Eps), "ankle height");
                }
            }
            Assert.That(tested, Is.GreaterThan(400));
        }

        [Test]
        public void HeelLiftAndToeBendPitchTheFootAndToes()
        {
            foreach (Case c in RandomCases(57, 300))
            {
                foreach (int side in TestMath.Sides)
                {
                    FootTargets target = c.Targets.Foot(side);
                    Vec3 restFoot = c.Rig.Ball(side) - c.Rig.Rest(HumanBoneHierarchy.Foot(side));
                    Vec3 foot = c.Pose.Joints[JointNames.Ball(side)] - c.Pose.Joints[JointNames.Ankle(side)];
                    Assert.That(TestMath.Elevation(foot), Is.EqualTo(TestMath.Elevation(restFoot) - target.HeelLift).Within(Eps),
                        "heel lift pitches the foot about the ball");
                    Assert.That(TestMath.SignedYaw(foot, target.Forward), Is.EqualTo(0).Within(Eps), "the foot points forward");
                    if (c.Rig.Has(HumanBoneHierarchy.Toes(side)))
                    {
                        // the toes turn like the foot, but tipped down by the toe bend instead of the heel lift
                        Vec3 restForward = c.Rig.FootForward(side);
                        Vec3 expected = MathUtil.Rot(c.Rig.ToeTip(side) - c.Rig.Ball(side), Vec3.Cross(restForward, Vec3.UnitZ),
                            -target.ToeBend);
                        expected = MathUtil.Rot(expected, Vec3.UnitZ, TestMath.SignedYaw(restForward, target.Forward));
                        Vec3 toes = c.Pose.Joints[JointNames.Toe(side)] - c.Pose.Joints[JointNames.Ball(side)];
                        TestMath.AssertClose(toes, expected, Eps, "toe bend tips the toes from their rest attitude");
                    }
                }
            }
        }

        [Test]
        public void HandsContinueTheForearmOrPointAlongTheirDirection()
        {
            foreach (Case c in RandomCases(58, 300))
            {
                foreach (int side in TestMath.Sides)
                {
                    ArmTargets arm = c.Targets.Arm(side);
                    Quat hand = c.Pose.WorldDeltas[HumanBoneHierarchy.Hand(side)];
                    if (arm.Direction.HasValue)
                    {
                        Vec3 direction = arm.Direction.Value.Normalized;
                        Vec3 restHand = c.Rig.Rest(HumanBoneHierarchy.Hand(side)) - c.Rig.Rest(HumanBoneHierarchy.LowerArm(side));
                        TestMath.AssertClose(hand.Rotate(restHand.Normalized), direction, Eps, "hand direction");
                        // the wrist bends without rolling the hand about the forearm: the turn from the forearm's
                        // rotation to the hand's is about an axis perpendicular to both directions
                        Vec3 forearm = (c.Pose.Joints[JointNames.Hand(side)] - c.Pose.Joints[JointNames.Elbow(side)]).Normalized;
                        Quat turn = hand * c.Pose.WorldDeltas[HumanBoneHierarchy.LowerArm(side)].Inverse;
                        var axis = new Vec3(turn.X, turn.Y, turn.Z);
                        Assert.That(Vec3.Dot(axis, forearm), Is.EqualTo(0).Within(Eps), "no roll about the forearm");
                        Assert.That(Vec3.Dot(axis, direction), Is.EqualTo(0).Within(Eps), "no roll about the hand");
                    }
                    else
                    {
                        Assert.That(hand, Is.EqualTo(c.Pose.WorldDeltas[HumanBoneHierarchy.LowerArm(side)]));
                    }
                    Assert.That(side > 0 ? c.Pose.RightGrip : c.Pose.LeftGrip, Is.EqualTo(arm.Grip));
                }
            }
        }

        [Test]
        public void GripAxesRollTheHandAboutItsDirection()
        {
            var rng = new Random(63);
            for (int i = 0; i < 300; i++)
            {
                HumanoidRig rig = TestRigs.Random(rng);
                PoseTargets targets = PoseTargets.RandomReachable(rig, rng, rng.Next(2) == 0);
                HumanoidPoseBuilder builder = targets.ApplyLimbs(targets.ApplyTorso(new HumanoidPoseBuilder(rig)));
                PoseResult withoutAxes = builder.Build();
                var directions = new Vec3[2];
                var axes = new Vec3[2];
                foreach (int side in TestMath.Sides)
                {
                    ArmTargets arm = targets.Arm(side);
                    Vec3 direction = TestMath.RandomUnit(rng) * TestMath.Uniform(rng, 0.5, 2);
                    Vec3 axis = direction + TestMath.RandomUnit(rng) * TestMath.Uniform(rng, 0.2, 3);
                    builder.SetArm(side, arm.Hand, arm.Pole, direction, arm.Grip, axis);
                    directions[PoseTargets.Index(side)] = direction;
                    axes[PoseTargets.Index(side)] = axis;
                }
                PoseResult pose = builder.Build();
                foreach (int side in TestMath.Sides)
                {
                    Quat hand = pose.WorldDeltas[HumanBoneHierarchy.Hand(side)];
                    Vec3 direction = directions[PoseTargets.Index(side)];
                    Vec3 restHand = rig.Rest(HumanBoneHierarchy.Hand(side)) - rig.Rest(HumanBoneHierarchy.LowerArm(side));
                    TestMath.AssertClose(hand.Rotate(restHand.Normalized), direction.Normalized, Eps, "hand direction");
                    // the width of the rest palm, +y (its part across the rest hand), turns along the grip axis
                    TestMath.AssertClose(hand.Rotate(TestMath.Perpendicular(Vec3.UnitY, restHand)),
                        TestMath.Perpendicular(axes[PoseTargets.Index(side)], direction), Eps, "palm width");
                    // the arm itself is posed as without the axis
                    foreach (HumanBone bone in new[] { HumanBoneHierarchy.UpperArm(side), HumanBoneHierarchy.LowerArm(side) })
                    {
                        Assert.That(pose.WorldDeltas[bone], Is.EqualTo(withoutAxes.WorldDeltas[bone]), bone.ToString());
                    }
                }
            }
            var b = new HumanoidPoseBuilder(HumanoidRig.Measure(SyntheticRigs.Generic(1.7)));
            Assert.Throws<ArgumentException>(() => b.SetArm(1, Vec3.Zero, Vec3.UnitY, null, 1, Vec3.UnitZ), "no hand direction");
            Assert.Throws<ArgumentException>(() => b.SetArm(1, Vec3.Zero, Vec3.UnitY, Vec3.UnitX, 1, Vec3.Zero), "zero");
            Assert.Throws<ArgumentException>(() => b.SetArm(1, Vec3.Zero, Vec3.UnitY, Vec3.UnitX, 1, Vec3.UnitX * -3), "along the hand");
            Assert.Throws<ArgumentException>(() => b.SetArm(-1, Vec3.Zero, Vec3.UnitY, Vec3.UnitX, 1, new Vec3(0, double.NaN, 0)), "NaN");
        }

        [Test]
        public void DeltasAreUnitQuaternionsOfBonesTheRigHas()
        {
            foreach (Case c in RandomCases(59, 300))
            {
                foreach (KeyValuePair<HumanBone, Quat> delta in c.Pose.WorldDeltas)
                {
                    TestMath.AssertUnit(delta.Value, delta.Key.ToString());
                    Assert.That(c.Rig.Has(delta.Key), delta.Key + " is not on the rig");
                }
                foreach (HumanBone bone in c.Rig.Bones)
                {
                    bool shoulder = bone == HumanBone.LeftShoulder || bone == HumanBone.RightShoulder;
                    Assert.That(c.Pose.WorldDeltas.ContainsKey(bone), Is.Not.EqualTo(shoulder),
                        bone + ": every bone but the shoulders, which follow the chest, is posed");
                }
            }
        }

        [Test]
        public void JointsAreForwardKinematicsOfTheDeltas()
        {
            var rng = new Random(60);
            foreach (Case c in RandomCases(60, 200))
            {
                TestPoses.AssertJointsAreFk(c.Rig, c.Pose, 1e-12);
                // and Unity, applying the deltas to a transform hierarchy, puts the bones on the joints
                var unity = new TransformHierarchy(c.Rig, rng);
                unity.Apply(c.Pose);
                (string joint, HumanBone bone)[] bones =
                {
                    (JointNames.Pelvis, HumanBone.Hips), (JointNames.Waist, HumanBone.Spine), (JointNames.NeckTop, HumanBone.Head),
                    (JointNames.Hand(1), HumanBone.RightHand), (JointNames.Elbow(-1), HumanBone.LeftLowerArm),
                    (JointNames.Knee(1), HumanBone.RightLowerLeg), (JointNames.Ankle(-1), HumanBone.LeftFoot),
                    (JointNames.Shoulder(-1), HumanBone.LeftUpperArm), (JointNames.Hip(1), HumanBone.RightUpperLeg),
                };
                foreach ((string joint, HumanBone bone) in bones)
                {
                    TestMath.AssertClose(unity.Position(bone), c.Pose.Joints[joint], Eps, joint);
                }
            }
        }

        [Test]
        public void TorsoPointsAreFinalBeforeTheLimbsAreSet()
        {
            var rng = new Random(61);
            for (int i = 0; i < 200; i++)
            {
                HumanoidRig rig = TestRigs.Random(rng);
                PoseTargets targets = PoseTargets.RandomReachable(rig, rng, rng.Next(2) == 0);
                var builder = new HumanoidPoseBuilder(rig);
                foreach (HumanBone bone in HumanBoneHierarchy.TorsoChain)
                {
                    TestMath.AssertClose(builder.TorsoPoint(bone), rig.TorsoRest(bone), 1e-12, "rest " + bone);
                }
                targets.ApplyTorso(builder);
                var early = new Dictionary<HumanBone, Vec3>();
                foreach (HumanBone bone in HumanBoneHierarchy.TorsoChain)
                {
                    early[bone] = builder.TorsoPoint(bone);
                }
                PoseResult pose = targets.ApplyLimbs(builder).Build();
                PosedSkeleton skeleton = HumanoidSkeleton.Pose(rig, pose);
                foreach (HumanBone bone in HumanBoneHierarchy.TorsoChain)
                {
                    Assert.That(builder.TorsoPoint(bone), Is.EqualTo(early[bone]), "limbs do not move " + bone);
                    TestMath.AssertClose(skeleton.TorsoPoint(bone), early[bone], 1e-12, bone.ToString());
                }
                TestMath.AssertClose(pose.Joints[JointNames.Pelvis], early[HumanBone.Hips], 1e-12, "pelvis");
                TestMath.AssertClose(pose.Joints[JointNames.Chest], early[HumanBone.Chest], 1e-12, "chest");
                TestMath.AssertClose(pose.Joints[JointNames.NeckBase], early[HumanBone.Neck], 1e-12, "neck base");
                TestMath.AssertClose(pose.Joints[JointNames.NeckTop], early[HumanBone.Head], 1e-12, "neck top");
                foreach (int side in TestMath.Sides)
                {
                    TestMath.AssertClose(pose.Joints[JointNames.Shoulder(side)], early[HumanBoneHierarchy.UpperArm(side)], 1e-12,
                        "shoulder");
                    TestMath.AssertClose(pose.Joints[JointNames.Hip(side)], early[HumanBoneHierarchy.UpperLeg(side)], 1e-12, "hip");
                }
            }
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new HumanoidPoseBuilder(HumanoidRig.Measure(SyntheticRigs.Generic(1.7))).TorsoPoint(HumanBone.LeftHand));
        }

        [Test]
        public void TorsoBonesShareTheTurnFromHipsToChestByHeight()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.7));
            Quat hip = Quat.FromAxisAngle(Vec3.UnitZ, 0.4) * Quat.FromAxisAngle(Vec3.UnitX, 0.2);
            Quat chest = hip * Quat.FromAxisAngle(Vec3.UnitZ, 0.6) * Quat.FromAxisAngle(Vec3.UnitX, 0.3);
            Quat head = chest * Quat.FromAxisAngle(Vec3.UnitX, -0.5) * Quat.FromAxisAngle(Vec3.UnitZ, 0.3);
            PoseResult pose = new HumanoidPoseBuilder(rig).SetPelvis(new Vec3(0.1, 0.2, 0.7), hip).SetTorso(chest).SetHead(head).Build();
            double hips = rig.HipsHeight, span = rig.Rest(HumanBone.UpperChest).Z - hips;
            double Share(HumanBone bone) => (rig.Rest(bone).Z - hips) / span;
            TestMath.AssertSameRotation(pose.WorldDeltas[HumanBone.Hips], hip, 1e-12, "hips");
            TestMath.AssertSameRotation(pose.WorldDeltas[HumanBone.Spine], Quat.Slerp(hip, chest, Share(HumanBone.Spine)), 1e-12, "spine");
            TestMath.AssertSameRotation(pose.WorldDeltas[HumanBone.Chest], Quat.Slerp(hip, chest, Share(HumanBone.Chest)), 1e-12, "chest");
            TestMath.AssertSameRotation(pose.WorldDeltas[HumanBone.UpperChest], chest, 1e-12, "upper chest");
            TestMath.AssertSameRotation(pose.WorldDeltas[HumanBone.Neck], Quat.Slerp(chest, head, HumanoidPoseBuilder.NeckShare), 1e-12,
                "neck");
            TestMath.AssertSameRotation(pose.WorldDeltas[HumanBone.Head], head, 1e-12, "head");
            Assert.That(Share(HumanBone.Spine), Is.GreaterThan(0).And.LessThan(Share(HumanBone.Chest)));
            Assert.That(Share(HumanBone.Chest), Is.LessThan(1));

            // with the Spine alone, it takes the chest frame; without a Neck the Head takes the head frame alone
            HumanoidRig minimal = HumanoidRig.Measure(SyntheticRigs.Generic(1.7, null, true));
            PoseResult small = new HumanoidPoseBuilder(minimal).SetPelvis(Vec3.Zero, hip).SetTorso(chest).SetHead(head).Build();
            Assert.That(small.WorldDeltas.Keys, Is.EquivalentTo(new[] { HumanBone.Hips, HumanBone.Spine, HumanBone.Head }));
            TestMath.AssertSameRotation(small.WorldDeltas[HumanBone.Spine], chest, 1e-12, "minimal spine");
            TestMath.AssertSameRotation(small.WorldDeltas[HumanBone.Head], head, 1e-12, "minimal head");
        }

        [Test]
        public void UnsetFramesFollowTheirParentPart()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.7));
            Quat hip = Quat.FromAxisAngle(new Vec3(0.2, 0.1, 1), 0.7);
            PoseResult hipsOnly = new HumanoidPoseBuilder(rig).SetPelvis(rig.Rest(HumanBone.Hips), hip).Build();
            foreach (KeyValuePair<HumanBone, Quat> delta in hipsOnly.WorldDeltas)
            {
                TestMath.AssertSameRotation(delta.Value, hip, 1e-12, delta.Key + " follows the hips");
            }
            Quat chest = Quat.FromAxisAngle(Vec3.UnitX, 0.4);
            PoseResult noHead = new HumanoidPoseBuilder(rig).SetTorso(chest).Build();
            TestMath.AssertSameRotation(noHead.WorldDeltas[HumanBone.Neck], chest, 1e-12, "neck follows the chest");
            TestMath.AssertSameRotation(noHead.WorldDeltas[HumanBone.Head], chest, 1e-12, "head follows the chest");
            TestMath.AssertSameRotation(noHead.WorldDeltas[HumanBone.Hips], Quat.Identity, 1e-12, "hips unturned");
            // unposed limbs move rigidly with the torso
            PosedSkeleton s = HumanoidSkeleton.Pose(rig, hipsOnly);
            foreach (HumanBone bone in rig.Bones)
            {
                Vec3 expected = rig.Rest(HumanBone.Hips) + hip.Rotate(rig.Rest(bone) - rig.Rest(HumanBone.Hips));
                TestMath.AssertClose(s.Position(bone), expected, 1e-12, bone.ToString());
            }
            // frames are normalized
            var scaled = new Quat(hip.W * 2, hip.X * 2, hip.Y * 2, hip.Z * 2);
            PoseResult fromScaled = new HumanoidPoseBuilder(rig).SetPelvis(rig.Rest(HumanBone.Hips), scaled).Build();
            TestMath.AssertUnit(fromScaled.WorldDeltas[HumanBone.Hips], "hips");
        }

        [TestCaseSource(typeof(TestRigs), nameof(TestRigs.Variants))]
        public void MirroredTargetsGiveMirroredPoses(RigInput input)
        {
            HumanoidRig rig = HumanoidRig.Measure(input);
            var rng = new Random(62);
            for (int i = 0; i < 40; i++)
            {
                PoseTargets targets = PoseTargets.RandomReachable(rig, rng, i % 2 == 0);
                PoseResult pose = targets.Build(rig);
                PoseResult mirrored = targets.Mirrored().Build(rig);
                TestMath.AssertClose(mirrored.HipsPosition, TestMath.Mirror(pose.HipsPosition), Eps, "hips");
                foreach (KeyValuePair<string, Vec3> joint in pose.Joints)
                {
                    TestMath.AssertClose(mirrored.Joints[TestMath.MirrorJointName(joint.Key)], TestMath.Mirror(joint.Value), Eps, joint.Key);
                }
                Assert.That(mirrored.WorldDeltas.Count, Is.EqualTo(pose.WorldDeltas.Count));
                foreach (KeyValuePair<HumanBone, Quat> delta in pose.WorldDeltas)
                {
                    TestMath.AssertSameRotation(mirrored.WorldDeltas[HumanBoneHierarchy.Mirror(delta.Key)], TestMath.Mirror(delta.Value),
                        Eps, delta.Key.ToString());
                }
                Assert.That(mirrored.LeftGrip, Is.EqualTo(pose.RightGrip));
                Assert.That(mirrored.RightGrip, Is.EqualTo(pose.LeftGrip));
            }
        }

        [Test]
        public void TargetsOutOfReachAreClampedDeterministically()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.7));
            var builder = new HumanoidPoseBuilder(rig);
            Vec3 shoulder = builder.TorsoPoint(HumanBone.RightUpperArm);
            Vec3 hip = builder.TorsoPoint(HumanBone.LeftUpperLeg);
            Vec3 handTarget = shoulder + new Vec3(1, 2, -0.5) * rig.ArmLength;
            var ballTarget = new Vec3(hip.X, hip.Y + 0.1, -rig.LegLength);
            builder.SetArm(1, handTarget, -Vec3.UnitY).SetFoot(-1, ballTarget, Vec3.UnitY, 0, Vec3.UnitY);
            PoseResult a = builder.Build();
            PoseResult b = builder.Build();
            foreach (KeyValuePair<string, Vec3> joint in a.Joints)
            {
                Assert.That(b.Joints[joint.Key], Is.EqualTo(joint.Value), joint.Key);
                Assert.That(TestMath.IsFinite(joint.Value), joint.Key);
            }
            foreach (KeyValuePair<HumanBone, Quat> delta in a.WorldDeltas)
            {
                Assert.That(b.WorldDeltas[delta.Key], Is.EqualTo(delta.Value), delta.Key.ToString());
            }
            double reach = rig.UpperArmLength(1) + rig.ForearmLength(1) - 1e-3;
            TestMath.AssertClose(a.Joints[JointNames.Hand(1)], shoulder + (handTarget - shoulder).Normalized * reach, Eps,
                "the hand reaches as far as it can towards its target");
            // the ankle falls short, and the foot points at the ball target
            Vec3 ankle = a.Joints[JointNames.Ankle(-1)];
            Assert.That(Vec3.Distance(hip, ankle), Is.EqualTo(rig.ThighLength(-1) + rig.ShinLength(-1) - 1e-3).Within(Eps));
            TestMath.AssertClose((a.Joints[JointNames.Ball(-1)] - ankle).Normalized, (ballTarget - ankle).Normalized, Eps, "foot aim");
        }

        [Test]
        public void PassesGripPropAndGroundingThrough()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.7));
            var prop = new PropPose(PropKind.Gun, new Vec3(0, 0.2, 0.9), new Vec3(0, 0.8, 0.9), Vec3.UnitZ);
            var builder = new HumanoidPoseBuilder(rig);
            PoseResult pose = builder.SetArm(-1, builder.TorsoPoint(HumanBone.LeftUpperArm) + new Vec3(0, 0.3, -0.2), -Vec3.UnitY, null, 0.75)
                .SetProp(prop).SetGroundToLowestPoint(true).Build();
            Assert.That(pose.LeftGrip, Is.EqualTo(0.75));
            Assert.That(pose.RightGrip, Is.EqualTo(0));
            Assert.That(pose.Prop, Is.SameAs(prop));
            Assert.That(pose.GroundToLowestPoint, Is.True);
            PoseResult cleared = builder.SetProp(null).SetGroundToLowestPoint(false).Build();
            Assert.That(cleared.Prop, Is.Null);
            Assert.That(cleared.GroundToLowestPoint, Is.False);
        }

        [Test]
        public void PosesVaryContinuouslyWithTheirTargets()
        {
            foreach (RigInput input in new[] { SyntheticRigs.Generic(1.7), SyntheticRigs.Chibi(1.0, true) })
            {
                HumanoidRig rig = HumanoidRig.Measure(input);
                PoseResult previous = null;
                const int steps = 400;
                for (int i = 0; i <= steps; i++)
                {
                    PoseResult pose = WalkCycle(rig, (double)i / steps);
                    if (previous != null)
                    {
                        foreach (KeyValuePair<HumanBone, Quat> delta in pose.WorldDeltas)
                        {
                            Assert.That(TestMath.RotationAngle(delta.Value, previous.WorldDeltas[delta.Key]), Is.LessThan(0.08),
                                delta.Key + " jumps at step " + i);
                        }
                        foreach (KeyValuePair<string, Vec3> joint in pose.Joints)
                        {
                            Assert.That(Vec3.Distance(joint.Value, previous.Joints[joint.Key]), Is.LessThan(0.03 * rig.HeadTopHeight),
                                joint.Key + " jumps at step " + i);
                        }
                    }
                    previous = pose;
                }
            }
        }

        // a walk-like loop (phase 0..1) with swinging arms, a turning torso and stepping feet
        private static PoseResult WalkCycle(HumanoidRig rig, double phase)
        {
            double a = MathUtil.Tau * phase;
            double legs = rig.LegLength, arms = rig.ArmLength;
            Quat hip = TestMath.Yaw(0.15 * Math.Sin(a)) * Quat.FromAxisAngle(Vec3.UnitX, 0.1);
            Quat chest = TestMath.Yaw(-0.25 * Math.Sin(a)) * Quat.FromAxisAngle(Vec3.UnitX, 0.15);
            var builder = new HumanoidPoseBuilder(rig)
                .SetPelvis(rig.Rest(HumanBone.Hips) + new Vec3(0, 0, -legs * (0.08 + 0.03 * Math.Cos(2 * a))), hip)
                .SetTorso(chest)
                .SetHead(TestMath.Yaw(0.3 * Math.Sin(a)));
            foreach (int side in TestMath.Sides)
            {
                double swing = Math.Sin(a + (side > 0 ? 0 : Math.PI));
                Vec3 shoulder = builder.TorsoPoint(HumanBoneHierarchy.UpperArm(side));
                Vec3 hand = shoulder + chest.Rotate(new Vec3(side * 0.15, 0.45 * swing, -0.8) * arms);
                builder.SetArm(side, hand, chest.Rotate(new Vec3(side * 0.3, -1, -0.2)), chest.Rotate(new Vec3(0, 0.3 * swing, -1)),
                    0.5 + 0.5 * swing);
                Vec3 ball = rig.Ball(side) + new Vec3(0, 0.3 * legs * swing, 0.1 * legs * Math.Max(0, Math.Cos(a + (side > 0 ? 0 : Math.PI))));
                builder.SetFoot(side, ball, TestMath.Yaw(0.1 * swing).Rotate(Vec3.UnitY), 0.4 * Math.Max(0, -swing),
                    hip.Rotate(Vec3.UnitY), 0.2 * Math.Max(0, swing));
            }
            return builder.Build();
        }

        [Test]
        public void RetargetsTheDragonsAnimations()
        {
            string[] animations =
            {
                AnimationSpec.Idle, AnimationSpec.IdleGun, AnimationSpec.Run, AnimationSpec.RunGun, AnimationSpec.Mine,
                AnimationSpec.Hover, AnimationSpec.HoverGun, AnimationSpec.Fly, AnimationSpec.FlyGun, AnimationSpec.TakeOff,
            };
            double dragonHips = DragonPoses.Pose(AnimationSpec.Idle, BodyShape.Male, 0).Pelvis.Z;
            int reached = 0;
            foreach (RigInput input in new[] { SyntheticRigs.Generic(1.7), SyntheticRigs.Chibi(1.2), SyntheticRigs.Generic(1.5, null, true) })
            {
                HumanoidRig rig = HumanoidRig.Measure(input);
                double scale = rig.HipsHeight / dragonHips;
                foreach (string animation in animations)
                {
                    for (int frame = 0; frame < 8; frame++)
                    {
                        DragonPose dragon = DragonPoses.Pose(animation, BodyShape.Male, frame / 8.0);
                        HumanoidPoseBuilder builder = Retarget(rig, dragon, scale);
                        PoseResult pose = builder.Build();
                        TestPoses.AssertJointsAreFk(rig, pose, 1e-12);
                        foreach (KeyValuePair<HumanBone, Quat> delta in pose.WorldDeltas)
                        {
                            TestMath.AssertUnit(delta.Value, animation + " " + delta.Key);
                        }
                        foreach (int side in TestMath.Sides)
                        {
                            Vec3 shoulder = pose.Joints[JointNames.Shoulder(side)];
                            Vec3 target = shoulder + (dragon.Arms[side].Hand - dragon.Arms[side].Shoulder) * scale;
                            double l1 = rig.UpperArmLength(side), l2 = rig.ForearmLength(side);
                            double distance = Vec3.Distance(shoulder, target);
                            if (distance > Math.Abs(l1 - l2) + 2e-3 && distance < l1 + l2 - 2e-3)
                            {
                                reached++;
                                TestMath.AssertClose(pose.Joints[JointNames.Hand(side)], target, Eps, animation + " hand");
                            }
                        }
                        Assert.That(pose.Prop, dragon.Prop == null ? Is.Null : Is.Not.Null);
                    }
                }
            }
            Assert.That(reached, Is.GreaterThan(100));
        }

        // the dragon's pose on a humanoid: its frames, its pelvis scaled, hands placed relative to the shoulders
        private static HumanoidPoseBuilder Retarget(HumanoidRig rig, DragonPose dragon, double scale)
        {
            var builder = new HumanoidPoseBuilder(rig)
                .SetPelvis(dragon.Pelvis * scale, Quat.FromBasis(dragon.HipRight, dragon.HipForward, dragon.HipUp))
                .SetTorso(Quat.FromBasis(dragon.TorsoRight, dragon.TorsoForward, dragon.TorsoUp))
                .SetHead(Quat.FromBasis(dragon.HeadRight, dragon.HeadForward, dragon.HeadUp))
                .SetProp(dragon.Prop?.ToPropPose());
            foreach (int side in TestMath.Sides)
            {
                DragonArm arm = dragon.Arms[side];
                Vec3 shoulder = builder.TorsoPoint(HumanBoneHierarchy.UpperArm(side));
                builder.SetArm(side, shoulder + (arm.Hand - arm.Shoulder) * scale, arm.Elbow - (arm.Shoulder + arm.Hand) / 2,
                    arm.HandDirection, dragon.Prop != null ? 1 : 0);
                DragonLeg leg = dragon.Legs[side];
                Vec3 forward = (leg.Toe - leg.Ball).Horizontal.Length > 1e-6 ? leg.Toe - leg.Ball : dragon.HipForward;
                builder.SetFoot(side, leg.Ball * scale, forward, 0.3, leg.Knee - (leg.Hip + leg.Ankle) / 2);
            }
            return builder;
        }

        [Test]
        public void DigitigradeRigsKeepTheirAnklesUp()
        {
            RigInput input = SyntheticRigs.Generic(1.7, null, true);
            foreach (int side in TestMath.Sides)
            {
                Vec3 hip = input.Bones[HumanBoneHierarchy.UpperLeg(side)];
                input.Bones[HumanBoneHierarchy.LowerLeg(side)] = new Vec3(hip.X, 0.1, hip.Z * 0.6);
                input.Bones[HumanBoneHierarchy.Foot(side)] = new Vec3(hip.X, -0.15, hip.Z * 0.33);
            }
            HumanoidRig rig = HumanoidRig.Measure(input);
            Assert.That(rig.LegStyle, Is.EqualTo(LegStyle.Digitigrade));
            var builder = new HumanoidPoseBuilder(rig).SetPelvis(rig.Rest(HumanBone.Hips) - new Vec3(0, 0, 0.05), Quat.Identity);
            foreach (int side in TestMath.Sides)
            {
                builder.SetFoot(side, rig.Ball(side) + new Vec3(0, 0.05, 0), Vec3.UnitY, 0, Vec3.UnitY);
            }
            PoseResult pose = builder.Build();
            foreach (int side in TestMath.Sides)
            {
                TestMath.AssertClose(pose.Joints[JointNames.Ball(side)], rig.Ball(side) + new Vec3(0, 0.05, 0), Eps, "ball");
                Assert.That(pose.Joints[JointNames.Ankle(side)].Z, Is.EqualTo(rig.AnkleHeight(side)).Within(Eps), "ankle stays up");
            }
        }

        [Test]
        public void RejectsBadArguments()
        {
            Assert.Throws<ArgumentNullException>(() => new HumanoidPoseBuilder(null));
            var b = new HumanoidPoseBuilder(HumanoidRig.Measure(SyntheticRigs.Generic(1.7)));
            var nan = new Vec3(double.NaN, 0, 0);
            var zero = new Quat(0, 0, 0, 0);
            Assert.Throws<ArgumentException>(() => b.SetPelvis(nan, Quat.Identity));
            Assert.Throws<ArgumentException>(() => b.SetPelvis(Vec3.Zero, zero));
            Assert.Throws<ArgumentException>(() => b.SetTorso(new Quat(double.NaN, 0, 0, 0)));
            Assert.Throws<ArgumentException>(() => b.SetHead(zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.SetArm(0, Vec3.Zero, Vec3.UnitY));
            Assert.Throws<ArgumentException>(() => b.SetArm(1, nan, Vec3.UnitY));
            Assert.Throws<ArgumentException>(() => b.SetArm(1, Vec3.Zero, nan));
            Assert.Throws<ArgumentException>(() => b.SetArm(1, Vec3.Zero, Vec3.UnitY, Vec3.Zero));
            Assert.Throws<ArgumentException>(() => b.SetArm(1, Vec3.Zero, Vec3.UnitY, nan));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.SetArm(1, Vec3.Zero, Vec3.UnitY, null, 1.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.SetArm(-1, Vec3.Zero, Vec3.UnitY, null, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.SetFoot(2, Vec3.Zero, Vec3.UnitY, 0, Vec3.UnitY));
            Assert.Throws<ArgumentException>(() => b.SetFoot(1, nan, Vec3.UnitY, 0, Vec3.UnitY));
            Assert.Throws<ArgumentException>(() => b.SetFoot(1, Vec3.Zero, Vec3.UnitZ, 0, Vec3.UnitY));
            Assert.Throws<ArgumentException>(() => b.SetFoot(1, Vec3.Zero, Vec3.UnitY, double.NaN, Vec3.UnitY));
            Assert.Throws<ArgumentException>(() => b.SetFoot(1, Vec3.Zero, Vec3.UnitY, 0, nan));
            Assert.Throws<ArgumentException>(() => b.SetFoot(1, Vec3.Zero, Vec3.UnitY, 0, Vec3.UnitY, double.PositiveInfinity));
        }
    }
}
