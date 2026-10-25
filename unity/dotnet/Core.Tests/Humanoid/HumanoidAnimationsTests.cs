using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Humanoid
{
    public class HumanoidAnimationsTests
    {
        public static IEnumerable<TestCaseData> Rigs()
        {
            yield return new TestCaseData(SyntheticRigs.Generic(1.6)).SetArgDisplayNames("generic");
            yield return new TestCaseData(SyntheticRigs.Chibi(1.0)).SetArgDisplayNames("chibi");
            yield return new TestCaseData(SyntheticRigs.Generic(1.8, null, true)).SetArgDisplayNames("minimal");
            yield return new TestCaseData(SyntheticRigs.Generic(1.7, new SyntheticRigProportions { Legs = 1.2, Arms = 0.85 }))
                .SetArgDisplayNames("long legs, short arms");
        }

        private static HumanoidAnimations Animations(RigInput input, out HumanoidRig rig)
        {
            rig = HumanoidRig.Measure(input);
            return new HumanoidAnimations(rig);
        }

        private static PoseResult Frame(HumanoidAnimations animations, string animation, int frame, int row = 0) =>
            animations.Pose(animation, AnimationSpec.FramePhase(animation, frame), AnimationSpec.Direction(animation, row).Move);

        [TestCaseSource(nameof(Rigs))]
        public void EveryFrameIsAConsistentPose(RigInput input)
        {
            HumanoidAnimations animations = Animations(input, out HumanoidRig rig);
            int count = 0;
            foreach ((string animation, int row, int frame, PoseResult pose) in animations.AllFrames(true))
            {
                string what = $"{animation} row {row + 1} frame {frame + 1}";
                foreach (KeyValuePair<HumanBone, Quat> delta in pose.WorldDeltas)
                {
                    TestMath.AssertUnit(delta.Value, what + " " + delta.Key);
                }
                Assert.That(pose.Joints.Keys, Is.EquivalentTo(TestPoses.AllJointNames()), what);
                foreach (KeyValuePair<string, Vec3> joint in pose.Joints)
                {
                    Assert.That(TestMath.IsFinite(joint.Value), what + " " + joint.Key);
                }
                // the builder fills the joints from forward kinematics; only the corpse is moved afterwards
                // (PoseTransforms), which must keep them matching its bones
                if (animation == AnimationSpec.Corpse)
                {
                    TestPoses.AssertJointsAreFk(rig, pose, 1e-9);
                }
                count++;
            }
            Assert.That(count, Is.EqualTo(AnimationSpec.ForPack(true).Sum(a => a.DirectionCount * a.FrameCount)));
        }

        [TestCaseSource(nameof(Rigs))]
        public void StandingFeetStayOnTheirRestBallPoints(RigInput input)
        {
            HumanoidAnimations animations = Animations(input, out HumanoidRig rig);
            for (int f = 0; f < AnimationSpec.Get(AnimationSpec.Idle).FrameCount; f++)
            {
                PoseResult pose = Frame(animations, AnimationSpec.Idle, f);
                foreach (int side in TestMath.Sides)
                {
                    Vec3 ball = pose.Joints[JointNames.Ball(side)];
                    Assert.That(ball.Z, Is.EqualTo(rig.Ball(side).Z).Within(1e-6), $"idle frame {f + 1} side {side}");
                }
            }
        }

        [TestCaseSource(nameof(Rigs))]
        public void RunningFeetArePlantedInTheirStanceWithTheToesInLineInTheAir(RigInput input)
        {
            HumanoidAnimations animations = Animations(input, out HumanoidRig rig);
            int frames = AnimationSpec.Get(AnimationSpec.Run).FrameCount;
            int swinging = 0;
            for (int f = 0; f < frames; f++)
            {
                double t = AnimationSpec.FramePhase(AnimationSpec.Run, f);
                PoseResult pose = Frame(animations, AnimationSpec.Run, f);
                foreach ((int side, double phase) in new[] { (-1, t), (1, t + 0.5) })
                {
                    string what = $"run frame {f + 1}: the {(side > 0 ? "right" : "left")} foot";
                    if (MathUtil.PositiveModulo(phase, 1) < 0.5)
                    {
                        Assert.That(pose.Joints[JointNames.Ball(side)].Z, Is.EqualTo(rig.Ball(side).Z).Within(1e-6),
                            what + " should be planted");
                        if (rig.Has(HumanBoneHierarchy.Toes(side)))
                        {
                            // without a Toes bone the whole foot pivots on the ball and its tip dips a little
                            Assert.That(pose.Joints[JointNames.Toe(side)].Z, Is.GreaterThan(-1e-3),
                                what + ": the toes stay flat at push-off instead of pitching into the ground");
                        }
                    }
                    else if (pose.Joints[JointNames.Ball(side)].Z > rig.Ball(side).Z + 1e-3)
                    {
                        Assert.That(pose.Joints[JointNames.Toe(side)].Z, Is.GreaterThan(0), what + ": the toe tip clears the ground");
                        if (rig.Has(HumanBoneHierarchy.Toes(side)))
                        {
                            double bend = TestMath.RotationAngle(pose.WorldDeltas[HumanBoneHierarchy.Toes(side)],
                                pose.WorldDeltas[HumanBoneHierarchy.Foot(side)]);
                            Assert.That(bend, Is.LessThan(MathUtil.Radians(3)), what + ": the toes stay in line with the foot");
                        }
                        swinging++;
                    }
                }
            }
            Assert.That(swinging, Is.GreaterThan(0), "some frames have a foot in the air");
        }

        [TestCaseSource(nameof(Rigs))]
        public void FlightIsOffTheGround(RigInput input)
        {
            HumanoidAnimations animations = Animations(input, out HumanoidRig rig);
            foreach (string animation in new[] { AnimationSpec.Hover, AnimationSpec.HoverGun, AnimationSpec.Fly, AnimationSpec.FlyGun })
            {
                for (int f = 0; f < AnimationSpec.Get(animation).FrameCount; f++)
                {
                    double lowest = Frame(animations, animation, f).Joints.Values.Min(p => p.Z);
                    Assert.That(lowest, Is.GreaterThan(0.1), $"{animation} frame {f + 1}");
                }
            }
        }

        [TestCaseSource(nameof(Rigs))]
        public void TakeOffEndsInTheFirstHoverFrameWithoutAGun(RigInput input)
        {
            HumanoidAnimations animations = Animations(input, out _);
            PoseResult end = animations.Pose(AnimationSpec.TakeOff, 1.0, 0);
            PoseResult hover = animations.Pose(AnimationSpec.Hover, 0.0, 0);
            foreach (KeyValuePair<string, Vec3> joint in hover.Joints)
            {
                TestMath.AssertClose(end.Joints[joint.Key], joint.Value, 1e-9, joint.Key);
            }
            for (int f = 0; f < AnimationSpec.Get(AnimationSpec.TakeOff).FrameCount; f++)
            {
                Assert.That(Frame(animations, AnimationSpec.TakeOff, f).Prop, Is.Null, $"frame {f + 1}: no gun");
            }
            PoseResult start = animations.Pose(AnimationSpec.TakeOff, 0.0, 0);
            PoseResult idle = animations.Pose(AnimationSpec.IdleGun, 0.0, 0);
            TestMath.AssertClose(start.Joints[JointNames.Ball(1)], idle.Joints[JointNames.Ball(1)], 1e-9, "take-off starts standing");
        }

        [TestCaseSource(typeof(HumanoidArmTests), nameof(HumanoidArmTests.GunRigs))]
        public void BothHandsHoldTheGun(RigInput input)
        {
            HumanoidAnimations animations = Animations(input, out HumanoidRig rig);
            double size = rig.HeadTopHeight / 1.5;
            foreach (string animation in new[] { AnimationSpec.IdleGun, AnimationSpec.RunGun, AnimationSpec.HoverGun, AnimationSpec.FlyGun })
            {
                AnimationInfo info = AnimationSpec.Get(animation);
                for (int row = 0; row < info.DirectionCount; row++)
                {
                    for (int frame = 0; frame < info.FrameCount; frame++)
                    {
                        PoseResult pose = Frame(animations, animation, frame, row);
                        string what = $"{animation} row {row + 1} frame {frame + 1}";
                        Assert.That(pose.Prop, Is.Not.Null);
                        Assert.That(pose.Prop.Kind, Is.EqualTo(PropKind.Gun));
                        Assert.That(pose.LeftGrip, Is.EqualTo(1));
                        Assert.That(pose.RightGrip, Is.EqualTo(1));
                        double gap = DistanceToSegment(pose.Joints[JointNames.Hand(1)], pose.Prop.Base, pose.Prop.Tip);
                        Assert.That(gap, Is.LessThan(0.06 * size), $"{what}: the right hand is {gap:0.000} tiles from the gun");
                        // the support hand closes round the gun just under its axis, however short the arm
                        Vec3 under = pose.Prop.Up * (0.02 * size);
                        double miss = DistanceToSegment(HumanoidArmTests.Fist(rig, animations, pose, -1), pose.Prop.Base - under,
                            pose.Prop.Tip - under);
                        Assert.That(miss, Is.LessThan(0.002 * size), $"{what}: the left fist is {miss:0.000} tiles from the gun");
                    }
                }
            }
        }

        [TestCaseSource(nameof(Rigs))]
        public void ThePickHitsTheGroundOnTheImpactFrame(RigInput input)
        {
            HumanoidAnimations animations = Animations(input, out HumanoidRig rig);
            PoseResult impact = Frame(animations, AnimationSpec.Mine, AnimationSpec.MineImpactFrame - 1);
            Assert.That(impact.Prop.Kind, Is.EqualTo(PropKind.Pickaxe));
            Assert.That(impact.Prop.Tip.Z, Is.LessThan(rig.HipsHeight * 0.6), "the pick head comes down");
            Assert.That(impact.Prop.Tip.Y, Is.GreaterThan(0), "in front of the body");
            // the point strikes the ground well ahead of the feet, as the engineer's pick reaches the ore in front of it
            Vec3 point = PickPoint(impact);
            Assert.That(point.Z, Is.EqualTo(0).Within(1e-9), "the point is on the ground");
            double toes = TestMath.Sides.Max(side => impact.Joints[JointNames.Toe(side)].Y);
            Assert.That(point.Y, Is.GreaterThan(toes + 0.15 * rig.HeadTopHeight), "well ahead of the front foot");
            Assert.That(point.Y, Is.GreaterThan(0.4 * rig.HeadTopHeight), "most of a tile ahead at the usual size");
            for (int f = 0; f < AnimationSpec.Get(AnimationSpec.Mine).FrameCount; f++)
            {
                Assert.That(PickPoint(Frame(animations, AnimationSpec.Mine, f)).Z, Is.GreaterThan(-1e-9), $"frame {f + 1}: not in the ground");
            }
            // the wind-up, frames 4 to 8
            for (int f = 3; f < 8; f++)
            {
                PoseResult raised = Frame(animations, AnimationSpec.Mine, f);
                Assert.That(raised.Prop.Tip.Z, Is.GreaterThan(raised.Joints[JointNames.NeckBase].Z),
                    $"frame {f + 1}: raised above the shoulders");
            }
        }

        [TestCaseSource(nameof(Rigs))]
        public void ASizeScalesTheGroundAnimationsAndTheirPace(RigInput input)
        {
            // the exporter's Size in game measures the rig for another height: on the ground, everything scales
            const double scale = 1.3;
            var normal = new HumanoidAnimations(HumanoidRig.Measure(input, Conventions.EngineerHeight));
            var tall = new HumanoidAnimations(HumanoidRig.Measure(input, Conventions.EngineerHeight * scale));
            foreach (string animation in new[] { AnimationSpec.Idle, AnimationSpec.IdleGun, AnimationSpec.Run, AnimationSpec.RunGun,
                AnimationSpec.Mine, AnimationSpec.Corpse })
            {
                AnimationInfo info = AnimationSpec.Get(animation);
                for (int row = 0; row < Math.Min(info.DirectionCount, 3); row++)
                {
                    for (int f = 0; f < info.FrameCount; f++)
                    {
                        PoseResult a = Frame(normal, animation, f, row), b = Frame(tall, animation, f, row);
                        foreach (KeyValuePair<string, Vec3> joint in a.Joints)
                        {
                            TestMath.AssertClose(b.Joints[joint.Key], joint.Value * scale, 1e-9, $"{animation} row {row + 1} frame {f + 1} {joint.Key}");
                        }
                        if (a.Prop != null)
                        {
                            TestMath.AssertClose(b.Prop.Tip, a.Prop.Tip * scale, 1e-9, $"{animation} frame {f + 1} prop");
                        }
                    }
                }
            }
            // longer steps at the same speed: the feet don't slide
            Assert.That(tall.Timing.DistancePerFrame, Is.EqualTo(normal.Timing.DistancePerFrame * scale).Within(1e-12));
        }

        /// <summary>The point of the pick's head, as drawn (<see cref="StickFigure"/>).</summary>
        private static Vec3 PickPoint(PoseResult pose) =>
            pose.Prop.Tip - pose.Prop.Up * (StickFigure.PickHeadPoint * Vec3.Distance(pose.Prop.Base, pose.Prop.Tip));

        [TestCaseSource(nameof(Rigs))]
        public void TheCorpseLiesFlat(RigInput input)
        {
            HumanoidAnimations animations = Animations(input, out HumanoidRig rig);
            PoseResult corpse = animations.Pose(AnimationSpec.Corpse, 0, 0);
            Assert.That(corpse.GroundToLowestPoint, Is.True);
            IEnumerable<Vec3> joints = corpse.Joints.Values;
            double height = joints.Max(p => p.Z) - joints.Min(p => p.Z);
            double length = joints.Max(p => p.X) - joints.Min(p => p.X);
            Assert.That(height, Is.LessThan(0.45 * rig.HeadTopHeight), "lying down, not standing");
            Assert.That(length, Is.GreaterThan(0.6 * rig.HeadTopHeight), "stretched out along x");
            double centre = (joints.Max(p => p.X) + joints.Min(p => p.X)) / 2;
            Assert.That(Math.Abs(centre), Is.LessThan(0.25 * rig.HeadTopHeight), "centred on the entity position");
            Assert.That(corpse.Joints[JointNames.HeadTop].X, Is.GreaterThan(corpse.Joints[JointNames.Toe(1)].X), "head to the east");
        }

        [TestCaseSource(nameof(Rigs))]
        public void RunTimingKeepsTheEngineersPace(RigInput input)
        {
            HumanoidAnimations animations = Animations(input, out HumanoidRig rig);
            // the game moves distance_per_frame / animation_speed tiles per running frame, so a 12-frame cycle at
            // speed 0.6 covers the engineer's 22 x 0.13 / 0.6 tiles, scaled by leg length (dragon legs: 0.6)
            double cycle = 12 * animations.Timing.DistancePerFrame / 0.6;
            Assert.That(cycle, Is.EqualTo(22 * 0.13 / 0.6 * rig.LegLength / 0.6).Within(1e-12));
            Assert.That(animations.Timing.MineImpactFrame, Is.EqualTo(AnimationSpec.MineImpactFrame));
        }

        [Test]
        public void GunRowsTurnTheLegsButNotTheGun()
        {
            HumanoidAnimations animations = Animations(SyntheticRigs.Generic(1.6), out _);
            // run-gun row 3 is (gun north, moving east): the gun still aims north, the feet face east
            PoseResult pose = Frame(animations, AnimationSpec.RunGun, 0, 2);
            Vec3 aim = (pose.Prop.Tip - pose.Prop.Base).Horizontal.Normalized;
            Assert.That(aim.Y, Is.GreaterThan(0.99), "aims forward");
            Vec3 foot = (pose.Joints[JointNames.Toe(1)] - pose.Joints[JointNames.Ankle(1)]).Horizontal.Normalized;
            Assert.That(foot.X, Is.GreaterThan(0.9), "the foot points east, a quarter turn clockwise");
        }

        [Test]
        public void RejectsUnknownAnimationsAndBadNumbers()
        {
            HumanoidAnimations animations = Animations(SyntheticRigs.Generic(1.6), out _);
            Assert.Throws<ArgumentException>(() => animations.Pose("dance", 0, 0));
            Assert.Throws<ArgumentException>(() => animations.Pose(AnimationSpec.Run, double.NaN, 0));
            Assert.Throws<ArgumentException>(() => animations.Pose(AnimationSpec.Run, 0, double.PositiveInfinity));
            Assert.Throws<ArgumentNullException>(() => new HumanoidAnimations(null));
        }

        private static double DistanceToSegment(Vec3 p, Vec3 a, Vec3 b)
        {
            Vec3 ab = b - a;
            double t = MathUtil.Clamp(Vec3.Dot(p - a, ab) / ab.LengthSquared, 0, 1);
            return Vec3.Distance(p, a + ab * t);
        }
    }
}
