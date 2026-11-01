using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// The preview draws again only the frames a slider changed (<see cref="PoseResult.SameAs"/>): the stride leaves
        /// the idle as it is and changes most frames of the run.
        /// </summary>
        [Test]
        public void SameAsFindsTheFramesATuningChanges()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.6));
            var plain = new HumanoidAnimations(rig);
            var longer = new HumanoidAnimations(rig, new PoseTuning { Stride = 1.2 });
            for (int frame = 0; frame < AnimationSpec.Get(AnimationSpec.Idle).FrameCount; frame += 7)
            {
                double t = AnimationSpec.FramePhase(AnimationSpec.Idle, frame);
                Assert.That(plain.Pose(AnimationSpec.Idle, t, 0).SameAs(longer.Pose(AnimationSpec.Idle, t, 0)), Is.True, $"idle {frame}");
            }
            int runFrames = AnimationSpec.Get(AnimationSpec.Run).FrameCount, changed = 0;
            for (int frame = 0; frame < runFrames; frame++)
            {
                double t = AnimationSpec.FramePhase(AnimationSpec.Run, frame);
                changed += plain.Pose(AnimationSpec.Run, t, 0).SameAs(longer.Pose(AnimationSpec.Run, t, 0)) ? 0 : 1;
            }
            // the feet passing each other are where they were
            Assert.That(changed, Is.GreaterThan(runFrames / 2));
            PoseResult pose = plain.Pose(AnimationSpec.Run, 0.3, 0);
            Assert.That(pose.SameAs(plain.Pose(AnimationSpec.Run, 0.3, 0)), Is.True);
            Assert.That(pose.SameAs(null), Is.False);
        }

        /// <summary>The wing and tail sliders change nothing for an avatar without wings or a tail.</summary>
        [Test]
        public void SameAsLeavesOutPartsTheAvatarDoesNotHave()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.6));
            var plain = new HumanoidAnimations(rig);
            var tuned = new HumanoidAnimations(rig, new PoseTuning { WingFlap = 1.5, WingRaise = 20, WingSweep = 10, TailSway = 0.5, TailLift = 20 });
            foreach (string animation in new[] { AnimationSpec.Idle, AnimationSpec.Run })
            {
                PoseResult a = plain.Pose(animation, 0.25, 0), b = tuned.Pose(animation, 0.25, 0);
                Assert.That(a.SameAs(b), Is.False, animation);
                Assert.That(a.SameAs(b, wings: false, tail: false), Is.True, animation);
            }
            // folded wings (the run's) don't show how far a spread wing beats
            var flap = new HumanoidAnimations(rig, new PoseTuning { WingFlap = 0 });
            PoseResult run = plain.Pose(AnimationSpec.Run, 0.25, 0);
            Assert.That(run.LeftWingSpread, Is.EqualTo(0));
            Assert.That(run.SameAs(flap.Pose(AnimationSpec.Run, 0.25, 0)), Is.True);
        }

        /// <summary>
        /// The flight method: wing flapping is as designed; arm flapping beats the arms out to the sides; floating
        /// keeps them down. Neither beats the wings, and the arm-flapping set goes only with wing flapping.
        /// </summary>
        [Test]
        public void TheFlightMethodChoosesHowTheFlightAnimationsFly()
        {
            HumanoidRig rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.6));
            var wings = new HumanoidAnimations(rig);
            var arms = new HumanoidAnimations(rig, new PoseTuning { Flight = FlightMethod.ArmFlapping });
            var floating = new HumanoidAnimations(rig, new PoseTuning { Flight = FlightMethod.Floating });
            foreach (string animation in new[] { AnimationSpec.Hover, AnimationSpec.Fly, AnimationSpec.HoverGun, AnimationSpec.FlyGun, AnimationSpec.TakeOff })
            {
                PoseResult winged = wings.Pose(animation, 0.75, 0);
                Assert.That(winged.LeftWingSpread, Is.GreaterThan(0), animation);
                foreach (HumanoidAnimations other in new[] { arms, floating })
                {
                    PoseResult pose = other.Pose(animation, 0.75, 0);
                    Assert.That(pose.LeftWingSpread + pose.RightWingSpread, Is.EqualTo(0), animation);
                }
            }
            double HandBelowShoulder(PoseResult pose) => pose.Joints[JointNames.Shoulder(1)].Z - pose.Joints[JointNames.Hand(1)].Z;
            foreach (string animation in new[] { AnimationSpec.Hover, AnimationSpec.Fly })
            {
                Assert.That(HandBelowShoulder(floating.Pose(animation, 0.25, 0)), Is.GreaterThan(rig.ArmLength * 0.6), animation);
                Assert.That(HandBelowShoulder(arms.Pose(animation, 0.25, 0)), Is.LessThan(rig.ArmLength * 0.3), animation);
                // the arm-flapping set itself is the same whatever the method
                PoseResult flap = wings.Pose(AnimationSpec.HoverFlap, 0.25, 0);
                Assert.That(flap.SameAs(arms.Pose(AnimationSpec.HoverFlap, 0.25, 0)), Is.True);
            }
            Assert.That(AnimationSpec.ForPack(true, false).Select(a => a.Name),
                Is.EquivalentTo(AnimationSpec.ForPack(true).Select(a => a.Name).Where(n => !AnimationSpec.IsArmFlapping(n))));
            Assert.That(AnimationSpec.ForPack(false, true).Count(), Is.EqualTo(AnimationSpec.ForPack(false, false).Count()));
            Assert.That(new PoseTuning { Flight = FlightMethod.Floating }.Describe(), Is.Empty);
            Assert.That(FlightMethod.Floating.Describe(), Is.EqualTo("floating"));
            Assert.That(new PoseTuning { Flight = FlightMethod.ArmFlapping }.Clamped().Flight, Is.EqualTo(FlightMethod.ArmFlapping));
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
