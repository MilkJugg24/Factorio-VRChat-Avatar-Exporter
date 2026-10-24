using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MilkJugg24.FactorioAvatarExporter.Core.Tests.Humanoid
{
    /// <summary>
    /// The humanoid poses keep the timing the pack declares (<see cref="HumanoidAnimations.Timing"/>) and the
    /// moving direction of the gun rows.
    /// </summary>
    public class HumanoidTimingTests
    {
        public static IEnumerable<TestCaseData> Rigs() => HumanoidAnimationsTests.Rigs();

        private static PoseResult Frame(HumanoidAnimations animations, string animation, int frame, int row = 0) =>
            animations.Pose(animation, AnimationSpec.FramePhase(animation, frame), AnimationSpec.Direction(animation, row).Move);

        /// <summary>Turns a model space vector clockwise (seen from above) by <paramref name="move"/> radians.</summary>
        private static Vec3 Turn(Vec3 v, double move) => Quat.FromAxisAngle(Vec3.UnitZ, -move).Rotate(v);

        [TestCaseSource(nameof(Rigs))]
        public void FeetLandOnTheFootstepFrames(RigInput input)
        {
            HumanoidRig rig = HumanoidRig.Measure(input);
            var animations = new HumanoidAnimations(rig);
            int frames = AnimationSpec.Get(AnimationSpec.Run).FrameCount;
            foreach ((int side, IReadOnlyList<int> landings) in new[]
                { (-1, animations.Timing.LeftFootFrames), (1, animations.Timing.RightFootFrames) })
            {
                string foot = side > 0 ? "right" : "left";
                foreach (int landing in landings)
                {
                    // 1-based frames: the frame before frame 1 is the last one
                    int before = (landing - 2 + frames) % frames + 1;
                    double planted = Frame(animations, AnimationSpec.Run, landing - 1).Joints[JointNames.Ball(side)].Z;
                    double lifted = Frame(animations, AnimationSpec.Run, before - 1).Joints[JointNames.Ball(side)].Z;
                    Assert.That(planted, Is.EqualTo(rig.Ball(side).Z).Within(1e-6), $"the {foot} foot is down on frame {landing}");
                    Assert.That(lifted, Is.GreaterThan(rig.Ball(side).Z + 1e-3),
                        $"the {foot} foot is still in the air on frame {before}");
                }
            }
        }

        [TestCaseSource(nameof(Rigs))]
        public void ThePickComesDownOnTheImpactFrame(RigInput input)
        {
            HumanoidRig rig = HumanoidRig.Measure(input);
            var animations = new HumanoidAnimations(rig);
            int impact = animations.Timing.MineImpactFrame;
            // the engineer's swing: still up three frames before, down fast
            PoseResult before = Frame(animations, AnimationSpec.Mine, impact - 4);
            PoseResult hit = Frame(animations, AnimationSpec.Mine, impact - 1);
            Assert.That(before.Prop.Tip.Z, Is.GreaterThan(rig.HipsHeight), $"still raised on frame {impact - 3}");
            Assert.That(hit.Prop.Tip.Z, Is.LessThan(rig.HipsHeight * 0.6), $"down on frame {impact}");
        }

        [TestCaseSource(nameof(Rigs))]
        public void GunRowsMoveTheHipsAndLegsWhileTheGunAimsAhead(RigInput input)
        {
            HumanoidRig rig = HumanoidRig.Measure(input);
            var animations = new HumanoidAnimations(rig);
            foreach (string animation in new[] { AnimationSpec.RunGun, AnimationSpec.FlyGun })
            {
                AnimationInfo info = AnimationSpec.Get(animation);
                for (int row = 0; row < info.DirectionCount; row++)
                {
                    Vec3 moving = Turn(Vec3.UnitY, AnimationSpec.Direction(animation, row).Move);
                    int steps = 0;
                    for (int f = 0; f < info.FrameCount; f++)
                    {
                        string what = $"{animation} row {row + 1} frame {f + 1}";
                        PoseResult pose = Frame(animations, animation, f, row);
                        Vec3 pelvis = pose.WorldDeltas[HumanBone.Hips].Rotate(Vec3.UnitY).Horizontal.Normalized;
                        Assert.That(Vec3.Dot(pelvis, moving), Is.GreaterThan(Math.Cos(0.15)),
                            what + ": the pelvis faces the moving direction, give or take the hip twist");
                        Vec3 aim = (pose.Prop.Tip - pose.Prop.Base).Horizontal.Normalized;
                        Assert.That(aim.Y, Is.GreaterThan(0.99), what + ": the gun aims ahead");
                        PoseResult next = Frame(animations, animation, (f + 1) % info.FrameCount, row);
                        foreach (int side in TestMath.Sides)
                        {
                            Vec3 hip = pose.Joints[JointNames.Hip(side)];
                            Vec3 ankle = pose.Joints[JointNames.Ankle(side)];
                            Vec3 ball = pose.Joints[JointNames.Ball(side)];
                            Vec3 nextBall = next.Joints[JointNames.Ball(side)];
                            if (animation == AnimationSpec.FlyGun)
                            {
                                Assert.That(Vec3.Dot((ankle - hip).Horizontal.Normalized, -moving), Is.GreaterThan(0.8),
                                    what + ": the legs trail behind");
                            }
                            else if (Math.Abs(ball.Z - rig.Ball(side).Z) < 1e-6 && Math.Abs(nextBall.Z - rig.Ball(side).Z) < 1e-6)
                            {
                                // a planted foot slides straight back under the body
                                Assert.That(Vec3.Dot((nextBall - ball).Horizontal.Normalized, -moving), Is.GreaterThan(0.99),
                                    what + $": the {(side > 0 ? "right" : "left")} foot slides back");
                                steps++;
                            }
                        }
                    }
                    if (animation == AnimationSpec.RunGun)
                    {
                        Assert.That(steps, Is.GreaterThanOrEqualTo(info.FrameCount - 2),
                            $"row {row + 1}: each foot is down about half the cycle");
                    }
                }
            }
        }
    }
}
