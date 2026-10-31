using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Humanoid
{
    /// <summary>The exporter window's animation tuning (<see cref="PoseTuning"/>).</summary>
    public class PoseTuningTests
    {
        public static IEnumerable<TestCaseData> Rigs() => HumanoidAnimationsTests.Rigs();

        private static IEnumerable<(string animation, double t, double move)> EveryFrame()
        {
            foreach (AnimationInfo info in AnimationSpec.All)
            {
                for (int row = 0; row < info.DirectionCount; row += Math.Max(1, info.DirectionCount / 3))
                {
                    for (int frame = 0; frame < info.FrameCount; frame++)
                    {
                        yield return (info.Name, AnimationSpec.FramePhase(info.Name, frame), AnimationSpec.Direction(info.Name, row).Move);
                    }
                }
            }
        }

        [TestCaseSource(nameof(Rigs))]
        public void TheDefaultsChangeNothing(RigInput input)
        {
            HumanoidRig rig = HumanoidRig.Measure(input);
            var plain = new HumanoidAnimations(rig);
            var tuned = new HumanoidAnimations(rig, new PoseTuning());
            Assert.That(tuned.Timing.DistancePerFrame, Is.EqualTo(plain.Timing.DistancePerFrame));
            Assert.That(tuned.Timing.SprintDistancePerFrame, Is.EqualTo(plain.Timing.SprintDistancePerFrame));
            foreach ((string animation, double t, double move) in EveryFrame())
            {
                PoseResult a = plain.Pose(animation, t, move), b = tuned.Pose(animation, t, move);
                foreach (KeyValuePair<string, Vec3> joint in a.Joints)
                {
                    Assert.That((b.Joints[joint.Key] - joint.Value).Length, Is.LessThan(1e-12), $"{animation} {joint.Key}");
                }
                Assert.That(b.WingElevation, Is.EqualTo(a.WingElevation), animation);
                Assert.That(b.TailSwing, Is.EqualTo(a.TailSwing), animation);
                Assert.That(b.TailLift, Is.EqualTo(a.TailLift), animation);
            }
        }

        /// <summary>Every slider at either end still poses every frame with finite joints.</summary>
        [TestCaseSource(nameof(Rigs))]
        public void EveryFramePosesAtTheEndsOfTheRanges(RigInput input)
        {
            HumanoidRig rig = HumanoidRig.Measure(input);
            var low = new PoseTuning
            {
                Lean = 0, HeadTilt = -30, ArmSpread = -1, ArmSwing = 0, ElbowBend = 0, Stride = 0.5, WingFlap = 0, WingRaise = -45, WingSweep = -45,
                ArmWingStroke = 0, ArmWingRoll = -60, TailSway = 0, TailLift = -45,
            };
            var high = new PoseTuning
            {
                Lean = 2, HeadTilt = 30, ArmSpread = 1, ArmSwing = 2, ElbowBend = 1, Stride = 1.5, WingFlap = 2, WingRaise = 45, WingSweep = 45,
                ArmWingStroke = 1.5, ArmWingRoll = 60, TailSway = 2, TailLift = 45,
            };
            foreach (PoseTuning tuning in new[] { low, high })
            {
                var animations = new HumanoidAnimations(rig, tuning);
                foreach ((string animation, double t, double move) in EveryFrame())
                {
                    PoseResult pose = animations.Pose(animation, t, move);
                    foreach (KeyValuePair<string, Vec3> joint in pose.Joints)
                    {
                        Assert.That(double.IsFinite(joint.Value.X) && double.IsFinite(joint.Value.Y) && double.IsFinite(joint.Value.Z),
                            $"{animation} {joint.Key}");
                    }
                }
            }
        }

        [Test]
        public void StrideSetsTheDistancePerFrame()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.6));
            var plain = new HumanoidAnimations(rig);
            var longer = new HumanoidAnimations(rig, new PoseTuning { Stride = 1.25 });
            Assert.That(longer.Timing.DistancePerFrame, Is.EqualTo(plain.Timing.DistancePerFrame * 1.25).Within(1e-12));
            Assert.That(longer.Timing.SprintDistancePerFrame, Is.EqualTo(plain.Timing.SprintDistancePerFrame * 1.25).Within(1e-12));
        }

        /// <summary>Elbow bend 0 leaves the running arms almost straight: the hands hang far from the shoulders.</summary>
        [TestCaseSource(nameof(Rigs))]
        public void ElbowBendStraightensTheRunningArms(RigInput input)
        {
            HumanoidRig rig = HumanoidRig.Measure(input);
            var bent = new HumanoidAnimations(rig);
            var straight = new HumanoidAnimations(rig, new PoseTuning { ElbowBend = 0 });
            foreach (string animation in new[] { AnimationSpec.Run, AnimationSpec.Sprint })
            {
                for (int frame = 0; frame < AnimationSpec.Get(animation).FrameCount; frame++)
                {
                    double t = AnimationSpec.FramePhase(animation, frame);
                    foreach (int side in new[] { -1, 1 })
                    {
                        double Reach(PoseResult pose) => (pose.Joints[JointNames.Hand(side)] - pose.Joints[JointNames.Shoulder(side)]).Length;
                        double reach = Reach(straight.Pose(animation, t, 0));
                        Assert.That(reach, Is.GreaterThan(rig.ArmLength * 0.85), $"{animation} {frame}");
                        Assert.That(reach, Is.GreaterThan(Reach(bent.Pose(animation, t, 0))), $"{animation} {frame}");
                    }
                }
            }
        }

        [Test]
        public void TheWingRaiseAndSweepReachThePose()
        {
            var animations = new HumanoidAnimations(HumanoidRig.Measure(SyntheticRigs.Generic(1.6)),
                new PoseTuning { WingRaise = 20, WingSweep = -10 });
            PoseResult pose = animations.Pose(AnimationSpec.Idle, 0, 0);
            Assert.That(pose.WingFoldedRaise, Is.EqualTo(20 * Math.PI / 180).Within(1e-12));
            Assert.That(pose.WingSweep, Is.EqualTo(-10 * Math.PI / 180).Within(1e-12));
        }

        [Test]
        public void ValuesOutsideTheRangesAreClamped()
        {
            PoseTuning clamped = new PoseTuning { Lean = 9, Stride = -3, ArmWingRoll = double.NaN }.Clamped();
            Assert.That(clamped.Lean, Is.EqualTo(2));
            Assert.That(clamped.Stride, Is.EqualTo(0.5));
            Assert.That(clamped.ArmWingRoll, Is.EqualTo(-60));
            Assert.That(new PoseTuning().Describe(), Is.Empty);
            Assert.That(new PoseTuning { Stride = 1.2 }.Describe(), Is.EqualTo("stride 1.2"));
        }
    }
}
