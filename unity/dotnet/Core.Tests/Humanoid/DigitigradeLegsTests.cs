using System;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Humanoid
{
    /// <summary>Digitigrade legs: knees forward, the ankle (hock) high up and behind, the foot nearly vertical.</summary>
    public class DigitigradeLegsTests
    {
        /// <summary>
        /// A digitigrade rig with its ankle a third of the way up the leg and a Toes bone just above the ground,
        /// <paramref name="toesOutward"/> and <paramref name="toesForward"/> meters from straight under the ankle.
        /// </summary>
        private static RigInput Digitigrade(double toesOutward, double toesForward)
        {
            RigInput input = SyntheticRigs.Generic(1.7, null, true);
            foreach (int side in TestMath.Sides)
            {
                Vec3 hip = input.Bones[HumanBoneHierarchy.UpperLeg(side)];
                input.Bones[HumanBoneHierarchy.LowerLeg(side)] = new Vec3(hip.X, 0.1, hip.Z * 0.6);
                input.Bones[HumanBoneHierarchy.Foot(side)] = new Vec3(hip.X, -0.15, hip.Z * 0.33);
                input.Bones[HumanBoneHierarchy.Toes(side)] = new Vec3(hip.X + side * toesOutward, -0.15 + toesForward, 0.03);
            }
            return input;
        }

        [TestCase(0.0, -0.003)]
        [TestCase(0.003, 0.0)]
        [TestCase(0.003, -0.003)]
        [TestCase(-0.003, -0.003)]
        [TestCase(0.003, 0.003)]
        public void ToesUnderTheHockLeaveTheFeetAndKneesPointingForward(double toesOutward, double toesForward)
        {
            HumanoidRig rig = HumanoidRig.Measure(Digitigrade(toesOutward, toesForward));
            Assert.That(rig.LegStyle, Is.EqualTo(LegStyle.Digitigrade));
            foreach (int side in TestMath.Sides)
            {
                TestMath.AssertClose(rig.FootForward(side), Vec3.UnitY, 1e-12, "forward");
                TestMath.AssertClose(rig.Ball(side), rig.Rest(HumanBoneHierarchy.Toes(side)), 1e-12, "ball = Toes");
            }
            var animations = new HumanoidAnimations(rig);
            foreach (string animation in new[] { AnimationSpec.Idle, AnimationSpec.Run, AnimationSpec.Mine, AnimationSpec.Hover })
            {
                for (int f = 0; f < AnimationSpec.Get(animation).FrameCount; f++)
                {
                    PoseResult pose = animations.Pose(animation, AnimationSpec.FramePhase(animation, f), 0);
                    foreach (int side in TestMath.Sides)
                    {
                        string what = $"{animation} frame {f + 1} side {side}";
                        Vec3 hip = pose.Joints[JointNames.Hip(side)];
                        Vec3 knee = pose.Joints[JointNames.Knee(side)];
                        Vec3 leg = (pose.Joints[JointNames.Ankle(side)] - hip).Normalized;
                        Vec3 kneeOut = knee - hip - leg * Vec3.Dot(knee - hip, leg);
                        Assert.That(kneeOut.Y, Is.GreaterThan(0.05 * rig.LegLength), what + ": the knee is in front of the leg");
                        double thigh = TestMath.RotationAngle(pose.WorldDeltas[HumanBoneHierarchy.UpperLeg(side)], Quat.Identity);
                        double most = animation == AnimationSpec.Idle || animation == AnimationSpec.Hover ? 20 : 70;
                        Assert.That(thigh, Is.LessThan(MathUtil.Radians(most)), what + ": the thigh turns little");
                    }
                }
            }
        }

        [Test]
        public void DanglingFeetKeepTheHockBentTheRestWay()
        {
            HumanoidRig rig = HumanoidRig.Measure(Digitigrade(0, 0));
            var animations = new HumanoidAnimations(rig);
            // which way the foot turns from the line of the shin, about the knee's bend axis: a foot pointed too far
            // folds back past the shin and the sign flips
            double Bend(Vec3 knee, Vec3 ankle, Vec3 ball, Vec3 axis) => Vec3.Dot(Vec3.Cross(ankle - knee, ball - ankle), axis);
            foreach (int side in TestMath.Sides)
            {
                double rest = Bend(rig.Rest(HumanBoneHierarchy.LowerLeg(side)), rig.Rest(HumanBoneHierarchy.Foot(side)),
                    rig.Ball(side), rig.KneeBendAxis(side));
                foreach (string animation in new[] { AnimationSpec.Hover, AnimationSpec.Fly })
                {
                    for (int f = 0; f < AnimationSpec.Get(animation).FrameCount; f++)
                    {
                        PoseResult pose = animations.Pose(animation, AnimationSpec.FramePhase(animation, f), 0);
                        Vec3 axis = pose.WorldDeltas[HumanBoneHierarchy.LowerLeg(side)].Rotate(rig.KneeBendAxis(side));
                        double bend = Bend(pose.Joints[JointNames.Knee(side)], pose.Joints[JointNames.Ankle(side)],
                            pose.Joints[JointNames.Ball(side)], axis);
                        Assert.That(Math.Sign(bend), Is.EqualTo(Math.Sign(rest)), $"{animation} frame {f + 1} side {side}");
                    }
                }
            }
        }
    }
}
