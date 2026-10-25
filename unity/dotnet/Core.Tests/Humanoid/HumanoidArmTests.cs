using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Humanoid
{
    /// <summary>How the humanoid animations pose the arms and hands that hold the pick and the gun.</summary>
    public class HumanoidArmTests
    {
        /// <summary>
        /// The rigs of <see cref="HumanoidAnimationsTests.Rigs"/> and short arms on average shoulders, which only
        /// reach the gun when it moves towards the left shoulder.
        /// </summary>
        public static IEnumerable<TestCaseData> GunRigs()
        {
            foreach (TestCaseData rig in HumanoidAnimationsTests.Rigs())
            {
                yield return rig;
            }
            yield return new TestCaseData(SyntheticRigs.Generic(1.2, new SyntheticRigProportions { Arms = 0.55, ShoulderWidth = 1.0 }))
                .SetArgDisplayNames("short arms");
            SyntheticRigProportions chibi = SyntheticRigProportions.Chibi();
            chibi.ShoulderWidth = 1.0;
            yield return new TestCaseData(SyntheticRigs.Generic(1.0, chibi)).SetArgDisplayNames("chibi, average shoulders");
        }

        /// <summary>
        /// The middle of a hand's fist, where it holds a handle: <see cref="HumanoidAnimations.GripOffset"/> from the
        /// wrist along the fingers.
        /// </summary>
        internal static Vec3 Fist(HumanoidRig rig, HumanoidAnimations animations, PoseResult pose, int side)
        {
            Vec3 restFingers = rig.Rest(HumanBoneHierarchy.Hand(side)) - rig.Rest(HumanBoneHierarchy.LowerArm(side));
            Vec3 fingers = pose.WorldDeltas[HumanBoneHierarchy.Hand(side)].Rotate(restFingers.Normalized);
            return pose.Joints[JointNames.Hand(side)] + fingers * animations.GripOffset;
        }

        [TestCaseSource(typeof(HumanoidAnimationsTests), nameof(HumanoidAnimationsTests.Rigs))]
        public void MiningKeepsTheUpperArmsWhereShouldersTakeThem(RigInput input)
        {
            HumanoidRig rig = HumanoidRig.Measure(input);
            var animations = new HumanoidAnimations(rig);
            // the last torso bone the rig has turns with the chest frame
            HumanBone chestBone = rig.Has(HumanBone.UpperChest) ? HumanBone.UpperChest
                : rig.Has(HumanBone.Chest) ? HumanBone.Chest : HumanBone.Spine;
            for (int f = 0; f < AnimationSpec.Get(AnimationSpec.Mine).FrameCount; f++)
            {
                PoseResult pose = animations.Pose(AnimationSpec.Mine, AnimationSpec.FramePhase(AnimationSpec.Mine, f), 0);
                Quat chest = pose.WorldDeltas[chestBone];
                foreach (int side in TestMath.Sides)
                {
                    string what = $"mine frame {f + 1}: the {(side > 0 ? "right" : "left")}";
                    Vec3 shoulder = pose.Joints[JointNames.Shoulder(side)];
                    // shoulders take an upper arm at most about 45 degrees behind the body's frontal plane, unless
                    // it is raised high
                    Vec3 arm = chest.Inverse.Rotate(pose.Joints[JointNames.Elbow(side)] - shoulder).Normalized;
                    double behind = Math.Asin(-arm.Y), elevation = Math.Asin(arm.Z);
                    if (elevation < MathUtil.Radians(60))
                    {
                        Assert.That(behind, Is.LessThan(MathUtil.Radians(45)),
                            $"{what} upper arm points {behind * 180 / Math.PI:0} degrees behind the shoulders at " +
                            $"{elevation * 180 / Math.PI:0} degrees up");
                    }
                    // the raised pick's hands go over the head, not on behind it
                    double handBehind = -chest.Inverse.Rotate(pose.Joints[JointNames.Hand(side)] - shoulder).Y;
                    Assert.That(handBehind, Is.LessThan(0.25 * rig.ArmLength),
                        $"{what} hand is {handBehind / rig.ArmLength:0.00} arm lengths behind the shoulder");
                }
            }
        }

        [TestCaseSource(typeof(HumanoidAnimationsTests), nameof(HumanoidAnimationsTests.Rigs))]
        public void ClosedHandsHoldTheHandleInTheFist(RigInput input)
        {
            HumanoidRig rig = HumanoidRig.Measure(input);
            var animations = new HumanoidAnimations(rig);
            double size = rig.HeadTopHeight / Conventions.DefaultCharacterHeight;
            int tested = 0;
            foreach ((string animation, int row, int frame, PoseResult pose) in animations.AllFrames(true))
            {
                PropPose prop = pose.Prop;
                if (prop == null)
                {
                    continue;
                }
                // the right hand round the pick's handle (swung one-handed, as the engineer does); the support hand
                // round the gun, just under its axis (the trigger hand holds the gun's grip, below it, like a pistol's)
                Vec3 axis = (prop.Tip - prop.Base).Normalized;
                Vec3 under = prop.Kind == PropKind.Gun ? prop.Up * (0.02 * size) : Vec3.Zero;
                foreach (int side in prop.Kind == PropKind.Gun ? new[] { -1 } : new[] { 1 })
                {
                    string what = $"{animation} row {row + 1} frame {frame + 1}, {(side > 0 ? "right" : "left")} hand";
                    Assert.That(side > 0 ? pose.RightGrip : pose.LeftGrip, Is.EqualTo(1), what);
                    // closed fingers curl about the width of the palm, +y in the palms-down T-pose
                    Vec3 width = pose.WorldDeltas[HumanBoneHierarchy.Hand(side)].Rotate(Vec3.UnitY);
                    Assert.That(Math.Abs(Vec3.Dot(width, axis)), Is.GreaterThan(Math.Cos(MathUtil.Radians(2))),
                        what + ": the palm's width runs along the handle");
                    Vec3 fist = Fist(rig, animations, pose, side);
                    Vec3 offset = fist - (prop.Base - under);
                    double miss = (offset - axis * Vec3.Dot(offset, axis)).Length;
                    Assert.That(miss, Is.LessThan(1e-6), what + ": the handle runs through the middle of the fist");
                    // and the hand gets there turning from the forearm no further than forearms turn and wrists bend
                    double wrist = TestMath.RotationAngle(pose.WorldDeltas[HumanBoneHierarchy.Hand(side)],
                        pose.WorldDeltas[HumanBoneHierarchy.LowerArm(side)]);
                    Assert.That(wrist, Is.LessThan(MathUtil.Radians(100)),
                        $"{what}: the hand turns {wrist * 180 / Math.PI:0} degrees from the forearm");
                    tested++;
                }
            }
            Assert.That(tested, Is.GreaterThan(500));
        }
    }
}
